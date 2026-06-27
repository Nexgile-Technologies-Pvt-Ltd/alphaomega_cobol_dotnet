using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

public sealed partial class OnlineTests
{
    /// <summary>
    /// COUSR03C (Delete User, TRANSID CU03) PF5 happy path: the admin keys an existing User ID and presses
    /// PF5 to delete. DELETE-USER-INFO does READ-USER-SEC-FILE (NORMAL) then DELETE-USER-SEC-FILE (NORMAL),
    /// which PERFORMs INITIALIZE-ALL-FIELDS and STRINGs the green confirmation
    /// <c>'User &lt;id&gt; has been deleted ...'</c> into WS-MESSAGE -> ERRMSGO. The row must be physically gone
    /// from USRSEC afterwards.
    /// <para>
    /// To avoid mutating any seeded master row, the test inserts its OWN throwaway user (key padded to the
    /// COBOL X(8) width, matching how the handler builds SEC-USR-ID via PadX(...,8)) directly through
    /// <see cref="UserSecurityRepository"/>, deletes it through the handler, then asserts the repository read
    /// returns RecordNotFound.
    /// </para>
    /// <para>
    /// This asserts the handler's CORE delete logic: it would FAIL if PF5 did not reach DELETE-USER-INFO,
    /// if the no-RIDFLD DELETE targeted the wrong key (FB-8: it re-reads whatever is in USRIDIN), or if the
    /// NORMAL branch failed to paint the exact "has been deleted" literal. Map fields used (BMS COUSR3A):
    /// input USRIDIN; output ERRMSG. Entity fields (CSUSR01Y): UsrId, FirstName, LastName, Pwd, UsrType.
    /// </para>
    /// </summary>
    [Fact]
    public void UserDeleteProgram_pf5_deletes_an_existing_user_from_usrsec_with_green_confirmation()
    {
        using var db = SeededDb();
        var repo = new UserSecurityRepository(db.Connection);

        // ---- seed our own throwaway row so no seeded master is disturbed -------------------------------
        const string keyedId = "DELME01";    // 7 chars, as the operator would key it
        const string storedKey = "DELME01 "; // X(8) padded — the form the handler's PadX(...,8) DELETE targets
        Assert.Equal(FileStatus.RecordNotFound, repo.ReadByKey(storedKey, out _));
        Assert.Equal(FileStatus.Ok, repo.Insert(new UserSecurity
        {
            UsrId = storedKey, FirstName = "Delete", LastName = "Victim", Pwd = "DELPW", UsrType = "U",
        }));
        Assert.Equal(FileStatus.Ok, repo.ReadByKey(storedKey, out _)); // precondition: it exists

        // ---- drive a reenter PF5 turn through CU03 -----------------------------------------------------
        // COUSR03C RECEIVEs first, then EVALUATEs EIBAID, so PF5 must be the AID the RECEIVE returns.
        var ca = MenuCommArea(admin: true);
        ca.SetReenter();
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Pf5,
            OnReceive = map => map.Field("USRIDIN").SetValue(keyedId),
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CU03", AidKey.Pf5, ca);

        // Pseudo-conversational re-drive: RETURN TRANSID('CU03'); no XCTL on the delete path.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CU03", outcome.TransId);

        // ---- the row is physically gone -----------------------------------------------------------------
        Assert.Equal(FileStatus.RecordNotFound, repo.ReadByKey(storedKey, out _));

        // ---- the exact green confirmation literal is painted into ERRMSG -------------------------------
        // STRING 'User ' + SEC-USR-ID (DELIMITED BY SPACE => right-trimmed id) + ' has been deleted ...'.
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("COUSR3A", screen.LastSentMap!.Name);
        Assert.Equal($"User {keyedId} has been deleted ...",
            screen.LastSentMap.Field("ERRMSG").Value.TrimEnd());
    }
}
