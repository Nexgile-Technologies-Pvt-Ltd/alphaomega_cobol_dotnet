using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    /// <summary>
    /// COCRDLIC (TRANSID CCLI) — the "List Credit Cards" paged grid. A reenter ENTER turn that arrives from
    /// this program itself (CDEMO-FROM-PROGRAM = COCRDLIC), with no filters keyed and no per-row selection,
    /// drives the EVALUATE-TRUE "WHEN OTHER" branch (// source: COCRDLIC.cbl:572-582): it MOVEs the (empty)
    /// WS-CA-FIRST-CARD-NUM into the browse RID — a LOW-VALUES start key = "from the first record" — and
    /// PERFORMs 9000-READ-FORWARD, which STARTBRs the CARD master GTEQ and READNEXTs forward, painting up to
    /// 7 cards (in ascending card_num order) into the row buffer, then 1000-SEND-MAP paints them onto CCRDLIA.
    ///
    /// This asserts that the FIRST card row painted on the grid (CRDNUM1/ACCTNO1/CRDSTS1) carries the master
    /// values of the lowest-keyed CARD row, that PAGENO shows page 1 (WS-CA-SCREEN-NUM ADD +1 on the first
    /// record — // source: COCRDLIC.cbl:1177-1178), and that the action-prompt INFOMSG literal
    /// "TYPE S FOR DETAIL, U TO UPDATE ANY RECORD" is shown (1400-SETUP-MESSAGE, CA-NEXT-PAGE-EXISTS branch —
    /// // source: COCRDLIC.cbl:115-116, 917-919). These fail if the forward browse / row-painting / paging
    /// core were broken.
    /// </summary>
    [Fact]
    public void Cocrdlic_reenter_enter_lists_first_card_page_from_card_master()
    {
        using var db = SeededDb();

        // The handler browses CARD by primary key ascending; the first painted row must equal the lowest
        // card_num master record. CardRepository.ReadAll() yields rows ORDER BY card_num ASC.
        var cards = new CardRepository(db.Connection);
        Card firstCard = cards.ReadAll().First();

        // A reenter COMMAREA whose FROM-PROGRAM is this program, so 0000-MAIN takes 2000-RECEIVE-MAP
        // (// source: COCRDLIC.cbl:357-362) and the EVALUATE dispatch reaches WHEN OTHER (no selection).
        var ca = new CardDemoCommArea { FromTranId = "CCLI", FromProgram = "COCRDLIC" };
        ca.SetUser();
        ca.SetReenter();

        // Plain ENTER, no filters keyed (ACCTSID/CARDSID blank), no row selection (CRDSEL1..7 blank).
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("ACCTSID").SetValue("");
                map.Field("CARDSID").SetValue("");
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CCLI", AidKey.Enter, ca);

        // Pseudo-conversational re-drive: RETURN TRANSID('CCLI') after painting the grid.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CCLI", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("CCRDLIA", screen.LastSentMap!.Name);

        // Row 1 is painted from the lowest-keyed CARD master record:
        //   CRDNUM1 = CARD-NUM (X16), ACCTNO1 = CARD-ACCT-ID zoned to 11, CRDSTS1 = CARD-ACTIVE-STATUS (X1).
        // (// source: COCRDLIC.cbl:1165-1171, 684-686.)
        Assert.Equal(firstCard.CardNum.TrimEnd(), screen.LastSentMap.Field("CRDNUM1").Value.TrimEnd());
        Assert.Equal(firstCard.AcctId.ToString("D11"), screen.LastSentMap.Field("ACCTNO1").Value.TrimEnd());
        Assert.Equal(firstCard.ActiveStatus.TrimEnd(), screen.LastSentMap.Field("CRDSTS1").Value.TrimEnd());

        // First record on a fresh list bumps WS-CA-SCREEN-NUM 0 -> 1, painted into PAGENO.
        // (// source: COCRDLIC.cbl:1177-1178, 667.)
        Assert.Equal("1", screen.LastSentMap.Field("PAGENO").Value.TrimEnd());

        // The action-prompt info literal is shown when there are records (CA-NEXT-PAGE-EXISTS).
        // (// source: COCRDLIC.cbl:115-116, 917-919, 928.)
        Assert.Equal("TYPE S FOR DETAIL, U TO UPDATE ANY RECORD",
            screen.LastSentMap.Field("INFOMSG").Value.TrimEnd());

        // The browse populated at least row 1 from the master (sanity: the grid is not empty / no
        // "NO RECORDS FOUND" error). (// source: COCRDLIC.cbl:121-122, 1241-1244.)
        Assert.DoesNotContain("NO RECORDS FOUND", screen.LastSentMap.Field("ERRMSG").Value);
    }
}
