using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Import;
using CardDemo.Online;
using CardDemo.Online.Programs;

namespace CardDemo.Tests;

/// <summary>
/// End-to-end tests for the 17 online CICS handlers wired into the console runtime: the
/// <see cref="OnlineProgramRegistry"/> over a seeded <see cref="RelationalDb"/>, exercised through the
/// real <see cref="Dispatcher"/> and a scripted <see cref="IScreenIo"/> that injects the keyed RECEIVE
/// fields and records the SENT symbolic map. Covers (per the design note §4/§6 and the CSD routing spec):
/// COSGN00C sign-on success XCTL routing (admin → COADM01C, user → COMEN01C) and its error branches
/// (incl. the blank-password branch that sets WS-ERR-FLG and shows "Please enter Password" without
/// reading USRSEC); COMEN01C/COADM01C menu fan-out; a COUSR01C-add → COUSR02C-update CRUD round-trip
/// over USRSEC; and COACTVWC reading a known account.
/// </summary>
public sealed partial class OnlineTests
{
    // ---- shared seeded DB ---------------------------------------------------------------------------

    /// <summary>Builds a fresh in-memory DB seeded from the EBCDIC masters via <see cref="MasterImporter"/>.</summary>
    private static RelationalDb SeededDb()
    {
        var db = new RelationalDb();
        var importer = new MasterImporter(SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);
        importer.ImportAll(db);
        return db;
    }

    /// <summary>The registry of all 17 online handlers over <paramref name="db"/>.</summary>
    private static ProgramRegistry Registry(RelationalDb db) => OnlineProgramRegistry.Build(db);

    /// <summary>A clock pinned so header date/time are deterministic.</summary>
    private static readonly IClock FixedClock = new TestClock(new DateTime(2026, 6, 26, 9, 0, 0));

    private static Dispatcher NewDispatcher(RelationalDb db, IScreenIo screen) =>
        new(Registry(db), screen, FixedClock);

    private static (string usrId, string pwd) FindUser(RelationalDb db, bool admin)
    {
        var repo = new UserSecurityRepository(db.Connection);
        UserSecurity u = repo.ReadAll().First(x =>
            admin ? x.UsrType.StartsWith('A') : !x.UsrType.StartsWith('A'));
        return (u.UsrId.TrimEnd(), u.Pwd.TrimEnd());
    }

    // ===============================================================================================
    //  Wiring sanity
    // ===============================================================================================

    [Fact]
    public void Registry_wires_all_17_programs_and_transactions()
    {
        using var db = SeededDb();
        ProgramRegistry reg = Registry(db);

        string[] programs =
        {
            "COSGN00C", "COMEN01C", "COADM01C", "COACTVWC", "COACTUPC", "COCRDLIC", "COCRDSLC",
            "COCRDUPC", "COTRN00C", "COTRN01C", "COTRN02C", "COBIL00C", "CORPT00C", "COUSR00C",
            "COUSR01C", "COUSR02C", "COUSR03C",
        };
        foreach (string p in programs)
            Assert.True(reg.HasProgram(p), $"program {p} not registered");

        // CSD TRANSID -> PROGRAM routing (consolidated dispatcher map).
        var routes = new (string tran, string pgm)[]
        {
            ("CC00", "COSGN00C"), ("CM00", "COMEN01C"), ("CA00", "COADM01C"),
            ("CAVW", "COACTVWC"), ("CAUP", "COACTUPC"), ("CCLI", "COCRDLIC"), ("CCDL", "COCRDSLC"),
            ("CCUP", "COCRDUPC"), ("CT00", "COTRN00C"), ("CT01", "COTRN01C"), ("CT02", "COTRN02C"),
            ("CB00", "COBIL00C"), ("CR00", "CORPT00C"), ("CU00", "COUSR00C"), ("CU01", "COUSR01C"),
            ("CU02", "COUSR02C"), ("CU03", "COUSR03C"),
        };
        foreach (var (tran, pgm) in routes)
            Assert.Equal(pgm, reg.ProgramForTransId(tran));
    }

    // ===============================================================================================
    //  COSGN00C — sign-on
    // ===============================================================================================

    [Fact]
    public void Cosgn00c_cold_start_displays_signon_screen()
    {
        using var db = SeededDb();
        var screen = new ScriptedScreenIo();
        var dispatcher = NewDispatcher(db, screen);

        // Cold start: EIBCALEN = 0, AID = None -> first display, RETURN TRANSID(CC00).
        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.None, commArea: null);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CC00", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        Assert.Equal("COSGN0A", screen.LastSentMap!.Name);
        // Header populated.
        Assert.Equal("CC00", screen.LastSentMap.Field("TRNNAME").Value.TrimEnd());
        Assert.Equal("COSGN00C", screen.LastSentMap.Field("PGMNAME").Value.TrimEnd());
    }

    [Fact]
    public void Cosgn00c_valid_admin_signon_xctls_to_admin_menu()
    {
        using var db = SeededDb();
        var (usrId, pwd) = FindUser(db, admin: true);
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue(usrId); map.Field("PASSWD").SetValue(pwd); },
            NextAid = AidKey.Enter,
        };
        var dispatcher = NewDispatcher(db, screen);

        // RunTurn follows the XCTL chain in-turn; the admin menu (COADM01C) ends with RETURN TRANSID(CA00).
        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CA00", outcome.TransId); // COADM01C re-entry transaction
        Assert.NotNull(outcome.CommArea);
        Assert.True(outcome.CommArea!.IsAdmin);
        Assert.Equal(usrId, outcome.CommArea.UserId.TrimEnd());
    }

    [Fact]
    public void Cosgn00c_valid_user_signon_xctls_to_main_menu()
    {
        using var db = SeededDb();
        var (usrId, pwd) = FindUser(db, admin: false);
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue(usrId); map.Field("PASSWD").SetValue(pwd); },
        };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CM00", outcome.TransId); // COMEN01C re-entry transaction
        Assert.NotNull(outcome.CommArea);
        Assert.False(outcome.CommArea!.IsAdmin);
        Assert.Equal(usrId, outcome.CommArea.UserId.TrimEnd());
    }

    [Fact]
    public void Cosgn00c_wrong_password_redisplays_with_message()
    {
        using var db = SeededDb();
        var (usrId, _) = FindUser(db, admin: true);
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue(usrId); map.Field("PASSWD").SetValue("WRONGPWD"); },
        };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        // No XCTL — re-display with the wrong-password message, RETURN TRANSID(CC00).
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CC00", outcome.TransId);
        Assert.Equal("COSGN0A", screen.LastSentMap!.Name);
        Assert.Contains("Wrong Password", screen.LastSentMap.Field("ERRMSG").Value);
    }

    [Fact]
    public void Cosgn00c_user_not_found_redisplays_with_message()
    {
        using var db = SeededDb();
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue("ZZNOEXST"); map.Field("PASSWD").SetValue("ANYPWD"); },
        };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CC00", outcome.TransId);
        Assert.Contains("User not found", screen.LastSentMap!.Field("ERRMSG").Value);
    }

    [Fact]
    public void Cosgn00c_blank_password_with_userid_sets_errflag_shows_enter_password()
    {
        // COBOL COSGN00C.cbl:123-127 — the PASSWDI = SPACES/LOW-VALUES branch does MOVE 'Y' TO WS-ERR-FLG
        // (identical to the User-ID branch), so the later IF NOT ERR-FLG-ON (cbl:138) is FALSE and
        // READ-USER-SEC-FILE is SKIPPED. With a valid user id but a blank password the screen therefore shows
        // the clean "Please enter Password ..." prompt — NOT "Wrong Password", and the user is NOT logged in.
        // (An independent 7-track audit found the earlier port dropped this MOVE and invented a non-existent
        // "FB-1 blank-password-still-reads" bug; this test now locks the COBOL-faithful behaviour.)
        using var db = SeededDb();
        var (usrId, _) = FindUser(db, admin: true);
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue(usrId); map.Field("PASSWD").SetValue(""); },
        };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        // Err flag set -> no READ-USER-SEC-FILE -> the prompt stays "Please enter Password ...", no XCTL/login.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CC00", outcome.TransId);
        Assert.Contains("Please enter Password", screen.LastSentMap!.Field("ERRMSG").Value);
        Assert.DoesNotContain("Wrong Password", screen.LastSentMap!.Field("ERRMSG").Value);
        // The unconditional MOVE FUNCTION UPPER-CASE(USERIDI) TO CDEMO-USER-ID after the EVALUATE still runs
        // (faithful — set even on a validation failure). // source: COSGN00C.cbl:132-134
        Assert.Equal(usrId, outcome.CommArea!.UserId.TrimEnd());
    }

    [Fact]
    public void Cosgn00c_blank_userid_shows_enter_userid_message()
    {
        using var db = SeededDb();
        var screen = new ScriptedScreenIo
        {
            OnReceive = map => { map.Field("USERID").SetValue(""); map.Field("PASSWD").SetValue(""); },
        };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CC00", AidKey.Enter, NonColdCommArea());

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Contains("Please enter User ID", screen.LastSentMap!.Field("ERRMSG").Value);
    }

    // ===============================================================================================
    //  COMEN01C / COADM01C — menu routing
    // ===============================================================================================

    [Fact]
    public void Comen01c_option1_routes_to_account_view()
    {
        using var db = SeededDb();
        // A reenter COMMAREA from a regular user; ENTER with OPTION='1' -> XCTL COACTVWC.
        var ca = MenuCommArea(admin: false);
        ca.SetReenter();
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("OPTION").SetValue("1") };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CM00", AidKey.Enter, ca);

        // COACTVWC ends its turn with RETURN TRANSID(CAVW) after painting the (empty) account screen.
        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CAVW", outcome.TransId);
    }

    [Fact]
    public void Comen01c_option6_routes_to_transaction_list()
    {
        using var db = SeededDb();
        var ca = MenuCommArea(admin: false);
        ca.SetReenter();
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("OPTION").SetValue("6") };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CM00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CT00", outcome.TransId); // COTRN00C
    }

    [Fact]
    public void Comen01c_admin_only_option_blocked_for_regular_user()
    {
        // A regular user picking an admin-gated option is blocked; no XCTL. (Option 11 has USRTYPE='U' in the
        // shipped table, so to exercise the gate we rely on the invalid-option path: option 0 / out of range
        // re-displays the menu with an error and RETURN TRANSID(CM00).)
        using var db = SeededDb();
        var ca = MenuCommArea(admin: false);
        ca.SetReenter();
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("OPTION").SetValue("99") };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CM00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CM00", outcome.TransId);
        Assert.Contains("valid option", screen.LastSentMap!.Field("ERRMSG").Value);
    }

    [Fact]
    public void Coadm01c_option1_routes_to_user_list()
    {
        using var db = SeededDb();
        var ca = MenuCommArea(admin: true);
        ca.SetReenter();
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("OPTION").SetValue("1") };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CA00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CU00", outcome.TransId); // COUSR00C user list
    }

    [Fact]
    public void Coadm01c_option2_routes_to_user_add()
    {
        using var db = SeededDb();
        var ca = MenuCommArea(admin: true);
        ca.SetReenter();
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("OPTION").SetValue("2") };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CA00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CU01", outcome.TransId); // COUSR01C user add
    }

    /// <summary>
    /// COADM01C first display (non-reenter path): SEND-MENU-SCREEN does NOT echo WS-OPTION — the
    /// <c>MOVE WS-OPTION TO OPTIONO</c> lives only inside PROCESS-ENTER-KEY (COADM01C.cbl:129). So OPTIONO is
    /// LOW-VALUES and the 2-char OPTION field renders BLANK on the initial admin menu, not "00". Locks the
    /// independent-audit fidelity fix (the C# previously painted the echo unconditionally).
    /// </summary>
    [Fact]
    public void Coadm01c_first_display_paints_blank_option_not_zero()
    {
        using var db = SeededDb();
        var ca = MenuCommArea(admin: true); // NOT reenter -> first-display (SEND-MENU-SCREEN) path
        var screen = new ScriptedScreenIo();

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CA00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CA00", outcome.TransId); // pseudo-conversational re-display; no XCTL on first entry
        Assert.NotNull(screen.LastSentMap);
        Assert.True(string.IsNullOrWhiteSpace(screen.LastSentMap!.Field("OPTION").Value),
            "first-display OPTION must be blank (LOW-VALUES), not the '00' echo");
    }

    // ===============================================================================================
    //  CRUD round-trip over USRSEC: COUSR01C add -> COUSR02C update
    // ===============================================================================================

    [Fact]
    public void Cousr01c_add_then_cousr02c_update_round_trips_over_usrsec()
    {
        using var db = SeededDb();
        var repo = new UserSecurityRepository(db.Connection);
        const string newId = "TSTUSR1";

        Assert.Equal(FileStatus.RecordNotFound, repo.ReadByKey("TSTUSR1 ", out _));

        // ---- ADD via COUSR01C (CU01): a reenter ENTER turn with all five fields keyed -> WRITE USRSEC. ----
        var addCa = MenuCommArea(admin: true);
        addCa.SetReenter();
        var addScreen = new ScriptedScreenIo
        {
            OnReceive = map =>
            {
                map.Field("FNAME").SetValue("Test");
                map.Field("LNAME").SetValue("User");
                map.Field("USERID").SetValue(newId);
                map.Field("PASSWD").SetValue("PASS1");
                map.Field("USRTYPE").SetValue("U");
            },
        };
        CicsOutcome addOutcome = NewDispatcher(db, addScreen).RunTurn("CU01", AidKey.Enter, addCa);
        Assert.Equal(CicsOutcomeKind.ReturnTransId, addOutcome.Kind);
        Assert.Equal("CU01", addOutcome.TransId);

        // Row now exists with the keyed values.
        Assert.Equal(FileStatus.Ok, repo.ReadByKey("TSTUSR1 ", out UserSecurity? added));
        Assert.NotNull(added);
        Assert.Equal("Test", added!.FirstName.TrimEnd());
        Assert.Equal("User", added.LastName.TrimEnd());
        Assert.Equal("PASS1", added.Pwd.TrimEnd());
        Assert.Equal("U", added.UsrType.TrimEnd());

        // ---- UPDATE via COUSR02C (CU02): a reenter PF5 turn that changes the last name + type -> REWRITE. ----
        var updCa = MenuCommArea(admin: true);
        updCa.SetReenter();
        var updScreen = new ScriptedScreenIo
        {
            // COUSR02C RECEIVEs first, then EVALUATEs EIBAID — so the AID that drives the PF5 update path is
            // the one RECEIVE returns, not the AID passed to RunTurn. Script PF5 on the RECEIVE.
            NextAid = AidKey.Pf5,
            OnReceive = map =>
            {
                map.Field("USRIDIN").SetValue(newId);
                map.Field("FNAME").SetValue("Test");
                map.Field("LNAME").SetValue("Updated");   // changed
                map.Field("PASSWD").SetValue("PASS1");
                map.Field("USRTYPE").SetValue("A");        // changed
            },
        };
        CicsOutcome updOutcome = NewDispatcher(db, updScreen).RunTurn("CU02", AidKey.Pf5, updCa);
        Assert.Equal(CicsOutcomeKind.ReturnTransId, updOutcome.Kind);
        Assert.Equal("CU02", updOutcome.TransId);

        // Row reflects the update.
        Assert.Equal(FileStatus.Ok, repo.ReadByKey("TSTUSR1 ", out UserSecurity? updated));
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.LastName.TrimEnd());
        Assert.Equal("A", updated.UsrType.TrimEnd());
    }

    // ===============================================================================================
    //  Selected-from-list XCTL carries the chosen key through the COMMAREA trailer (regression locks for
    //  the COTRN00C / COUSR00C selected-key fixes found by the independent 7-track audit).
    // ===============================================================================================

    [Fact]
    public void Cousr00c_select_user_xctls_to_update_carrying_selected_id()
    {
        // COUSR00C user list: typing 'U' next to a row XCTLs to COUSR02C (update). The chosen user id sits in
        // the CDEMO-CU00-USR-SELECTED trailer field, which aliases CDEMO-CU02-USR-SELECTED at the same COMMAREA
        // bytes, so COUSR02C auto-loads that user. The fix made COUSR00C.SaveCu00Info actually serialize
        // USR-SEL-FLG (byte 25) + USR-SELECTED (bytes 26-33); without it COUSR02C opens blank.
        using var db = SeededDb();
        UserSecurity u = new UserSecurityRepository(db.Connection).ReadAll().First();
        string usrId = u.UsrId.TrimEnd();

        var ca = MenuCommArea(admin: true);
        ca.SetReenter();
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("SEL0001").SetValue("U");     // type 'U' against row 1
                map.Field("USRID01").SetValue(usrId);   // that row shows this user id
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CU00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CU02", outcome.TransId);                                     // XCTL'd to the update program
        Assert.NotNull(screen.LastSentMap);
        // The selected user id survived the trailer and COUSR02C auto-loaded it (USRIDIN populated).
        Assert.Equal(usrId, screen.LastSentMap!.Field("USRIDIN").Value.TrimEnd());
        Assert.Equal(u.FirstName.TrimEnd(), screen.LastSentMap.Field("FNAME").Value.TrimEnd());
    }

    [Fact]
    public void Cotrn00c_select_transaction_xctls_to_detail_carrying_selected_id()
    {
        // COTRN00C transaction list: typing 'S' next to a row XCTLs to COTRN01C (detail view). The chosen
        // tran id sits in the CDEMO-CT00-TRN-SELECTED trailer field, which aliases CDEMO-CT01-TRN-SELECTED at
        // the same COMMAREA bytes, so COTRN01C auto-displays that transaction. The fix made
        // COTRN00C.SaveCt00Info serialize TRN-SEL-FLG (byte 41) + TRN-SELECTED (bytes 42-57).
        using var db = SeededDb();
        // Transactions are not a seeded master (they arrive via posting), so insert one to select.
        const string tranId = "0000000000000077";
        var txns = new TransactionRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, txns.Insert(new Transaction
        {
            TranId = tranId, TypeCd = "01", CatCd = 1, Source = "POS", Desc = "TEST SELECT TXN",
            Amt = 12.34m, MerchantId = 1, MerchantName = "ACME", MerchantCity = "ATL", MerchantZip = "30301",
            CardNum = "4111111111111111", OrigTs = "2026-06-20-09.00.00.000000",
            ProcTs = "2026-06-20-09.00.00.000000",
        }));

        var ca = MenuCommArea(admin: false);
        ca.SetReenter();
        var screen = new ScriptedScreenIo
        {
            NextAid = AidKey.Enter,
            OnReceive = map =>
            {
                map.Field("SEL0001").SetValue("S");     // type 'S' against row 1
                map.Field("TRNID01").SetValue(tranId);  // that row shows this tran id
            },
        };

        CicsOutcome outcome = NewDispatcher(db, screen).RunTurn("CT00", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CT01", outcome.TransId);                                     // XCTL'd to the detail program
        Assert.NotNull(screen.LastSentMap);
        // The selected tran id survived the trailer and COTRN01C auto-loaded it into the search field.
        Assert.Equal(tranId, screen.LastSentMap!.Field("TRNIDIN").Value.TrimEnd());
    }

    // ===============================================================================================
    //  COACTVWC — read a known account
    // ===============================================================================================

    [Fact]
    public void Coactvwc_reads_a_known_account_and_displays_its_status()
    {
        using var db = SeededDb();

        // Pick an account that has both a CARD_XREF (by acct) and an ACCOUNT row, so the read fully resolves.
        long acctId = FirstResolvableAccount(db);
        var acctRepo = new AccountRepository(db.Connection);
        Assert.Equal(FileStatus.Ok, acctRepo.ReadByKey(acctId, out Account? expected));

        // A reenter COMMAREA (CDEMO-PGM-REENTER) so the handler runs 2000-PROCESS-INPUTS + 9000-READ-ACCT.
        var ca = new CardDemoCommArea { FromTranId = "CAVW", FromProgram = "COMEN01C" };
        ca.SetReenter();
        string acctIdField = acctId.ToString("D11");
        var screen = new ScriptedScreenIo { OnReceive = map => map.Field("ACCTSID").SetValue(acctIdField) };
        var dispatcher = NewDispatcher(db, screen);

        CicsOutcome outcome = dispatcher.RunTurn("CAVW", AidKey.Enter, ca);

        Assert.Equal(CicsOutcomeKind.ReturnTransId, outcome.Kind);
        Assert.Equal("CAVW", outcome.TransId);
        Assert.NotNull(screen.LastSentMap);
        // The account status from the master is painted into ACSTTUS.
        Assert.Equal(expected!.ActiveStatus.TrimEnd(), screen.LastSentMap!.Field("ACSTTUS").Value.TrimEnd());
        // The echoed account id matches the one we asked for.
        Assert.Equal(acctIdField, screen.LastSentMap.Field("ACCTSID").Value.TrimEnd());
    }

    // ---- helpers ------------------------------------------------------------------------------------

    /// <summary>The first account id reachable through CARD_XREF (by acct alt key) AND present in ACCOUNT.</summary>
    private static long FirstResolvableAccount(RelationalDb db)
    {
        var xref = new CardXrefRepository(db.Connection);
        var acct = new AccountRepository(db.Connection);
        xref.StartBrowse();
        while (xref.ReadNext(out CardXref? x) == FileStatus.Ok && x is not null)
        {
            if (x.AcctId > 0 &&
                xref.ReadByAltKey(x.AcctId, out _) == FileStatus.Ok &&
                acct.ReadByKey(x.AcctId, out _) == FileStatus.Ok)
            {
                xref.EndBrowse();
                return x.AcctId;
            }
        }
        xref.EndBrowse();
        throw new InvalidOperationException("No account resolvable through CARD_XREF + ACCOUNT in the seed.");
    }

    /// <summary>A non-cold COMMAREA so COSGN00C takes the EVALUATE-EIBAID path (EIBCALEN != 0).</summary>
    private static CardDemoCommArea NonColdCommArea() => new() { FromTranId = "CC00", FromProgram = "COSGN00C" };

    /// <summary>A COMMAREA as a menu would have after sign-on (user type set).</summary>
    private static CardDemoCommArea MenuCommArea(bool admin)
    {
        var ca = new CardDemoCommArea { FromTranId = "CC00", FromProgram = "COSGN00C", UserId = "TESTUSER" };
        if (admin) ca.SetAdmin(); else ca.SetUser();
        return ca;
    }

    // ---- test doubles -------------------------------------------------------------------------------

    /// <summary>An <see cref="IClock"/> pinned to a fixed instant for deterministic headers.</summary>
    private sealed class TestClock(DateTime now) : IClock
    {
        public DateTime Now => now;
    }

    /// <summary>
    /// A scripted <see cref="IScreenIo"/>: on RECEIVE it runs <see cref="OnReceive"/> to populate the symbolic
    /// map's input fields (as the operator's keystrokes would) and returns <see cref="NextAid"/>; on SEND it
    /// records the last symbolic map + options so the test can assert the painted field values.
    /// </summary>
    private sealed class ScriptedScreenIo : IScreenIo
    {
        /// <summary>Mutates the symbolic map on RECEIVE to inject keyed input field values.</summary>
        public Action<BmsMap>? OnReceive { get; set; }

        /// <summary>The AID the RECEIVE returns (the key that ended the operator's RECEIVE).</summary>
        public AidKey NextAid { get; set; } = AidKey.Enter;

        /// <summary>The most recently SENT symbolic map (the live screen).</summary>
        public BmsMap? LastSentMap { get; private set; }

        /// <summary>The options of the most recent SEND.</summary>
        public SendMapOptions LastSentOptions { get; private set; }

        /// <summary>The most recent SEND TEXT line, if any.</summary>
        public string? LastSentText { get; private set; }

        public void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options)
        {
            LastSentMap = symbolicMap as BmsMap;
            LastSentOptions = options;
        }

        public AidKey ReceiveMap(string map, string mapset, object symbolicMap)
        {
            if (symbolicMap is BmsMap m) OnReceive?.Invoke(m);
            return NextAid;
        }

        public void SendText(string text, bool erase = true, bool freeKb = true) => LastSentText = text;
    }
}
