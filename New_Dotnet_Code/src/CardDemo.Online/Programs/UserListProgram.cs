using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COUSR00C</c> — the "List Users" paged browse of
/// the USRSEC (USER_SECURITY) file (TRANSID <c>CU00</c>, BMS map <c>COUSR0A</c> / mapset <c>COUSR00</c>).
/// </summary>
/// <remarks>
/// <para>
/// COUSR00C browses the USRSEC file forward/backward in primary-key (user-id) order and displays up to
/// <b>10 users per page</b> on a 24x80 BMS screen, with an optional "Search User ID" filter field and a
/// per-row selection field. Pressing ENTER on a row marked <c>U</c>/<c>u</c> XCTLs to the user-update
/// program (<c>COUSR02C</c>); <c>D</c>/<c>d</c> XCTLs to the user-delete program (<c>COUSR03C</c>). PF3
/// returns to the admin menu (<c>COADM01C</c>). PF7/PF8 page up/down. It is pseudo-conversational: it
/// re-drives itself via <c>RETURN TRANSID('CU00')</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COUSR00C.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-specific <c>CDEMO-CU00-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the USRSEC master is accessed, by primary-key browse:
/// <c>STARTBR</c> = <see cref="UserSecurityRepository.StartBrowse"/> (at-or-after the X(8) user-id RID;
/// LOW-VALUES = from the first row, HIGH-VALUES = past the last row), <c>READNEXT</c> =
/// <see cref="UserSecurityRepository.ReadNext"/>, <c>READPREV</c> =
/// <see cref="UserSecurityRepository.ReadPrevious"/>, <c>ENDBR</c> =
/// <see cref="UserSecurityRepository.EndBrowse"/>. The repository FileStatus is mapped to the CICS RESP the
/// COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13),
/// EndOfFile('10')→ENDFILE(20), anything else→an OTHER/file-error. No write/rewrite/delete is performed.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>B-1 — Abbreviated relation condition <c>IF EIBAID NOT = DFHENTER AND DFHPF7 AND DFHPF3</c> in
/// PROCESS-PAGE-FORWARD (and the analogous <c>NOT = DFHENTER AND DFHPF8</c> in PROCESS-PAGE-BACKWARD)
/// expands to <c>NOT=ENTER AND NOT=PF7 AND NOT=PF3</c>; on PF8 it fires an extra READNEXT (skip one record)
/// before the page loop. Reproduced exactly. source: COUSR00C.cbl:288-290,342-344</item>
/// <item>B-2 — Selection-error message ('Invalid selection. Valid values are U and D') is built into
/// WS-MESSAGE but there is NO early SEND, so an invalid sel char does NOT stop the program; control falls
/// through to the user-id edit + page logic and the message gets overwritten by the page send. Reproduced
/// (no early SEND on the WHEN OTHER selection branch). source: COUSR00C.cbl:210-215</item>
/// <item>B-3 — The program <c>MOVE -1 TO USRIDINL</c> (cursor) repeatedly even on data rows / error paths;
/// the final cursor always lands on the search field. Reproduced. source: COUSR00C.cbl:108,134,214,224,246,268</item>
/// <item>B-4 — STARTBR NOTFND path does <c>SET USER-SEC-EOF</c> + SEND but does NOT set WS-ERR-FLG, so the
/// caller's surrounding <c>IF NOT ERR-FLG-ON</c> still runs the (now EOF-guarded) page loop; the page is
/// left as initialised. Reproduced via the EOF/ERR flag gating. source: COUSR00C.cbl:600-606,284-331</item>
/// <item>B-5 — Each file-access paragraph (STARTBR/READNEXT/READPREV) issues its own
/// <c>PERFORM SEND-USRLST-SCREEN</c> on the NOTFND/ENDFILE/error branch, so on a short/empty file the map
/// is SENT multiple times in one task. The console runtime performs each SEND; only the last is visible.
/// Reproduced (every PERFORM kept). source: COUSR00C.cbl:606,640,674</item>
/// <item>B-6 — There is no numeric/format validation of the search User ID: USRIDINI is moved straight into
/// SEC-USR-ID (X8). Reproduced (no IS NUMERIC class test, unlike COTRN00C). source: COUSR00C.cbl:218-222</item>
/// </list>
/// </remarks>
public sealed class UserListProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COUSR00C.cbl:35-54
    // =============================================================================================
    private const string WS_PGMNAME = "COUSR00C";     // 05 WS-PGMNAME PIC X(08) VALUE 'COUSR00C'. source: :36
    private const string WS_TRANID = "CU00";          // 05 WS-TRANID  PIC X(04) VALUE 'CU00'.     source: :37
    private const string WS_USRSEC_FILE = "USRSEC  "; // 05 WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC  '. source: :39

    private string _wsMessage = "";                   // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON
    private bool ErrFlgOff => !_errFlgOn; // 88 ERR-FLG-OFF

    // 05 WS-USER-SEC-EOF PIC X(01) VALUE 'N'. 88 USER-SEC-EOF='Y' / USER-SEC-NOT-EOF='N'. source: :43-45
    private bool _userSecEof;
    private bool UserSecEof => _userSecEof;       // 88 USER-SEC-EOF
    private bool UserSecNotEof => !_userSecEof;   // 88 USER-SEC-NOT-EOF

    // 05 WS-SEND-ERASE-FLG PIC X(01) VALUE 'Y'. 88 SEND-ERASE-YES='Y' / SEND-ERASE-NO='N'. source: :46-48
    private bool _sendEraseYes = true;
    private bool SendEraseYes => _sendEraseYes;   // 88 SEND-ERASE-YES

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. source: :50-51
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-REC-COUNT / WS-IDX / WS-PAGE-NUM PIC S9(04) COMP. source: :52-54
    // (WS-REC-COUNT, WS-PAGE-NUM declared but never referenced; WS-IDX drives the row loops.)
    private int _wsIdx;

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared screen header / messages.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CU00-INFO (the program-private paging state). source: :66-75
    // =============================================================================================
    // 10 CDEMO-CU00-USRID-FIRST   PIC X(08). source: :67
    private string _cu00UsridFirst = "";
    // 10 CDEMO-CU00-USRID-LAST    PIC X(08). source: :68
    private string _cu00UsridLast = "";
    // 10 CDEMO-CU00-PAGE-NUM      PIC 9(08). source: :69
    private int _cu00PageNum;
    // 10 CDEMO-CU00-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. 88 NEXT-PAGE-YES='Y' / NEXT-PAGE-NO='N'. source: :70-72
    private char _cu00NextPageFlg = 'N';
    private bool NextPageYes => _cu00NextPageFlg == 'Y'; // 88 NEXT-PAGE-YES
    private void SetNextPageYes() => _cu00NextPageFlg = 'Y';
    private void SetNextPageNo() => _cu00NextPageFlg = 'N';
    // 10 CDEMO-CU00-USR-SEL-FLG   PIC X(01). source: :73
    private string _cu00UsrSelFlg = "";
    // 10 CDEMO-CU00-USR-SELECTED  PIC X(08). source: :74
    private string _cu00UsrSelected = "";

    // =============================================================================================
    //  SEC-USR-ID RIDFLD + the SEC-USER-DATA currently read by the browse (CSUSR01Y). source: :81,588,623
    // =============================================================================================
    // SEC-USR-ID X(8) — the STARTBR/READNEXT/READPREV RID. Empty string = LOW-VALUES (browse from first),
    // the 8x0xFF sentinel = HIGH-VALUES (browse past the last record).
    private string _secUsrId = "";
    private const string HighValues8 = "\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF";

    // The SEC-USER-DATA record just read by the browse.
    private UserSecurity? _secUserData;
    private string SecUsrId => _secUserData?.UsrId ?? "";
    private string SecUsrFname => _secUserData?.FirstName ?? "";
    private string SecUsrLname => _secUserData?.LastName ?? "";
    private string SecUsrType => _secUserData?.UsrType ?? "";

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :89-92,114
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private UserSecurityRepository _users = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The USER_SECURITY repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public UserListProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public UserListProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COUSR00C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CU00 -> COUSR00C. source: CSD_TRANSACTIONS.md:84; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COUSR00C.cbl:98-144
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COUSR00 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _users = new UserSecurityRepository(_db.Connection);

        // SET ERR-FLG-OFF / USER-SEC-NOT-EOF / NEXT-PAGE-NO / SEND-ERASE-YES TO TRUE. source: :100-103
        _errFlgOn = false;
        _userSecEof = false;
        SetNextPageNo();
        _sendEraseYes = true;

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COUSR0AO. source: :105-106
        _wsMessage = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        // MOVE -1 TO USRIDINL OF COUSR0AI. source: :108
        _map.Field("USRIDIN").CursorLength = -1;

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :110-112
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :114
            _commArea = ctx.CommArea!;
            RestoreCu00Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :115
                _commArea.SetReenter();              // SET CDEMO-PGM-REENTER TO TRUE. source: :116
                MoveLowValuesToMapOut();             // MOVE LOW-VALUES TO COUSR0AO. source: :117
                ProcessEnterKey(ctx);                // PERFORM PROCESS-ENTER-KEY. source: :118
                SendUsrlstScreen(ctx);               // PERFORM SEND-USRLST-SCREEN. source: :119
            }
            else
            {
                ReceiveUsrlstScreen(ctx);            // PERFORM RECEIVE-USRLST-SCREEN. source: :121
                // EVALUATE EIBAID. source: :122
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);        // WHEN DFHENTER. source: :123-124
                        break;
                    case AidKey.Pf3:
                        _commArea.ToProgram = "COADM01C"; // WHEN DFHPF3 -> MOVE 'COADM01C'. source: :125-126
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :127
                        break;
                    case AidKey.Pf7:
                        ProcessPf7Key(ctx);          // WHEN DFHPF7. source: :128-129
                        break;
                    case AidKey.Pf8:
                        ProcessPf8Key(ctx);          // WHEN DFHPF8. source: :130-131
                        break;
                    default:
                        // WHEN OTHER. source: :132-136
                        _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :133
                        _map.Field("USRIDIN").CursorLength = -1;           // MOVE -1 TO USRIDINL. source: :134
                        _wsMessage = CCDA_MSG_INVALID_KEY;                 // MOVE CCDA-MSG-INVALID-KEY. source: :135
                        SendUsrlstScreen(ctx);                             // PERFORM SEND-USRLST-SCREEN. source: :136
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :141-144
        if (ctx.Outcome is null)
        {
            SaveCu00Info();
            ctx.ReturnTransId(WS_TRANID, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COUSR00C.cbl:149-232
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE — first non-blank selection row wins. source: :151-185
        if (NotSpacesOrLow(SelIn(1)))
        {
            _cu00UsrSelFlg = SelIn(1); _cu00UsrSelected = UsrIdIn(1);   // source: :152-154
        }
        else if (NotSpacesOrLow(SelIn(2)))
        {
            _cu00UsrSelFlg = SelIn(2); _cu00UsrSelected = UsrIdIn(2);   // source: :155-157
        }
        else if (NotSpacesOrLow(SelIn(3)))
        {
            _cu00UsrSelFlg = SelIn(3); _cu00UsrSelected = UsrIdIn(3);   // source: :158-160
        }
        else if (NotSpacesOrLow(SelIn(4)))
        {
            _cu00UsrSelFlg = SelIn(4); _cu00UsrSelected = UsrIdIn(4);   // source: :161-163
        }
        else if (NotSpacesOrLow(SelIn(5)))
        {
            _cu00UsrSelFlg = SelIn(5); _cu00UsrSelected = UsrIdIn(5);   // source: :164-166
        }
        else if (NotSpacesOrLow(SelIn(6)))
        {
            _cu00UsrSelFlg = SelIn(6); _cu00UsrSelected = UsrIdIn(6);   // source: :167-169
        }
        else if (NotSpacesOrLow(SelIn(7)))
        {
            _cu00UsrSelFlg = SelIn(7); _cu00UsrSelected = UsrIdIn(7);   // source: :170-172
        }
        else if (NotSpacesOrLow(SelIn(8)))
        {
            _cu00UsrSelFlg = SelIn(8); _cu00UsrSelected = UsrIdIn(8);   // source: :173-175
        }
        else if (NotSpacesOrLow(SelIn(9)))
        {
            _cu00UsrSelFlg = SelIn(9); _cu00UsrSelected = UsrIdIn(9);   // source: :176-178
        }
        else if (NotSpacesOrLow(SelIn(10)))
        {
            _cu00UsrSelFlg = SelIn(10); _cu00UsrSelected = UsrIdIn(10); // source: :179-181
        }
        else
        {
            _cu00UsrSelFlg = "";       // WHEN OTHER -> MOVE SPACES. source: :182-184
            _cu00UsrSelected = "";
        }

        // IF (USR-SEL-FLG NOT = SPACES AND LOW-VALUES) AND (USR-SELECTED NOT = SPACES AND LOW-VALUES). source: :187-216
        if (NotSpacesOrLow(_cu00UsrSelFlg) && NotSpacesOrLow(_cu00UsrSelected))
        {
            // EVALUATE CDEMO-CU00-USR-SEL-FLG. source: :189-215
            string flg = _cu00UsrSelFlg.Length > 0 ? _cu00UsrSelFlg.Substring(0, 1) : "";
            if (flg == "U" || flg == "u")
            {
                // WHEN 'U' / 'u' — XCTL to the user update program. source: :190-199
                _commArea.ToProgram = "COUSR02C";   // MOVE 'COUSR02C' TO CDEMO-TO-PROGRAM. source: :192
                _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :193
                _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :194
                _commArea.SetFirstEntry();          // MOVE 0 TO CDEMO-PGM-CONTEXT. source: :195
                // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :196-199
                SaveCu00Info();
                ctx.Xctl("COUSR02C", _commArea);
                return;
            }
            else if (flg == "D" || flg == "d")
            {
                // WHEN 'D' / 'd' — XCTL to the user delete program. source: :200-209
                _commArea.ToProgram = "COUSR03C";   // MOVE 'COUSR03C' TO CDEMO-TO-PROGRAM. source: :202
                _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :203
                _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :204
                _commArea.SetFirstEntry();          // MOVE 0 TO CDEMO-PGM-CONTEXT. source: :205
                // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :206-209
                SaveCu00Info();
                ctx.Xctl("COUSR03C", _commArea);
                return;
            }
            else
            {
                // WHEN OTHER — build the invalid-selection message. B-2: there is NO SEND here, so the
                // program does NOT stop; control falls through to the user-id edit + page logic and the
                // message gets overwritten by the page send. source: :210-215
                _wsMessage = "Invalid selection. Valid values are U and D"; // source: :211-213
                _map.Field("USRIDIN").CursorLength = -1;                    // MOVE -1 TO USRIDINL. source: :214
            }
        }

        // IF USRIDINI = SPACES OR LOW-VALUES -> MOVE LOW-VALUES TO SEC-USR-ID; ELSE MOVE USRIDINI. source: :218-222
        // B-6: there is NO numeric/format validation here, unlike COTRN00C.
        string usridin = _map.Field("USRIDIN").Value;
        if (IsSpacesOrLowValues(usridin))
            _secUsrId = ""; // MOVE LOW-VALUES TO SEC-USR-ID. source: :219
        else
            _secUsrId = PadX(usridin, 8); // MOVE USRIDINI (X(8)) TO SEC-USR-ID (X(8)). source: :221

        // MOVE -1 TO USRIDINL. source: :224
        _map.Field("USRIDIN").CursorLength = -1;

        // MOVE 0 TO CDEMO-CU00-PAGE-NUM; PERFORM PROCESS-PAGE-FORWARD. source: :227-228
        _cu00PageNum = 0;
        ProcessPageForward(ctx);

        // IF NOT ERR-FLG-ON -> MOVE SPACE TO USRIDINO. source: :230-232
        if (!ErrFlgOn)
            _map.Field("USRIDIN").SetValue(" ", setMdt: false);
    }

    // =============================================================================================
    //  PROCESS-PF7-KEY — source: COUSR00C.cbl:237-255
    // =============================================================================================
    private void ProcessPf7Key(CicsContext ctx)
    {
        // IF CDEMO-CU00-USRID-FIRST = SPACES OR LOW-VALUES -> LOW-VALUES else USRID-FIRST. source: :239-243
        _secUsrId = IsSpacesOrLowValues(_cu00UsridFirst) ? "" : _cu00UsridFirst;

        SetNextPageYes();                              // SET NEXT-PAGE-YES TO TRUE. source: :245
        _map.Field("USRIDIN").CursorLength = -1;       // MOVE -1 TO USRIDINL. source: :246

        if (_cu00PageNum > 1)
        {
            ProcessPageBackward(ctx);                  // PERFORM PROCESS-PAGE-BACKWARD. source: :249
        }
        else
        {
            _wsMessage = "You are already at the top of the page..."; // source: :251-252
            _sendEraseYes = false;                     // SET SEND-ERASE-NO TO TRUE. source: :253
            SendUsrlstScreen(ctx);                     // PERFORM SEND-USRLST-SCREEN. source: :254
        }
    }

    // =============================================================================================
    //  PROCESS-PF8-KEY — source: COUSR00C.cbl:260-277
    // =============================================================================================
    private void ProcessPf8Key(CicsContext ctx)
    {
        // IF CDEMO-CU00-USRID-LAST = SPACES OR LOW-VALUES -> HIGH-VALUES else USRID-LAST. source: :262-266
        _secUsrId = IsSpacesOrLowValues(_cu00UsridLast) ? HighValues8 : _cu00UsridLast;

        _map.Field("USRIDIN").CursorLength = -1;       // MOVE -1 TO USRIDINL. source: :268

        if (NextPageYes)
        {
            ProcessPageForward(ctx);                   // PERFORM PROCESS-PAGE-FORWARD. source: :271
        }
        else
        {
            _wsMessage = "You are already at the bottom of the page..."; // source: :273-274
            _sendEraseYes = false;                     // SET SEND-ERASE-NO TO TRUE. source: :275
            SendUsrlstScreen(ctx);                     // PERFORM SEND-USRLST-SCREEN. source: :276
        }
    }

    // =============================================================================================
    //  PROCESS-PAGE-FORWARD — source: COUSR00C.cbl:282-331
    // =============================================================================================
    private void ProcessPageForward(CicsContext ctx)
    {
        StartbrUserSecFile(ctx);               // PERFORM STARTBR-USER-SEC-FILE. source: :284

        if (!ErrFlgOn)                         // IF NOT ERR-FLG-ON. source: :286
        {
            // B-1: IF EIBAID NOT = DFHENTER AND DFHPF7 AND DFHPF3 -> abbreviated condition expands to
            //   NOT=ENTER AND NOT=PF7 AND NOT=PF3. On PF8 (and any other key) this fires one extra
            //   READNEXT to step over the boundary record. source: :288-290
            if (ctx.EibAid != AidKey.Enter && ctx.EibAid != AidKey.Pf7 && ctx.EibAid != AidKey.Pf3)
                ReadnextUserSecFile(ctx);      // PERFORM READNEXT-USER-SEC-FILE. source: :289

            // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF -> clear the 10 display rows. source: :292-296
            if (UserSecNotEof && ErrFlgOff)
            {
                for (_wsIdx = 1; _wsIdx <= 10; _wsIdx++) // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL > 10. source: :293
                    InitializeUserData();                 // PERFORM INITIALIZE-USER-DATA. source: :294
            }

            _wsIdx = 1;                        // MOVE 1 TO WS-IDX. source: :298

            // PERFORM UNTIL WS-IDX >= 11 OR USER-SEC-EOF OR ERR-FLG-ON. source: :300-306
            while (!(_wsIdx >= 11 || UserSecEof || ErrFlgOn))
            {
                ReadnextUserSecFile(ctx);      // PERFORM READNEXT-USER-SEC-FILE. source: :301
                if (UserSecNotEof && ErrFlgOff) // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF. source: :302
                {
                    PopulateUserData();          // PERFORM POPULATE-USER-DATA. source: :303
                    _wsIdx = _wsIdx + 1;         // COMPUTE WS-IDX = WS-IDX + 1. source: :304
                }
            }

            // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF — peek for a next page. source: :308-323
            if (UserSecNotEof && ErrFlgOff)
            {
                _cu00PageNum = _cu00PageNum + 1; // COMPUTE CDEMO-CU00-PAGE-NUM = + 1. source: :309-310
                ReadnextUserSecFile(ctx);        // PERFORM READNEXT-USER-SEC-FILE. source: :311
                if (UserSecNotEof && ErrFlgOff)  // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF. source: :312
                    SetNextPageYes();            // SET NEXT-PAGE-YES TO TRUE. source: :313
                else
                    SetNextPageNo();             // SET NEXT-PAGE-NO TO TRUE. source: :315
            }
            else
            {
                SetNextPageNo();                 // SET NEXT-PAGE-NO TO TRUE. source: :318
                if (_wsIdx > 1)                  // IF WS-IDX > 1. source: :319
                    _cu00PageNum = _cu00PageNum + 1; // COMPUTE CDEMO-CU00-PAGE-NUM = + 1. source: :320-321
            }

            EndbrUserSecFile();                  // PERFORM ENDBR-USER-SEC-FILE. source: :325

            // MOVE CDEMO-CU00-PAGE-NUM TO PAGENUMI; MOVE SPACE TO USRIDINO; PERFORM SEND. source: :327-329
            // CDEMO-CU00-PAGE-NUM PIC 9(08) -> PAGENUMI PIC X(8): the numeric-to-alphanumeric MOVE copies the
            // 8 zoned digits left-justified, so page 1 renders as "00000001" (zero-filled), not "1".
            _map.Field("PAGENUM").SetValue(_cu00PageNum.ToString("D8"), setMdt: false);
            _map.Field("USRIDIN").SetValue(" ", setMdt: false);
            SendUsrlstScreen(ctx);
        }
    }

    // =============================================================================================
    //  PROCESS-PAGE-BACKWARD — source: COUSR00C.cbl:336-379
    // =============================================================================================
    private void ProcessPageBackward(CicsContext ctx)
    {
        StartbrUserSecFile(ctx);               // PERFORM STARTBR-USER-SEC-FILE. source: :338

        if (!ErrFlgOn)                         // IF NOT ERR-FLG-ON. source: :340
        {
            // B-1: IF EIBAID NOT = DFHENTER AND DFHPF8 -> NOT=ENTER AND NOT=PF8. On PF7 this fires an extra
            //   READPREV to step over the boundary record. source: :342-344
            if (ctx.EibAid != AidKey.Enter && ctx.EibAid != AidKey.Pf8)
                ReadprevUserSecFile(ctx);      // PERFORM READPREV-USER-SEC-FILE. source: :343

            // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF -> clear the 10 display rows. source: :346-350
            if (UserSecNotEof && ErrFlgOff)
            {
                for (_wsIdx = 1; _wsIdx <= 10; _wsIdx++) // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL > 10. source: :347
                    InitializeUserData();                 // PERFORM INITIALIZE-USER-DATA. source: :348
            }

            _wsIdx = 10;                       // MOVE 10 TO WS-IDX. source: :352

            // PERFORM UNTIL WS-IDX <= 0 OR USER-SEC-EOF OR ERR-FLG-ON. source: :354-360
            while (!(_wsIdx <= 0 || UserSecEof || ErrFlgOn))
            {
                ReadprevUserSecFile(ctx);      // PERFORM READPREV-USER-SEC-FILE. source: :355
                if (UserSecNotEof && ErrFlgOff) // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF. source: :356
                {
                    PopulateUserData();          // PERFORM POPULATE-USER-DATA. source: :357
                    _wsIdx = _wsIdx - 1;         // COMPUTE WS-IDX = WS-IDX - 1. source: :358
                }
            }

            // IF USER-SEC-NOT-EOF AND ERR-FLG-OFF — adjust the page number. source: :362-372
            if (UserSecNotEof && ErrFlgOff)
            {
                ReadprevUserSecFile(ctx);        // PERFORM READPREV-USER-SEC-FILE. source: :363
                if (NextPageYes)                 // IF NEXT-PAGE-YES. source: :364
                {
                    if (UserSecNotEof && ErrFlgOff && _cu00PageNum > 1) // source: :365-366
                        _cu00PageNum = _cu00PageNum - 1; // SUBTRACT 1 FROM CDEMO-CU00-PAGE-NUM. source: :367
                    else
                        _cu00PageNum = 1;        // MOVE 1 TO CDEMO-CU00-PAGE-NUM. source: :369
                }
            }

            EndbrUserSecFile();                  // PERFORM ENDBR-USER-SEC-FILE. source: :374

            // MOVE CDEMO-CU00-PAGE-NUM TO PAGENUMI; PERFORM SEND. source: :376-377
            // CDEMO-CU00-PAGE-NUM PIC 9(08) -> PAGENUMI PIC X(8): zero-filled 8-digit render (e.g. "00000002").
            _map.Field("PAGENUM").SetValue(_cu00PageNum.ToString("D8"), setMdt: false);
            SendUsrlstScreen(ctx);
        }
    }

    // =============================================================================================
    //  POPULATE-USER-DATA — source: COUSR00C.cbl:384-441
    // =============================================================================================
    private void PopulateUserData()
    {
        // EVALUATE WS-IDX -> stamp the row's USRID / FNAME / LNAME / UTYPE fields. source: :386-441
        int n = _wsIdx;
        if (n is < 1 or > 10) return; // WHEN OTHER -> CONTINUE. source: :439-440

        _map.Field($"USRID{n:D2}").SetValue(SecUsrId, setMdt: false);    // MOVE SEC-USR-ID TO USRIDnnI. source: :388,394,...
        if (n == 1) _cu00UsridFirst = SecUsrId;  // WHEN 1 also -> CDEMO-CU00-USRID-FIRST. source: :388-389
        if (n == 10) _cu00UsridLast = SecUsrId;  // WHEN 10 also -> CDEMO-CU00-USRID-LAST. source: :434-435
        _map.Field($"FNAME{n:D2}").SetValue(SecUsrFname, setMdt: false); // MOVE SEC-USR-FNAME TO FNAMEnnI. source: :390,...
        _map.Field($"LNAME{n:D2}").SetValue(SecUsrLname, setMdt: false); // MOVE SEC-USR-LNAME TO LNAMEnnI. source: :391,...
        _map.Field($"UTYPE{n:D2}").SetValue(SecUsrType, setMdt: false);  // MOVE SEC-USR-TYPE TO UTYPEnnI. source: :392,...
    }

    // =============================================================================================
    //  INITIALIZE-USER-DATA — source: COUSR00C.cbl:446-501
    // =============================================================================================
    private void InitializeUserData()
    {
        // EVALUATE WS-IDX -> MOVE SPACES to the row's four display fields. source: :448-501
        int n = _wsIdx;
        if (n is < 1 or > 10) return; // WHEN OTHER -> CONTINUE. source: :499-500
        _map.Field($"USRID{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO USRIDnnI. source: :450,...
        _map.Field($"FNAME{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO FNAMEnnI. source: :451,...
        _map.Field($"LNAME{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO LNAMEnnI. source: :452,...
        _map.Field($"UTYPE{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO UTYPEnnI. source: :453,...
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COUSR00C.cbl:506-517
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :508-510
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :511
        _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :512
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :513

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :514-517
        SaveCu00Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-USRLST-SCREEN — source: COUSR00C.cbl:522-544
    // =============================================================================================
    private void SendUsrlstScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                     // PERFORM POPULATE-HEADER-INFO. source: :524

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false);   // MOVE WS-MESSAGE TO ERRMSGO. source: :526

        // IF SEND-ERASE-YES -> SEND ... ERASE CURSOR; ELSE SEND ... CURSOR (no erase). source: :528-544
        ctx.SendMap("COUSR0A", "COUSR00", _map, new SendMapOptions
        {
            Erase = SendEraseYes,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO USRIDINL set throughout.
        });
        _wsRespCd = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-USRLST-SCREEN — source: COUSR00C.cbl:549-557
    // =============================================================================================
    private void ReceiveUsrlstScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COUSR0A') MAPSET('COUSR00') INTO(COUSR0AI) RESP. source: :551-557
        ctx.ReceiveMap("COUSR0A", "COUSR00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COUSR00C.cbl:562-581
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :564
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :566
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :567
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);    // MOVE WS-TRANID  TO TRNNAMEO. source: :568
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :569

        // CURDATEO = mm/dd/yy (year last two digits). source: :571-575
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :577-581
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  STARTBR-USER-SEC-FILE — source: COUSR00C.cbl:586-614
    // =============================================================================================
    private void StartbrUserSecFile(CicsContext ctx)
    {
        // EXEC CICS STARTBR DATASET(WS-USRSEC-FILE) RIDFLD(SEC-USR-ID) RESP. source: :588-595
        // STARTBR is GTEQ (the GTEQ is commented but CICS default is GTEQ): position at-or-after SEC-USR-ID.
        // LOW-VALUES (empty) -> from the first record; HIGH-VALUES -> past the last record (NOTFND).
        _ = WS_USRSEC_FILE; // dataset name (fixed) — repository is keyed by usr_id.
        if (_secUsrId.Length == 0)
            _users.StartBrowse();
        else
            _users.StartBrowse(_secUsrId);
        string fileStatus = PeekForwardExists() ? FileStatus.Ok : FileStatus.RecordNotFound;
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :597-614
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :598-599
                break;
            case Resp.NotFnd: // WHEN DFHRESP(NOTFND). source: :600-606
                // NOTE (B-4): unlike COTRN00C, NO 'MOVE Y TO WS-ERR-FLG' here, so the caller's
                //   IF NOT ERR-FLG-ON still runs the EOF-guarded page loop.
                _userSecEof = true;                                   // SET USER-SEC-EOF TO TRUE. source: :602
                _wsMessage = "You are at the top of the page...";     // source: :603-604
                _map.Field("USRIDIN").CursorLength = -1;              // MOVE -1 TO USRIDINL. source: :605
                SendUsrlstScreen(ctx);                                // PERFORM SEND-USRLST-SCREEN. source: :606
                break;
            default: // WHEN OTHER. source: :607-613
                _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :609
                _wsMessage = "Unable to lookup User...";              // source: :610-611
                _map.Field("USRIDIN").CursorLength = -1;              // MOVE -1 TO USRIDINL. source: :612
                SendUsrlstScreen(ctx);                                // PERFORM SEND-USRLST-SCREEN. source: :613
                break;
        }
    }

    // =============================================================================================
    //  READNEXT-USER-SEC-FILE — source: COUSR00C.cbl:619-648
    // =============================================================================================
    private void ReadnextUserSecFile(CicsContext ctx)
    {
        // EXEC CICS READNEXT DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA) RIDFLD(SEC-USR-ID) RESP. source: :621-629
        string fileStatus = _users.ReadNext(out _secUserData);
        if (fileStatus == FileStatus.Ok && _secUserData is not null)
            _secUsrId = _secUserData.UsrId; // RIDFLD is updated with the key just read. source: :625
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :631-648
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :632-633
                break;
            case Resp.EndFile: // WHEN DFHRESP(ENDFILE). source: :634-640
                _userSecEof = true;                                        // SET USER-SEC-EOF TO TRUE. source: :636
                _wsMessage = "You have reached the bottom of the page..."; // source: :637-638
                _map.Field("USRIDIN").CursorLength = -1;                   // MOVE -1 TO USRIDINL. source: :639
                SendUsrlstScreen(ctx);                                     // PERFORM SEND-USRLST-SCREEN. source: :640
                break;
            default: // WHEN OTHER. source: :641-647
                _errFlgOn = true;                                          // MOVE 'Y' TO WS-ERR-FLG. source: :643
                _wsMessage = "Unable to lookup User...";                  // source: :644-645
                _map.Field("USRIDIN").CursorLength = -1;                   // MOVE -1 TO USRIDINL. source: :646
                SendUsrlstScreen(ctx);                                     // PERFORM SEND-USRLST-SCREEN. source: :647
                break;
        }
    }

    // =============================================================================================
    //  READPREV-USER-SEC-FILE — source: COUSR00C.cbl:653-682
    // =============================================================================================
    private void ReadprevUserSecFile(CicsContext ctx)
    {
        // EXEC CICS READPREV DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA) RIDFLD(SEC-USR-ID) RESP. source: :655-663
        string fileStatus = _users.ReadPrevious(out _secUserData);
        if (fileStatus == FileStatus.Ok && _secUserData is not null)
            _secUsrId = _secUserData.UsrId; // RIDFLD updated with the key just read. source: :659
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :665-682
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :666-667
                break;
            case Resp.EndFile: // WHEN DFHRESP(ENDFILE). source: :668-674
                _userSecEof = true;                                   // SET USER-SEC-EOF TO TRUE. source: :670
                _wsMessage = "You have reached the top of the page..."; // source: :671-672
                _map.Field("USRIDIN").CursorLength = -1;              // MOVE -1 TO USRIDINL. source: :673
                SendUsrlstScreen(ctx);                                // PERFORM SEND-USRLST-SCREEN. source: :674
                break;
            default: // WHEN OTHER. source: :675-681
                _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :677
                _wsMessage = "Unable to lookup User...";              // source: :678-679
                _map.Field("USRIDIN").CursorLength = -1;              // MOVE -1 TO USRIDINL. source: :680
                SendUsrlstScreen(ctx);                                // PERFORM SEND-USRLST-SCREEN. source: :681
                break;
        }
    }

    // =============================================================================================
    //  ENDBR-USER-SEC-FILE — source: COUSR00C.cbl:687-691
    // =============================================================================================
    private void EndbrUserSecFile() => _users.EndBrowse(); // EXEC CICS ENDBR DATASET(WS-USRSEC-FILE). source: :689

    // =============================================================================================
    //  STARTBR positioning helper — emulate CICS RESP after STARTBR GTEQ.
    // =============================================================================================
    /// <summary>
    /// CICS STARTBR with GTEQ positions the browse and returns NORMAL when at least one record is at-or-after
    /// the RID, NOTFND otherwise. The repository browse is lazy, so we peek one row forward to learn whether
    /// any record exists, then re-position the cursor at the same start key for the subsequent READNEXT/READPREV.
    /// </summary>
    private bool PeekForwardExists()
    {
        string st = _users.ReadNext(out _);
        // Re-position at-or-after the same RID so the caller's first READNEXT returns the same record,
        // and a first READPREV returns the record at-or-before it (matching CICS browse after STARTBR).
        if (_secUsrId.Length == 0) _users.StartBrowse();
        else _users.StartBrowse(_secUsrId);
        return st == FileStatus.Ok;
    }

    // =============================================================================================
    //  WS-RESP-CD mapper — repository FileStatus -> CICS RESP. source: EVALUATE WS-RESP-CD branches.
    // =============================================================================================
    private void SetResp(string fileStatus)
    {
        _wsRespCd = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.EndOfFile => (int)Resp.EndFile,     // '10' -> DFHRESP(ENDFILE)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            FileStatus.DuplicateKey => (int)Resp.DupRec,   // '02' -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  Symbolic-map input readers — SEL000nI / USRIDnnI.
    // =============================================================================================
    private string SelIn(int n) => _map.Field($"SEL{n:D4}").Value;     // SEL0001I..SEL0010I
    private string UsrIdIn(int n) => _map.Field($"USRID{n:D2}").Value; // USRID01I..USRID10I

    /// <summary>MOVE LOW-VALUES TO COUSR0AO — blank every named output field + clear per-turn overrides. source: :117</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  CDEMO-CU00-INFO (de)serialize — carried across turns in the COMMAREA's unused customer slots.
    //  source: COUSR00C.cbl:66-75,114,141-144
    // =============================================================================================
    // COUSR00C never reads/writes CDEMO-CUSTOMER-INFO; the trailer is packed there so the paging state
    // (USRID-FIRST/LAST, PAGE-NUM, NEXT-PAGE-FLG, USR-SEL-FLG, USR-SELECTED) round-trips losslessly.
    // CDEMO-CU00-INFO aliases CDEMO-CU02-INFO / CDEMO-CU03-INFO at identical physical COMMAREA bytes in the
    // COBOL, so when a list row is selected ('U' -> COUSR02C update, 'D' -> COUSR03C delete) the XCTL hands
    // that program the chosen user id via USR-SEL-FLG (byte 25) + USR-SELECTED (bytes 26-33). The pack layout
    // below MUST match COUSR02C/COUSR03C Save/RestoreCuNNInfo or the selection is lost across the XCTL.
    // Pack layout into CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   USRID-FIRST X(8) | USRID-LAST X(8) | PAGE-NUM 9(8) | NEXT X(1) | SEL-FLG X(1) | USR-SELECTED X(8).
    private void SaveCu00Info()
    {
        string packed =
            PadX(_cu00UsridFirst, 8) +
            PadX(_cu00UsridLast, 8) +
            Zoned(_cu00PageNum, 8) +
            (_cu00NextPageFlg == '\0' ? 'N' : _cu00NextPageFlg) +
            PadX(_cu00UsrSelFlg, 1) +
            PadX(_cu00UsrSelected, 8);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCu00Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        packed = PadX(packed, 75);
        _cu00UsridFirst = packed.Substring(0, 8).TrimEnd();
        _cu00UsridLast = packed.Substring(8, 8).TrimEnd();
        _cu00PageNum = (int)ParseLong(packed.Substring(16, 8));
        char nx = packed[24];
        _cu00NextPageFlg = nx == 'Y' ? 'Y' : 'N';
        _cu00UsrSelFlg = packed.Substring(25, 1).TrimEnd();
        _cu00UsrSelected = packed.Substring(26, 8).TrimEnd();
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "NOT = SPACES AND LOW-VALUES" guard.</summary>
    private static bool NotSpacesOrLow(string? s) => !IsSpacesOrLowValues(s);

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL).</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>Renders a numeric as a zero-padded zoned-decimal DISPLAY string of width <paramref name="width"/>.</summary>
    private static string Zoned(long value, int width)
    {
        ulong mag = value < 0 ? (ulong)(-value) : (ulong)value;
        string s = mag.ToString();
        if (s.Length >= width) return s[^width..];
        return s.PadLeft(width, '0');
    }

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    /// <summary>Parses a digit string (ignoring non-digits) to a long; null/empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    // =============================================================================================
    //  BMS map builder — COUSR0A in mapset COUSR00 (24x80).
    //  source: app/bms/COUSR00.bms:19-460 / SCREEN_COUSR00.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COUSR00.bms:26.</summary>
    public const string MapName = "COUSR0A";

    /// <summary>The DFHMSD mapset name. source: COUSR00.bms:19.</summary>
    public const string MapsetName = "COUSR00";

    /// <summary>
    /// Constructs the <c>COUSR0A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The keyable fields are <c>USRIDIN</c>
    /// (6,21) L8 and the ten per-row selection fields <c>SEL0001..SEL0010</c>; no <c>IC</c> is coded, so
    /// CICS defaults the cursor to the first unprotected field (<c>USRIDIN</c>). No PICIN/PICOUT clauses
    /// appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                              // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                 // bms:34-37
            Out("TITLE01", 1, 21, 40, AskipFset, BmsColor.Yellow),             // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                             // bms:42-46
            OutInit("CURDATE", 1, 71, 8, AskipFset, BmsColor.Blue, "mm/dd/yy"),// bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                              // bms:52-56
            Out("PGMNAME", 2, 7, 8, AskipFset, BmsColor.Blue),                 // bms:57-60
            Out("TITLE02", 2, 21, 40, AskipFset, BmsColor.Yellow),             // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                             // bms:65-69
            OutInit("CURTIME", 2, 71, 8, AskipFset, BmsColor.Blue, "hh:mm:ss"),// bms:70-74

            // ----- 'List Users' heading + Page nbr (bms:75-89) -----
            LitAttr(4, 35, 10, AskipBrt, BmsColor.Neutral, "List Users"),       // bms:75-79
            LitAttr(4, 65, 5, AskipBrt, BmsColor.Turquoise, "Page:"),           // bms:80-84
            OutInit("PAGENUM", 4, 71, 8, AskipFset, BmsColor.Blue, " "),        // bms:85-89

            // ----- Search User ID label + input field (bms:90-102) -----
            Lit(6, 5, 15, BmsColor.Turquoise, "Search User ID:"),              // bms:90-94
            // USRIDIN: ATTRB=(FSET,NORM,UNPROT) GREEN UNDERLINE — first unprotected field (default cursor).
            new ScreenField
            {
                Name = "USRIDIN", Row = 6, Col = 21, Length = 8,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:95-99
            Stopper(6, 30),                                                    // bms:100-102

            // ----- grid column headers (NEUTRAL) (bms:103-127) -----
            LitAttr(8, 5, 3, Askip, BmsColor.Neutral, "Sel"),                 // bms:103-107
            LitAttr(8, 12, 8, Askip, BmsColor.Neutral, "User ID "),          // bms:108-112
            LitAttr(8, 24, 20, Askip, BmsColor.Neutral, "     First Name     "), // bms:113-117
            LitAttr(8, 48, 20, Askip, BmsColor.Neutral, "     Last Name      "), // bms:118-122
            LitAttr(8, 72, 4, Askip, BmsColor.Neutral, "Type"),               // bms:123-127

            // ----- grid rules (bms:128-152) -----
            LitAttr(9, 5, 3, Askip, BmsColor.Neutral, "---"),                 // bms:128-132
            LitAttr(9, 12, 8, Askip, BmsColor.Neutral, "--------"),           // bms:133-137
            LitAttr(9, 24, 20, Askip, BmsColor.Neutral, "--------------------"), // bms:138-142
            LitAttr(9, 48, 20, Askip, BmsColor.Neutral, "--------------------"), // bms:143-147
            LitAttr(9, 72, 4, Askip, BmsColor.Neutral, "----"),               // bms:148-152

            // ----- 10 detail rows (bms:153-442) -----
            // Each row: SEL000n (UNPROT GREEN UNDERLINE L1 INITIAL ' '), stopper, USRIDnn / FNAMEnn /
            // LNAMEnn / UTYPEnn (ASKIP FSET BLUE, INITIAL ' ').
        };

        for (int n = 1; n <= 10; n++)
        {
            int row = 9 + n; // rows 10..19
            fields.Add(RowSel($"SEL{n:D4}", row));            // SEL000n (row,6) L1. bms:153,182,...
            fields.Add(Stopper(row, 8));                      // (row,8) L0 stopper. bms:159,...
            fields.Add(RowOut($"USRID{n:D2}", row, 12, 8));   // USRIDnn (row,12) L8. bms:162,...
            fields.Add(RowOut($"FNAME{n:D2}", row, 24, 20));  // FNAMEnn (row,24) L20. bms:167,...
            fields.Add(RowOut($"LNAME{n:D2}", row, 48, 20));  // LNAMEnn (row,48) L20. bms:172,...
            fields.Add(RowOut($"UTYPE{n:D2}", row, 73, 1));   // UTYPEnn (row,73) L1. bms:177,...
        }

        // ----- instruction + error + footer (bms:443-458) -----
        fields.Add(LitAttr(21, 12, 56, AskipBrt, BmsColor.Neutral,
            "Type 'U' to Update or 'D' to Delete a User from the list"));     // bms:443-448
        fields.Add(Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red));     // bms:449-452
        fields.Add(LitAttr(24, 1, 48, Askip, BmsColor.Yellow,
            "ENTER=Continue  F3=Back  F7=Backward  F8=Forward"));             // bms:453-458

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) and the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>Per-row selection field SEL000n: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L1 INITIAL ' '.</summary>
    private static ScreenField RowSel(string name, int row) =>
        new()
        {
            Name = name, Row = row, Col = 6, Length = 1,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
            Color = BmsColor.Green, Hilight = BmsHilight.Underline, Value = " ",
        };

    /// <summary>Per-row data field (USRIDnn/FNAMEnn/LNAMEnn/UTYPEnn): ATTRB=(ASKIP,FSET,NORM) BLUE INITIAL ' '.</summary>
    private static ScreenField RowOut(string name, int row, int col, int len) =>
        new()
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = AskipFset, Color = BmsColor.Blue, Value = " ",
        };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, ATTRB=(ASKIP,NORM)).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };
}
