using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COTRN01C</c> — "View a Transaction from the
/// TRANSACT file" (TRANSID <c>CT01</c>, BMS map <c>COTRN1A</c> / mapset <c>COTRN01</c>).
/// </summary>
/// <remarks>
/// <para>
/// COTRN01C is the single-record transaction-detail viewer. The operator keys a 16-char transaction id in
/// the <c>TRNIDIN</c> field and presses ENTER; the program READs the TRANSACT (TRANSACTION) file by that
/// primary key and paints all the transaction's display fields (id, card number, type/category codes,
/// source, amount, description, origination / processing timestamps, and the four merchant fields). PF3
/// returns to the caller (<c>COMEN01C</c> when no <c>CDEMO-FROM-PROGRAM</c> is set, else back to that
/// program); PF4 clears the screen; PF5 chains to the transaction list (<c>COTRN00C</c>). It is
/// pseudo-conversational: it re-drives itself via <c>RETURN TRANSID('CT01')</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and a <c>// source: COTRN01C.cbl:NNN</c>
/// citation. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-specific <c>CDEMO-CT01-INFO</c> trailer that overlays
/// the unused customer slots), and every faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the TRANSACT master is accessed, by a single keyed READ:
/// <c>EXEC CICS READ DATASET(TRANSACT) RIDFLD(TRAN-ID) UPDATE</c> =
/// <see cref="TransactionRepository.ReadByKey"/>. The repository FileStatus is mapped to the CICS RESP the
/// COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13),
/// anything else→an OTHER/file-error. No write/rewrite/delete is performed (it is a view-only screen).</para>
/// <para><b>Faithful behaviours / bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — The READ specifies <c>UPDATE</c> (exclusive lock) even though the program never REWRITEs or
/// DELETEs and then issues <c>RETURN</c>; it is a read-only viewer that needlessly takes the update intent.
/// Repository <c>ReadByKey</c> models the same keyed read. source: COTRN01C.cbl:269-278</item>
/// <item>FB-2 — <c>WS-TRAN-AMT</c> is <c>PIC +99999999.99</c> (8 integer digit positions) but
/// <c>TRAN-AMT</c> is <c>PIC S9(09)V99</c> (9 integer digits); the high-order integer digit is silently
/// truncated on the <c>MOVE TRAN-AMT TO WS-TRAN-AMT</c>. Reproduced by the 8-digit edit. source: cbl:49,177</item>
/// <item>FB-3 — On the first-display "selected from list" path, the program MOVEs
/// <c>CDEMO-CT01-TRN-SELECTED</c> into <c>TRNIDINI</c> and PERFORMs PROCESS-ENTER-KEY, then PERFORMs
/// SEND-TRNVIEW-SCREEN <em>unconditionally</em> — so PROCESS-ENTER-KEY's own SEND runs first and the map is
/// SENT twice in the one task. Both SENDs are kept; only the last is visible. source: cbl:107-109</item>
/// <item>FB-4 — <c>WS-USR-MODIFIED</c> (88 USR-MODIFIED-YES/NO) is declared and SET to NO at entry but is
/// never read anywhere — dead state, kept for fidelity. source: cbl:45-47,89</item>
/// <item>FB-5 — The OTHER (file-error) branch of READ-TRANSACT-FILE does a <c>DISPLAY 'RESP:'...'REAS:'</c>
/// to the job log before SENDing the error map. Modeled as a no-op write to the runtime trace. source: cbl:290</item>
/// </list>
/// </remarks>
public sealed class TransactionViewProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COTRN01C.cbl:35-50
    // =============================================================================================
    private const string ProgramId = "COTRN01C";       // WS-PGMNAME       PIC X(08) VALUE 'COTRN01C'. source: :36
    private const string TranId = "CT01";              // WS-TRANID        PIC X(04) VALUE 'CT01'.     source: :37
    private const string TransactFileName = "TRANSACT"; // WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT'. source: :39

    private string _message = "";                       // WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errorFlagOn;                           // WS-ERR-FLG
    private bool ErrorFlagOn => _errorFlagOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :43-44
    private int _responseCode;                           // WS-RESP-CD
    private int _reasonCode;                             // WS-REAS-CD

    // 05 WS-USR-MODIFIED PIC X(01) VALUE 'N'. 88 USR-MODIFIED-YES='Y' / USR-MODIFIED-NO='N'. source: :45-47
    // FB-4: SET to NO at entry, never inspected — dead state kept for fidelity.
    private bool _userModifiedYes;                       // WS-USR-MODIFIED
    private void SetUserModifiedNo() => _userModifiedYes = false; // SET USR-MODIFIED-NO TO TRUE. source: :89

    // 05 WS-TRAN-AMT  PIC +99999999.99.            source: :49 (8 integer digits — see FB-2)
    // 05 WS-TRAN-DATE PIC X(08) VALUE '00/00/00'.  source: :50 (declared, never referenced here)
    private string _transactionDate = "00/00/00";        // WS-TRAN-DATE

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared header / messages.
    private const string Title01 = "      AWS Mainframe Modernization       ";
    private const string Title02 = "              CardDemo                  ";
    private const string MsgInvalidKey = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CT01-INFO (program-private state, overlays the unused customer slots).
    //  source: COTRN01C.cbl:53-61
    // =============================================================================================
    // 10 CDEMO-CT01-TRNID-FIRST   PIC X(16).            source: :54
    private string _firstTranId = "";        // CDEMO-CT01-TRNID-FIRST
    // 10 CDEMO-CT01-TRNID-LAST    PIC X(16).            source: :55
    private string _lastTranId = "";         // CDEMO-CT01-TRNID-LAST
    // 10 CDEMO-CT01-PAGE-NUM      PIC 9(08).            source: :56
    private int _pageNumber;                 // CDEMO-CT01-PAGE-NUM
    // 10 CDEMO-CT01-NEXT-PAGE-FLG PIC X(01) VALUE 'N'.  source: :57-59
    private char _nextPageFlag = 'N';        // CDEMO-CT01-NEXT-PAGE-FLG
    // 10 CDEMO-CT01-TRN-SEL-FLG   PIC X(01).            source: :60
    private string _tranSelectFlag = "";     // CDEMO-CT01-TRN-SEL-FLG
    // 10 CDEMO-CT01-TRN-SELECTED  PIC X(16).            source: :61
    private string _selectedTranId = "";     // CDEMO-CT01-TRN-SELECTED

    // =============================================================================================
    //  TRAN-ID RIDFLD + the TRAN-RECORD read by the keyed READ (CVTRA05Y). source: :172,269-274
    // =============================================================================================
    // TRAN-ID X(16) — the READ RIDFLD.
    private string _recordKey = "";          // TRAN-ID (RIDFLD)
    // The TRAN-RECORD just read.
    private Transaction? _transactionRecord; // TRAN-RECORD

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :78-80,98,136-139
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private TransactionRepository _transactions = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The TRANSACTION repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public TransactionViewProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public TransactionViewProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => ProgramId; // PROGRAM-ID. COTRN01C. source: :23

    /// <inheritdoc/>
    public string TransId => TranId;         // CSD: CT01 -> COTRN01C. source: CSD_TRANSACTIONS.md:82; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COTRN01C.cbl:86-139
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COTRN01 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _transactions = new TransactionRepository(_db.Connection);

        // SET ERR-FLG-OFF TO TRUE / SET USR-MODIFIED-NO TO TRUE. source: :88-89
        _errorFlagOn = false;
        SetUserModifiedNo();

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COTRN1AO. source: :91-92
        _message = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :94-96
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :98
            _commArea = ctx.CommArea!;
            RestoreCt01Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :99
                _commArea.SetReenter();              // SET CDEMO-PGM-REENTER TO TRUE. source: :100
                MoveLowValuesToMapOut();             // MOVE LOW-VALUES TO COTRN1AO. source: :101
                _map.Field("TRNIDIN").CursorLength = -1; // MOVE -1 TO TRNIDINL OF COTRN1AI. source: :102

                // IF CDEMO-CT01-TRN-SELECTED NOT = SPACES AND LOW-VALUES. source: :103-104
                if (NotSpacesOrLow(_selectedTranId))
                {
                    // MOVE CDEMO-CT01-TRN-SELECTED TO TRNIDINI OF COTRN1AI. source: :105-106
                    _map.Field("TRNIDIN").SetValue(_selectedTranId, setMdt: false);
                    ProcessEnterKey(ctx);            // PERFORM PROCESS-ENTER-KEY. source: :107
                }

                // FB-3: PERFORM SEND-TRNVIEW-SCREEN is UNCONDITIONAL (outside the IF) — runs even after
                // PROCESS-ENTER-KEY already SENT, so the map is sent twice. source: :109
                SendTrnviewScreen(ctx);
            }
            else
            {
                ReceiveTrnviewScreen(ctx);            // PERFORM RECEIVE-TRNVIEW-SCREEN. source: :111
                // EVALUATE EIBAID. source: :112
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);        // WHEN DFHENTER. source: :113-114
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3 — back to caller, or COMEN01C when no FROM-PROGRAM. source: :115-122
                        if (IsSpacesOrLowValues(_commArea.FromProgram))
                            _commArea.ToProgram = "COMEN01C";          // source: :116-117
                        else
                            _commArea.ToProgram = _commArea.FromProgram; // source: :118-120
                        ReturnToPrevScreen(ctx);                       // PERFORM RETURN-TO-PREV-SCREEN. source: :122
                        break;
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);     // WHEN DFHPF4. source: :123-124
                        break;
                    case AidKey.Pf5:
                        _commArea.ToProgram = "COTRN00C"; // WHEN DFHPF5 -> MOVE 'COTRN00C'. source: :125-126
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :127
                        break;
                    default:
                        // WHEN OTHER. source: :128-131
                        _errorFlagOn = true;                       // MOVE 'Y' TO WS-ERR-FLG. source: :129
                        _message = MsgInvalidKey;           // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. source: :130
                        SendTrnviewScreen(ctx);                    // PERFORM SEND-TRNVIEW-SCREEN. source: :131
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :136-139
        if (ctx.Outcome is null)
        {
            SaveCt01Info();
            ctx.ReturnTransId(TranId, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COTRN01C.cbl:144-192
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE. source: :146-156
        if (IsSpacesOrLowValues(_map.Field("TRNIDIN").Value))
        {
            // WHEN TRNIDINI = SPACES OR LOW-VALUES. source: :147-152
            _errorFlagOn = true;                                // MOVE 'Y' TO WS-ERR-FLG. source: :148
            _message = "Tran ID can NOT be empty...";           // source: :149-150
            _map.Field("TRNIDIN").CursorLength = -1;            // MOVE -1 TO TRNIDINL. source: :151
            SendTrnviewScreen(ctx);                             // PERFORM SEND-TRNVIEW-SCREEN. source: :152
        }
        else
        {
            // WHEN OTHER -> MOVE -1 TO TRNIDINL, CONTINUE. source: :153-155
            _map.Field("TRNIDIN").CursorLength = -1;
        }

        // IF NOT ERR-FLG-ON. source: :158-174
        if (!ErrorFlagOn)
        {
            // MOVE SPACES TO the twelve display out-fields. source: :159-171
            _map.Field("TRNID").SetValue("", setMdt: false);    // TRNIDI    source: :159
            _map.Field("CARDNUM").SetValue("", setMdt: false);  // CARDNUMI  source: :160
            _map.Field("TTYPCD").SetValue("", setMdt: false);   // TTYPCDI   source: :161
            _map.Field("TCATCD").SetValue("", setMdt: false);   // TCATCDI   source: :162
            _map.Field("TRNSRC").SetValue("", setMdt: false);   // TRNSRCI   source: :163
            _map.Field("TRNAMT").SetValue("", setMdt: false);   // TRNAMTI   source: :164
            _map.Field("TDESC").SetValue("", setMdt: false);    // TDESCI    source: :165
            _map.Field("TORIGDT").SetValue("", setMdt: false);  // TORIGDTI  source: :166
            _map.Field("TPROCDT").SetValue("", setMdt: false);  // TPROCDTI  source: :167
            _map.Field("MID").SetValue("", setMdt: false);      // MIDI      source: :168
            _map.Field("MNAME").SetValue("", setMdt: false);    // MNAMEI    source: :169
            _map.Field("MCITY").SetValue("", setMdt: false);    // MCITYI    source: :170
            _map.Field("MZIP").SetValue("", setMdt: false);     // MZIPI     source: :171

            // MOVE TRNIDINI OF COTRN1AI TO TRAN-ID. source: :172
            _recordKey = PadX(_map.Field("TRNIDIN").Value, 16);
            ReadTransactFile(ctx);                              // PERFORM READ-TRANSACT-FILE. source: :173
        }

        // IF NOT ERR-FLG-ON. source: :176-192
        if (!ErrorFlagOn)
        {
            // MOVE TRAN-AMT TO WS-TRAN-AMT (PIC +99999999.99 — FB-2 truncates the 9th integer digit). source: :177
            string wsTranAmt = EditTranAmt(TranAmt);

            _map.Field("TRNID").SetValue(TranRecId, setMdt: false);     // MOVE TRAN-ID          TO TRNIDI.   source: :178
            _map.Field("CARDNUM").SetValue(TranCardNum, setMdt: false); // MOVE TRAN-CARD-NUM    TO CARDNUMI. source: :179
            _map.Field("TTYPCD").SetValue(TranTypeCd, setMdt: false);   // MOVE TRAN-TYPE-CD     TO TTYPCDI.  source: :180
            _map.Field("TCATCD").SetValue(TranCatCd, setMdt: false);    // MOVE TRAN-CAT-CD      TO TCATCDI.  source: :181
            _map.Field("TRNSRC").SetValue(TranSource, setMdt: false);   // MOVE TRAN-SOURCE      TO TRNSRCI.  source: :182
            _map.Field("TRNAMT").SetValue(wsTranAmt, setMdt: false);    // MOVE WS-TRAN-AMT      TO TRNAMTI.  source: :183
            _map.Field("TDESC").SetValue(TranDesc, setMdt: false);      // MOVE TRAN-DESC        TO TDESCI.   source: :184
            _map.Field("TORIGDT").SetValue(TranOrigTs, setMdt: false);  // MOVE TRAN-ORIG-TS     TO TORIGDTI. source: :185
            _map.Field("TPROCDT").SetValue(TranProcTs, setMdt: false);  // MOVE TRAN-PROC-TS     TO TPROCDTI. source: :186
            _map.Field("MID").SetValue(TranMerchantId, setMdt: false);  // MOVE TRAN-MERCHANT-ID TO MIDI.     source: :187
            _map.Field("MNAME").SetValue(TranMerchantName, setMdt: false);// MOVE TRAN-MERCHANT-NAME TO MNAMEI. source: :188
            _map.Field("MCITY").SetValue(TranMerchantCity, setMdt: false);// MOVE TRAN-MERCHANT-CITY TO MCITYI. source: :189
            _map.Field("MZIP").SetValue(TranMerchantZip, setMdt: false); // MOVE TRAN-MERCHANT-ZIP TO MZIPI.   source: :190
            SendTrnviewScreen(ctx);                                     // PERFORM SEND-TRNVIEW-SCREEN. source: :191
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COTRN01C.cbl:197-208
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :199-201
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = TranId;     // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :202
        _commArea.FromProgram = ProgramId; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :203
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :204

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :205-208
        SaveCt01Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-TRNVIEW-SCREEN — source: COTRN01C.cbl:213-225
    // =============================================================================================
    private void SendTrnviewScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                    // PERFORM POPULATE-HEADER-INFO. source: :215

        _map.Field("ERRMSG").SetValue(_message, setMdt: false);    // MOVE WS-MESSAGE TO ERRMSGO. source: :217

        // EXEC CICS SEND MAP('COTRN1A') MAPSET('COTRN01') FROM(COTRN1AO) ERASE CURSOR. source: :219-225
        ctx.SendMap("COTRN1A", "COTRN01", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO TRNIDINL set throughout.
        });
        _responseCode = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-TRNVIEW-SCREEN — source: COTRN01C.cbl:230-238
    // =============================================================================================
    private void ReceiveTrnviewScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COTRN1A') MAPSET('COTRN01') INTO(COTRN1AI) RESP RESP2. source: :232-238
        ctx.ReceiveMap("COTRN1A", "COTRN01", _map);
        _responseCode = (int)Resp.Normal;
        _reasonCode = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COTRN01C.cbl:243-262
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :245
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(Title01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :247
        _map.Field("TITLE02").SetValue(Title02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :248
        _map.Field("TRNNAME").SetValue(TranId, setMdt: false);       // MOVE WS-TRANID  TO TRNNAMEO. source: :249
        _map.Field("PGMNAME").SetValue(ProgramId, setMdt: false);    // MOVE WS-PGMNAME TO PGMNAMEO. source: :250

        // CURDATEO = mm/dd/yy (year last two digits). source: :252-256
        _map.Field("CURDATE").SetValue(
            $"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :258-262
        _map.Field("CURTIME").SetValue(
            $"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  READ-TRANSACT-FILE — source: COTRN01C.cbl:267-296
    // =============================================================================================
    private void ReadTransactFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-TRANSACT-FILE) INTO(TRAN-RECORD) RIDFLD(TRAN-ID) UPDATE RESP RESP2.
        // FB-1: UPDATE intent on a view-only read. The relational read is the keyed ReadByKey. source: :269-278
        string fileStatus = _transactions.ReadByKey(_recordKey, out _transactionRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :280-296
        switch ((Resp)_responseCode)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :281-282
                break;
            case Resp.NotFnd: // WHEN DFHRESP(NOTFND). source: :283-288
                _errorFlagOn = true;                           // MOVE 'Y' TO WS-ERR-FLG. source: :284
                _message = "Transaction ID NOT found...";      // source: :285-286
                _map.Field("TRNIDIN").CursorLength = -1;       // MOVE -1 TO TRNIDINL. source: :287
                SendTrnviewScreen(ctx);                        // PERFORM SEND-TRNVIEW-SCREEN. source: :288
                break;
            default: // WHEN OTHER. source: :289-295
                // FB-5: DISPLAY 'RESP:' WS-RESP-CD 'REAS:' WS-REAS-CD -> job-log trace; no-op here. source: :290
                _errorFlagOn = true;                           // MOVE 'Y' TO WS-ERR-FLG. source: :291
                _message = "Unable to lookup Transaction...";  // source: :292-293
                _map.Field("TRNIDIN").CursorLength = -1;       // MOVE -1 TO TRNIDINL. source: :294
                SendTrnviewScreen(ctx);                        // PERFORM SEND-TRNVIEW-SCREEN. source: :295
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COTRN01C.cbl:301-304
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();   // PERFORM INITIALIZE-ALL-FIELDS. source: :303
        SendTrnviewScreen(ctx);  // PERFORM SEND-TRNVIEW-SCREEN. source: :304
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COTRN01C.cbl:309-326
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("TRNIDIN").CursorLength = -1;             // MOVE -1 TO TRNIDINL. source: :311
        // MOVE SPACES TO TRNIDINI + the twelve display fields + WS-MESSAGE. source: :312-326
        _map.Field("TRNIDIN").SetValue("", setMdt: false);  // TRNIDINI  source: :312
        _map.Field("TRNID").SetValue("", setMdt: false);    // TRNIDI    source: :313
        _map.Field("CARDNUM").SetValue("", setMdt: false);  // CARDNUMI  source: :314
        _map.Field("TTYPCD").SetValue("", setMdt: false);   // TTYPCDI   source: :315
        _map.Field("TCATCD").SetValue("", setMdt: false);   // TCATCDI   source: :316
        _map.Field("TRNSRC").SetValue("", setMdt: false);   // TRNSRCI   source: :317
        _map.Field("TRNAMT").SetValue("", setMdt: false);   // TRNAMTI   source: :318
        _map.Field("TDESC").SetValue("", setMdt: false);    // TDESCI    source: :319
        _map.Field("TORIGDT").SetValue("", setMdt: false);  // TORIGDTI  source: :320
        _map.Field("TPROCDT").SetValue("", setMdt: false);  // TPROCDTI  source: :321
        _map.Field("MID").SetValue("", setMdt: false);      // MIDI      source: :322
        _map.Field("MNAME").SetValue("", setMdt: false);    // MNAMEI    source: :323
        _map.Field("MCITY").SetValue("", setMdt: false);    // MCITYI    source: :324
        _map.Field("MZIP").SetValue("", setMdt: false);     // MZIPI     source: :325
        _message = "";                                      // WS-MESSAGE source: :326
    }

    // =============================================================================================
    //  TRAN-RECORD field accessors (CVTRA05Y), with COBOL fixed-width formatting.
    // =============================================================================================
    private string TranRecId => PadX(_transactionRecord?.TranId, 16);            // TRAN-ID            X(16)
    private string TranCardNum => PadX(_transactionRecord?.CardNum, 16);         // TRAN-CARD-NUM      X(16)
    private string TranTypeCd => PadX(_transactionRecord?.TypeCd, 2);            // TRAN-TYPE-CD       X(02)
    private string TranCatCd => Zoned(_transactionRecord?.CatCd ?? 0, 4);        // TRAN-CAT-CD        9(04)
    private string TranSource => PadX(_transactionRecord?.Source, 10);           // TRAN-SOURCE        X(10)
    private decimal TranAmt => _transactionRecord?.Amt ?? 0m;                    // TRAN-AMT           S9(09)V99
    private string TranDesc => PadX(_transactionRecord?.Desc, 100);             // TRAN-DESC          X(100) (TDESC field is L60; SetValue truncates)
    private string TranOrigTs => PadX(_transactionRecord?.OrigTs, 26);          // TRAN-ORIG-TS       X(26) (TORIGDT field is L10; SetValue truncates)
    private string TranProcTs => PadX(_transactionRecord?.ProcTs, 26);          // TRAN-PROC-TS       X(26) (TPROCDT field is L10; SetValue truncates)
    private string TranMerchantId => Zoned(_transactionRecord?.MerchantId ?? 0, 9); // TRAN-MERCHANT-ID 9(09) (MID field is L9)
    private string TranMerchantName => PadX(_transactionRecord?.MerchantName, 50);  // TRAN-MERCHANT-NAME X(50) (MNAME field is L30; SetValue truncates)
    private string TranMerchantCity => PadX(_transactionRecord?.MerchantCity, 50);  // TRAN-MERCHANT-CITY X(50) (MCITY field is L25; SetValue truncates)
    private string TranMerchantZip => PadX(_transactionRecord?.MerchantZip, 10);    // TRAN-MERCHANT-ZIP  X(10) (MZIP field is L10)

    /// <summary>MOVE LOW-VALUES TO COTRN1AO — blank every named output field + clear per-turn overrides. source: :101</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  WS-RESP-CD mapper — repository FileStatus -> CICS RESP. source: EVALUATE WS-RESP-CD branches.
    // =============================================================================================
    private void SetResp(string fileStatus)
    {
        _responseCode = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            FileStatus.EndOfFile => (int)Resp.EndFile,     // '10' -> DFHRESP(ENDFILE)
            FileStatus.DuplicateKey => (int)Resp.DupRec,   // '02' -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _reasonCode = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  WS-TRAN-AMT edit — PIC +99999999.99 (sign, 8 int digits, '.', 2 dec). source: :49,177
    //  FB-2: only 8 integer digits, so the 9th (high-order) digit of TRAN-AMT S9(9)V99 truncates.
    // =============================================================================================
    private static string EditTranAmt(decimal amt)
    {
        // Truncate toward zero to 2 decimals (no rounding), then format the fixed +99999999.99 picture.
        decimal scaled = decimal.Truncate(amt * 100m) / 100m;
        bool negative = scaled < 0m;
        decimal mag = negative ? -scaled : scaled;
        long cents = (long)decimal.Truncate(mag * 100m);
        long intPart = cents / 100;
        long decPart = cents % 100;
        // 8 integer digits, zero-filled; the leading sign is always present (+ for >=0, - for <0).
        string ip = (intPart % 100000000L).ToString("D8");
        string dp = decPart.ToString("D2");
        return (negative ? "-" : "+") + ip + "." + dp;
    }

    // =============================================================================================
    //  CDEMO-CT01-INFO (de)serialize — carried across turns in the COMMAREA's unused customer slots.
    //  source: COTRN01C.cbl:53-61,98,136-139
    // =============================================================================================
    // COTRN01C never reads/writes CDEMO-CUSTOMER-INFO; the CDEMO-CT01-INFO trailer is packed there so its
    // state (TRNID-FIRST/LAST, PAGE-NUM, NEXT-PAGE-FLG, TRN-SEL-FLG, TRN-SELECTED) round-trips losslessly
    // each turn. Only TRN-SELECTED is consumed (the "selected from list" first-display path), but all of
    // CDEMO-CT01-INFO is preserved for fidelity with the COBOL overlay.
    // Pack layout into CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   TRNID-FIRST X(16) | TRNID-LAST X(16) | PAGE-NUM 9(8) | NEXT X(1) | SEL-FLG X(1) | TRN-SELECTED X(16).
    private void SaveCt01Info()
    {
        string packed =
            PadX(_firstTranId, 16) +
            PadX(_lastTranId, 16) +
            Zoned(_pageNumber, 8) +
            (_nextPageFlag == '\0' ? 'N' : _nextPageFlag) +
            (string.IsNullOrEmpty(_tranSelectFlag) ? " " : _tranSelectFlag.Substring(0, 1)) +
            PadX(_selectedTranId, 16);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCt01Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        packed = PadX(packed, 75);
        _firstTranId = packed.Substring(0, 16).TrimEnd();
        _lastTranId = packed.Substring(16, 16).TrimEnd();
        _pageNumber = (int)ParseLong(packed.Substring(32, 8));
        char nx = packed[40];
        _nextPageFlag = nx == 'Y' ? 'Y' : 'N';
        char sf = packed[41];
        _tranSelectFlag = sf == ' ' ? "" : sf.ToString();
        _selectedTranId = packed.Substring(42, 16).TrimEnd();
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
    //  BMS map builder — COTRN1A in mapset COTRN01 (24x80).
    //  source: app/bms/COTRN01.bms:19-270 / SCREEN_COTRN01.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COTRN01.bms:26.</summary>
    public const string MapName = "COTRN1A";

    /// <summary>The DFHMSD mapset name. source: COTRN01.bms:19.</summary>
    public const string MapsetName = "COTRN01";

    /// <summary>
    /// Constructs the <c>COTRN1A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The only keyable field is
    /// <c>TRNIDIN</c> (6,21) L16 which carries <c>IC</c> (the initial cursor); every other named field is an
    /// ASKIP display field. No PICIN/PICOUT clauses appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                               // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                  // bms:34-37
            Out("TITLE01", 1, 21, 40, AskipFset, BmsColor.Yellow),              // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                              // bms:42-46
            OutInit("CURDATE", 1, 71, 8, AskipFset, BmsColor.Blue, "mm/dd/yy"), // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                               // bms:52-56
            Out("PGMNAME", 2, 7, 8, AskipFset, BmsColor.Blue),                  // bms:57-60
            Out("TITLE02", 2, 21, 40, AskipFset, BmsColor.Yellow),              // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                              // bms:65-69
            OutInit("CURTIME", 2, 71, 8, AskipFset, BmsColor.Blue, "hh:mm:ss"), // bms:70-74

            // ----- 'View Transaction' heading (bms:75-79) -----
            LitAttr(4, 30, 16, AskipBrt, BmsColor.Neutral, "View Transaction"), // bms:75-79

            // ----- Enter Tran ID label + the lone input field (bms:80-93) -----
            Lit(6, 6, 14, BmsColor.Turquoise, "Enter Tran ID:"),                // bms:80-84
            // TRNIDIN: ATTRB=(FSET,IC,NORM,UNPROT) GREEN UNDERLINE INITIAL ' ' — the IC (cursor) field.
            new ScreenField
            {
                Name = "TRNIDIN", Row = 6, Col = 21, Length = 16,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
                Value = " ",
            },                                                                  // bms:85-90
            Stopper(6, 38),                                                     // bms:91-93

            // ----- divider (70 dashes) (bms:94-99) -----
            Lit(8, 6, 70, BmsColor.Neutral, new string('-', 70)),               // bms:94-99

            // ----- row 10: Transaction ID + Card Number (bms:100-126) -----
            Lit(10, 6, 15, BmsColor.Turquoise, "Transaction ID:"),              // bms:100-104
            OutInit("TRNID", 10, 22, 16, Askip, BmsColor.Blue, " "),            // bms:105-109
            Stopper(10, 39),                                                    // bms:110-112
            Lit(10, 45, 12, BmsColor.Turquoise, "Card Number:"),                // bms:113-117
            OutInit("CARDNUM", 10, 58, 16, Askip, BmsColor.Blue, " "),          // bms:118-122
            StopperColor(10, 75, BmsColor.Green),                              // bms:123-126 (L0, COLOR=GREEN)

            // ----- row 12: Type CD / Category CD / Source (bms:127-162) -----
            Lit(12, 6, 8, BmsColor.Turquoise, "Type CD:"),                      // bms:127-131
            OutInit("TTYPCD", 12, 15, 2, Askip, BmsColor.Blue, " "),            // bms:132-136
            StopperBare(12, 18),                                                // bms:137-138 (L0, no ATTRB/COLOR)
            Lit(12, 23, 12, BmsColor.Turquoise, "Category CD:"),                // bms:139-143
            OutInit("TCATCD", 12, 36, 4, Askip, BmsColor.Blue, " "),            // bms:144-148
            StopperBare(12, 41),                                                // bms:149-150
            Lit(12, 46, 7, BmsColor.Turquoise, "Source:"),                      // bms:151-155
            OutInit("TRNSRC", 12, 54, 10, Askip, BmsColor.Blue, " "),           // bms:156-160
            StopperBare(12, 65),                                                // bms:161-162

            // ----- row 14: Description (bms:163-174) -----
            Lit(14, 6, 12, BmsColor.Turquoise, "Description:"),                 // bms:163-167
            OutInit("TDESC", 14, 19, 60, Askip, BmsColor.Blue, " "),            // bms:168-172
            StopperBare(14, 80),                                                // bms:173-174

            // ----- row 16: Amount / Orig Date / Proc Date (bms:175-210) -----
            Lit(16, 6, 7, BmsColor.Turquoise, "Amount:"),                       // bms:175-179
            OutInit("TRNAMT", 16, 14, 12, Askip, BmsColor.Blue, " "),           // bms:180-184
            StopperBare(16, 27),                                                // bms:185-186
            Lit(16, 31, 10, BmsColor.Turquoise, "Orig Date:"),                  // bms:187-191
            OutInit("TORIGDT", 16, 42, 10, Askip, BmsColor.Blue, " "),          // bms:192-196
            StopperBare(16, 53),                                                // bms:197-198
            Lit(16, 57, 10, BmsColor.Turquoise, "Proc Date:"),                  // bms:199-203
            OutInit("TPROCDT", 16, 68, 10, Askip, BmsColor.Blue, " "),          // bms:204-208
            StopperBare(16, 79),                                                // bms:209-210

            // ----- row 18: Merchant ID / Merchant Name (bms:211-234) -----
            Lit(18, 6, 12, BmsColor.Turquoise, "Merchant ID:"),                 // bms:211-215
            OutInit("MID", 18, 19, 9, Askip, BmsColor.Blue, " "),               // bms:216-220
            StopperBare(18, 29),                                                // bms:221-222
            Lit(18, 33, 14, BmsColor.Turquoise, "Merchant Name:"),              // bms:223-227
            OutInit("MNAME", 18, 48, 30, Askip, BmsColor.Blue, " "),            // bms:228-232
            StopperBare(18, 79),                                                // bms:233-234

            // ----- row 20: Merchant City / Merchant Zip (bms:235-258) -----
            Lit(20, 6, 14, BmsColor.Turquoise, "Merchant City:"),               // bms:235-239
            OutInit("MCITY", 20, 21, 25, Askip, BmsColor.Blue, " "),            // bms:240-244
            StopperBare(20, 47),                                                // bms:245-246
            Lit(20, 53, 13, BmsColor.Turquoise, "Merchant Zip:"),               // bms:247-251
            OutInit("MZIP", 20, 67, 10, Askip, BmsColor.Blue, " "),             // bms:252-256
            StopperBare(20, 78),                                                // bms:257-258

            // ----- error line + footer (bms:259-268) -----
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),               // bms:259-262
            Lit(24, 1, 47, BmsColor.Yellow,
                "ENTER=Fetch  F3=Back  F4=Clear  F5=Browse Tran."),             // bms:263-268
        };

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

    /// <summary>Named output field (no INITIAL).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>A LENGTH=0 stopper field with ATTRB=(ASKIP,NORM) and no COLOR (device default).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };

    /// <summary>A LENGTH=0 stopper field with ATTRB=(ASKIP,NORM) and an explicit COLOR (e.g. GREEN at 10,75).</summary>
    private static ScreenField StopperColor(int row, int col, BmsColor color) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = color };

    /// <summary>A bare LENGTH=0 stopper field (DFHMDF LENGTH=0 POS=... with no ATTRB/COLOR operands).</summary>
    private static ScreenField StopperBare(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = BmsAttribute.None, Color = BmsColor.Default };
}
