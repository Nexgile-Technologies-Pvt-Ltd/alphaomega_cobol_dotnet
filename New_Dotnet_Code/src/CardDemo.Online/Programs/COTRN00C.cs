using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COTRN00C</c> — the "List Transactions" paged
/// browse (TRANSID <c>CT00</c>, BMS map <c>COTRN0A</c> / mapset <c>COTRN00</c>).
/// </summary>
/// <remarks>
/// <para>
/// COTRN00C browses the TRANSACT (TRANSACTION) file forward/backward in primary-key (tran-id) order and
/// displays up to <b>10 transactions per page</b> on a 24x80 BMS screen, with an optional "Search Tran ID"
/// filter field and a per-row selection field. Pressing ENTER on a row marked <c>S</c>/<c>s</c> XCTLs to
/// the transaction-detail program (<c>COTRN01C</c>). PF3 returns to the previous program
/// (<c>COMEN01C</c>). PF7/PF8 page up/down. It is pseudo-conversational: it re-drives itself via
/// <c>RETURN TRANSID('CT00')</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COTRN00C.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-specific <c>CDEMO-CT00-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the TRANSACT master is accessed, by primary-key browse:
/// <c>STARTBR</c> = <see cref="TransactionRepository.StartBrowse"/> (at-or-after the X(16) tran-id RID;
/// LOW-VALUES = from the first row, HIGH-VALUES = past the last row), <c>READNEXT</c> =
/// <see cref="TransactionRepository.ReadNext"/>, <c>READPREV</c> =
/// <see cref="TransactionRepository.ReadPrevious"/>, <c>ENDBR</c> =
/// <see cref="TransactionRepository.EndBrowse"/>. The repository FileStatus is mapped to the CICS RESP the
/// COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13),
/// EndOfFile('10')→ENDFILE(20), anything else→an OTHER/file-error. No write/rewrite/delete is performed.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>B-1 — Abbreviated relation condition <c>IF EIBAID NOT = DFHENTER AND DFHPF7 AND DFHPF3</c> in
/// PROCESS-PAGE-FORWARD (and the analogous <c>NOT = DFHENTER AND DFHPF8</c> in PROCESS-PAGE-BACKWARD)
/// expands to <c>NOT=ENTER AND NOT=PF7 AND NOT=PF3</c>; on PF8 it fires an extra READNEXT (skip one record)
/// before the page loop. Reproduced exactly. source: COTRN00C.cbl:285-287,339-341</item>
/// <item>B-2 — Selection-error message ('Invalid selection. Valid value is S') is built into WS-MESSAGE but
/// the SEND is commented out, so an invalid sel char does NOT stop the program; it falls through and
/// re-lists, and the message is overwritten by the page logic. Reproduced (no early SEND). source:
/// COTRN00C.cbl:196-203</item>
/// <item>B-3 — In SEND/RECEIVE the program <c>MOVE -1 TO TRNIDINL</c> (cursor) repeatedly even on the data
/// rows / error paths; the final cursor always lands on the search field. Reproduced. source:
/// COTRN00C.cbl:105,131,201,216,221,243,265</item>
/// <item>B-4 — STARTBR NOTFND path does <c>SET TRANSACT-EOF</c> + SEND but the surrounding
/// <c>IF NOT ERR-FLG-ON</c> in the caller still runs the (now EOF-guarded) loop; the page is left as
/// initialised. Reproduced via the EOF/ERR flag gating. source: COTRN00C.cbl:602-619,283-328</item>
/// <item>B-5 — Each file-access paragraph (STARTBR/READNEXT/READPREV) issues its own
/// <c>PERFORM SEND-TRNLST-SCREEN</c> on the NOTFND/ENDFILE/error branch, so on a short/empty file the map
/// is SENT multiple times in one task. The console runtime performs each SEND; only the last is visible.
/// Reproduced (every PERFORM kept). source: COTRN00C.cbl:608-618,642-652,676-686</item>
/// </list>
/// </remarks>
public sealed class Cotrn00c : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COTRN00C.cbl:35-57
    // =============================================================================================
    private const string WS_PGMNAME = "COTRN00C";       // 05 WS-PGMNAME PIC X(08) VALUE 'COTRN00C'. source: :36
    private const string WS_TRANID = "CT00";            // 05 WS-TRANID  PIC X(04) VALUE 'CT00'.     source: :37
    private const string WS_TRANSACT_FILE = "TRANSACT"; // 05 WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT'. source: :39

    private string _wsMessage = "";                     // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON
    private bool ErrFlgOff => !_errFlgOn; // 88 ERR-FLG-OFF

    // 05 WS-TRANSACT-EOF PIC X(01) VALUE 'N'. 88 TRANSACT-EOF='Y' / TRANSACT-NOT-EOF='N'. source: :43-45
    private bool _transactEof;
    private bool TransactEof => _transactEof;       // 88 TRANSACT-EOF
    private bool TransactNotEof => !_transactEof;   // 88 TRANSACT-NOT-EOF

    // 05 WS-SEND-ERASE-FLG PIC X(01) VALUE 'Y'. 88 SEND-ERASE-YES='Y' / SEND-ERASE-NO='N'. source: :46-48
    private bool _sendEraseYes = true;
    private bool SendEraseYes => _sendEraseYes;     // 88 SEND-ERASE-YES

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. source: :50-51
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-REC-COUNT / WS-IDX / WS-PAGE-NUM PIC S9(04) COMP. source: :52-54
    // (WS-REC-COUNT, WS-PAGE-NUM declared but never referenced; WS-IDX drives the row loops.)
    private int _wsIdx;

    // 05 WS-TRAN-AMT PIC +99999999.99 (12-char signed edited). source: :56
    // 05 WS-TRAN-DATE PIC X(08) VALUE '00/00/00'. source: :57
    private string _wsTranDate = "00/00/00";

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared screen header / messages.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CT00-INFO (the program-private paging state). source: :62-70
    // =============================================================================================
    // 10 CDEMO-CT00-TRNID-FIRST   PIC X(16). source: :63
    private string _ct00TrnidFirst = "";
    // 10 CDEMO-CT00-TRNID-LAST    PIC X(16). source: :64
    private string _ct00TrnidLast = "";
    // 10 CDEMO-CT00-PAGE-NUM      PIC 9(08). source: :65
    private int _ct00PageNum;
    // 10 CDEMO-CT00-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. 88 NEXT-PAGE-YES='Y' / NEXT-PAGE-NO='N'. source: :66-68
    private char _ct00NextPageFlg = 'N';
    private bool NextPageYes => _ct00NextPageFlg == 'Y'; // 88 NEXT-PAGE-YES
    private void SetNextPageYes() => _ct00NextPageFlg = 'Y';
    private void SetNextPageNo() => _ct00NextPageFlg = 'N';
    // 10 CDEMO-CT00-TRN-SEL-FLG   PIC X(01). source: :69
    private string _ct00TrnSelFlg = "";
    // 10 CDEMO-CT00-TRN-SELECTED  PIC X(16). source: :70
    private string _ct00TrnSelected = "";

    // =============================================================================================
    //  TRAN-ID RIDFLD + the TRAN-RECORD currently read by the browse (CVTRA05Y). source: :78,595,628
    // =============================================================================================
    // TRAN-ID X(16) — the STARTBR/READNEXT/READPREV RID. Empty string = LOW-VALUES (browse from first),
    // the 16x0xFF sentinel = HIGH-VALUES (browse past the last record).
    private string _tranId = "";
    private const string HighValues16 = "\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF";

    // The TRAN-RECORD just read by the browse.
    private Transaction? _tranRecord;
    private string TranRecId => _tranRecord?.TranId ?? "";
    private decimal TranAmt => _tranRecord?.Amt ?? 0m;
    private string TranDesc => _tranRecord?.Desc ?? "";
    private string TranOrigTs => _tranRecord?.OrigTs ?? "";

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :87-89,111
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private TransactionRepository _transactions = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The TRANSACTION repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public Cotrn00c(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cotrn00c() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COTRN00C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CT00 -> COTRN00C. source: CSD_TRANSACTIONS.md:81; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COTRN00C.cbl:95-141
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COTRN00 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _transactions = new TransactionRepository(_db.Connection);

        // SET ERR-FLG-OFF / TRANSACT-NOT-EOF / NEXT-PAGE-NO / SEND-ERASE-YES TO TRUE. source: :97-100
        _errFlgOn = false;
        _transactEof = false;
        SetNextPageNo();
        _sendEraseYes = true;

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COTRN0AO. source: :102-103
        _wsMessage = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        // MOVE -1 TO TRNIDINL OF COTRN0AI. source: :105
        _map.Field("TRNIDIN").CursorLength = -1;

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :107-109
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :111
            _commArea = ctx.CommArea!;
            RestoreCt00Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :112
                _commArea.SetReenter();              // SET CDEMO-PGM-REENTER TO TRUE. source: :113
                MoveLowValuesToMapOut();             // MOVE LOW-VALUES TO COTRN0AO. source: :114
                ProcessEnterKey(ctx);                // PERFORM PROCESS-ENTER-KEY. source: :115
                SendTrnlstScreen(ctx);               // PERFORM SEND-TRNLST-SCREEN. source: :116
            }
            else
            {
                ReceiveTrnlstScreen(ctx);            // PERFORM RECEIVE-TRNLST-SCREEN. source: :118
                // EVALUATE EIBAID. source: :119
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);        // WHEN DFHENTER. source: :120-121
                        break;
                    case AidKey.Pf3:
                        _commArea.ToProgram = "COMEN01C"; // WHEN DFHPF3 -> MOVE 'COMEN01C'. source: :122-123
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :124
                        break;
                    case AidKey.Pf7:
                        ProcessPf7Key(ctx);          // WHEN DFHPF7. source: :125-126
                        break;
                    case AidKey.Pf8:
                        ProcessPf8Key(ctx);          // WHEN DFHPF8. source: :127-128
                        break;
                    default:
                        // WHEN OTHER. source: :129-133
                        _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :130
                        _map.Field("TRNIDIN").CursorLength = -1;           // MOVE -1 TO TRNIDINL. source: :131
                        _wsMessage = CCDA_MSG_INVALID_KEY;                 // MOVE CCDA-MSG-INVALID-KEY. source: :132
                        SendTrnlstScreen(ctx);                             // PERFORM SEND-TRNLST-SCREEN. source: :133
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :138-141
        if (ctx.Outcome is null)
        {
            SaveCt00Info();
            ctx.ReturnTransId(WS_TRANID, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COTRN00C.cbl:146-229
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE — first non-blank selection row wins. source: :148-182
        if (NotSpacesOrLow(SelIn(1)))
        {
            _ct00TrnSelFlg = SelIn(1); _ct00TrnSelected = TrnIdIn(1);   // source: :149-151
        }
        else if (NotSpacesOrLow(SelIn(2)))
        {
            _ct00TrnSelFlg = SelIn(2); _ct00TrnSelected = TrnIdIn(2);   // source: :152-154
        }
        else if (NotSpacesOrLow(SelIn(3)))
        {
            _ct00TrnSelFlg = SelIn(3); _ct00TrnSelected = TrnIdIn(3);   // source: :155-157
        }
        else if (NotSpacesOrLow(SelIn(4)))
        {
            _ct00TrnSelFlg = SelIn(4); _ct00TrnSelected = TrnIdIn(4);   // source: :158-160
        }
        else if (NotSpacesOrLow(SelIn(5)))
        {
            _ct00TrnSelFlg = SelIn(5); _ct00TrnSelected = TrnIdIn(5);   // source: :161-163
        }
        else if (NotSpacesOrLow(SelIn(6)))
        {
            _ct00TrnSelFlg = SelIn(6); _ct00TrnSelected = TrnIdIn(6);   // source: :164-166
        }
        else if (NotSpacesOrLow(SelIn(7)))
        {
            _ct00TrnSelFlg = SelIn(7); _ct00TrnSelected = TrnIdIn(7);   // source: :167-169
        }
        else if (NotSpacesOrLow(SelIn(8)))
        {
            _ct00TrnSelFlg = SelIn(8); _ct00TrnSelected = TrnIdIn(8);   // source: :170-172
        }
        else if (NotSpacesOrLow(SelIn(9)))
        {
            _ct00TrnSelFlg = SelIn(9); _ct00TrnSelected = TrnIdIn(9);   // source: :173-175
        }
        else if (NotSpacesOrLow(SelIn(10)))
        {
            _ct00TrnSelFlg = SelIn(10); _ct00TrnSelected = TrnIdIn(10); // source: :176-178
        }
        else
        {
            _ct00TrnSelFlg = "";       // WHEN OTHER -> MOVE SPACES. source: :179-181
            _ct00TrnSelected = "";
        }

        // IF (TRN-SEL-FLG NOT = SPACES AND LOW-VALUES) AND (TRN-SELECTED NOT = SPACES AND LOW-VALUES). source: :183-204
        if (NotSpacesOrLow(_ct00TrnSelFlg) && NotSpacesOrLow(_ct00TrnSelected))
        {
            // EVALUATE CDEMO-CT00-TRN-SEL-FLG. source: :185-203
            string flg = _ct00TrnSelFlg.Length > 0 ? _ct00TrnSelFlg.Substring(0, 1) : "";
            if (flg == "S" || flg == "s")
            {
                // WHEN 'S' / 's' — XCTL to the transaction detail program. source: :186-195
                _commArea.ToProgram = "COTRN01C";   // MOVE 'COTRN01C' TO CDEMO-TO-PROGRAM. source: :188
                _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :189
                _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :190
                _commArea.SetFirstEntry();          // MOVE 0 TO CDEMO-PGM-CONTEXT. source: :191
                // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :192-195
                SaveCt00Info();
                ctx.Xctl("COTRN01C", _commArea);
                return;
            }
            else
            {
                // WHEN OTHER — build the invalid-selection message. B-2: the SEND is commented out, so the
                // program does NOT stop here; control falls through to the tran-id edit + page logic and the
                // message gets overwritten. source: :196-203
                _wsMessage = "Invalid selection. Valid value is S"; // source: :198-200
                _map.Field("TRNIDIN").CursorLength = -1;            // MOVE -1 TO TRNIDINL. source: :201
                // PERFORM SEND-TRNLST-SCREEN is commented out in the COBOL. source: :202
            }
        }

        // IF TRNIDINI = SPACES OR LOW-VALUES -> MOVE LOW-VALUES TO TRAN-ID. source: :206-219
        string trnidin = _map.Field("TRNIDIN").Value;
        if (IsSpacesOrLowValues(trnidin))
        {
            _tranId = ""; // MOVE LOW-VALUES TO TRAN-ID. source: :207
        }
        else
        {
            // IF TRNIDINI IS NUMERIC -> MOVE TO TRAN-ID; ELSE error. source: :209-218
            if (IsNumericX(trnidin, 16))
            {
                _tranId = PadX(trnidin, 16); // MOVE TRNIDINI (X(16)) TO TRAN-ID (X(16)). source: :210
            }
            else
            {
                _errFlgOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :212
                _wsMessage = "Tran ID must be Numeric ...";    // source: :213-215
                _map.Field("TRNIDIN").CursorLength = -1;       // MOVE -1 TO TRNIDINL. source: :216
                SendTrnlstScreen(ctx);                         // PERFORM SEND-TRNLST-SCREEN. source: :217
            }
        }

        // MOVE -1 TO TRNIDINL. source: :221
        _map.Field("TRNIDIN").CursorLength = -1;

        // MOVE 0 TO CDEMO-CT00-PAGE-NUM; PERFORM PROCESS-PAGE-FORWARD. source: :224-225
        _ct00PageNum = 0;
        ProcessPageForward(ctx);

        // IF NOT ERR-FLG-ON -> MOVE SPACE TO TRNIDINO. source: :227-229
        if (!ErrFlgOn)
            _map.Field("TRNIDIN").SetValue(" ", setMdt: false);
    }

    // =============================================================================================
    //  PROCESS-PF7-KEY — source: COTRN00C.cbl:234-252
    // =============================================================================================
    private void ProcessPf7Key(CicsContext ctx)
    {
        // IF CDEMO-CT00-TRNID-FIRST = SPACES OR LOW-VALUES -> LOW-VALUES else TRNID-FIRST. source: :236-240
        _tranId = IsSpacesOrLowValues(_ct00TrnidFirst) ? "" : _ct00TrnidFirst;

        SetNextPageYes();                              // SET NEXT-PAGE-YES TO TRUE. source: :242
        _map.Field("TRNIDIN").CursorLength = -1;       // MOVE -1 TO TRNIDINL. source: :243

        if (_ct00PageNum > 1)
        {
            ProcessPageBackward(ctx);                  // PERFORM PROCESS-PAGE-BACKWARD. source: :246
        }
        else
        {
            _wsMessage = "You are already at the top of the page..."; // source: :248-249
            _sendEraseYes = false;                     // SET SEND-ERASE-NO TO TRUE. source: :250
            SendTrnlstScreen(ctx);                     // PERFORM SEND-TRNLST-SCREEN. source: :251
        }
    }

    // =============================================================================================
    //  PROCESS-PF8-KEY — source: COTRN00C.cbl:257-274
    // =============================================================================================
    private void ProcessPf8Key(CicsContext ctx)
    {
        // IF CDEMO-CT00-TRNID-LAST = SPACES OR LOW-VALUES -> HIGH-VALUES else TRNID-LAST. source: :259-263
        _tranId = IsSpacesOrLowValues(_ct00TrnidLast) ? HighValues16 : _ct00TrnidLast;

        _map.Field("TRNIDIN").CursorLength = -1;       // MOVE -1 TO TRNIDINL. source: :265

        if (NextPageYes)
        {
            ProcessPageForward(ctx);                   // PERFORM PROCESS-PAGE-FORWARD. source: :268
        }
        else
        {
            _wsMessage = "You are already at the bottom of the page..."; // source: :270-271
            _sendEraseYes = false;                     // SET SEND-ERASE-NO TO TRUE. source: :272
            SendTrnlstScreen(ctx);                     // PERFORM SEND-TRNLST-SCREEN. source: :273
        }
    }

    // =============================================================================================
    //  PROCESS-PAGE-FORWARD — source: COTRN00C.cbl:279-328
    // =============================================================================================
    private void ProcessPageForward(CicsContext ctx)
    {
        StartbrTransactFile(ctx);              // PERFORM STARTBR-TRANSACT-FILE. source: :281

        if (!ErrFlgOn)                         // IF NOT ERR-FLG-ON. source: :283
        {
            // B-1: IF EIBAID NOT = DFHENTER AND DFHPF7 AND DFHPF3 -> abbreviated condition expands to
            //   NOT=ENTER AND NOT=PF7 AND NOT=PF3. On PF8 (and any other key) this fires one extra
            //   READNEXT to step over the boundary record. source: :285-287
            if (ctx.EibAid != AidKey.Enter && ctx.EibAid != AidKey.Pf7 && ctx.EibAid != AidKey.Pf3)
                ReadnextTransactFile(ctx);     // PERFORM READNEXT-TRANSACT-FILE. source: :286

            // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF -> clear the 10 display rows. source: :289-293
            if (TransactNotEof && ErrFlgOff)
            {
                for (_wsIdx = 1; _wsIdx <= 10; _wsIdx++) // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL > 10. source: :290
                    InitializeTranData();                 // PERFORM INITIALIZE-TRAN-DATA. source: :291
            }

            _wsIdx = 1;                         // MOVE 1 TO WS-IDX. source: :295

            // PERFORM UNTIL WS-IDX >= 11 OR TRANSACT-EOF OR ERR-FLG-ON. source: :297-303
            while (!(_wsIdx >= 11 || TransactEof || ErrFlgOn))
            {
                ReadnextTransactFile(ctx);      // PERFORM READNEXT-TRANSACT-FILE. source: :298
                if (TransactNotEof && ErrFlgOff) // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF. source: :299
                {
                    PopulateTranData();          // PERFORM POPULATE-TRAN-DATA. source: :300
                    _wsIdx = _wsIdx + 1;         // COMPUTE WS-IDX = WS-IDX + 1. source: :301
                }
            }

            // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF — peek for a next page. source: :305-320
            if (TransactNotEof && ErrFlgOff)
            {
                _ct00PageNum = _ct00PageNum + 1; // COMPUTE CDEMO-CT00-PAGE-NUM = + 1. source: :306-307
                ReadnextTransactFile(ctx);       // PERFORM READNEXT-TRANSACT-FILE. source: :308
                if (TransactNotEof && ErrFlgOff) // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF. source: :309
                    SetNextPageYes();            // SET NEXT-PAGE-YES TO TRUE. source: :310
                else
                    SetNextPageNo();             // SET NEXT-PAGE-NO TO TRUE. source: :312
            }
            else
            {
                SetNextPageNo();                 // SET NEXT-PAGE-NO TO TRUE. source: :315
                if (_wsIdx > 1)                  // IF WS-IDX > 1. source: :316
                    _ct00PageNum = _ct00PageNum + 1; // COMPUTE CDEMO-CT00-PAGE-NUM = + 1. source: :317-318
            }

            EndbrTransactFile();                 // PERFORM ENDBR-TRANSACT-FILE. source: :322

            // MOVE CDEMO-CT00-PAGE-NUM TO PAGENUMI; MOVE SPACE TO TRNIDINO; PERFORM SEND. source: :324-326
            // CDEMO-CT00-PAGE-NUM PIC 9(08) -> PAGENUMI PIC X(8): a numeric-to-alphanumeric MOVE copies the
            // 8 zoned digits, so page 1 renders as "00000001" (zero-filled), not "1".
            _map.Field("PAGENUM").SetValue(_ct00PageNum.ToString("D8"), setMdt: false);
            _map.Field("TRNIDIN").SetValue(" ", setMdt: false);
            SendTrnlstScreen(ctx);
        }
    }

    // =============================================================================================
    //  PROCESS-PAGE-BACKWARD — source: COTRN00C.cbl:333-376
    // =============================================================================================
    private void ProcessPageBackward(CicsContext ctx)
    {
        StartbrTransactFile(ctx);              // PERFORM STARTBR-TRANSACT-FILE. source: :335

        if (!ErrFlgOn)                         // IF NOT ERR-FLG-ON. source: :337
        {
            // B-1: IF EIBAID NOT = DFHENTER AND DFHPF8 -> NOT=ENTER AND NOT=PF8. On PF7 this fires an extra
            //   READPREV to step over the boundary record. source: :339-341
            if (ctx.EibAid != AidKey.Enter && ctx.EibAid != AidKey.Pf8)
                ReadprevTransactFile(ctx);     // PERFORM READPREV-TRANSACT-FILE. source: :340

            // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF -> clear the 10 display rows. source: :343-347
            if (TransactNotEof && ErrFlgOff)
            {
                for (_wsIdx = 1; _wsIdx <= 10; _wsIdx++) // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL > 10. source: :344
                    InitializeTranData();                 // PERFORM INITIALIZE-TRAN-DATA. source: :345
            }

            _wsIdx = 10;                        // MOVE 10 TO WS-IDX. source: :349

            // PERFORM UNTIL WS-IDX <= 0 OR TRANSACT-EOF OR ERR-FLG-ON. source: :351-357
            while (!(_wsIdx <= 0 || TransactEof || ErrFlgOn))
            {
                ReadprevTransactFile(ctx);      // PERFORM READPREV-TRANSACT-FILE. source: :352
                if (TransactNotEof && ErrFlgOff) // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF. source: :353
                {
                    PopulateTranData();          // PERFORM POPULATE-TRAN-DATA. source: :354
                    _wsIdx = _wsIdx - 1;         // COMPUTE WS-IDX = WS-IDX - 1. source: :355
                }
            }

            // IF TRANSACT-NOT-EOF AND ERR-FLG-OFF — adjust the page number. source: :359-369
            if (TransactNotEof && ErrFlgOff)
            {
                ReadprevTransactFile(ctx);       // PERFORM READPREV-TRANSACT-FILE. source: :360
                if (NextPageYes)                 // IF NEXT-PAGE-YES. source: :361
                {
                    if (TransactNotEof && ErrFlgOff && _ct00PageNum > 1) // source: :362-363
                        _ct00PageNum = _ct00PageNum - 1; // SUBTRACT 1 FROM CDEMO-CT00-PAGE-NUM. source: :364
                    else
                        _ct00PageNum = 1;        // MOVE 1 TO CDEMO-CT00-PAGE-NUM. source: :366
                }
            }

            EndbrTransactFile();                 // PERFORM ENDBR-TRANSACT-FILE. source: :371

            // MOVE CDEMO-CT00-PAGE-NUM TO PAGENUMI; PERFORM SEND. source: :373-374
            // CDEMO-CT00-PAGE-NUM PIC 9(08) -> PAGENUMI PIC X(8): a numeric-to-alphanumeric MOVE copies the
            // 8 zoned digits, so page 1 renders as "00000001" (zero-filled), not "1".
            _map.Field("PAGENUM").SetValue(_ct00PageNum.ToString("D8"), setMdt: false);
            SendTrnlstScreen(ctx);
        }
    }

    // =============================================================================================
    //  POPULATE-TRAN-DATA — source: COTRN00C.cbl:381-445
    // =============================================================================================
    private void PopulateTranData()
    {
        // MOVE TRAN-AMT TO WS-TRAN-AMT (edited +99999999.99). source: :383
        string wsTranAmt = EditTranAmt(TranAmt);

        // MOVE TRAN-ORIG-TS TO WS-TIMESTAMP; build mm/dd/yy from the timestamp's yy/mm/dd. source: :384-388
        string ts = PadX(TranOrigTs, 26);
        // WS-TIMESTAMP layout: YYYY(0..3) '-' MM(5..6) '-' DD(8..9) ' ' HH:MM:SS.MS6
        string yy = ts.Substring(2, 2);   // WS-TIMESTAMP-DT-YYYY(3:2) -> last 2 digits of year. source: :385
        string mm = ts.Substring(5, 2);   // WS-TIMESTAMP-DT-MM. source: :386
        string dd = ts.Substring(8, 2);   // WS-TIMESTAMP-DT-DD. source: :387
        _wsTranDate = $"{mm}/{dd}/{yy}";  // MOVE WS-CURDATE-MM-DD-YY TO WS-TRAN-DATE. source: :388

        // EVALUATE WS-IDX -> stamp the row's TRNID / TDATE / TDESC / TAMT fields. source: :390-445
        int n = _wsIdx;
        if (n is < 1 or > 10) return; // WHEN OTHER -> CONTINUE. source: :443-444

        _map.Field($"TRNID{n:D2}").SetValue(TranRecId, setMdt: false); // MOVE TRAN-ID TO TRNIDnnI. source: :392,398,...
        if (n == 1) _ct00TrnidFirst = TranRecId;  // WHEN 1 also -> CDEMO-CT00-TRNID-FIRST. source: :392-393
        if (n == 10) _ct00TrnidLast = TranRecId;  // WHEN 10 also -> CDEMO-CT00-TRNID-LAST. source: :438-439
        _map.Field($"TDATE{n:D2}").SetValue(_wsTranDate, setMdt: false);  // MOVE WS-TRAN-DATE TO TDATEnnI. source: :394,...
        _map.Field($"TDESC{n:D2}").SetValue(TranDesc, setMdt: false);     // MOVE TRAN-DESC TO TDESCnnI. source: :395,...
        _map.Field($"TAMT{n:D3}").SetValue(wsTranAmt, setMdt: false);     // MOVE WS-TRAN-AMT TO TAMT0nnI. source: :396,...
    }

    // =============================================================================================
    //  INITIALIZE-TRAN-DATA — source: COTRN00C.cbl:450-505
    // =============================================================================================
    private void InitializeTranData()
    {
        // EVALUATE WS-IDX -> MOVE SPACES to the row's four display fields. source: :452-505
        int n = _wsIdx;
        if (n is < 1 or > 10) return; // WHEN OTHER -> CONTINUE. source: :503-504
        _map.Field($"TRNID{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO TRNIDnnI. source: :454,...
        _map.Field($"TDATE{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO TDATEnnI. source: :455,...
        _map.Field($"TDESC{n:D2}").SetValue(" ", setMdt: false); // MOVE SPACES TO TDESCnnI. source: :456,...
        _map.Field($"TAMT{n:D3}").SetValue(" ", setMdt: false);  // MOVE SPACES TO TAMT0nnI. source: :457,...
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COTRN00C.cbl:510-521
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :512-514
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :515
        _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :516
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :517

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :518-521
        SaveCt00Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-TRNLST-SCREEN — source: COTRN00C.cbl:527-549
    // =============================================================================================
    private void SendTrnlstScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                     // PERFORM POPULATE-HEADER-INFO. source: :529

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false);   // MOVE WS-MESSAGE TO ERRMSGO. source: :531

        // IF SEND-ERASE-YES -> SEND ... ERASE CURSOR; ELSE SEND ... CURSOR (no erase). source: :533-549
        ctx.SendMap("COTRN0A", "COTRN00", _map, new SendMapOptions
        {
            Erase = SendEraseYes,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO TRNIDINL set throughout.
        });
        _wsRespCd = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-TRNLST-SCREEN — source: COTRN00C.cbl:554-562
    // =============================================================================================
    private void ReceiveTrnlstScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COTRN0A') MAPSET('COTRN00') INTO(COTRN0AI) RESP. source: :556-562
        ctx.ReceiveMap("COTRN0A", "COTRN00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COTRN00C.cbl:567-586
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :569
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :571
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :572
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);    // MOVE WS-TRANID  TO TRNNAMEO. source: :573
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :574

        // CURDATEO = mm/dd/yy (year last two digits). source: :576-580
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :582-586
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  STARTBR-TRANSACT-FILE — source: COTRN00C.cbl:591-619
    // =============================================================================================
    private void StartbrTransactFile(CicsContext ctx)
    {
        // EXEC CICS STARTBR DATASET(WS-TRANSACT-FILE) RIDFLD(TRAN-ID) RESP. source: :593-600
        // STARTBR is GTEQ (the GTEQ is commented but CICS default is GTEQ): position at-or-after TRAN-ID.
        // LOW-VALUES (empty) -> from the first record; HIGH-VALUES -> past the last record (NOTFND).
        // STARTBR GTEQ positions at-or-after TRAN-ID and returns NORMAL when a record exists there, else
        // NOTFND. LOW-VALUES (empty) -> from the first record (NOTFND only on an empty file); HIGH-VALUES ->
        // no record is >= 0xFF...FF so NOTFND (the COBOL PF8-with-no-prior-page quirk).
        if (_tranId.Length == 0)
            _transactions.StartBrowse();
        else
            _transactions.StartBrowse(_tranId);
        string fileStatus = PeekForwardExists() ? FileStatus.Ok : FileStatus.RecordNotFound;
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :602-619
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :603-604
                break;
            case Resp.NotFnd: // WHEN DFHRESP(NOTFND). source: :605-611
                _transactEof = true;                                  // SET TRANSACT-EOF TO TRUE. source: :607
                _wsMessage = "You are at the top of the page...";     // source: :608-609
                _map.Field("TRNIDIN").CursorLength = -1;              // MOVE -1 TO TRNIDINL. source: :610
                SendTrnlstScreen(ctx);                                // PERFORM SEND-TRNLST-SCREEN. source: :611
                break;
            default: // WHEN OTHER. source: :612-618
                _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :614
                _wsMessage = "Unable to lookup transaction...";       // source: :615-616
                _map.Field("TRNIDIN").CursorLength = -1;              // MOVE -1 TO TRNIDINL. source: :617
                SendTrnlstScreen(ctx);                                // PERFORM SEND-TRNLST-SCREEN. source: :618
                break;
        }
    }

    // =============================================================================================
    //  READNEXT-TRANSACT-FILE — source: COTRN00C.cbl:624-653
    // =============================================================================================
    private void ReadnextTransactFile(CicsContext ctx)
    {
        // EXEC CICS READNEXT DATASET(WS-TRANSACT-FILE) INTO(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :626-634
        string fileStatus = _transactions.ReadNext(out _tranRecord);
        if (fileStatus == FileStatus.Ok && _tranRecord is not null)
            _tranId = _tranRecord.TranId; // RIDFLD is updated with the key just read. source: :630
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :636-653
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :637-638
                break;
            case Resp.EndFile: // WHEN DFHRESP(ENDFILE). source: :639-645
                _transactEof = true;                                       // SET TRANSACT-EOF TO TRUE. source: :641
                _wsMessage = "You have reached the bottom of the page..."; // source: :642-643
                _map.Field("TRNIDIN").CursorLength = -1;                   // MOVE -1 TO TRNIDINL. source: :644
                SendTrnlstScreen(ctx);                                     // PERFORM SEND-TRNLST-SCREEN. source: :645
                break;
            default: // WHEN OTHER. source: :646-652
                _errFlgOn = true;                                          // MOVE 'Y' TO WS-ERR-FLG. source: :648
                _wsMessage = "Unable to lookup transaction...";           // source: :649-650
                _map.Field("TRNIDIN").CursorLength = -1;                   // MOVE -1 TO TRNIDINL. source: :651
                SendTrnlstScreen(ctx);                                     // PERFORM SEND-TRNLST-SCREEN. source: :652
                break;
        }
    }

    // =============================================================================================
    //  READPREV-TRANSACT-FILE — source: COTRN00C.cbl:658-687
    // =============================================================================================
    private void ReadprevTransactFile(CicsContext ctx)
    {
        // EXEC CICS READPREV DATASET(WS-TRANSACT-FILE) INTO(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :660-668
        string fileStatus = _transactions.ReadPrevious(out _tranRecord);
        if (fileStatus == FileStatus.Ok && _tranRecord is not null)
            _tranId = _tranRecord.TranId; // RIDFLD updated with the key just read. source: :664
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :670-687
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :671-672
                break;
            case Resp.EndFile: // WHEN DFHRESP(ENDFILE). source: :673-679
                _transactEof = true;                                  // SET TRANSACT-EOF TO TRUE. source: :675
                _wsMessage = "You have reached the top of the page..."; // source: :676-677
                _map.Field("TRNIDIN").CursorLength = -1;              // MOVE -1 TO TRNIDINL. source: :678
                SendTrnlstScreen(ctx);                                // PERFORM SEND-TRNLST-SCREEN. source: :679
                break;
            default: // WHEN OTHER. source: :680-686
                _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :682
                _wsMessage = "Unable to lookup transaction...";       // source: :683-684
                _map.Field("TRNIDIN").CursorLength = -1;              // MOVE -1 TO TRNIDINL. source: :685
                SendTrnlstScreen(ctx);                                // PERFORM SEND-TRNLST-SCREEN. source: :686
                break;
        }
    }

    // =============================================================================================
    //  ENDBR-TRANSACT-FILE — source: COTRN00C.cbl:692-696
    // =============================================================================================
    private void EndbrTransactFile() => _transactions.EndBrowse(); // EXEC CICS ENDBR DATASET(WS-TRANSACT-FILE). source: :694

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
        string st = _transactions.ReadNext(out _);
        // Re-position at-or-after the same RID so the caller's first READNEXT returns the same record,
        // and a first READPREV returns the record at-or-before it (matching CICS browse after STARTBR).
        if (_tranId.Length == 0) _transactions.StartBrowse();
        else _transactions.StartBrowse(_tranId);
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
    //  Symbolic-map input readers — SEL000nI / TRNIDnnI.
    // =============================================================================================
    private string SelIn(int n) => _map.Field($"SEL{n:D4}").Value;     // SEL0001I..SEL0010I
    private string TrnIdIn(int n) => _map.Field($"TRNID{n:D2}").Value; // TRNID01I..TRNID10I

    /// <summary>MOVE LOW-VALUES TO COTRN0AO — blank every named output field + clear per-turn overrides. source: :114</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  WS-TRAN-AMT edit — PIC +99999999.99 (sign, 8 int digits, '.', 2 dec). source: :56,383
    // =============================================================================================
    private static string EditTranAmt(decimal amt)
    {
        // MOVE S9(9)V99 TO +99999999.99: truncate toward zero to 2 decimals (no rounding), drop the
        // high-order integer digit that does not fit the 8 integer positions, and prefix the sign.
        bool negative = amt < 0m;
        decimal mag = negative ? -amt : amt;
        long cents = (long)decimal.Truncate(mag * 100m); // toward-zero truncation, no rounding
        long intPart = cents / 100;
        long decPart = cents % 100;
        // 8 integer digits, zero-filled; the leading sign is always present (+ for >=0, - for <0).
        string ip = (intPart % 100000000L).ToString("D8");
        string dp = decPart.ToString("D2");
        return (negative ? "-" : "+") + ip + "." + dp;
    }

    // =============================================================================================
    //  CDEMO-CT00-INFO (de)serialize — carried across turns in the COMMAREA's unused customer slots.
    //  source: COTRN00C.cbl:62-70,111,138-141
    // =============================================================================================
    // COTRN00C never reads/writes CDEMO-CUSTOMER-INFO; the trailer is packed there so the paging state
    // (TRNID-FIRST/LAST, PAGE-NUM, NEXT-PAGE-FLG, TRN-SEL-FLG/SELECTED) round-trips losslessly each turn.
    // Pack layout into CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   TRNID-FIRST X(16) | TRNID-LAST X(16) | PAGE-NUM 9(8) | NEXT X(1) | (spare).
    private void SaveCt00Info()
    {
        string packed =
            PadX(_ct00TrnidFirst, 16) +
            PadX(_ct00TrnidLast, 16) +
            Zoned(_ct00PageNum, 8) +
            (_ct00NextPageFlg == '\0' ? 'N' : _ct00NextPageFlg);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCt00Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        if (packed.Length < 41) packed = PadX(packed, 75);
        _ct00TrnidFirst = packed.Substring(0, 16).TrimEnd();
        _ct00TrnidLast = packed.Substring(16, 16).TrimEnd();
        _ct00PageNum = (int)ParseLong(packed.Substring(32, 8));
        char nx = packed[40];
        _ct00NextPageFlg = nx == 'Y' ? 'Y' : 'N';
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "NOT = SPACES AND LOW-VALUES" guard.</summary>
    private static bool NotSpacesOrLow(string? s) => !IsSpacesOrLowValues(s);

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL).</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// COBOL class test <c>field IS NUMERIC</c> on the X(width) search field: the field is the fixed COBOL
    /// width, so a partial entry has trailing spaces and every character of the full width must be a digit
    /// '0'-'9' (spaces/low-values fail). Returns true when the field IS numeric.
    /// </summary>
    private static bool IsNumericX(string? value, int width)
    {
        string v = PadX(value, width);
        foreach (char c in v)
            if (c is < '0' or > '9') return false;
        return true;
    }

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
    //  BMS map builder — COTRN0A in mapset COTRN00 (24x80).
    //  source: app/bms/COTRN00.bms:19-461 / SCREEN_COTRN00.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COTRN00.bms:26.</summary>
    public const string MapName = "COTRN0A";

    /// <summary>The DFHMSD mapset name. source: COTRN00.bms:19.</summary>
    public const string MapsetName = "COTRN00";

    /// <summary>
    /// Constructs the <c>COTRN0A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The keyable fields are <c>TRNIDIN</c>
    /// (6,21) L16 and the ten per-row selection fields <c>SEL0001..SEL0010</c>; no <c>IC</c> is coded, so
    /// CICS defaults the cursor to the first unprotected field (<c>TRNIDIN</c>). No PICIN/PICOUT clauses
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

            // ----- 'List Transactions' heading + Page nbr (bms:75-89) -----
            LitAttr(4, 30, 17, AskipBrt, BmsColor.Neutral, "List Transactions"), // bms:75-79
            LitAttr(4, 65, 5, AskipBrt, BmsColor.Turquoise, "Page:"),            // bms:80-84
            OutInit("PAGENUM", 4, 71, 8, AskipFset, BmsColor.Blue, " "),         // bms:85-89

            // ----- Search Tran ID label + input field (bms:90-102) -----
            Lit(6, 5, 15, BmsColor.Turquoise, "Search Tran ID:"),               // bms:90-94
            // TRNIDIN: ATTRB=(FSET,NORM,UNPROT) GREEN UNDERLINE — first unprotected field (default cursor).
            new ScreenField
            {
                Name = "TRNIDIN", Row = 6, Col = 21, Length = 16,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:95-99
            Stopper(6, 38),                                                     // bms:100-102

            // ----- grid column headers (NEUTRAL) (bms:103-127) -----
            LitAttr(8, 2, 3, Askip, BmsColor.Neutral, "Sel"),                  // bms:103-107
            LitAttr(8, 8, 16, Askip, BmsColor.Neutral, " Transaction ID "),    // bms:108-112
            LitAttr(8, 27, 8, Askip, BmsColor.Neutral, "  Date  "),            // bms:113-117
            LitAttr(8, 38, 26, Askip, BmsColor.Neutral, "     Description          "), // bms:118-122
            LitAttr(8, 67, 12, Askip, BmsColor.Neutral, "   Amount   "),       // bms:123-127

            // ----- grid rules (bms:128-152) -----
            LitAttr(9, 2, 3, Askip, BmsColor.Neutral, "---"),                  // bms:128-132
            LitAttr(9, 8, 16, Askip, BmsColor.Neutral, "----------------"),    // bms:133-137
            LitAttr(9, 27, 8, Askip, BmsColor.Neutral, "--------"),            // bms:138-142
            LitAttr(9, 38, 26, Askip, BmsColor.Neutral, "--------------------------"), // bms:143-147
            LitAttr(9, 67, 12, Askip, BmsColor.Neutral, "------------"),       // bms:148-152

            // ----- 10 detail rows (bms:153-442) -----
            // Each row: SEL000n (UNPROT GREEN UNDERLINE L1 INITIAL ' '), stopper, TRNIDnn / TDATEnn /
            // TDESCnn / TAMT0nn (ASKIP FSET BLUE, INITIAL ' ').
        };

        for (int n = 1; n <= 10; n++)
        {
            int row = 9 + n; // rows 10..19
            fields.Add(RowSel($"SEL{n:D4}", row));     // SEL000n (row,3) L1. bms:153,182,...
            fields.Add(Stopper(row, 5));               // (row,5) L0 stopper. bms:159,...
            fields.Add(RowOut($"TRNID{n:D2}", row, 8, 16)); // TRNIDnn (row,8) L16. bms:162,...
            fields.Add(RowOut($"TDATE{n:D2}", row, 27, 8)); // TDATEnn (row,27) L8. bms:167,...
            fields.Add(RowOut($"TDESC{n:D2}", row, 38, 26)); // TDESCnn (row,38) L26. bms:172,...
            fields.Add(RowOut($"TAMT{n:D3}", row, 67, 12)); // TAMT0nn (row,67) L12. bms:177,...
        }

        // ----- instruction + error + footer (bms:444-459) -----
        fields.Add(LitAttr(21, 12, 50, AskipBrt, BmsColor.Neutral,
            "Type 'S' to View Transaction details from the list"));           // bms:444-449
        fields.Add(Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red));     // bms:450-453
        fields.Add(LitAttr(24, 1, 48, Askip, BmsColor.Yellow,
            "ENTER=Continue  F3=Back  F7=Backward  F8=Forward"));             // bms:454-459

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
            Name = name, Row = row, Col = 3, Length = 1,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
            Color = BmsColor.Green, Hilight = BmsHilight.Underline, Value = " ",
        };

    /// <summary>Per-row data field (TRNIDnn/TDATEnn/TDESCnn/TAMT0nn): ATTRB=(ASKIP,FSET,NORM) BLUE INITIAL ' '.</summary>
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
