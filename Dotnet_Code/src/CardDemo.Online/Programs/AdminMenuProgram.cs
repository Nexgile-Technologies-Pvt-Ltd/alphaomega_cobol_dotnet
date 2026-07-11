using CardDemo.Runtime;
using CardDemo.Data;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of CICS COBOL program <c>COADM01C</c> — the Admin Menu for administrator
/// CardDemo users (TRANSID <c>CA00</c>, BMS map <c>COADM1A</c> / mapset <c>COADM01</c>). It renders the
/// admin function menu, validates the operator's two-digit option, and on a valid in-range option
/// <c>XCTL</c>s to the program registered for that option in the static <c>COADM02Y</c> menu table.
/// </summary>
/// <remarks>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method (carrying the original paragraph name and a <c>// source:</c> citation),
/// the <c>MAIN-PARA</c> dispatch on <c>EIBCALEN</c> / <c>EIBAID</c> is preserved exactly, the
/// <c>EVALUATE</c> / <c>PERFORM</c> flow and statement order are kept, and every validation message is
/// reproduced verbatim. The program performs <b>no</b> data I/O — <c>WS-USRSEC-FILE='USRSEC'</c> and
/// the <c>CSUSR01Y</c> copybook are dead working storage (spec §2/§12.7) — so it carries no repository;
/// the <see cref="RelationalDb"/> constructor argument is accepted only to satisfy the uniform online
/// handler factory contract.
/// </para>
/// <para>
/// <c>EXEC CICS HANDLE CONDITION PGMIDERR(PGMIDERR-ERR-PARA)</c> is modelled as a try/catch around the
/// option XCTL dispatch: when the target program is not installed (the registry cannot resolve it), the
/// <see cref="HandleProgramNotInstalled"/> body runs — a green "not installed" message, a re-SEND, and the
/// paragraph's own RETURN — matching the mainframe's PGMIDERR branch. source: COADM01C.cbl:77-79, 270-283.
/// </para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — <c>PROCESS-ENTER-KEY</c> has no early exit after the validation <c>PERFORM
/// SEND-MENU-SCREEN</c>; control falls through to <c>IF NOT ERR-FLG-ON</c>, which gates dispatch. The
/// "not installed" green message is then built and SENT unconditionally inside that same
/// <c>IF NOT ERR-FLG-ON</c> immediately after the XCTL block — dead code after a real XCTL, but the
/// intended output for a skipped 'DUMMY' option. Statement order is preserved, not restructured into an
/// else. source: COADM01C.cbl:140-158.</item>
/// <item>FB-2 — the option name <c>CDEMO-ADMIN-OPT-NAME(WS-OPTION)</c> is commented out of both
/// "not installed" STRING statements, so the message reads <c>"This option is not installed ..."</c>
/// (single space, no option name). Kept omitted. source: COADM01C.cbl:152-156, 273-277.</item>
/// <item>FB-3 — <c>ERRMSGC=DFHGREEN</c> overrides the map's RED ERRMSG colour for the informational
/// "not installed" message on the two PGMIDERR/DUMMY paths, so an info message renders green while real
/// errors render red. source: COADM01C.cbl:151, 272; COADM01.bms:154-155.</item>
/// <item>FB-4 — <c>WS-MESSAGE</c> is X(80) but the map field <c>ERRMSGO</c> is X(78); the assignment
/// silently drops the last two characters (<see cref="SetErrMsg"/>). source: COADM01C.cbl:38; COADM01.CPY:260.</item>
/// <item>FB-5 — the menu table is <c>OCCURS 9 TIMES</c> but only 6 entries are populated and
/// <c>CDEMO-ADMIN-OPT-COUNT=6</c> bounds every loop/range check; slots 7-9 are never valid options. The
/// 9-slot declaration with 6 valid rows is preserved (not trimmed to 6). source: COADM02Y.cpy:22, 55-59.</item>
/// <item>FB-6 — the <c>WS-OPTION IS NOT NUMERIC</c> disjunct is always false after the space-&gt;'0'
/// INSPECT + MOVE into PIC 9(02); the redundant test is kept. source: COADM01C.cbl:127-131.</item>
/// <item>FB-7 — RESP/RESP2 from RECEIVE are captured but never inspected (no MAPFAIL handling). source:
/// COADM01C.cbl:198-199.</item>
/// <item>FB-8 — the 'DUMMY' guard at :141 is unreachable with shipped data (no COADM02Y entry begins
/// with 'DUMMY'), but ported faithfully. source: COADM01C.cbl:141; COADM02Y.cpy:26-53.</item>
/// </list>
/// </remarks>
public sealed class AdminMenuProgram : ITransactionHandler
{
    // === WS-VARIABLES (WORKING-STORAGE) — COBOL VALUE clauses. source: COADM01C.cbl:35-48 ===

    private const string ProgramId = "COADM01C"; // WS-PGMNAME PIC X(08). source: COADM01C.cbl:36
    private const string TranId = "CA00";        // WS-TRANID  PIC X(04). source: COADM01C.cbl:37

    // WS-ERR-FLG PIC X(01): 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: COADM01C.cbl:40-42
    private string _errorFlag = "N"; // WS-ERR-FLG
    private bool ErrorFlagOn => _errorFlag == "Y"; // 88 ERR-FLG-ON

    private string _message = ""; // WS-MESSAGE PIC X(80). source: COADM01C.cbl:38
    private int _option;          // WS-OPTION  PIC 9(02) VALUE 0. source: COADM01C.cbl:46

    // True once PROCESS-ENTER-KEY has done MOVE WS-OPTION TO OPTIONO (:129). On the first display
    // (:93 MOVE LOW-VALUES TO COADM1AO) and any non-ENTER SEND, OPTIONO stays LOW-VALUES and renders
    // blank — only an ENTER turn echoes the typed option. SEND-MENU-SCREEN itself never touches OPTIONO.
    private bool _optionEchoed;

    // Per-turn override of the ERRMSG colour byte (ERRMSGC OF COADM1AO): MOVE DFHGREEN on the
    // "not installed" info paths. Null = use the map's default RED. source: COADM01C.cbl:151, 272.
    private BmsColor? _errMsgColor;

    // === Constants from the copybooks referenced by the program ===

    /// <summary>CCDA-TITLE01 PIC X(40). source: COTTL01Y.cpy:18-20.</summary>
    private const string Title01 = "      AWS Mainframe Modernization       ";

    /// <summary>CCDA-TITLE02 PIC X(40). source: COTTL01Y.cpy:21-23.</summary>
    private const string Title02 = "              CardDemo                  ";

    /// <summary>CCDA-MSG-INVALID-KEY PIC X(50). source: CSMSG01Y.cpy:20-21.</summary>
    private const string InvalidKeyMessage = "Invalid key pressed. Please see below...         ";

    /// <summary>CDEMO-ADMIN-OPT-COUNT PIC 9(02) VALUE 6 — only 6 options populated. source: COADM02Y.cpy:22.</summary>
    private const int AdminOptionCount = 6;

    // === The CARDDEMO-ADMIN-MENU-OPTIONS table (copybook COADM02Y, REDEFINES gives 9 OCCURS slots) ===
    // 6 populated rows + 3 empty slots (FB-5). Each entry = NUM 9(02) + NAME X(35) + PGMNAME X(08).
    // source: COADM02Y.cpy:26-59.
    private readonly record struct AdminOpt(int Num, string Name, string PgmName);

    private static readonly AdminOpt[] AdminOptions =
    {
        new(1, "User List (Security)               ", "COUSR00C"), // source: COADM02Y.cpy:26-29
        new(2, "User Add (Security)                ", "COUSR01C"), // source: COADM02Y.cpy:31-34
        new(3, "User Update (Security)             ", "COUSR02C"), // source: COADM02Y.cpy:36-39
        new(4, "User Delete (Security)             ", "COUSR03C"), // source: COADM02Y.cpy:41-44
        new(5, "Transaction Type List/Update (Db2) ", "COTRTLIC"), // source: COADM02Y.cpy:46-49
        new(6, "Transaction Type Maintenance (Db2) ", "COTRTUPC"), // source: COADM02Y.cpy:50-53
        new(0, "",                                    ""),         // OCCURS slot 7: unpopulated FILLER (FB-5)
        new(0, "",                                    ""),         // OCCURS slot 8: unpopulated FILLER (FB-5)
        new(0, "",                                    ""),         // OCCURS slot 9: unpopulated FILLER (FB-5)
    };

    /// <summary>
    /// 1-based subscript into <see cref="AdminOptions"/> matching COBOL <c>CDEMO-ADMIN-OPT(idx)</c>.
    /// A subscript of 0 or beyond the 9-slot table resolves to an empty record — the deterministic
    /// stand-in for COBOL's undefined subscript-0 / past-end storage. The CDEMO-ADMIN-OPT-COUNT=6
    /// range guard normally keeps callers in 1..6, so this only matters on the FB-1 fall-through.
    /// </summary>
    private static AdminOpt OptionAt(int idx) =>
        idx >= 1 && idx <= AdminOptions.Length ? AdminOptions[idx - 1] : EmptyOption;

    /// <summary>The empty record returned for a 0 / out-of-range subscript (non-null fields).</summary>
    private static readonly AdminOpt EmptyOption = new(0, "", "");

    // === CARDDEMO-COMMAREA (typed view) restored each turn; mirrors COCOM01Y. ===
    private CardDemoCommArea _commArea = new();

    /// <summary>
    /// Factory-friendly constructor. <paramref name="db"/> is accepted for a uniform handler signature;
    /// COADM01C performs no data I/O so no repository is created. The argument is intentionally unused.
    /// </summary>
    public AdminMenuProgram(RelationalDb db)
    {
        _ = db; // COADM01C has no VSAM/SQL access (WS-USRSEC-FILE / CSUSR01Y are dead). source: COADM01C.cbl:39, 58; spec §2
    }

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public AdminMenuProgram()
    {
    }

    public string ProgramName => "COADM01C"; // PROGRAM-ID. source: COADM01C.cbl:23
    public string TransId => "CA00";         // CSD TRANSACTION(CA00). source: CSD_TRANSACTIONS.md; COADM01C.cbl:37

    /// <summary>
    /// The <c>HANDLE CONDITION PGMIDERR</c> probe: "is the XCTL target program installed?" Defaults to
    /// the program registry on the context (<see cref="CicsContext"/> has none of its own here, so the
    /// dispatcher injects this). When the registry cannot resolve the target the XCTL raises PGMIDERR
    /// and <see cref="HandleProgramNotInstalled"/> runs. Overridable so the not-installed branch stays testable.
    /// source: COADM01C.cbl:77-79, 145-148, 270-283.
    /// </summary>
    public Func<string, bool>? IsProgramInstalled { get; set; }

    // === MAIN-PARA. source: COADM01C.cbl:75-114 ===
    public void Handle(CicsContext ctx)
    {
        RunMainLogic(ctx);
    }

    private void RunMainLogic(CicsContext ctx) // COBOL paragraph: MAIN-PARA
    {
        // EXEC CICS HANDLE CONDITION PGMIDERR(PGMIDERR-ERR-PARA) — registered for the task; the XCTL in
        // PROCESS-ENTER-KEY is wrapped in a try/catch on PgmidErrCondition below. source: COADM01C.cbl:77-79

        // SET ERR-FLG-OFF TO TRUE. source: COADM01C.cbl:81
        _errorFlag = "N";

        // MOVE SPACES TO WS-MESSAGE, ERRMSGO OF COADM1AO. source: COADM01C.cbl:83-84
        _message = "";
        _errMsgColor = null;

        if (ctx.EibCalen == 0) // IF EIBCALEN = 0. source: COADM01C.cbl:86
        {
            // MOVE 'COSGN00C' TO CDEMO-FROM-PROGRAM. source: COADM01C.cbl:87
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.FromProgram = "COSGN00C";
            // PERFORM RETURN-TO-SIGNON-SCREEN. source: COADM01C.cbl:88
            ReturnToSignonScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: COADM01C.cbl:90
            _commArea = ctx.CommArea!;

            if (!_commArea.IsReenter) // IF NOT CDEMO-PGM-REENTER. source: COADM01C.cbl:91
            {
                _commArea.SetReenter(); // SET CDEMO-PGM-REENTER TO TRUE. source: COADM01C.cbl:92
                // MOVE LOW-VALUES TO COADM1AO (fresh symbolic out-map). source: COADM01C.cbl:93
                SendMenuScreen(ctx);    // PERFORM SEND-MENU-SCREEN. source: COADM01C.cbl:94
            }
            else
            {
                ReceiveMenuScreen(ctx); // PERFORM RECEIVE-MENU-SCREEN. source: COADM01C.cbl:96

                // EVALUATE EIBAID. source: COADM01C.cbl:97-107
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:        // WHEN DFHENTER. source: COADM01C.cbl:98
                        ProcessEnterKey(ctx); // PERFORM PROCESS-ENTER-KEY. source: COADM01C.cbl:99
                        break;

                    case AidKey.Pf3:          // WHEN DFHPF3. source: COADM01C.cbl:100
                        _commArea.ToProgram = "COSGN00C"; // MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM. source: :101
                        ReturnToSignonScreen(ctx);        // PERFORM RETURN-TO-SIGNON-SCREEN. source: :102
                        break;

                    default:                  // WHEN OTHER. source: COADM01C.cbl:103
                        _errorFlag = "Y";                   // MOVE 'Y' TO WS-ERR-FLG. source: :104
                        _message = InvalidKeyMessage; // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. source: :105
                        SendMenuScreen(ctx);               // PERFORM SEND-MENU-SCREEN. source: :106
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: COADM01C.cbl:111-114
        // Issued only when the handler did not already XCTL away or take the PGMIDERR-ERR-PARA RETURN.
        // CICS would never reach the MAIN-PARA RETURN after an XCTL; mirror that by not overwriting an
        // existing outcome.
        if (ctx.Outcome is null)
            ctx.ReturnTransId(TranId, _commArea);
    }

    // === PROCESS-ENTER-KEY. source: COADM01C.cbl:119-158 ===
    private void ProcessEnterKey(CicsContext ctx)
    {
        // OPTIONI OF COADM1AI — the 2-char keyed option field, as received (not trimmed).
        string optionInput = OptionInput();

        // PERFORM VARYING WS-IDX FROM LENGTH OF OPTIONI BY -1 UNTIL
        //   OPTIONI(WS-IDX:1) NOT = SPACES OR WS-IDX = 1. source: COADM01C.cbl:121-125
        int index = optionInput.Length; // LENGTH OF OPTIONI = 2  // WS-IDX
        while (!(CharAt(optionInput, index) != ' ' || index == 1))
            index--;

        // MOVE OPTIONI(1:WS-IDX) TO WS-OPTION-X (PIC X(02) JUST RIGHT). source: COADM01C.cbl:126
        string optionText = JustRight(Substr(optionInput, 1, index), 2); // WS-OPTION-X
        // INSPECT WS-OPTION-X REPLACING ALL ' ' BY '0'. source: COADM01C.cbl:127
        optionText = optionText.Replace(' ', '0');
        // MOVE WS-OPTION-X TO WS-OPTION (alphanumeric -> PIC 9(02)). source: COADM01C.cbl:128
        _option = ToNumeric2(optionText);
        // MOVE WS-OPTION TO OPTIONO OF COADM1AO (echo, zero-padded). source: COADM01C.cbl:129
        _optionEchoed = true; // OPTIONO is now set; SEND-MENU-SCREEN will paint the echo (applied via OptionEcho).

        // IF WS-OPTION IS NOT NUMERIC OR WS-OPTION > CDEMO-ADMIN-OPT-COUNT OR WS-OPTION = ZEROS.
        // FB-6: the IS NOT NUMERIC disjunct is always false after the MOVE; kept verbatim.
        // source: COADM01C.cbl:131-138
        if (IsNotNumeric(_option) || _option > AdminOptionCount || _option == 0)
        {
            _errorFlag = "Y";                                        // MOVE 'Y' TO WS-ERR-FLG. source: :134
            _message = "Please enter a valid option number...";   // source: :135-136 (three dots, no trailing space)
            SendMenuScreen(ctx);                                    // PERFORM SEND-MENU-SCREEN. source: :137
            // FB-1: NO early exit — fall through to the IF NOT ERR-FLG-ON guard below.
        }

        // IF NOT ERR-FLG-ON. source: COADM01C.cbl:140
        if (!ErrorFlagOn)
        {
            AdminOpt opt = OptionAt(_option);

            // IF CDEMO-ADMIN-OPT-PGMNAME(WS-OPTION)(1:5) NOT = 'DUMMY'. source: COADM01C.cbl:141
            // FB-8: no shipped entry begins with 'DUMMY', so the XCTL branch always runs for a valid option.
            if (Substr(opt.PgmName, 1, 5) != "DUMMY")
            {
                _commArea.FromTranId = TranId;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :142
                _commArea.FromProgram = ProgramId; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :143
                _commArea.PgmContext = 0;           // MOVE ZEROS     TO CDEMO-PGM-CONTEXT. source: :144

                // EXEC CICS XCTL PROGRAM(CDEMO-ADMIN-OPT-PGMNAME(WS-OPTION)) COMMAREA(CARDDEMO-COMMAREA).
                // source: COADM01C.cbl:145-148. HANDLE CONDITION PGMIDERR -> PGMIDERR-ERR-PARA on a
                // not-installed target. source: COADM01C.cbl:77-79.
                if (TargetInstalled(ctx, opt.PgmName))
                {
                    ctx.Xctl(opt.PgmName, _commArea);
                    return; // XCTL transfers control; the trailing fall-through SEND never runs.
                }

                // PGMIDERR raised: branch to PGMIDERR-ERR-PARA, which issues its OWN RETURN.
                HandleProgramNotInstalled(ctx);
                return;
            }

            // FB-1 fall-through (still inside IF NOT ERR-FLG-ON): for a 'DUMMY' (skipped) option this is
            // the intended green "not installed" info message; for a real, installed option the XCTL
            // above already left the program so this is dead code. source: COADM01C.cbl:150-157.
            _message = "";               // MOVE SPACES   TO WS-MESSAGE. source: :150
            _errMsgColor = BmsColor.Green; // MOVE DFHGREEN TO ERRMSGC OF COADM1AO (FB-3). source: :151
            // STRING 'This option ' DELIMITED BY SIZE, 'is not installed ...' DELIMITED BY SIZE INTO
            // WS-MESSAGE. FB-2: CDEMO-ADMIN-OPT-NAME(WS-OPTION) is commented out -> name omitted.
            // source: COADM01C.cbl:152-156.
            _message = StringInto("This option ", "is not installed ...");

            SendMenuScreen(ctx); // PERFORM SEND-MENU-SCREEN. source: :157
        }
    }

    // === RETURN-TO-SIGNON-SCREEN. source: COADM01C.cbl:163-170 ===
    private void ReturnToSignonScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES. source: COADM01C.cbl:165
        // (Abbreviated comparison: (TO-PROGRAM = LOW-VALUES) OR (TO-PROGRAM = SPACES).)
        if (IsLowValuesOrSpaces(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C"; // MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM. source: :166

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM). source: COADM01C.cbl:168-170 (no COMMAREA passed).
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), null);
    }

    // === SEND-MENU-SCREEN. source: COADM01C.cbl:175-187 ===
    private void SendMenuScreen(CicsContext ctx)
    {
        BmsMap map = BuildMap();

        PopulateHeaderInfo(ctx, map); // PERFORM POPULATE-HEADER-INFO. source: :177
        BuildMenuOptions(map);        // PERFORM BUILD-MENU-OPTIONS.  source: :178

        // SEND-MENU-SCREEN itself does NOT move WS-OPTION to OPTIONO (COADM01C.cbl:175-187). OPTIONO is
        // LOW-VALUES (blank) on the first display (:93) and on any non-ENTER SEND; it carries the typed
        // option only after PROCESS-ENTER-KEY did MOVE WS-OPTION TO OPTIONO (:129).
        if (_optionEchoed)
            map.Field("OPTION").SetValue(OptionEcho());

        // MOVE WS-MESSAGE TO ERRMSGO OF COADM1AO. source: COADM01C.cbl:180
        SetErrMsg(map, _message); // FB-4: clamps the X(80) message to the X(78) ERRMSGO field.
        if (_errMsgColor is { } c)  // ERRMSGC override (DFHGREEN on the not-installed path, FB-3). source: :151
            map.Field("ERRMSG").ColorOverride = c;

        // EXEC CICS SEND MAP('COADM1A') MAPSET('COADM01') FROM(COADM1AO) ERASE. source: :182-187
        ctx.SendMap(MapName, MapsetName, map, SendMapOptions.FirstDisplay);
    }

    // === RECEIVE-MENU-SCREEN. source: COADM01C.cbl:192-200 ===
    private void ReceiveMenuScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COADM1A') MAPSET('COADM01') INTO(COADM1AI) RESP(WS-RESP-CD) RESP2(WS-REAS-CD).
        // FB-7: RESP/RESP2 are captured but never inspected by the COBOL — replicate (no error handling).
        _receivedMap = BuildMap();
        ctx.ReceiveMap(MapName, MapsetName, _receivedMap);
    }

    // === POPULATE-HEADER-INFO. source: COADM01C.cbl:205-224 ===
    private void PopulateHeaderInfo(CicsContext ctx, BmsMap map)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: COADM01C.cbl:207
        DateTime now = ctx.Clock.Now;

        map.Field("TITLE01").SetValue(Title01); // MOVE CCDA-TITLE01 TO TITLE01O. source: :209
        map.Field("TITLE02").SetValue(Title02); // MOVE CCDA-TITLE02 TO TITLE02O. source: :210
        map.Field("TRNNAME").SetValue(TranId);    // MOVE WS-TRANID    TO TRNNAMEO. source: :211
        map.Field("PGMNAME").SetValue(ProgramId);   // MOVE WS-PGMNAME   TO PGMNAMEO. source: :212

        // Build mm/dd/yy from WS-CURDATE-MM/DD + WS-CURDATE-YEAR(3:2) (last 2 digits). source: :214-218
        map.Field("CURDATE").SetValue(now.ToString("MM/dd/yy"));
        // Build hh:mm:ss from WS-CURTIME-HH/MM/SS. source: :220-224
        map.Field("CURTIME").SetValue(now.ToString("HH:mm:ss"));
    }

    // === BUILD-MENU-OPTIONS. source: COADM01C.cbl:229-266 ===
    private void BuildMenuOptions(BmsMap map)
    {
        // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > CDEMO-ADMIN-OPT-COUNT (1..6). source: :231-232
        for (int index = 1; !(index > AdminOptionCount); index++) // WS-IDX
        {
            AdminOpt opt = OptionAt(index);

            // MOVE SPACES TO WS-ADMIN-OPT-TXT (PIC X(40)). source: :234
            // STRING CDEMO-ADMIN-OPT-NUM(WS-IDX) || '. ' || CDEMO-ADMIN-OPT-NAME(WS-IDX). source: :236-239
            // All DELIMITED BY SIZE: num is PIC 9(02) (zero-padded), name is X(35) (full width).
            string optionText = Pic2(opt.Num) + ". " + opt.Name; // WS-ADMIN-OPT-TXT
            optionText = ClampOrPad(optionText, 40); // WS-ADMIN-OPT-TXT is X(40)

            // EVALUATE WS-IDX -> OPTNnnnO (1..10; WHEN OTHER -> CONTINUE). source: :241-264
            string field = index switch
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
                _ => "", // WHEN OTHER -> CONTINUE (OPTN011/OPTN012 never populated). source: :262-263
            };
            if (field.Length > 0)
                map.Field(field).SetValue(optionText);
        }
    }

    // === PGMIDERR-ERR-PARA. source: COADM01C.cbl:270-284 ===
    // HANDLE CONDITION target: an XCTL to a real-but-not-installed program. Issues its OWN RETURN.
    private void HandleProgramNotInstalled(CicsContext ctx) // COBOL paragraph: PGMIDERR-ERR-PARA
    {
        // MOVE SPACES   TO WS-MESSAGE. source: COADM01C.cbl:271
        _message = "";
        // MOVE DFHGREEN TO ERRMSGC OF COADM1AO (FB-3). source: COADM01C.cbl:272
        _errMsgColor = BmsColor.Green;
        // STRING 'This option ' DELIMITED BY SIZE, 'is not installed ...' DELIMITED BY SIZE INTO
        // WS-MESSAGE. FB-2: CDEMO-ADMIN-OPT-NAME(WS-OPTION) commented out -> name omitted.
        // source: COADM01C.cbl:273-277.
        _message = StringInto("This option ", "is not installed ...");

        // PERFORM SEND-MENU-SCREEN. source: COADM01C.cbl:279
        SendMenuScreen(ctx);

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: COADM01C.cbl:280-283
        // This paragraph's own RETURN — it does NOT fall back into MAIN-PARA's RETURN.
        ctx.ReturnTransId(TranId, _commArea);
    }

    /// <summary>
    /// The PGMIDERR probe for an XCTL target: true when the target program is installed (XCTL succeeds),
    /// false when it would raise PGMIDERR. Uses <see cref="IsProgramInstalled"/> when supplied; otherwise
    /// treats the target as installed so the option XCTLs through. source: COADM01C.cbl:77-79, 145-148.
    /// </summary>
    private bool TargetInstalled(CicsContext ctx, string program)
    {
        _ = ctx;
        return IsProgramInstalled?.Invoke(program) ?? true;
    }

    // === Symbolic-map I/O helpers ===

    /// <summary>The symbolic in-map captured by the last RECEIVE (COADM1AI).</summary>
    private BmsMap? _receivedMap;

    /// <summary>
    /// OPTIONI OF COADM1AI as a fixed 2-char field (right-justified, spaces where untyped), matching the
    /// BMS NUM + JUSTIFY=(RIGHT,ZERO) input. The handler re-derives the option from these exact chars.
    /// </summary>
    private string OptionInput()
    {
        string raw = _receivedMap?.Find("OPTION")?.Value ?? "";
        // Field width is 2; pad/clamp to width preserving the keyed characters as received.
        return ClampOrPad(raw, 2);
    }

    /// <summary>MOVE WS-OPTION TO OPTIONO — the zero-padded 2-digit echo (PIC 9(02)). source: :129.</summary>
    private string OptionEcho() => Pic2(_option);

    /// <summary>
    /// MOVE WS-MESSAGE TO ERRMSGO OF COADM1AO with the FB-4 clamp: ERRMSGO is X(78) but WS-MESSAGE is
    /// X(80), so the last two characters are silently dropped. source: COADM01C.cbl:38, 180; COADM01.bms:154-157.
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
    /// left-padded with spaces (truncating leftmost chars if longer). source: COADM01C.cbl:45, 126.
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
    private static bool IsNotNumeric(int wsOption)
    {
        _ = wsOption;
        return false;
    }

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
    /// COBOL <c>STRING a b INTO WS-MESSAGE</c> with WS-MESSAGE PIC X(80): concatenate the operands
    /// (each DELIMITED BY SIZE = full width), then clamp/pad to 80 (WS-MESSAGE was MOVEd SPACES first,
    /// so the effective result is the concatenation space-padded to 80). source: COADM01C.cbl:152-156.
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
    //  BMS map COADM1A (mapset COADM01) — built from app/bms/COADM01.bms / SCREEN_COADM01.md.
    // ============================================================================================

    /// <summary>The DFHMDI map name. source: COADM01.bms:26.</summary>
    public const string MapName = "COADM1A";

    /// <summary>The DFHMSD mapset name. source: COADM01.bms:19.</summary>
    public const string MapsetName = "COADM01";

    private BmsMap BuildMap() => BuildBmsMap();

    /// <summary>
    /// Constructs a fresh <see cref="BmsMap"/> for COADM1A: every <c>DFHMDF</c> with its exact
    /// Row/Col/Length/attribute/colour/highlight/initial value, in source order. The single input field
    /// is <c>OPTION</c> (20,41) L2, NUM + JUSTIFY=(RIGHT,ZERO) + IC (cursor). source: COADM01.bms:26-164.
    /// </summary>
    public static BmsMap BuildBmsMap()
    {
        var fields = new List<ScreenField>
        {
            // --- shared 3-line header ---
            Literal(1, 1, 5, "Tran:", BmsColor.Blue),                                   // source: COADM01.bms:29-33
            Named("TRNNAME", 1, 7, 4, Protected(BmsAttribute.Fset), BmsColor.Blue),     // source: COADM01.bms:34-37
            Named("TITLE01", 1, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow), // source: COADM01.bms:38-41
            Literal(1, 65, 5, "Date:", BmsColor.Blue),                                  // source: COADM01.bms:42-46
            NamedInit("CURDATE", 1, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue, "mm/dd/yy"), // source: :47-51

            Literal(2, 1, 5, "Prog:", BmsColor.Blue),                                   // source: COADM01.bms:52-56
            Named("PGMNAME", 2, 7, 8, Protected(BmsAttribute.Fset), BmsColor.Blue),     // source: COADM01.bms:57-60
            Named("TITLE02", 2, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow), // source: COADM01.bms:61-64
            Literal(2, 65, 5, "Time:", BmsColor.Blue),                                  // source: COADM01.bms:65-69
            NamedInit("CURTIME", 2, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue, "hh:mm:ss"), // source: :70-74

            // --- "Admin Menu" heading (BRT NEUTRAL) ---
            Literal(4, 35, 10, "Admin Menu", BmsColor.Neutral, bright: true),           // source: COADM01.bms:75-79

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
            }, // source: COADM01.bms:145-149

            // stopper after OPTION (LENGTH=0, GREEN, ASKIP) — bounds the 2-byte input.
            Stopper(20, 44, BmsColor.Green), // source: COADM01.bms:150-153

            // --- error line (BRT RED) ---
            Named("ERRMSG", 23, 1, 78, Protected(BmsAttribute.Bright | BmsAttribute.Fset), BmsColor.Red), // source: :154-157

            // --- function-key footer (YELLOW) ---
            Literal(24, 1, 23, "ENTER=Continue  F3=Exit", BmsColor.Yellow), // source: COADM01.bms:158-162
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
