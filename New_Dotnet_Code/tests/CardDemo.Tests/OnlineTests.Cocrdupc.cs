using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;

namespace CardDemo.Tests;

/// <summary>
/// COCRDUPC (TRANSID CCUP) — the online "Update Credit Card Details" screen, driven through its full
/// three-turn happy path (load → validate → save).
/// </summary>
public sealed partial class OnlineTests
{
    /// <summary>
    /// COCRDUPC card update — the validate→F5 two-phase save REWRITEs CARD_DATA.
    ///
    /// COCRDUPC is pseudo-conversational with a private CCUP-CHANGE-ACTION state machine carried across
    /// turns (COCRDUPC.cbl:274-321). A faithful happy-path edit therefore takes three operator turns:
    ///   Turn A (load): with CCUP-DETAILS-NOT-FETCHED and the 11-digit account + 16-digit card keyed into
    ///     ACCTSID/CARDSID, 2000-DECIDE-ACTION runs 9000-READ-DATA, reads CARDDAT by card number
    ///     (9100-GETCARD-BYACCTCARD), SETs CCUP-SHOW-DETAILS, and paints the on-file values into the now
    ///     unprotected CRDNAME/CRDSTCD/EXPMON/EXPYEAR fields with INFOMSG "Details of selected card shown
    ///     above" (COCRDUPC.cbl:160-161,954-977,1107-1112).
    ///   Turn B (validate): the operator changes an allowed field (here the active status Y→N) and presses
    ///     ENTER. In CCUP-SHOW-DETAILS 1200-EDIT-MAP-INPUTS validates the edits and 2000 SETs
    ///     CCUP-CHANGES-OK-NOT-CONFIRMED, so INFOMSG becomes "Changes validated.Press F5 to save"
    ///     (COCRDUPC.cbl:166-167,971-977,1149-1150).
    ///   Turn C (save): pressing F5 in CCUP-CHANGES-OK-NOT-CONFIRMED runs 9200-WRITE-PROCESSING — a re-read
    ///     under UPDATE, the 9300 optimistic re-compare against the OLD snapshot, then REWRITE CARDDAT
    ///     (CardRepository.Update). On success 2000 SETs CCUP-CHANGES-OKAYED-AND-DONE and INFOMSG becomes
    ///     "Changes committed to database" (COCRDUPC.cbl:168-169,988-1000,1151-1152,1477-1483).
    ///
    /// The ACCTSID/CARDSID search keys are protected (not re-typeable) once the card is fetched, but a 3270
    /// returns their painted values on every RECEIVE; the program re-keys WS-CARD-RID-CARDNUM from CARDSIDI
    /// on each turn (FB-8.1, COCRDUPC.cbl:1380,1425). The scripted RECEIVE therefore re-supplies them so the
    /// 9200 re-read locks the right row — otherwise CARDSIDI arrives blank and 9200 NOTFNDs into
    /// "Could not lock record for update", proving the assertions below are load-bearing.
    ///
    /// Meaningful assertion: the CARD_DATA row's active_status actually flips Y→N on disk. The faithful CVV
    /// bug (FB-8.3, COCRDUPC.cbl:1464-1465: the screen has no CVV field so the rewrite zeroes cvv_cd) is
    /// asserted too, locking the port's known-quirk behaviour.
    /// </summary>
    [Fact]
    public void Cocrdupc_validate_then_f5_rewrites_card_active_status()
    {
        using var db = SeededDb();
        var cards = new CardRepository(db.Connection);

        // Seed our OWN card (do not mutate a shared master row). Year 2025 is inside the 1950..2099
        // valid-year window (FB-8.8), and the name is alphabetic+space so 1230-EDIT-NAME passes.
        const string cardNum = "4000000000000077";
        const long acctId = 99999999999L;
        const int seededCvv = 123;
        Assert.Equal(FileStatus.Ok, cards.Insert(new Card
        {
            CardNum = cardNum,
            AcctId = acctId,
            CvvCd = seededCvv,
            EmbossedName = "JOHN Q PUBLIC",
            ExpirationDate = "2025-08-15",
            ActiveStatus = "Y",
        }));

        string acctField = acctId.ToString("D11"); // CC-ACCT-ID X(11)

        // ---- Turn A: load the card by account + card number (CCUP-DETAILS-NOT-FETCHED -> SHOW-DETAILS). ----
        var caLoad = new CardDemoCommArea { FromTranId = "CCUP", FromProgram = "COMEN01C", UserId = "TESTUSER" };
        caLoad.SetUser();
        caLoad.SetReenter(); // CDEMO-PGM-REENTER so 0000-MAIN takes the WHEN-OTHER process path, not a fresh prompt.
        var loadScreen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("ACCTSID").SetValue(acctField); // ACCTSIDI OF CCRDUPAI X(11)
                map.Field("CARDSID").SetValue(cardNum);   // CARDSIDI OF CCRDUPAI X(16)
            },
        };
        CicsOutcome loadOutcome = NewDispatcher(db, loadScreen).RunTurn("CCUP", AidKey.Enter, caLoad);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, loadOutcome.Kind);
        Assert.Equal("CCUP", loadOutcome.TransId);
        // 9100 read the card and painted its on-file values; the status shown is the seeded 'Y'.
        Assert.Equal("Details of selected card shown above",
            loadScreen.LastSentMap!.Field("INFOMSG").Value.TrimEnd());
        Assert.Equal("JOHN Q PUBLIC", loadScreen.LastSentMap!.Field("CRDNAME").Value.TrimEnd());
        Assert.Equal("Y", loadScreen.LastSentMap!.Field("CRDSTCD").Value.TrimEnd());

        // ---- Turn B: change active status Y -> N and ENTER (validate; CCUP-CHANGES-OK-NOT-CONFIRMED). ----
        var caValidate = loadOutcome.CommArea!;
        var validateScreen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                // Protected search keys come back on every 3270 RECEIVE; re-supply so 9200 re-reads the row.
                map.Field("ACCTSID").SetValue(acctField);
                map.Field("CARDSID").SetValue(cardNum);
                // Edited card data: same name/expiry as on file, status flipped to 'N'.
                map.Field("CRDNAME").SetValue("JOHN Q PUBLIC");
                map.Field("CRDSTCD").SetValue("N");
                map.Field("EXPMON").SetValue("08");
                map.Field("EXPYEAR").SetValue("2025");
            },
        };
        CicsOutcome validateOutcome = NewDispatcher(db, validateScreen).RunTurn("CCUP", AidKey.Enter, caValidate);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, validateOutcome.Kind);
        Assert.Equal("Changes validated.Press F5 to save",
            validateScreen.LastSentMap!.Field("INFOMSG").Value.TrimEnd());
        // No validation error surfaced.
        Assert.Equal("", validateScreen.LastSentMap!.Field("ERRMSG").Value.TrimEnd());
        // The change has NOT been written yet — still the seeded 'Y' on disk.
        Assert.Equal(FileStatus.Ok, cards.ReadByKey(cardNum, out Card? midFlight));
        Assert.Equal("Y", midFlight!.ActiveStatus.TrimEnd());

        // ---- Turn C: press F5 to commit (9200-WRITE-PROCESSING -> REWRITE CARDDAT). ----
        var caSave = validateOutcome.CommArea!;
        var saveScreen = new ScriptedScreenIo
        {
            NextAid = AidKey.Pf5,
            OnReceive = map =>
            {
                map.Field("ACCTSID").SetValue(acctField);
                map.Field("CARDSID").SetValue(cardNum);
                map.Field("CRDNAME").SetValue("JOHN Q PUBLIC");
                map.Field("CRDSTCD").SetValue("N");
                map.Field("EXPMON").SetValue("08");
                map.Field("EXPYEAR").SetValue("2025");
            },
        };
        // 0000-MAIN's YYYY-STORE-PFKEY reads EIBAID (the AID passed to RunTurn), so PF5 must be the turn's AID.
        CicsOutcome saveOutcome = NewDispatcher(db, saveScreen).RunTurn("CCUP", AidKey.Pf5, caSave);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, saveOutcome.Kind);
        Assert.Equal("CCUP", saveOutcome.TransId);
        // Exact success literal from the COBOL (COCRDUPC.cbl:168-169).
        Assert.Equal("Changes committed to database",
            saveScreen.LastSentMap!.Field("INFOMSG").Value.TrimEnd());

        // CORE ASSERTION: the CARD_DATA row was REWRITTEN — active_status flipped Y -> N on disk.
        Assert.Equal(FileStatus.Ok, cards.ReadByKey(cardNum, out Card? after));
        Assert.Equal("N", after!.ActiveStatus.TrimEnd());
        // The embossed name we did not change is intact.
        Assert.Equal("JOHN Q PUBLIC", after.EmbossedName.TrimEnd());
        // FB-8.6 (faithful bug): EXPDAY is protected/dark so the operator never re-keys it; the rewrite
        // rebuilds the date as NEW-EXPYEAR '-' NEW-EXPMON '-' NEW-EXPDAY with a BLANK day (the never-received
        // EXPDAYI), so "2025-08-15" becomes "2025-08-  " on disk (COCRDUPC.cbl:621,1122-1123,1467-1474).
        Assert.Equal("2025-08-", after.ExpirationDate.TrimEnd());
        Assert.Equal(acctId, after.AcctId); // FB-8.2: rewrite acct_id taken from the typed account (COCRDUPC.cbl:1463).
        // FB-8.3 (faithful bug): the rewrite zeroes cvv_cd because the screen has no CVV field, so the
        // never-populated CCUP-NEW-CVV-CD flows through to CARD-UPDATE-CVV-CD (COCRDUPC.cbl:1464-1465).
        Assert.Equal(0, after.CvvCd);
        Assert.NotEqual(seededCvv, after.CvvCd);
    }
}
