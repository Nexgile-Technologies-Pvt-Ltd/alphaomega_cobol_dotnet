using CardDemo.Runtime;
using CardDemo.Data;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of CICS COBOL program <c>COMEN01C</c> — the Main Menu for the regular
/// (non-admin) CardDemo users (TRANSID <c>CM00</c>, BMS map <c>COMEN1A</c> / mapset <c>COMEN01</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method (with the original paragraph name and a <c>// source:</c> citation), the
/// <c>MAIN-PARA</c> dispatch on <c>EIBCALEN</c> / <c>EIBAID</c> is preserved exactly, and every
/// validation message is reproduced verbatim. The program performs <b>no</b> data I/O (the
/// <c>WS-USRSEC-FILE</c> declaration is dead working storage), so it carries no repository; the
/// <see cref="RelationalDb"/> constructor argument is accepted only to satisfy the uniform online
/// handler factory contract.
/// </para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — <c>PROCESS-ENTER-KEY</c> has no early exit after a validation <c>PERFORM
/// SEND-MENU-SCREEN</c>; control falls through to the admin-only check and then to
/// <c>IF NOT ERR-FLG-ON</c> (which gates dispatch). Statement order and the unguarded subscript are
/// preserved.</item>
/// <item>FB-2 — <c>MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM</c> is issued twice in the OTHER dispatch
/// branch (two identical consecutive statements). Harmless but reproduced.</item>
/// <item>FB-3 — the option subscript can reach <c>WS-OPTION = 0</c> or the unpopulated 12th OCCURS
/// slot because the admin-only check at :136-143 runs even when the range check already flagged an
/// error. The flat 12-slot option table mirrors this: slot 12 is an empty record and a 0/out-of-range
/// subscript resolves to an empty, non-'A' record (deterministic stand-in for COBOL's undefined
/// subscript-0 storage; see <c>OptionAt</c>).</item>
/// <item>FB-4 — every shipped option's user-type is <c>'U'</c>, so the "No access - Admin Only
/// option..." branch is dead code; it is kept verbatim.</item>
/// <item>FB-5 — <c>WS-MESSAGE</c> is X(80) but the map field <c>ERRMSGO</c> is X(78); the assignment
/// clamps to 78 chars (<see cref="SetErrMsg"/>).</item>
/// <item>FB-6 — the <c>WS-OPTION IS NOT NUMERIC</c> disjunct is always false after the
/// space-&gt;'0' INSPECT + MOVE into PIC 9(02); the redundant test is kept.</item>
/// <item>FB-7 — only option 11 (<c>COPAUS0C</c>) is gated by an <c>INQUIRE PROGRAM</c> installed-check;
/// options 1-10 XCTL unconditionally. The asymmetry is reproduced.</item>
/// </list>
/// </remarks>
public sealed class MainMenuProgram : ITransactionHandler
{
    // === WS-VARIABLES (WORKING-STORAGE) — COBOL VALUE clauses. source: COMEN01C.cbl:35-48 ===

    private const string WS_PGMNAME = "COMEN01C"; // source: COMEN01C.cbl:36
    private const string WS_TRANID = "CM00";      // source: COMEN01C.cbl:37

    // WS-ERR-FLG PIC X(01): 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: COMEN01C.cbl:40-42
    private string _wsErrFlg = "N";
    private bool ErrFlgOn => _wsErrFlg == "Y";    // 88 ERR-FLG-ON

    private string _wsMessage = "";               // WS-MESSAGE PIC X(80). source: COMEN01C.cbl:38
    private int _wsOption;                         // WS-OPTION PIC 9(02). source: COMEN01C.cbl:46

    // Per-turn override of the ERRMSG colour byte (ERRMSGC), used by the not-installed / coming-soon
    // branches (MOVE DFHRED / DFHGREEN TO ERRMSGC OF COMEN1AO). Null = use the map's default RED.
    private BmsColor? _errMsgColor;

    // === Constants from the copybooks referenced by the program ===

    /// <summary>CCDA-TITLE01 PIC X(40). source: COTTL01Y.cpy:18-20.</summary>
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";

    /// <summary>CCDA-TITLE02 PIC X(40). source: COTTL01Y.cpy:21-23.</summary>
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    /// <summary>CCDA-MSG-INVALID-KEY PIC X(50). source: CSMSG01Y.cpy:20-21.</summary>
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    /// <summary>CDEMO-MENU-OPT-COUNT PIC 9(02) VALUE 11. source: COMEN02Y.cpy:21.</summary>
    private const int CDEMO_MENU_OPT_COUNT = 11;

    // === The CARDDEMO-MAIN-MENU-OPTIONS table (copybook COMEN02Y, REDEFINES gives 12 OCCURS slots) ===
    // 11 populated rows + an empty 12th slot. source: COMEN02Y.cpy:23-98.
    private readonly record struct MenuOpt(int Num, string Name, string PgmName, string UsrType);

    private static readonly MenuOpt[] CdemoMenuOpt =
    {
        new( 1, "Account View                       ", "COACTVWC", "U"), // source: COMEN02Y.cpy:25-28
        new( 2, "Account Update                     ", "COACTUPC", "U"), // source: COMEN02Y.cpy:31-34
        new( 3, "Credit Card List                   ", "COCRDLIC", "U"), // source: COMEN02Y.cpy:37-40
        new( 4, "Credit Card View                   ", "COCRDSLC", "U"), // source: COMEN02Y.cpy:43-46
        new( 5, "Credit Card Update                 ", "COCRDUPC", "U"), // source: COMEN02Y.cpy:49-52
        new( 6, "Transaction List                   ", "COTRN00C", "U"), // source: COMEN02Y.cpy:55-58
        new( 7, "Transaction View                   ", "COTRN01C", "U"), // source: COMEN02Y.cpy:61-64
        new( 8, "Transaction Add                    ", "COTRN02C", "U"), // source: COMEN02Y.cpy:67-72 (FB-4)
        new( 9, "Transaction Reports                ", "CORPT00C", "U"), // source: COMEN02Y.cpy:75-78
        new(10, "Bill Payment                       ", "COBIL00C", "U"), // source: COMEN02Y.cpy:81-84
        new(11, "Pending Authorization View         ", "COPAUS0C", "U"), // source: COMEN02Y.cpy:87-90
        new( 0, "",                                    "",         ""),  // OCCURS slot 12: unpopulated FILLER
    };

    /// <summary>
    /// 1-based subscript into <see cref="CdemoMenuOpt"/> matching COBOL <c>CDEMO-MENU-OPT(idx)</c>.
    /// A subscript of 0 or beyond the 12-slot table resolves to an empty record — the deterministic
    /// stand-in for COBOL's undefined subscript-0 / past-end storage (FB-1/FB-3). The value never
    /// equals <c>'A'</c>, so the admin guard stays false exactly as the shipped data behaves.
    /// </summary>
    private static MenuOpt OptionAt(int idx) =>
        idx >= 1 && idx <= CdemoMenuOpt.Length ? CdemoMenuOpt[idx - 1] : EmptyOpt;

    /// <summary>The empty record returned for a 0 / out-of-range subscript (non-null fields).</summary>
    private static readonly MenuOpt EmptyOpt = new(0, "", "", "");

    // === CARDDEMO-COMMAREA (typed view) restored each turn; mirrors COCOM01Y. ===
    private CardDemoCommArea _commArea = new();

    /// <summary>
    /// Factory-friendly constructor. <paramref name="db"/> is accepted for a uniform handler signature;
    /// COMEN01C performs no data I/O so no repository is created. The argument is intentionally unused.
    /// </summary>
    public MainMenuProgram(RelationalDb db)
    {
        _ = db; // COMEN01C has no VSAM/SQL access (WS-USRSEC-FILE is dead). source: COMEN01C.cbl:39, spec §2
    }

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public MainMenuProgram()
    {
    }

    public string ProgramName => "COMEN01C"; // PROGRAM-ID. source: COMEN01C.cbl:23
    public string TransId => "CM00";         // CSD TRANSACTION(CM00). source: CSD_TRANSACTIONS.md, COMEN01C.cbl:37

    /// <summary>
    /// The CICS <c>INQUIRE PROGRAM</c> "is the program installed" probe used by the COPAUS0C branch
    /// (source: COMEN01C.cbl:148-151). COPAUS0C is shipped, so it is treated as installed by default —
    /// option 11 XCTLs. Overridable so the "not installed" branch (FB-7) stays reachable in tests.
    /// </summary>
    public Func<string, bool> InquireProgram { get; set; } = _ => true;

    // === MAIN-PARA. source: COMEN01C.cbl:75-110 ===
    public void Handle(CicsContext ctx)
    {
        MainPara(ctx);
    }

    private void MainPara(CicsContext ctx)
    {
        // SET ERR-FLG-OFF TO TRUE. source: COMEN01C.cbl:77
        _wsErrFlg = "N";

        // MOVE SPACES TO WS-MESSAGE, ERRMSGO OF COMEN1AO. source: COMEN01C.cbl:79-80
        _wsMessage = "";
        _errMsgColor = null;

        if (ctx.EibCalen == 0) // IF EIBCALEN = 0. source: COMEN01C.cbl:82
        {
            // MOVE 'COSGN00C' TO CDEMO-FROM-PROGRAM. source: COMEN01C.cbl:83
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.FromProgram = "COSGN00C";
            // PERFORM RETURN-TO-SIGNON-SCREEN. source: COMEN01C.cbl:84
            ReturnToSignonScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: COMEN01C.cbl:86
            _commArea = ctx.CommArea!;

            if (!_commArea.IsReenter) // IF NOT CDEMO-PGM-REENTER. source: COMEN01C.cbl:87
            {
                _commArea.SetReenter();         // SET CDEMO-PGM-REENTER TO TRUE. source: COMEN01C.cbl:88
                // MOVE LOW-VALUES TO COMEN1AO (fresh symbolic out-map). source: COMEN01C.cbl:89
                SendMenuScreen(ctx);            // PERFORM SEND-MENU-SCREEN. source: COMEN01C.cbl:90
            }
            else
            {
                ReceiveMenuScreen(ctx);         // PERFORM RECEIVE-MENU-SCREEN. source: COMEN01C.cbl:92

                // EVALUATE EIBAID. source: COMEN01C.cbl:93-103
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:          // WHEN DFHENTER. source: COMEN01C.cbl:94
                        ProcessEnterKey(ctx);   // PERFORM PROCESS-ENTER-KEY. source: COMEN01C.cbl:95
                        break;

                    case AidKey.Pf3:            // WHEN DFHPF3. source: COMEN01C.cbl:96
                        _commArea.ToProgram = "COSGN00C"; // MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM. source: :97
                        ReturnToSignonScreen(ctx);        // PERFORM RETURN-TO-SIGNON-SCREEN. source: :98
                        break;

                    default:                    // WHEN OTHER. source: COMEN01C.cbl:99
                        _wsErrFlg = "Y";                       // MOVE 'Y' TO WS-ERR-FLG. source: :100
                        _wsMessage = CCDA_MSG_INVALID_KEY;     // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. source: :101
                        SendMenuScreen(ctx);                   // PERFORM SEND-MENU-SCREEN. source: :102
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: COMEN01C.cbl:107-110
        // Issued only when the handler did not already XCTL away (RETURN-TO-SIGNON-SCREEN /
        // PROCESS-ENTER-KEY may XCTL, which records the terminating outcome). CICS would never reach the
        // RETURN after an XCTL; mirror that by not overwriting an existing XCTL outcome.
        if (ctx.Outcome is null)
            ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // === PROCESS-ENTER-KEY. source: COMEN01C.cbl:115-191 ===
    private void ProcessEnterKey(CicsContext ctx)
    {
        // OPTIONI OF COMEN1AI — the 2-char keyed option field, as received (not trimmed).
        string optionI = OptionInput(ctx);

        // PERFORM VARYING WS-IDX FROM LENGTH OF OPTIONI BY -1 UNTIL
        //   OPTIONI(WS-IDX:1) NOT = SPACES OR WS-IDX = 1. source: COMEN01C.cbl:117-121
        int wsIdx = optionI.Length; // LENGTH OF OPTIONI = 2
        while (!(CharAt(optionI, wsIdx) != ' ' || wsIdx == 1))
            wsIdx--;

        // MOVE OPTIONI(1:WS-IDX) TO WS-OPTION-X (PIC X(02) JUST RIGHT). source: COMEN01C.cbl:122
        string wsOptionX = JustRight(Substr(optionI, 1, wsIdx), 2);
        // INSPECT WS-OPTION-X REPLACING ALL ' ' BY '0'. source: COMEN01C.cbl:123
        wsOptionX = wsOptionX.Replace(' ', '0');
        // MOVE WS-OPTION-X TO WS-OPTION (alphanumeric -> PIC 9(02)). source: COMEN01C.cbl:124
        _wsOption = ToNumeric2(wsOptionX);
        // MOVE WS-OPTION TO OPTIONO OF COMEN1AO (echo, zero-padded). source: COMEN01C.cbl:125
        // (Applied in BUILD-MENU-OPTIONS/SEND time via OptionEcho.)

        // IF WS-OPTION IS NOT NUMERIC OR WS-OPTION > CDEMO-MENU-OPT-COUNT OR WS-OPTION = ZEROS.
        // FB-6: the IS NOT NUMERIC disjunct is always false after the MOVE; kept verbatim.
        // source: COMEN01C.cbl:127-134
        if (IsNotNumeric(_wsOption) || _wsOption > CDEMO_MENU_OPT_COUNT || _wsOption == 0)
        {
            _wsErrFlg = "Y";                                             // MOVE 'Y' TO WS-ERR-FLG. source: :130
            _wsMessage = "Please enter a valid option number...";       // source: :131-132
            SendMenuScreen(ctx);                                        // PERFORM SEND-MENU-SCREEN. source: :133
            // FB-1: NO early exit — fall through to the admin check and the IF NOT ERR-FLG-ON guard.
        }

        // IF CDEMO-USRTYP-USER AND CDEMO-MENU-OPT-USRTYPE(WS-OPTION) = 'A'. source: COMEN01C.cbl:136-137
        // FB-3: WS-OPTION may be 0 / out of range here (no re-guard); OptionAt() resolves it to an
        // empty, non-'A' slot, so the guard stays false exactly as the shipped table behaves (FB-4).
        if (_commArea.IsUser && OptionAt(_wsOption).UsrType == "A")
        {
            _wsErrFlg = "Y";                                       // SET ERR-FLG-ON TO TRUE. source: :138
            _wsMessage = "";                                       // MOVE SPACES TO WS-MESSAGE. source: :139
            _wsMessage = "No access - Admin Only option... ";      // source: :140-141 (trailing space)
            SendMenuScreen(ctx);                                   // PERFORM SEND-MENU-SCREEN. source: :142
        }

        // IF NOT ERR-FLG-ON. source: COMEN01C.cbl:145
        if (!ErrFlgOn)
        {
            MenuOpt opt = OptionAt(_wsOption);

            // EVALUATE TRUE. source: COMEN01C.cbl:146-188
            if (opt.PgmName == "COPAUS0C")
            {
                // WHEN CDEMO-MENU-OPT-PGMNAME(WS-OPTION) = 'COPAUS0C'. source: :147
                // EXEC CICS INQUIRE PROGRAM(...) NOHANDLE. source: :148-151
                bool installed = InquireProgram(opt.PgmName); // EIBRESP = DFHRESP(NORMAL) when installed
                if (installed) // IF EIBRESP = DFHRESP(NORMAL). source: :152
                {
                    _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID TO CDEMO-FROM-TRANID. source: :153
                    _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :154
                    _commArea.PgmContext = 0;           // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :155
                    // EXEC CICS XCTL PROGRAM(COPAUS0C) COMMAREA(CARDDEMO-COMMAREA). source: :156-159
                    ctx.Xctl(opt.PgmName, _commArea);
                    return; // XCTL transfers control; the trailing PERFORM SEND-MENU-SCREEN never runs.
                }
                else // ELSE (not installed). source: :160
                {
                    _wsMessage = "";                    // MOVE SPACES TO WS-MESSAGE. source: :161
                    _errMsgColor = BmsColor.Red;        // MOVE DFHRED TO ERRMSGC OF COMEN1AO. source: :162
                    // STRING 'This option ' || name(DELIMITED BY '  ') || ' is not installed...'. source: :163-167
                    _wsMessage = StringInto(
                        "This option ",
                        Delimited(opt.Name, "  "),
                        " is not installed...");
                }
            }
            else if (Substr(opt.PgmName, 1, 5) == "DUMMY")
            {
                // WHEN CDEMO-MENU-OPT-PGMNAME(WS-OPTION)(1:5) = 'DUMMY'. source: :169
                // (No shipped option starts with DUMMY — unreachable with the current table.)
                _wsMessage = "";                        // MOVE SPACES TO WS-MESSAGE. source: :170
                _errMsgColor = BmsColor.Green;          // MOVE DFHGREEN TO ERRMSGC OF COMEN1AO. source: :171
                // STRING 'This option ' || name(DELIMITED BY SPACE) || 'is coming soon ...'. source: :172-176
                _wsMessage = StringInto(
                    "This option ",
                    Delimited(opt.Name, " "),
                    "is coming soon ...");
            }
            else
            {
                // WHEN OTHER (options 1-10). source: :177-187
                _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID TO CDEMO-FROM-TRANID. source: :178
                _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :179
                _commArea.FromProgram = WS_PGMNAME; // FB-2: duplicate MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :180
                _commArea.PgmContext = 0;           // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :183
                // EXEC CICS XCTL PROGRAM(CDEMO-MENU-OPT-PGMNAME(WS-OPTION)) COMMAREA(...). source: :184-187
                ctx.Xctl(opt.PgmName, _commArea);
                return; // XCTL transfers control; the trailing PERFORM SEND-MENU-SCREEN never runs.
            }

            // PERFORM SEND-MENU-SCREEN. source: COMEN01C.cbl:190
            // Reached only on the COPAUS0C-not-installed or DUMMY branches (no XCTL happened).
            SendMenuScreen(ctx);
        }
    }

    // === RETURN-TO-SIGNON-SCREEN. source: COMEN01C.cbl:196-203 ===
    private void ReturnToSignonScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES. source: COMEN01C.cbl:198
        // (Abbreviated comparison: (TO-PROGRAM = LOW-VALUES) OR (TO-PROGRAM = SPACES).)
        if (IsLowValuesOrSpaces(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C"; // MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM. source: :199

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM). source: COMEN01C.cbl:201-203 (no COMMAREA passed)
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), null);
    }

    // === SEND-MENU-SCREEN. source: COMEN01C.cbl:208-220 ===
    private void SendMenuScreen(CicsContext ctx)
    {
        BmsMap map = BuildMap();

        PopulateHeaderInfo(ctx, map); // PERFORM POPULATE-HEADER-INFO. source: :210
        BuildMenuOptions(map);        // PERFORM BUILD-MENU-OPTIONS. source: :211

        // MOVE WS-OPTION TO OPTIONO OF COMEN1AO — the echo from PROCESS-ENTER-KEY (:125), painted now.
        map.Field("OPTION").SetValue(OptionEcho());

        // MOVE WS-MESSAGE TO ERRMSGO OF COMEN1AO. source: COMEN01C.cbl:213
        SetErrMsg(map, _wsMessage); // FB-5: clamps the X(80) message to the X(78) ERRMSGO field.
        if (_errMsgColor is { } c)  // ERRMSGC override (DFHRED / DFHGREEN). source: :162, :171
            map.Field("ERRMSG").ColorOverride = c;

        // EXEC CICS SEND MAP('COMEN1A') MAPSET('COMEN01') FROM(COMEN1AO) ERASE. source: :215-220
        ctx.SendMap(MapName, MapsetName, map, SendMapOptions.FirstDisplay);
    }

    // === RECEIVE-MENU-SCREEN. source: COMEN01C.cbl:225-233 ===
    private void ReceiveMenuScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COMEN1A') MAPSET('COMEN01') INTO(COMEN1AI) RESP/RESP2.
        // RESP/RESP2 are captured but never inspected by the COBOL — replicate (no error handling).
        _receivedMap = BuildMap();
        ctx.ReceiveMap(MapName, MapsetName, _receivedMap);
    }

    // === POPULATE-HEADER-INFO. source: COMEN01C.cbl:238-257 ===
    private void PopulateHeaderInfo(CicsContext ctx, BmsMap map)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: COMEN01C.cbl:240
        DateTime now = ctx.Clock.Now;

        map.Field("TITLE01").SetValue(CCDA_TITLE01); // MOVE CCDA-TITLE01 TO TITLE01O. source: :242
        map.Field("TITLE02").SetValue(CCDA_TITLE02); // MOVE CCDA-TITLE02 TO TITLE02O. source: :243
        map.Field("TRNNAME").SetValue(WS_TRANID);    // MOVE WS-TRANID  TO TRNNAMEO. source: :244
        map.Field("PGMNAME").SetValue(WS_PGMNAME);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :245

        // Build mm/dd/yy from WS-CURDATE-MM/DD + WS-CURDATE-YEAR(3:2). source: :247-251
        map.Field("CURDATE").SetValue(now.ToString("MM/dd/yy"));
        // Build hh:mm:ss from WS-CURTIME-HH/MM/SS. source: :253-257
        map.Field("CURTIME").SetValue(now.ToString("HH:mm:ss"));
    }

    // === BUILD-MENU-OPTIONS. source: COMEN01C.cbl:262-303 ===
    private void BuildMenuOptions(BmsMap map)
    {
        // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > CDEMO-MENU-OPT-COUNT. source: :264-265
        for (int wsIdx = 1; !(wsIdx > CDEMO_MENU_OPT_COUNT); wsIdx++)
        {
            MenuOpt opt = OptionAt(wsIdx);

            // MOVE SPACES TO WS-MENU-OPT-TXT (PIC X(40)). source: :267
            // STRING CDEMO-MENU-OPT-NUM(WS-IDX) || '. ' || CDEMO-MENU-OPT-NAME(WS-IDX). source: :269-272
            // All DELIMITED BY SIZE: num is PIC 9(02) (zero-padded), name is X(35) (full width).
            string wsMenuOptTxt = Pic2(opt.Num) + ". " + opt.Name;
            wsMenuOptTxt = ClampOrPad(wsMenuOptTxt, 40); // WS-MENU-OPT-TXT is X(40)

            // EVALUATE WS-IDX -> OPTNnnnO. source: :274-301
            string field = wsIdx switch
            {
                1 => "OPTN001",
                2 => "OPTN002",
                3 => "OPTN003",
                4 => "OPTN004",
                5 => "OPTN005",
                6 => "OPTN006",
                7 => "OPTN007",
                8 => "OPTN008",
                9 => "OPTN009",
                10 => "OPTN010",
                11 => "OPTN011",
                12 => "OPTN012",
                _ => "", // WHEN OTHER -> CONTINUE
            };
            if (field.Length > 0)
                map.Field(field).SetValue(wsMenuOptTxt);
        }
    }

    // === Symbolic-map I/O helpers ===

    /// <summary>The symbolic in-map captured by the last RECEIVE (COMEN1AI).</summary>
    private BmsMap? _receivedMap;

    /// <summary>
    /// OPTIONI OF COMEN1AI as a fixed 2-char field (right-justified, spaces where untyped), matching the
    /// BMS NUM + JUSTIFY=(RIGHT,ZERO) input. The handler re-derives the option from these exact chars.
    /// </summary>
    private string OptionInput(CicsContext ctx)
    {
        string raw = _receivedMap?.Find("OPTION")?.Value ?? "";
        // Field width is 2; pad/clamp to width preserving the keyed characters as received.
        return ClampOrPad(raw, 2);
    }

    /// <summary>MOVE WS-OPTION TO OPTIONO — the zero-padded 2-digit echo (PIC 9(02)). source: :125.</summary>
    private string OptionEcho() => Pic2(_wsOption);

    /// <summary>
    /// MOVE WS-MESSAGE TO ERRMSGO OF COMEN1AO with the FB-5 clamp: ERRMSGO is X(78) but WS-MESSAGE is
    /// X(80), so the last two characters are silently dropped. source: COMEN01C.cbl:38, 213; COMEN01.bms:154-157.
    /// </summary>
    private static void SetErrMsg(BmsMap map, string message)
    {
        string msg80 = ClampOrPad(message, 80); // WS-MESSAGE is PIC X(80)
        string msg78 = msg80[..78];             // ERRMSGO is X(78): assignment truncates to 78
        map.Field("ERRMSG").SetValue(msg78);
    }

    // === COBOL primitive semantics helpers ===

    /// <summary>1-based <c>field(pos:1)</c> char read; returns space past the (1-based) string end.</summary>
    private static char CharAt(string s, int pos1Based) =>
        pos1Based >= 1 && pos1Based <= s.Length ? s[pos1Based - 1] : ' ';

    /// <summary>COBOL reference modification <c>s(start:len)</c> (1-based), space-safe.</summary>
    private static string Substr(string s, int start1Based, int len)
    {
        if (len <= 0) return "";
        var sb = new System.Text.StringBuilder(len);
        for (int i = 0; i < len; i++)
            sb.Append(CharAt(s, start1Based + i));
        return sb.ToString();
    }

    /// <summary>
    /// MOVE of a source string into a <c>PIC X(width) JUST RIGHT</c> field: the source is right-aligned,
    /// left-padded with spaces (truncating leftmost chars if longer). source: COMEN01C.cbl:45, 122.
    /// </summary>
    private static string JustRight(string src, int width)
    {
        src ??= "";
        if (src.Length >= width) return src[^width..];
        return src.PadLeft(width, ' ');
    }

    /// <summary>MOVE of a zero-filled X(2) string into <c>WS-OPTION PIC 9(02)</c> (low 2 digits).</summary>
    private static int ToNumeric2(string x)
    {
        int v = 0;
        foreach (char c in x)
            v = v * 10 + (c is >= '0' and <= '9' ? c - '0' : 0);
        return v % 100; // PIC 9(02): keep the low two digits
    }

    /// <summary>FB-6: after the space-&gt;'0' INSPECT + MOVE into PIC 9(02), the value is always numeric.</summary>
    private static bool IsNotNumeric(int wsOption) => false;

    /// <summary>Renders a value as <c>PIC 9(02)</c> — zero-padded, low two digits.</summary>
    private static string Pic2(int n) => Math.Abs(n % 100).ToString("D2");

    /// <summary>True when an X(n) field is all LOW-VALUES (modeled as NUL) or all spaces / empty.</summary>
    private static bool IsLowValuesOrSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        foreach (char c in s)
            if (c != ' ' && c != '\0') return false;
        return true;
    }

    /// <summary>
    /// COBOL <c>STRING source DELIMITED BY delim</c>: copies <paramref name="source"/> up to (but not
    /// including) the first occurrence of <paramref name="delim"/>; if absent, the whole string.
    /// source: COMEN01C.cbl:163-167 (delim '  '), :172-176 (delim ' ').
    /// </summary>
    private static string Delimited(string source, string delim)
    {
        source ??= "";
        int i = source.IndexOf(delim, StringComparison.Ordinal);
        return i < 0 ? source : source[..i];
    }

    /// <summary>
    /// COBOL <c>STRING a b c INTO WS-MESSAGE</c> with WS-MESSAGE PIC X(80): concatenate the operands,
    /// then clamp/pad to 80 (STRING leaves the receiving field's tail unchanged; here WS-MESSAGE was
    /// MOVEd SPACES first, so the effective result is the concatenation space-padded to 80).
    /// </summary>
    private static string StringInto(params string[] parts) =>
        ClampOrPad(string.Concat(parts), 80);

    /// <summary>Truncates to <paramref name="width"/> or right-pads with spaces — a fixed PIC X(width) cell.</summary>
    private static string ClampOrPad(string s, int width)
    {
        s ??= "";
        if (s.Length > width) return s[..width];
        return s.PadRight(width, ' ');
    }

    // ============================================================================================
    //  BMS map COMEN1A (mapset COMEN01) — built from app/bms/COMEN01.bms / SCREEN_COMEN01.md.
    // ============================================================================================

    /// <summary>The DFHMDI map name. source: COMEN01.bms:26.</summary>
    public const string MapName = "COMEN1A";

    /// <summary>The DFHMSD mapset name. source: COMEN01.bms:19.</summary>
    public const string MapsetName = "COMEN01";

    private BmsMap BuildMap() => BuildBmsMap();

    /// <summary>
    /// Constructs a fresh <see cref="BmsMap"/> for COMEN1A: every <c>DFHMDF</c> with its exact
    /// Row/Col/Length/attribute/colour/highlight/initial value, in source order. The single input field
    /// is <c>OPTION</c> (20,41) L2, NUM + JUSTIFY=(RIGHT,ZERO) + IC (cursor). source: COMEN01.bms:26-162.
    /// </summary>
    public static BmsMap BuildBmsMap()
    {
        var fields = new List<ScreenField>
        {
            // --- shared 3-line header ---
            Literal(1, 1, 5, "Tran:", BmsColor.Blue),                                  // source: COMEN01.bms:29-33
            Named("TRNNAME", 1, 7, 4, Protected(BmsAttribute.Fset), BmsColor.Blue),    // source: COMEN01.bms:34-37
            Named("TITLE01", 1, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow),// source: COMEN01.bms:38-41
            Literal(1, 65, 5, "Date:", BmsColor.Blue),                                 // source: COMEN01.bms:42-46
            NamedInit("CURDATE", 1, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue, "mm/dd/yy"), // source: :47-51

            Literal(2, 1, 5, "Prog:", BmsColor.Blue),                                  // source: COMEN01.bms:52-56
            Named("PGMNAME", 2, 7, 8, Protected(BmsAttribute.Fset), BmsColor.Blue),    // source: COMEN01.bms:57-60
            Named("TITLE02", 2, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow),// source: COMEN01.bms:61-64
            Literal(2, 65, 5, "Time:", BmsColor.Blue),                                 // source: COMEN01.bms:65-69
            NamedInit("CURTIME", 2, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue, "hh:mm:ss"), // source: :70-74

            // --- "Main Menu" heading (BRT NEUTRAL) ---
            Literal(4, 35, 9, "Main Menu", BmsColor.Neutral, bright: true),            // source: COMEN01.bms:75-79

            // --- 12 menu-option output lines (col 20, rows 6..17), INITIAL=' ' ---
            NamedInit("OPTN001", 6, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "),  // source: :80-84
            NamedInit("OPTN002", 7, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "),  // source: :85-89
            NamedInit("OPTN003", 8, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "),  // source: :90-94
            NamedInit("OPTN004", 9, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "),  // source: :95-99
            NamedInit("OPTN005", 10, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :100-104
            NamedInit("OPTN006", 11, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :105-109
            NamedInit("OPTN007", 12, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :110-114
            NamedInit("OPTN008", 13, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :115-119
            NamedInit("OPTN009", 14, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :120-124
            NamedInit("OPTN010", 15, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :125-129
            NamedInit("OPTN011", 16, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :130-134
            NamedInit("OPTN012", 17, 20, 40, Protected(BmsAttribute.Fset), BmsColor.Blue, " "), // source: :135-139

            // --- prompt + the only input field ---
            Literal(20, 15, 25, "Please select an option :", BmsColor.Turquoise, bright: true), // source: :140-144

            // OPTION: ATTRB=(FSET,IC,NORM,NUM,UNPROT), HILIGHT=UNDERLINE, JUSTIFY=(RIGHT,ZERO), L2.
            new ScreenField
            {
                Name = "OPTION",
                Row = 20,
                Col = 41,
                Length = 2,
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal |
                            BmsAttribute.Numeric | BmsAttribute.Fset | BmsAttribute.Ic,
                Color = BmsColor.Default, // no COLOR= coded
                Hilight = BmsHilight.Underline,
                RightJustify = true,
                ZeroFill = true,
            }, // source: COMEN01.bms:145-149

            // stopper after OPTION (LENGTH=0, GREEN, ASKIP) — bounds the 2-byte input.
            Stopper(20, 44, BmsColor.Green), // source: COMEN01.bms:150-153

            // --- error line (BRT RED) ---
            Named("ERRMSG", 23, 1, 78, Protected(BmsAttribute.Bright | BmsAttribute.Fset), BmsColor.Red), // source: :154-157

            // --- function-key footer (YELLOW) ---
            Literal(24, 1, 23, "ENTER=Continue  F3=Exit", BmsColor.Yellow), // source: COMEN01.bms:158-162
        };

        return new BmsMap(MapName, MapsetName, fields);
    }

    /// <summary>ASKIP, NORM + the given extra attribute bits (the protected-field default).</summary>
    private static BmsAttribute Protected(BmsAttribute extra) =>
        BmsAttribute.AutoSkip | BmsAttribute.Normal | extra;

    private static ScreenField Literal(int row, int col, int len, string text, BmsColor color, bool bright = false) =>
        new()
        {
            Row = row,
            Col = col,
            Length = len,
            Attribute = BmsAttribute.AutoSkip | (bright ? BmsAttribute.Bright : BmsAttribute.Normal),
            Color = color,
            Value = text,
        };

    private static ScreenField Named(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    private static ScreenField NamedInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    private static ScreenField Stopper(int row, int col, BmsColor color) =>
        new()
        {
            Row = row,
            Col = col,
            Length = 0,
            Attribute = BmsAttribute.AutoSkip | BmsAttribute.Normal,
            Color = color,
        };
}
