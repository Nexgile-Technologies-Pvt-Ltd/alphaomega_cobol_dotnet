using CardDemo.Online;
using CardDemo.Runtime;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    // ===============================================================================================
    //  CORPT00C — Transaction Reports (TRANSID CR00): report submit happy path
    // ===============================================================================================

    /// <summary>
    /// CORPT00C "Transaction Reports" submit happy path: a reenter ENTER turn with the <c>MONTHLY</c> radio
    /// keyed and <c>CONFIRM='Y'</c> drives PROCESS-ENTER-KEY down the Monthly branch — it computes the current
    /// month's start/end dates, builds the JCL job-stream, "writes" it to the JOBS TDQ via SUBMIT-JOB-TO-INTRDR,
    /// and then runs the success tail (IF NOT ERR-FLG-ON) which paints the green confirmation message and
    /// RETURNs TRANSID(CR00). This locks the core report-submit logic: the assertion checks the EXACT COBOL
    /// success literal (STRING WS-REPORT-NAME DELIMITED BY SPACE ' report submitted for printing ...') and the
    /// DFHGREEN colour override — both of which only appear when the program ran the full Monthly submit path
    /// without setting WS-ERR-FLG. If the confirm guard, the report-type EVALUATE, or the success tail were
    /// broken, ERRMSG would instead carry a "Please confirm to print the Monthly report..." prompt (red) or a
    /// different message, and the assertions would fail. source: CORPT00C.cbl:213-238,445-456,464-510
    /// </summary>
    [Fact]
    public void TransactionReportProgram_monthly_report_with_confirm_submits_and_shows_green_confirmation()
    {
        using var db = SeededDb();

        // A reenter COMMAREA (CDEMO-PGM-REENTER) so MAIN-PARA takes RECEIVE + EVALUATE EIBAID = DFHENTER ->
        // PROCESS-ENTER-KEY (the non-reenter path would just paint the empty form and RETURN). source: cbl:177-186
        var ca = new CardDemoCommArea { FromTranId = "CR00", FromProgram = "CORPT00C" };
        ca.SetReenter();

        // Key the Monthly radio (first non-blank report type wins the EVALUATE) and confirm with 'Y'. The
        // operator pressed ENTER. source: cbl:213, cbl:478-479
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("MONTHLY").SetValue("1");
                map.Field("CONFIRM").SetValue("Y");
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CR00", AidKey.Enter, ca);

        // SEND-TRNRPT-SCREEN ends with RETURN TRANSID(CR00); the program is pseudo-conversational. source: cbl:580-591
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CR00", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("CORPT0A", screen.LastSentMap!.Name);

        // The header was painted by POPULATE-HEADER-INFO on the SEND (proves we reached SEND-TRNRPT-SCREEN).
        Assert.Equal("CR00", screen.LastSentMap.Field("TRNNAME").Value.TrimEnd());     // source: cbl:615
        Assert.Equal("CORPT00C", screen.LastSentMap.Field("PGMNAME").Value.TrimEnd()); // source: cbl:616

        // The EXACT success message literal from the Monthly submit success tail (STRING ... DELIMITED BY SPACE).
        // This is only produced when WS-ERR-FLG stayed OFF through the whole Monthly + confirm path. source: cbl:449-452
        Assert.Equal("Monthly report submitted for printing ...",
            screen.LastSentMap.Field("ERRMSG").Value.TrimEnd());

        // MOVE DFHGREEN TO ERRMSGC — the success message is painted green (not the red error attribute). source: cbl:448
        Assert.Equal(BmsColor.Green, screen.LastSentMap.Field("ERRMSG").EffectiveColor);
    }
}
