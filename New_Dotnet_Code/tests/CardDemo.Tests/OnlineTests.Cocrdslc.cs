using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    // ===============================================================================================
    //  COCRDSLC (CCDL) — View Credit Card Detail
    // ===============================================================================================

    /// <summary>
    /// COCRDSLC card-detail happy path: a re-entry ENTER turn carrying a keyed 11-digit account number and a
    /// real 16-digit card number drives 2000-PROCESS-INPUTS (both filter edits pass), then 9000-READ-DATA
    /// reads CARDDAT by primary key (card number), sets FOUND-CARDS-FOR-ACCOUNT and 1200-SETUP-SCREEN-VARS
    /// paints the card master's embossed name, active status, and expiry month/year into the symbolic map.
    /// <para>
    /// This locks the program's core read-and-paint behaviour against the CARD master: CRDNAME must equal the
    /// card's embossed_name, CRDSTCD its active_status, and EXPMON/EXPYEAR the MM/YYYY slices of its X(10)
    /// expiration_date (parsed via the CARD-EXPIRAION-DATE-X REDEFINES, YYYY-MM-DD). It also confirms the
    /// INFOMSG carries the exact "   Displaying requested details" literal that FOUND-CARDS-FOR-ACCOUNT emits,
    /// and that the program re-drives itself with RETURN TRANSID('CCDL'). If the READ keyed wrong, the
    /// EVALUATE NORMAL branch were dropped, or the detail-field MOVEs were broken, these assertions fail.
    /// </para>
    /// source: COCRDSLC.cbl:357-371 (REENTER path), :582-595 (2000-PROCESS-INPUTS),
    /// :736-754 (9100 READ + NORMAL), :474-485 (1200 paints CRDNAME/CRDSTCD/EXPMON/EXPYEAR), :129-130 (INFOMSG).
    /// </summary>
    [Fact]
    public void Cocrdslc_reads_a_known_card_and_paints_its_detail_fields()
    {
        using var db = SeededDb();

        // Pick a real CARD master row whose card number is a clean 16-digit numeric key (the program's READ
        // keys on card number only — FB-1) and whose expiry date is the expected X(10) YYYY-MM-DD shape.
        var cards = new CardRepository(db.Connection);
        Card expected = cards.ReadAll().First(c =>
            c.CardNum.Trim().Length == 16 && c.CardNum.Trim().All(char.IsDigit) &&
            c.ExpirationDate.Length >= 7 && c.ExpirationDate[4] == '-');

        string cardNum = expected.CardNum.Trim();                 // 16-digit primary key
        string acctId = expected.AcctId.ToString("D11");          // a format-valid 11-digit account filter
        string expYear = expected.ExpirationDate.Substring(0, 4); // CARD-EXPIRY-YEAR  (chars 1-4)
        string expMon = expected.ExpirationDate.Substring(5, 2);  // CARD-EXPIRY-MONTH (chars 6-7)

        // A re-entry COMMAREA (CDEMO-PGM-REENTER) NOT arriving from the card-list program, so 0000-MAIN takes
        // the WHEN CDEMO-PGM-REENTER branch: PERFORM 2000-PROCESS-INPUTS -> (no input error) -> 9000-READ-DATA.
        var ca = new CardDemoCommArea { FromTranId = "CCDL", FromProgram = "COMEN01C" };
        ca.SetReenter();

        // The operator keys both filters; COCRDSLC RECEIVEs the map then EVALUATEs EIBAID, so script ENTER.
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("ACCTSID").SetValue(acctId);
                map.Field("CARDSID").SetValue(cardNum);
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CCDL", AidKey.Enter, ca);

        // Pseudo-conversational re-drive: RETURN TRANSID('CCDL'). source: COCRDSLC.cbl:402-406.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CCDL", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("CCRDSLA", screen.LastSentMap!.Name);

        // The card master values are painted into the detail fields (the core read-and-display behaviour).
        Assert.Equal(expected.EmbossedName.TrimEnd(), screen.LastSentMap.Field("CRDNAME").Value.TrimEnd());
        Assert.Equal(expected.ActiveStatus.TrimEnd(), screen.LastSentMap.Field("CRDSTCD").Value.TrimEnd());
        Assert.Equal(expMon, screen.LastSentMap.Field("EXPMON").Value.TrimEnd());
        Assert.Equal(expYear, screen.LastSentMap.Field("EXPYEAR").Value.TrimEnd());

        // The echoed filters survive back onto the screen.
        Assert.Equal(acctId, screen.LastSentMap.Field("ACCTSID").Value.TrimEnd());
        Assert.Equal(cardNum, screen.LastSentMap.Field("CARDSID").Value.TrimEnd());

        // FOUND-CARDS-FOR-ACCOUNT emits this exact INFOMSG literal (3 leading spaces preserved). source: :129-130.
        Assert.Equal("   Displaying requested details", screen.LastSentMap.Field("INFOMSG").Value.TrimEnd());

        // No error message on the happy path (WS-RETURN-MSG stayed OFF). source: :494.
        Assert.Equal("", screen.LastSentMap.Field("ERRMSG").Value.Trim());
    }
}
