using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    /// <summary>
    /// COTRN02C "Add Transaction" (TRANSID CT02, map COTRN2A): a reenter ENTER turn that keys a valid
    /// Account-ID (resolvable through CARD_XREF) plus every required data field, with the confirmation field
    /// left blank. This drives the program's whole "add" turn up to the confirm gate:
    /// <para>
    /// VALIDATE-INPUT-KEY-FIELDS takes the ACTIDINI branch (cbl:196-208): it NUMVALs the account id, reads
    /// CARD-XREF by the account-id alternate index (READ-CXACAIX-FILE = <see cref="CardXrefRepository.ReadByAltKey"/>),
    /// and paints the resolved card number into CARDNINI (cbl:209). VALIDATE-INPUT-DATA-FIELDS then passes the
    /// full empty/numeric/format/CSUTLDTC chain (cbl:251-436). Finally PROCESS-ENTER-KEY's EVALUATE CONFIRMI
    /// sees a blank confirm, so it takes WHEN SPACES (cbl:175-181): MOVE 'Y' TO WS-ERR-FLG, message
    /// 'Confirm to add this transaction...', and SEND-then-RETURN(CT02).
    /// </para>
    /// <para>
    /// Why blank-confirm and not a Y-confirm WRITE: the faithful ADD-TRANSACTION first does
    /// MOVE HIGH-VALUES TO TRAN-ID then STARTBR GTEQ (cbl:444-445). With HIGH-VALUES no key sorts at-or-after
    /// it, so STARTBR returns NOTFND and the program SEND-RETURNs "Transaction ID NOT found..." (faithful bug
    /// B-2, cbl:655-660) before any WRITE — i.e. the keyed-WRITE branch is unreachable in this port for any
    /// seed. The blank-confirm reject is therefore the strongest deterministic end-to-end assertion: it
    /// FAILS unless the key resolution (CARD_XREF read) AND the entire data-validation chain succeeded.
    /// </para>
    /// Assertions lock: (1) the card number painted into CARDNIN equals the CARD_XREF master's card for that
    /// account (proves the alternate-index read ran), and (2) the error line is the exact confirm-prompt
    /// literal (proves validation passed and the confirm gate fired) — not any earlier validation message.
    /// </summary>
    [Fact]
    public void TransactionAddProgram_valid_keyed_add_with_blank_confirm_resolves_card_and_prompts_for_confirmation()
    {
        using var db = SeededDb();

        // A seeded account reachable through CARD_XREF's account-id alternate index, so the ACTIDIN key path
        // resolves to a real card number (otherwise the turn would stop on "Account ID NOT found...").
        long acctId = FirstResolvableAccount(db);
        var xref = new CardXrefRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, xref.ReadByAltKey(acctId, out CardXref? expectedXref));
        Assert.NotNull(expectedXref);
        string expectedCard = expectedXref!.XrefCardNum.TrimEnd();

        // A reenter COMMAREA so COTRN02C takes RECEIVE -> EVALUATE EIBAID -> PROCESS-ENTER-KEY (cbl:131-135).
        var ca = new CardDemoCommArea { FromTranId = "CT02", FromProgram = "COMEN01C" };
        ca.SetReenter();

        // Key a fully valid transaction against the resolvable account, but leave CONFIRM blank so the program
        // stops at the confirmation gate instead of attempting the (unreachable) WRITE.
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("ACTIDIN").SetValue(acctId.ToString("D11")); // 9(11) account id -> ACTIDINI
                map.Field("TTYPCD").SetValue("01");                    // Type CD (numeric)
                map.Field("TCATCD").SetValue("1");                     // Category CD (numeric)
                map.Field("TRNSRC").SetValue("POS");                   // Source
                map.Field("TDESC").SetValue("ONLINE ADD TEST TXN");    // Description
                map.Field("TRNAMT").SetValue("-00000100.00");          // -99999999.99 format
                map.Field("TORIGDT").SetValue("2026-06-20");           // YYYY-MM-DD (valid)
                map.Field("TPROCDT").SetValue("2026-06-20");           // YYYY-MM-DD (valid)
                map.Field("MID").SetValue("123456789");                // Merchant ID (numeric)
                map.Field("MNAME").SetValue("ACME STORE");             // Merchant Name
                map.Field("MCITY").SetValue("ATLANTA");                // Merchant City
                map.Field("MZIP").SetValue("30301");                   // Merchant Zip
                map.Field("CONFIRM").SetValue("");                     // blank -> "Confirm to add this transaction..."
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CT02", AidKey.Enter, ca);

        // SEND-then-RETURN(CT02) — the task is redisplayed, not XCTL'd away (cbl:530-534).
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CT02", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("COTRN2A", screen.LastSentMap!.Name);

        // (1) The key-field validation read CARD_XREF by the account-id alternate index and painted the
        //     master card number into CARDNINI (cbl:208-209). This would be blank/zero if the read were broken.
        Assert.Equal(expectedCard, screen.LastSentMap.Field("CARDNIN").Value.TrimEnd());

        // (2) The full data-field validation chain passed and the confirm gate fired with the exact COBOL
        //     literal (cbl:178-179) — NOT any earlier "... can NOT be empty / must be Numeric / format" message,
        //     and NOT the key-lookup "Account ID NOT found..." message.
        Assert.Equal("Confirm to add this transaction...", screen.LastSentMap.Field("ERRMSG").Value.TrimEnd());
        Assert.DoesNotContain("NOT found", screen.LastSentMap.Field("ERRMSG").Value);
        Assert.DoesNotContain("empty", screen.LastSentMap.Field("ERRMSG").Value);

        // No transaction was written (the WRITE branch is gated behind a Y confirm we deliberately withheld).
        Assert.Empty(new TransactionRepository(db.Connection).ReadAll());
    }
}
