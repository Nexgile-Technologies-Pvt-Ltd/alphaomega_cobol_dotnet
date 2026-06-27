using System.Globalization;
using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Mq.Programs;

/// <summary>
/// Faithful .NET port of the optional CICS/IMS/DB2/MQ COBOL program <c>COPAUA0C</c> — the CardDemo
/// <b>Card Authorization Decision Processor</b> (CICS transaction <c>CP00</c>, MQ-triggered server, no
/// BMS screen). It drains the PAUTH request queue of comma-delimited authorization requests and, for
/// each one: schedules its IMS PSB; reads the card cross-reference (XREF) to resolve account/customer
/// ids; reads the ACCOUNT and CUSTOMER masters; reads the IMS pending-authorization <i>summary</i> root
/// segment; decides approve/decline (decline if <c>transaction-amt &gt; available credit</c> — available
/// = credit-limit − credit-balance from the IMS summary if present, else the account master — or if the
/// card was not found); PUTs a CSV reply to the requester's reply queue; then, if the card was found,
/// persists the result to IMS by updating/inserting the summary running totals and inserting a new
/// pending-authorization <i>detail</i> child segment. It loops until the request queue is empty (5 s wait
/// expiry) or 500 messages have been processed, taking a SYNCPOINT (COMMIT) after each message.
/// </summary>
/// <remarks>
/// <para><b>Structure.</b> This is a "called service" rather than a screen program: there is no
/// <c>EXEC CICS SEND/RECEIVE MAP</c>, no EIBAID/PFKey handling and no COMMAREA flow (the LINKAGE
/// <c>DFHCOMMAREA</c> is declared but never referenced). The "online" surface is the MQ request→reply
/// envelope, so the port is an <see cref="IMqServer"/> in <c>CardDemo.Mq</c> (per COPAUA0C.md §1/§8 and
/// MQ_SHIM.md §6.2), driven by the in-proc <see cref="MqBroker"/>. Each COBOL paragraph is one method
/// carrying its original name and a <c>// source: COPAUA0C.cbl:NNN</c> citation; statement order, the
/// <c>EVALUATE</c>/<c>PERFORM</c> control flow and every faithful bug are preserved verbatim.</para>
///
/// <para><b>EXEC SQL / DL/I / MQ mapping.</b> The CICS file READs become repository <c>ReadByKey</c>
/// calls returning the two-char <see cref="FileStatus"/> the COBOL <c>EVALUATE WS-RESP-CD</c> branches on
/// (Ok '00' → DFHRESP(NORMAL); RecordNotFound '23' → DFHRESP(NOTFND); anything else → critical, the
/// abend path). The DL/I calls map to <see cref="PautSummaryRepository"/> / <see cref="PautDetailRepository"/>
/// (GU → <c>ReadByKey</c>; REPL → <c>Update</c>; ISRT → <c>Insert</c>; status <c>'  '</c>/<c>GE</c> →
/// '00'/'23'). The MQ verbs map to the <see cref="MqBroker"/> shim (MQOPEN/MQGET → <see cref="MqBroker.Open"/>
/// / <see cref="MqBroker.Get"/>; MQPUT1 → <see cref="MqBroker.Put1"/>; MQCLOSE → <see cref="MqBroker.Close"/>).
/// The CICS TD WRITEQ('CSSL') error log maps to the injected <see cref="IErrorLog"/>. Money is COBOL
/// fixed-point: truncate toward zero, no rounding, silent high-order overflow (<see cref="Decimals"/>).</para>
///
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="number">
/// <item>FB-1 — declined-amount accumulates the <i>detail</i> field <c>PA-TRANSACTION-AMT</c> in
/// 8400-UPDATE-SUMMARY <b>before</b> 8500-INSERT-AUTH populates it, so it adds the prior value (0 on a
/// fresh INITIALIZEd summary, the previous message's amount on an existing one). source: :820-821 vs :885.</item>
/// <item>FB-2 — CUSTOMER record is read but never used by the decision/writes (only sets the found flag +
/// logs). source: :571-609.</item>
/// <item>FB-3 — decline reasons 4200/4300/5100/5200 are dead (their flags are never SET). source: :140-145,707-714.</item>
/// <item>FB-4 — the M003 MQGET failure logs subsystem <c>'C'</c> (CICS) instead of <c>'M'</c> (MQ). source: :419-421.</item>
/// <item>FB-5 — A002/A003 messages say "IN XREF" though they report the ACCT/CUST master reads. source: :544,592.</item>
/// <item>FB-6 — the STRING pointer <c>WS-RESP-LENGTH</c> (VALUE 1) is never reset per message, so on the
/// 2nd+ message the reply STRING begins at the leftover position and the PUT length keeps growing (the
/// pointer is threaded across messages, capped at the 200-byte buffer, STRING overflow stops silently).
/// source: :46,722-731,756.</item>
/// <item>FB-7 — optimistic presets (<c>CARD-FOUND-XREF</c>, <c>FOUND-ACCT-IN-MSTR</c> = true) before the
/// reads; the read EVALUATEs correct them, and 5100's NOTFND also forces <c>NFOUND-ACCT-IN-MSTR</c>.
/// source: :441-457,490-492.</item>
/// <item>FB-8 — <c>MOVE 0 TO PA-CASH-BALANCE</c> only on approve (untouched on decline). source: :818.</item>
/// <item>FB-9 — credit/cash limits copied from ACCOUNT into the summary unconditionally, even when the
/// account read failed (stale ACCOUNT-RECORD). source: :810-811.</item>
/// </list>
/// </remarks>
public sealed class Copaua0c : IMqServer
{
    // =================================================================================================
    //  Injected collaborators (the relational data layer + MQ shim + error sink + clock).
    // =================================================================================================
    private readonly CardXrefRepository _xref;
    private readonly AccountRepository _accounts;
    private readonly CustomerRepository _customers;
    private readonly PautSummaryRepository _summary;
    private readonly PautDetailRepository _detail;
    private readonly IErrorLog _errorLog;
    private readonly IClock _clock;

    // The broker for the current Handle() invocation (the CICS-MQ adapter's ambient connection).
    private MqBroker _mq = null!;
    private MqQueueHandle? _requestHandle;

    // =================================================================================================
    //  WS-VARIABLES — source: COPAUA0C.cbl:32-67
    // =================================================================================================
    private const string WsPgmAuth = "COPAUA0C";                                       // :33
    private const string WsCicsTranid = "CP00";                                        // :34
    private const string WsCcxrefFile = "CCXREF  ";                                    // :39  (logical, info only)
    private const short WsReqstsProcessLimit = 500;                                    // :40

    private short _wsMsgProcessed;                                                     // :42 S9(4) COMP
    private string _wsRequestQname = "";                                              // :43 X(48)
    private string _wsReplyQname = "";                                                // :44 X(48)
    private byte[] _wsSaveCorrelid = MqConstants.MqciNone;                            // :45 X(24)

    // WS-RESP-LENGTH PIC S9(4) VALUE 1 — the STRING pointer. NOT reset per message (FB-6). source: :46
    private short _wsRespLength = 1;

    private int _wsWaitInterval;                                                       // :60 ms (set 5000)
    private decimal _wsAvailableAmt;                                                   // :62 S9(9)V99 COMP-3
    private string _wsTransactionAmtAn = "";                                          // :63 X(13)
    private decimal _wsTransactionAmt;                                                 // :64 S9(10)V99
    private decimal _wsApprovedAmt;                                                    // :65 S9(10)V99
    private string _wsApprovedAmtDis = "";                                            // :66 -zzzzzzzzz9.99
    private string _wsTriggerData = "";                                              // :67 X(64)

    // MQ work fields (W01 = request GET; W02 = reply PUT). source: :99-108
    private string _w01GetBuffer = "";          // X(500) — the raw GET payload (sub-stringed by DATALEN)
    private int _w01DataLen;                     // length of the message actually returned by GET

    // MQGET / MQPUT completion + reason (WS-COMPCODE / WS-REASON). source: :58-59
    private int _wsCompCode;
    private int _wsReason;

    // Time work fields. source: :50-56
    private string _wsCurDateX6 = "";           // FORMATTIME YYDDD (8500) / YYMMDD (9500)
    private string _wsCurTimeX6 = "";           // FORMATTIME TIME (HHMMSS)
    private int _wsCurTimeN6;                    // 9(6) numeric HHMMSS
    private int _wsCurTimeMs;                    // S9(8) COMP — milliseconds 0..999
    private int _wsYyddd;                        // 9(5)
    private long _wsTimeWithMs;                  // S9(9) COMP-3

    // =================================================================================================
    //  WS-SWITCHES — source: COPAUA0C.cbl:110-145
    // =================================================================================================
    private char _authRespFlg;                  // 'A' approved / 'D' declined
    private char _msgLoopFlg = 'N';             // 88 WS-LOOP-END = 'E'
    private char _msgAvailableFlg = 'M';        // 88 NO-MORE-MSG-AVAILABLE = 'N' / MORE = 'M'
    private char _requestMqFlg = 'C';           // 88 WS-REQUEST-MQ-OPEN = 'O' / CLSE = 'C'
    private char _xrefReadFlg;                   // 88 CARD-FOUND-XREF = 'Y' / NFOUND = 'N'
    private char _acctMasterReadFlg;            // 88 FOUND-ACCT-IN-MSTR = 'Y' / NFOUND = 'N'
    private char _custMasterReadFlg;            // 88 FOUND-CUST-IN-MSTR = 'Y' / NFOUND = 'N'
    private char _pautSmrySegFlg;               // 88 FOUND-PAUT-SMRY-SEG = 'Y' / NFOUND = 'N'
    private char _declineFlg;                   // 88 APPROVE-AUTH = 'A' / DECLINE-AUTH = 'D'
    private char _declineReasonFlg;             // 88 INSUFFICIENT-FUND='I' / CARD-NOT-ACTIVE='A' / ...

    // IMS scheduling flag (WS-IMS-PSB-SCHD-FLG). source: :95-97
    private char _imsPsbSchdFlg;                // 88 IMS-PSB-SCHD = 'Y' / NOT = 'N'

    // =================================================================================================
    //  Staging records (the parsed request, the built reply, the IMS segments). The COBOL keeps one of
    //  each in WORKING-STORAGE; here they are mutable fields reused across the drain loop (the COBOL
    //  does NOT re-INITIALIZE them between messages either — fields persist their prior values). source:
    //  CCPAURQY / CCPAURLY / CIPAUSMY / CIPAUDTY.
    // =================================================================================================
    private readonly PendingAuthRequest _rq = new();      // PENDING-AUTH-REQUEST  (CCPAURQY)
    private readonly PendingAuthResponse _rl = new();      // PENDING-AUTH-RESPONSE (CCPAURLY)
    private PautSummary _paSummary = new();                // PENDING-AUTH-SUMMARY  (CIPAUSMY)
    private readonly PautDetail _paDetail = new();         // PENDING-AUTH-DETAILS  (CIPAUDTY)

    // VSAM record buffers (read from the masters). source: CVACT03Y / CVACT01Y / CVCUS01Y.
    private CardXref? _cardXref;
    private Account? _account;
    // Customer is read but never consumed (FB-2); kept for parity/side-effect only.

    // =================================================================================================
    //  ERROR-LOG-RECORD — source: CCPAUERY.cpy (ERR-* fields, 122 bytes).
    // =================================================================================================
    private string _errLocation = "";
    private char _errLevel;                     // 'L'/'I'/'W'/'C'
    private char _errSubsystem;                 // 'A'/'C'/'I'/'D'/'M'/'F'
    private string _errCode1 = "";
    private string _errCode2 = "";
    private string _errMessage = "";
    private string _errEventKey = "";

    /// <summary>Signals the COBOL 9990-END-ROUTINE hard exit (critical error → TERM + EXEC CICS RETURN).</summary>
    private sealed class EndRoutineSignal : Exception { }

    // -------------------------------------------------------------------------------------------------

    /// <summary>Wires the auth processor over the relational DB, an error sink, and a clock.</summary>
    /// <param name="db">The relational connection backing the VSAM→SQL reads and the IMS-table writes.</param>
    /// <param name="errorLog">The CSSL TD-queue stand-in (defaults to a discard sink).</param>
    /// <param name="clock">Clock for ASKTIME/FORMATTIME (defaults to the system clock).</param>
    public Copaua0c(RelationalDb db, IErrorLog? errorLog = null, IClock? clock = null)
    {
        _xref = new CardXrefRepository(db);
        _accounts = new AccountRepository(db);
        _customers = new CustomerRepository(db);
        _summary = new PautSummaryRepository(db);
        _detail = new PautDetailRepository(db);
        _errorLog = errorLog ?? NullErrorLog.Instance;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>The request queue this server drains (the dispatcher key — MQ_SHIM.md §6.4).</summary>
    public string RequestQueue => MqQueues.PauthRequest;

    // =================================================================================================
    //  MAIN-PARA — source: COPAUA0C.cbl:220-227
    // =================================================================================================
    /// <summary>
    /// Drives the program: 1000-INITIALIZE → 2000-MAIN-PROCESS → 9000-TERMINATE, then EXEC CICS RETURN.
    /// A critical 9500-LOG-ERROR raises <see cref="EndRoutineSignal"/> (the COBOL 9990 short-circuit:
    /// 9000-TERMINATE + EXEC CICS RETURN) which unwinds here and ends the transaction. Returns the number
    /// of messages processed.
    /// </summary>
    public int Handle(TriggerMessage trigger, MqBroker mq)
    {
        _mq = mq;
        try
        {
            Initialize1000(trigger);          // PERFORM 1000-INITIALIZE
            MainProcess2000();                // PERFORM 2000-MAIN-PROCESS
            Terminate9000();                  // PERFORM 9000-TERMINATE
        }
        catch (EndRoutineSignal)
        {
            // 9990-END-ROUTINE already performed 9000-TERMINATE; EXEC CICS RETURN ends the transaction.
        }
        // EXEC CICS RETURN. source: :226-227
        return _wsMsgProcessed;
    }

    // =================================================================================================
    //  1000-INITIALIZE — source: COPAUA0C.cbl:230-250
    // =================================================================================================
    private void Initialize1000(TriggerMessage trigger)
    {
        // EXEC CICS RETRIEVE INTO(MQTM) NOHANDLE; if NORMAL move MQTM-QNAME/TRIGGERDATA. source: :233-240
        // The shim always supplies the trigger (RETRIEVE succeeds).
        _wsRequestQname = trigger.QueueName;
        _wsTriggerData = trigger.TriggerData;

        _wsWaitInterval = 5000;                                 // MOVE 5000 TO WS-WAIT-INTERVAL. source: :242

        OpenRequestQueue1100();                                 // PERFORM 1100-OPEN-REQUEST-QUEUE. source: :244
        ReadRequestMq3100();                                   // PERFORM 3100-READ-REQUEST-MQ (prime). source: :246
    }

    // =================================================================================================
    //  1100-OPEN-REQUEST-QUEUE — source: COPAUA0C.cbl:255-287
    // =================================================================================================
    private void OpenRequestQueue1100()
    {
        // MQOD-OBJECTTYPE = MQOT-Q; MQOD-OBJECTNAME = WS-REQUEST-QNAME; WS-OPTIONS = MQOO-INPUT-SHARED.
        // CALL 'MQOPEN'. source: :257-268
        _requestHandle = _mq.Open(_wsRequestQname, MqooInputShared);
        _wsCompCode = MqConstants.MqccOk;                       // the in-proc Open always succeeds
        _wsReason = MqConstants.MqrcNone;

        if (_wsCompCode == MqConstants.MqccOk)                  // IF WS-COMPCODE = MQCC-OK. source: :270
        {
            SetRequestMqOpen();                                // SET WS-REQUEST-MQ-OPEN TO TRUE
        }
        else
        {
            _errLocation = "M001";                             // source: :273-282
            _errLevel = 'C'; _errSubsystem = 'M';
            _errCode1 = CodeDisplay(_wsCompCode);
            _errCode2 = CodeDisplay(_wsReason);
            _errMessage = "REQ MQ OPEN ERROR";
            LogError9500();
        }
    }

    // =================================================================================================
    //  1200-SCHEDULE-PSB — source: COPAUA0C.cbl:292-321
    // =================================================================================================
    private void SchedulePsb1200()
    {
        // EXEC DLI SCHD PSB(PSBPAUTB) NODHABEND; MOVE DIBSTAT TO IMS-RETURN-CODE.
        // The relational re-host opens a unit of work; SCHD always succeeds (status spaces = OK).
        // The TC ("scheduled more than once") TERM+re-SCHD branch cannot arise in-proc. source: :293-307
        string imsReturnCode = ImsOk;

        if (IsStatusOk(imsReturnCode))                         // IF STATUS-OK. source: :308
        {
            SetImsPsbSchd();                                   // SET IMS-PSB-SCHD TO TRUE
        }
        else
        {
            _errLocation = "I001";                             // source: :311-316
            _errLevel = 'C'; _errSubsystem = 'I';
            _errCode1 = imsReturnCode;
            _errMessage = "IMS SCHD FAILED";
            LogError9500();
        }
    }

    // =================================================================================================
    //  2000-MAIN-PROCESS — source: COPAUA0C.cbl:323-348
    // =================================================================================================
    private void MainProcess2000()
    {
        // PERFORM UNTIL NO-MORE-MSG-AVAILABLE OR WS-LOOP-END
        while (!IsNoMoreMsgAvailable() && !IsLoopEnd())
        {
            ExtractRequestMsg2100();                           // PERFORM 2100-EXTRACT-REQUEST-MSG. source: :328
            ProcessAuth5000();                                 // PERFORM 5000-PROCESS-AUTH. source: :330

            _wsMsgProcessed++;                                 // ADD 1 TO WS-MSG-PROCESSED. source: :332

            // EXEC CICS SYNCPOINT (COMMIT). In-proc the repository writes are already committed; the
            // syncpoint boundary is the implicit per-statement commit on the shared connection. source: :334-336
            SetImsPsbNotSchd();                                // SET IMS-PSB-NOT-SCHD TO TRUE. source: :337

            if (_wsMsgProcessed > WsReqstsProcessLimit)        // IF WS-MSG-PROCESSED > 500. source: :339
            {
                SetLoopEnd();                                  // SET WS-LOOP-END TO TRUE
            }
            else
            {
                ReadRequestMq3100();                           // PERFORM 3100-READ-REQUEST-MQ (next). source: :342
            }
        }
    }

    // =================================================================================================
    //  2100-EXTRACT-REQUEST-MSG — source: COPAUA0C.cbl:351-383
    // =================================================================================================
    private void ExtractRequestMsg2100()
    {
        // UNSTRING W01-GET-BUFFER(1:W01-DATALEN) DELIMITED BY ',' INTO the 18 request receivers.
        // COBOL UNSTRING: a value longer than its receiver truncates to the receiver size; fewer commas
        // leave trailing receivers UNCHANGED (their prior value persists — faithful). source: :354-374
        string buffer = _w01DataLen <= 0
            ? ""
            : _w01GetBuffer.Substring(0, Math.Min(_w01DataLen, _w01GetBuffer.Length));
        string[] parts = buffer.Split(',');

        int i = 0;
        Unstring(parts, ref i, 6, v => _rq.AuthDate = v);                 // 1  PA-RQ-AUTH-DATE X(6)
        Unstring(parts, ref i, 6, v => _rq.AuthTime = v);                 // 2  PA-RQ-AUTH-TIME X(6)
        Unstring(parts, ref i, 16, v => _rq.CardNum = v);                 // 3  PA-RQ-CARD-NUM X(16)
        Unstring(parts, ref i, 4, v => _rq.AuthType = v);                 // 4  PA-RQ-AUTH-TYPE X(4)
        Unstring(parts, ref i, 4, v => _rq.CardExpiryDate = v);           // 5  PA-RQ-CARD-EXPIRY-DATE X(4)
        Unstring(parts, ref i, 6, v => _rq.MessageType = v);              // 6  PA-RQ-MESSAGE-TYPE X(6)
        Unstring(parts, ref i, 6, v => _rq.MessageSource = v);            // 7  PA-RQ-MESSAGE-SOURCE X(6)
        Unstring(parts, ref i, 6, v => _rq.ProcessingCode = ToNum6(v));   // 8  PA-RQ-PROCESSING-CODE 9(6)
        Unstring(parts, ref i, 13, v => _wsTransactionAmtAn = v);         // 9  -> WS-TRANSACTION-AMT-AN X(13)
        Unstring(parts, ref i, 4, v => _rq.MerchantCatagoryCode = v);     // 10 PA-RQ-MERCHANT-CATAGORY-CODE X(4)
        Unstring(parts, ref i, 3, v => _rq.AcqrCountryCode = v);          // 11 PA-RQ-ACQR-COUNTRY-CODE X(3)
        Unstring(parts, ref i, 2, v => _rq.PosEntryMode = ToNum2(v));     // 12 PA-RQ-POS-ENTRY-MODE 9(2)
        Unstring(parts, ref i, 15, v => _rq.MerchantId = v);              // 13 PA-RQ-MERCHANT-ID X(15)
        Unstring(parts, ref i, 22, v => _rq.MerchantName = v);            // 14 PA-RQ-MERCHANT-NAME X(22)
        Unstring(parts, ref i, 13, v => _rq.MerchantCity = v);            // 15 PA-RQ-MERCHANT-CITY X(13)
        Unstring(parts, ref i, 2, v => _rq.MerchantState = v);            // 16 PA-RQ-MERCHANT-STATE X(2)
        Unstring(parts, ref i, 9, v => _rq.MerchantZip = v);              // 17 PA-RQ-MERCHANT-ZIP X(9)
        Unstring(parts, ref i, 15, v => _rq.TransactionId = v);           // 18 PA-RQ-TRANSACTION-ID X(15)

        // COMPUTE PA-RQ-TRANSACTION-AMT = FUNCTION NUMVAL(WS-TRANSACTION-AMT-AN). source: :376-377
        // PA-RQ-TRANSACTION-AMT is edited PIC +9(10).99 (10 int digits, 2 frac, signed). Store truncates.
        _rq.TransactionAmt = Decimals.Store(NumVal(_wsTransactionAmtAn), 10, 2, signed: true);

        // MOVE PA-RQ-TRANSACTION-AMT TO WS-TRANSACTION-AMT (S9(10)V99 working copy). source: :379
        _wsTransactionAmt = Decimals.Store(_rq.TransactionAmt, 10, 2, signed: true);
    }

    // =================================================================================================
    //  3100-READ-REQUEST-MQ — source: COPAUA0C.cbl:386-435
    // =================================================================================================
    private void ReadRequestMq3100()
    {
        // MQGMO-OPTIONS = NO-SYNCPOINT + WAIT + CONVERT + FAIL-IF-QUIESCING; WAITINTERVAL = 5000.
        // MD MSGID=MQMI-NONE, CORRELID=MQCI-NONE, FORMAT=MQFMT-STRING; W01-BUFFLEN = 500.
        // CALL 'MQGET'. source: :389-409
        MqResult r = _mq.Get(_requestHandle!, out MqMessage? msg);
        _wsCompCode = r.CompletionCode;
        _wsReason = r.ReasonCode;

        if (_wsCompCode == MqConstants.MqccOk && msg is not null)   // IF WS-COMPCODE = MQCC-OK. source: :410
        {
            // GET buffer is X(500): the COBOL truncates a longer payload to 500 bytes; DATALEN is the
            // returned length (capped at the 500-byte buffer). source: :398,406
            _w01GetBuffer = msg.Body.Length > 500 ? msg.Body.Substring(0, 500) : msg.Body;
            _w01DataLen = Math.Min(msg.Body.Length, 500);

            _wsSaveCorrelid = msg.CorrelId;                          // MOVE MQMD-CORRELID -> WS-SAVE-CORRELID. :411
            _wsReplyQname = msg.ReplyToQueue;                        // MOVE MQMD-REPLYTOQ -> WS-REPLY-QNAME. :413
        }
        else
        {
            if (_wsReason == MqConstants.MqrcNoMsgAvailable)         // IF WS-REASON = MQRC-NO-MSG-AVAILABLE. :416
            {
                SetNoMoreMsgAvailable();                            // SET NO-MORE-MSG-AVAILABLE TO TRUE
            }
            else
            {
                _errLocation = "M003";                             // source: :419-429
                _errLevel = 'C'; _errSubsystem = 'C';              // FB-4: subsystem set CICS, not MQ
                _errCode1 = CodeDisplay(_wsCompCode);
                _errCode2 = CodeDisplay(_wsReason);
                _errMessage = "FAILED TO READ REQUEST MQ";
                _errEventKey = _paDetail.CardNum;                  // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
                LogError9500();
            }
        }
    }

    // =================================================================================================
    //  5000-PROCESS-AUTH — source: COPAUA0C.cbl:438-469
    // =================================================================================================
    private void ProcessAuth5000()
    {
        SetApproveAuth();                                          // SET APPROVE-AUTH TO TRUE. source: :441

        SchedulePsb1200();                                        // PERFORM 1200-SCHEDULE-PSB. source: :443

        SetCardFoundXref();                                       // SET CARD-FOUND-XREF (optimistic). :445
        SetFoundAcctInMstr();                                     // SET FOUND-ACCT-IN-MSTR (optimistic). :446

        ReadXrefRecord5100();                                    // PERFORM 5100-READ-XREF-RECORD. source: :448

        if (IsCardFoundXref())                                    // IF CARD-FOUND-XREF. source: :450
        {
            ReadAcctRecord5200();                               // PERFORM 5200-READ-ACCT-RECORD. source: :451
            ReadCustRecord5300();                               // PERFORM 5300-READ-CUST-RECORD. source: :452
            ReadAuthSummry5500();                               // PERFORM 5500-READ-AUTH-SUMMRY. source: :454
            ReadProfileData5600();                              // PERFORM 5600-READ-PROFILE-DATA. source: :456
        }

        MakeDecision6000();                                     // PERFORM 6000-MAKE-DECISION. source: :459
        SendResponse7100();                                    // PERFORM 7100-SEND-RESPONSE. source: :461

        if (IsCardFoundXref())                                  // IF CARD-FOUND-XREF. source: :463
        {
            WriteAuthToDb8000();                               // PERFORM 8000-WRITE-AUTH-TO-DB. source: :464
        }
    }

    // =================================================================================================
    //  5100-READ-XREF-RECORD — source: COPAUA0C.cbl:472-517
    // =================================================================================================
    private void ReadXrefRecord5100()
    {
        // MOVE PA-RQ-CARD-NUM TO XREF-CARD-NUM; EXEC CICS READ DATASET(CCXREF) RIDFLD(XREF-CARD-NUM).
        // source: :475-485
        string xrefCardNum = _rq.CardNum;
        string resp = _xref.ReadByKey(xrefCardNum, out _cardXref);

        switch (resp)                                              // EVALUATE WS-RESP-CD. source: :487
        {
            case FileStatus.Ok:                                   // WHEN DFHRESP(NORMAL). source: :488
                SetCardFoundXref();
                break;

            case FileStatus.RecordNotFound:                       // WHEN DFHRESP(NOTFND). source: :490
                SetCardNfoundXref();
                SetNfoundAcctInMstr();                            // SET NFOUND-ACCT-IN-MSTR TO TRUE. :492
                _errLocation = "A001";                            // source: :494-500
                _errLevel = 'W'; _errSubsystem = 'A';
                _errMessage = "CARD NOT FOUND IN XREF";
                _errEventKey = xrefCardNum;
                LogError9500();
                break;

            default:                                              // WHEN OTHER. source: :501
                _errLocation = "C001";                            // source: :502-512
                _errLevel = 'C'; _errSubsystem = 'C';
                _errCode1 = CodeDisplay(RespToCode(resp));
                _errCode2 = CodeDisplay(0);
                _errMessage = "FAILED TO READ XREF FILE";
                _errEventKey = xrefCardNum;
                LogError9500();
                break;
        }
    }

    // =================================================================================================
    //  5200-READ-ACCT-RECORD — source: COPAUA0C.cbl:520-565
    // =================================================================================================
    private void ReadAcctRecord5200()
    {
        // MOVE XREF-ACCT-ID TO WS-CARD-RID-ACCT-ID (numeric redefine; X-alias is the RIDFLD).
        // EXEC CICS READ DATASET(ACCTDAT). source: :523-533
        long acctId = _cardXref?.AcctId ?? 0L;
        string resp = _accounts.ReadByKey(acctId, out _account);

        switch (resp)                                              // EVALUATE WS-RESP-CD. source: :535
        {
            case FileStatus.Ok:                                   // WHEN DFHRESP(NORMAL). source: :536
                SetFoundAcctInMstr();
                break;

            case FileStatus.RecordNotFound:                       // WHEN DFHRESP(NOTFND). source: :538
                SetNfoundAcctInMstr();
                _errLocation = "A002";                            // source: :541-547
                _errLevel = 'W'; _errSubsystem = 'A';
                _errMessage = "ACCT NOT FOUND IN XREF";          // FB-5: text says XREF; it is ACCT
                _errEventKey = AcctIdKey(acctId);
                LogError9500();
                break;

            default:                                              // WHEN OTHER. source: :549
                _errLocation = "C002";                            // source: :550-560
                _errLevel = 'C'; _errSubsystem = 'C';
                _errCode1 = CodeDisplay(RespToCode(resp));
                _errCode2 = CodeDisplay(0);
                _errMessage = "FAILED TO READ ACCT FILE";
                _errEventKey = AcctIdKey(acctId);
                LogError9500();
                break;
        }
    }

    // =================================================================================================
    //  5300-READ-CUST-RECORD — source: COPAUA0C.cbl:568-613  (FB-2: result never used by the decision)
    // =================================================================================================
    private void ReadCustRecord5300()
    {
        // MOVE XREF-CUST-ID TO WS-CARD-RID-CUST-ID; EXEC CICS READ DATASET(CUSTDAT). source: :571-581
        long custId = _cardXref?.CustId ?? 0L;
        string resp = _customers.ReadByKey(custId, out _);        // record discarded by the logic (FB-2)

        switch (resp)                                              // EVALUATE WS-RESP-CD. source: :583
        {
            case FileStatus.Ok:                                   // WHEN DFHRESP(NORMAL). source: :584
                SetFoundCustInMstr();
                break;

            case FileStatus.RecordNotFound:                       // WHEN DFHRESP(NOTFND). source: :586
                SetNfoundCustInMstr();
                _errLocation = "A003";                            // source: :589-595
                _errLevel = 'W'; _errSubsystem = 'A';
                _errMessage = "CUST NOT FOUND IN XREF";          // FB-5: text says XREF; it is CUST
                _errEventKey = CustIdKey(custId);
                LogError9500();
                break;

            default:                                              // WHEN OTHER. source: :597
                _errLocation = "C003";                            // source: :598-608
                _errLevel = 'C'; _errSubsystem = 'C';
                _errCode1 = CodeDisplay(RespToCode(resp));
                _errCode2 = CodeDisplay(0);
                _errMessage = "FAILED TO READ CUST FILE";
                _errEventKey = CustIdKey(custId);
                LogError9500();
                break;
        }
    }

    // =================================================================================================
    //  5500-READ-AUTH-SUMMRY — source: COPAUA0C.cbl:616-644
    // =================================================================================================
    private void ReadAuthSummry5500()
    {
        // MOVE XREF-ACCT-ID TO PA-ACCT-ID; EXEC DLI GU SEGMENT(PAUTSUM0) WHERE(ACCNTID = PA-ACCT-ID).
        // MOVE DIBSTAT TO IMS-RETURN-CODE. source: :619-626
        long acctId = _cardXref?.AcctId ?? 0L;
        _paSummary.AcctId = acctId;
        string status = _summary.ReadByKey(acctId, out PautSummary? found);
        string imsReturnCode = SqlToIms(status);                 // '00' -> '  '; '23' -> 'GE'

        // EVALUATE TRUE. source: :627
        if (IsStatusOk(imsReturnCode))                           // WHEN STATUS-OK. source: :628
        {
            _paSummary = found!;                                 // INTO(PENDING-AUTH-SUMMARY)
            SetFoundPautSmrySeg();
        }
        else if (IsSegmentNotFound(imsReturnCode))               // WHEN SEGMENT-NOT-FOUND. source: :630
        {
            SetNfoundPautSmrySeg();
        }
        else                                                     // WHEN OTHER. source: :632
        {
            _errLocation = "I002";                                // source: :633-639
            _errLevel = 'C'; _errSubsystem = 'I';
            _errCode1 = imsReturnCode;
            _errMessage = "IMS GET SUMMARY FAILED";
            _errEventKey = _paDetail.CardNum;                    // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
            LogError9500();
        }
    }

    // =================================================================================================
    //  5600-READ-PROFILE-DATA — source: COPAUA0C.cbl:647-654  (CONTINUE — stub; fraud profile not impl.)
    // =================================================================================================
    private void ReadProfileData5600()
    {
        // CONTINUE. source: :650
    }

    // =================================================================================================
    //  6000-MAKE-DECISION — source: COPAUA0C.cbl:657-735
    // =================================================================================================
    private void MakeDecision6000()
    {
        // Echo into reply. source: :660-662
        _rl.CardNum = _rq.CardNum;                               // PA-RQ-CARD-NUM -> PA-RL-CARD-NUM
        _rl.TransactionId = _rq.TransactionId;                  // PA-RQ-TRANSACTION-ID -> PA-RL-TRANSACTION-ID
        _rl.AuthIdCode = _rq.AuthTime;                          // PA-RQ-AUTH-TIME -> PA-RL-AUTH-ID-CODE

        // Decline if above available limit; use IMS summary if present, else account master. source: :665-683
        if (IsFoundPautSmrySeg())                                // IF FOUND-PAUT-SMRY-SEG. source: :665
        {
            // COMPUTE WS-AVAILABLE-AMT = PA-CREDIT-LIMIT - PA-CREDIT-BALANCE (S9(9)V99 receiver). :666-667
            _wsAvailableAmt = Decimals.Store(
                _paSummary.CreditLimit - _paSummary.CreditBalance, 9, 2, signed: true);
            if (_wsTransactionAmt > _wsAvailableAmt)             // IF WS-TRANSACTION-AMT > WS-AVAILABLE-AMT. :668
            {
                SetDeclineAuth();
                SetInsufficientFund();
            }
        }
        else
        {
            if (IsFoundAcctInMstr())                            // IF FOUND-ACCT-IN-MSTR. source: :673
            {
                // COMPUTE WS-AVAILABLE-AMT = ACCT-CREDIT-LIMIT - ACCT-CURR-BAL. source: :674-675
                decimal acctCreditLimit = _account?.CreditLimit ?? 0m;
                decimal acctCurrBal = _account?.CurrBal ?? 0m;
                _wsAvailableAmt = Decimals.Store(acctCreditLimit - acctCurrBal, 9, 2, signed: true);
                if (_wsTransactionAmt > _wsAvailableAmt)         // source: :676
                {
                    SetDeclineAuth();
                    SetInsufficientFund();
                }
            }
            else
            {
                SetDeclineAuth();                               // SET DECLINE-AUTH (no reason flag). :681
            }
        }

        if (IsDeclineAuth())                                     // IF DECLINE-AUTH. source: :685
        {
            SetAuthRespDeclined();                              // SET AUTH-RESP-DECLINED TO TRUE
            _rl.AuthRespCode = "05";                            // MOVE '05' TO PA-RL-AUTH-RESP-CODE. :688
            _rl.ApprovedAmt = 0m;                               // MOVE 0 TO PA-RL-APPROVED-AMT
            _wsApprovedAmt = 0m;                               //         WS-APPROVED-AMT. :689-690
        }
        else
        {
            SetAuthRespApproved();                             // SET AUTH-RESP-APPROVED TO TRUE. :692
            _rl.AuthRespCode = "00";                            // MOVE '00' TO PA-RL-AUTH-RESP-CODE. :693
            _rl.ApprovedAmt = _rq.TransactionAmt;              // MOVE PA-RQ-TRANSACTION-AMT -> PA-RL-APPROVED-AMT
            _wsApprovedAmt = _rq.TransactionAmt;              //         WS-APPROVED-AMT. :694-695
        }

        _rl.AuthRespReason = "0000";                            // MOVE '0000' TO PA-RL-AUTH-RESP-REASON. :698
        if (IsAuthRespDeclined())                                // IF AUTH-RESP-DECLINED. source: :699
        {
            // EVALUATE TRUE. 4200/4300/5100/5200 are dead (FB-3). source: :700-717
            if (IsCardNfoundXref() || IsNfoundAcctInMstr() || IsNfoundCustInMstr())
                _rl.AuthRespReason = "3100";
            else if (IsInsufficientFund())
                _rl.AuthRespReason = "4100";
            else if (IsCardNotActive())
                _rl.AuthRespReason = "4200";
            else if (IsAccountClosed())
                _rl.AuthRespReason = "4300";
            else if (IsCardFraud())
                _rl.AuthRespReason = "5100";
            else if (IsMerchantFraud())
                _rl.AuthRespReason = "5200";
            else
                _rl.AuthRespReason = "9000";
        }

        // MOVE WS-APPROVED-AMT TO WS-APPROVED-AMT-DIS (edit into -zzzzzzzzz9.99). source: :720
        _wsApprovedAmtDis = EditedNumeric.Format(_wsApprovedAmt, "-ZZZZZZZZZ9.99");

        // STRING the 6-field CSV (each followed by ',', trailing comma included) WITH POINTER
        // WS-RESP-LENGTH. The pointer is NOT reset per message (FB-6). source: :722-731
        StringIntoReply();
    }

    // =================================================================================================
    //  7100-SEND-RESPONSE — source: COPAUA0C.cbl:738-783
    // =================================================================================================
    private void SendResponse7100()
    {
        // Reply OD: OBJECTTYPE=MQOT-Q, OBJECTNAME=WS-REPLY-QNAME.
        // Reply MD: MSGTYPE=MQMT-REPLY, CORRELID=WS-SAVE-CORRELID, MSGID=MQMI-NONE, REPLYTOQ/QMGR=SPACES,
        // PERSISTENCE=MQPER-NOT-PERSISTENT, EXPIRY=50, FORMAT=MQFMT-STRING.
        // PMO=MQPMO-NO-SYNCPOINT+MQPMO-DEFAULT-CONTEXT; W02-BUFFLEN = WS-RESP-LENGTH. CALL 'MQPUT1'.
        // source: :741-766
        var reply = new MqMessage
        {
            // W02-BUFFLEN = WS-RESP-LENGTH bytes of W02-PUT-BUFFER (the cumulative-pointer length, FB-6).
            Body = ReplyBufferToLength(_wsRespLength),
            MsgType = MqConstants.MqmtReply,
            CorrelId = _wsSaveCorrelid,
            MsgId = MqConstants.MqmiNone,
            ReplyToQueue = "",
            ReplyToQMgr = "",
            Persistence = MqConstants.MqperNotPersistent,
            Expiry = MqConstants.AuthReplyExpiry,            // 50 = 5.0 s
            Format = MqConstants.MqfmtString,
        };

        MqResult r = _mq.Put1(_wsReplyQname, reply);          // MQPUT1 by name (open-put-close). :758-766
        _wsCompCode = r.CompletionCode;
        _wsReason = r.ReasonCode;

        if (_wsCompCode != MqConstants.MqccOk)                // IF WS-COMPCODE NOT = MQCC-OK. source: :767
        {
            _errLocation = "M004";                            // source: :768-778
            _errLevel = 'C'; _errSubsystem = 'M';
            _errCode1 = CodeDisplay(_wsCompCode);
            _errCode2 = CodeDisplay(_wsReason);
            _errMessage = "FAILED TO PUT ON REPLY MQ";
            _errEventKey = _paDetail.CardNum;                 // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
            LogError9500();
        }
    }

    // =================================================================================================
    //  8000-WRITE-AUTH-TO-DB — source: COPAUA0C.cbl:786-795
    // =================================================================================================
    private void WriteAuthToDb8000()
    {
        UpdateSummary8400();                                  // PERFORM 8400-UPDATE-SUMMARY. source: :790
        InsertAuth8500();                                    // PERFORM 8500-INSERT-AUTH. source: :791
    }

    // =================================================================================================
    //  8400-UPDATE-SUMMARY — source: COPAUA0C.cbl:798-851
    // =================================================================================================
    private void UpdateSummary8400()
    {
        if (IsNfoundPautSmrySeg())                            // IF NFOUND-PAUT-SMRY-SEG. source: :801
        {
            // INITIALIZE PENDING-AUTH-SUMMARY REPLACING NUMERIC DATA BY ZERO (numerics 0, chars spaces).
            // Then overlay acct/cust id. source: :802-806
            _paSummary = NewInitializedSummary();
            _paSummary.AcctId = _cardXref?.AcctId ?? 0L;     // MOVE XREF-ACCT-ID TO PA-ACCT-ID
            _paSummary.CustId = _cardXref?.CustId ?? 0L;     // MOVE XREF-CUST-ID TO PA-CUST-ID
        }

        // Always copy the account limits — FB-9: unconditional, even on a failed account read (stale).
        // S9(9)V99 receivers truncate. source: :810-811
        _paSummary.CreditLimit = Decimals.Store(_account?.CreditLimit ?? 0m, 9, 2, signed: true);
        _paSummary.CashLimit = Decimals.Store(_account?.CashCreditLimit ?? 0m, 9, 2, signed: true);

        if (IsAuthRespApproved())                            // IF AUTH-RESP-APPROVED. source: :813
        {
            _paSummary.ApprovedAuthCnt = AddCount(_paSummary.ApprovedAuthCnt, 1);             // :814
            _paSummary.ApprovedAuthAmt = Add9v2(_paSummary.ApprovedAuthAmt, _wsApprovedAmt); // :815
            _paSummary.CreditBalance = Add9v2(_paSummary.CreditBalance, _wsApprovedAmt);      // :817
            _paSummary.CashBalance = 0m;                                                       // FB-8. :818
        }
        else
        {
            _paSummary.DeclinedAuthCnt = AddCount(_paSummary.DeclinedAuthCnt, 1);             // :820
            // FB-1: PA-TRANSACTION-AMT (the DETAIL field) is not populated until 8500, so this adds its
            // prior value (0 on a fresh summary, the previous message's amount on an existing one). :821
            _paSummary.DeclinedAuthAmt = Add9v2(_paSummary.DeclinedAuthAmt, _paDetail.TransactionAmt);
        }

        string status;
        if (IsFoundPautSmrySeg())                            // IF FOUND-PAUT-SMRY-SEG -> REPL. source: :824
        {
            status = _summary.Update(_paSummary);            // EXEC DLI REPL SEGMENT(PAUTSUM0). :825-828
        }
        else
        {
            status = _summary.Insert(_paSummary);            // EXEC DLI ISRT SEGMENT(PAUTSUM0). :830-833
        }
        string imsReturnCode = SqlToIms(status);             // MOVE DIBSTAT TO IMS-RETURN-CODE. :835

        if (IsStatusOk(imsReturnCode))                       // IF STATUS-OK -> CONTINUE. source: :837-838
        {
            // CONTINUE
        }
        else
        {
            _errLocation = "I003";                           // source: :840-846
            _errLevel = 'C'; _errSubsystem = 'I';
            _errCode1 = imsReturnCode;
            _errMessage = "IMS UPDATE SUMRY FAILED";
            _errEventKey = _paDetail.CardNum;
            LogError9500();
        }
    }

    // =================================================================================================
    //  8500-INSERT-AUTH — source: COPAUA0C.cbl:854-936
    // =================================================================================================
    private void InsertAuth8500()
    {
        // EXEC CICS ASKTIME / FORMATTIME YYDDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6) MILLISECONDS(...).
        // source: :857-866
        DateTime now = _clock.Now;
        _wsCurDateX6 = FormatTimeYyddd(now);                 // 5-digit YYDDD + a trailing char (X(6))
        _wsCurTimeX6 = now.ToString("HHmmss", CultureInfo.InvariantCulture);
        _wsCurTimeMs = now.Millisecond;                      // 0..999

        _wsYyddd = ToNum5(_wsCurDateX6.Substring(0, 5));     // MOVE WS-CUR-DATE-X6(1:5) TO WS-YYDDD. :868
        _wsCurTimeN6 = ToNum6(_wsCurTimeX6);                 // MOVE WS-CUR-TIME-X6 TO WS-CUR-TIME-N6. :869

        // COMPUTE WS-TIME-WITH-MS = (WS-CUR-TIME-N6 * 1000) + WS-CUR-TIME-MS. source: :871-872
        _wsTimeWithMs = (long)_wsCurTimeN6 * 1000 + _wsCurTimeMs;

        // 9s-complement descending keys — preserve verbatim (do NOT "fix"). source: :874-875
        _paDetail.AuthDate9c = (int)(99999 - _wsYyddd);      // COMPUTE PA-AUTH-DATE-9C = 99999 - WS-YYDDD
        _paDetail.AuthTime9c = 999999999 - _wsTimeWithMs;    // COMPUTE PA-AUTH-TIME-9C = 999999999 - ...

        // Copy all request fields into the detail segment. source: :877-895
        _paDetail.AuthOrigDate = _rq.AuthDate;
        _paDetail.AuthOrigTime = _rq.AuthTime;
        _paDetail.CardNum = _rq.CardNum;
        _paDetail.AuthType = _rq.AuthType;
        _paDetail.CardExpiryDate = _rq.CardExpiryDate;
        _paDetail.MessageType = _rq.MessageType;
        _paDetail.MessageSource = _rq.MessageSource;
        _paDetail.ProcessingCode = _rq.ProcessingCode;
        _paDetail.TransactionAmt = Decimals.Store(_rq.TransactionAmt, 10, 2, signed: true);  // PA-TRANSACTION-AMT
        _paDetail.MerchantCatagoryCode = _rq.MerchantCatagoryCode;
        _paDetail.AcqrCountryCode = _rq.AcqrCountryCode;
        _paDetail.PosEntryMode = _rq.PosEntryMode;
        _paDetail.MerchantId = _rq.MerchantId;
        _paDetail.MerchantName = _rq.MerchantName;
        _paDetail.MerchantCity = _rq.MerchantCity;
        _paDetail.MerchantState = _rq.MerchantState;
        _paDetail.MerchantZip = _rq.MerchantZip;
        _paDetail.TransactionId = _rq.TransactionId;

        // Copy reply fields. source: :897-900
        _paDetail.AuthIdCode = _rl.AuthIdCode;
        _paDetail.AuthRespCode = _rl.AuthRespCode;
        _paDetail.AuthRespReason = _rl.AuthRespReason;
        _paDetail.ApprovedAmt = Decimals.Store(_rl.ApprovedAmt, 10, 2, signed: true);  // PA-APPROVED-AMT

        if (IsAuthRespApproved())                            // IF AUTH-RESP-APPROVED. source: :902
            _paDetail.MatchStatus = "P";                    // SET PA-MATCH-PENDING TO TRUE
        else
            _paDetail.MatchStatus = "D";                    // SET PA-MATCH-AUTH-DECLINED TO TRUE

        _paDetail.AuthFraud = " ";                           // MOVE SPACE TO PA-AUTH-FRAUD (X(1)). source: :908
        _paDetail.FraudRptDate = "        ";                 // MOVE SPACE TO PA-FRAUD-RPT-DATE (X(8) spaces)

        _paDetail.AcctId = _cardXref?.AcctId ?? 0L;          // MOVE XREF-ACCT-ID TO PA-ACCT-ID. source: :911

        // The 8-byte PAUT9CTS child sequence key = date-9C (5 digits) + time-9C (9 digits), 9s-complement,
        // so ascending key order == newest-first (matches IX_PAUT_DETAIL_SEQ and the COPAUS0C/CBPAUP0C
        // scan order). Built as a fixed-width zero-padded decimal concatenation. source: :913-919; IMS_SCHEMA.md §2/§3.7
        _paDetail.AuthKey = BuildAuthKey(_paDetail.AuthDate9c, _paDetail.AuthTime9c);

        // EXEC DLI ISRT SEGMENT(PAUTSUM0) WHERE(ACCNTID=PA-ACCT-ID) SEGMENT(PAUTDTL1) FROM(...). The
        // WHERE only locates the parent (FK enforces it); relationally it is just an INSERT. source: :913-919
        string status = _detail.Insert(ClonePaDetail());
        string imsReturnCode = SqlToIms(status);             // MOVE DIBSTAT TO IMS-RETURN-CODE. :920

        if (IsStatusOk(imsReturnCode))                       // IF STATUS-OK -> CONTINUE. source: :922-923
        {
            // CONTINUE
        }
        else
        {
            _errLocation = "I004";                           // source: :925-931
            _errLevel = 'C'; _errSubsystem = 'I';
            _errCode1 = imsReturnCode;
            _errMessage = "IMS INSERT DETL FAILED";
            _errEventKey = _paDetail.CardNum;
            LogError9500();
        }
    }

    // =================================================================================================
    //  9000-TERMINATE — source: COPAUA0C.cbl:940-951
    // =================================================================================================
    private void Terminate9000()
    {
        // IF IMS-PSB-SCHD EXEC DLI TERM (normally a no-op — 2000 sets NOT-SCHD after each message).
        // source: :943-945  (the relational unit of work ends implicitly).
        CloseRequestQueue9100();                             // PERFORM 9100-CLOSE-REQUEST-QUEUE. source: :947
    }

    // =================================================================================================
    //  9100-CLOSE-REQUEST-QUEUE — source: COPAUA0C.cbl:953-980
    // =================================================================================================
    private void CloseRequestQueue9100()
    {
        if (IsRequestMqOpen() && _requestHandle is not null)  // IF WS-REQUEST-MQ-OPEN. source: :955
        {
            MqResult r = _mq.Close(_requestHandle);          // CALL 'MQCLOSE' ... MQCO-NONE. :956-961
            _wsCompCode = r.CompletionCode;
            _wsReason = r.ReasonCode;

            if (_wsCompCode == MqConstants.MqccOk)           // IF WS-COMPCODE = MQCC-OK. source: :963
            {
                SetRequestMqClse();
            }
            else
            {
                _errLocation = "M005";                       // source: :966-975
                _errLevel = 'W'; _errSubsystem = 'M';
                _errCode1 = CodeDisplay(_wsCompCode);
                _errCode2 = CodeDisplay(_wsReason);
                _errMessage = "FAILED TO CLOSE REQUEST MQ";
                LogError9500();
            }
        }
    }

    // =================================================================================================
    //  9500-LOG-ERROR — source: COPAUA0C.cbl:983-1013
    // =================================================================================================
    private void LogError9500()
    {
        // EXEC CICS ASKTIME / FORMATTIME YYMMDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6). source: :986-994
        DateTime now = _clock.Now;
        string errDate = now.ToString("yyMMdd", CultureInfo.InvariantCulture);
        string errTime = now.ToString("HHmmss", CultureInfo.InvariantCulture);

        // Build the 119-byte ERROR-LOG-RECORD (fixed offsets) and WRITEQ TD QUEUE('CSSL'). source: :996-1006
        string record = BuildErrorLogRecord(errDate, errTime);
        _errorLog.Write(record);                             // NOHANDLE — failures ignored

        if (_errLevel == 'C')                                // IF ERR-CRITICAL. source: :1008
        {
            EndRoutine9990();                                // PERFORM 9990-END-ROUTINE
        }
    }

    // =================================================================================================
    //  9990-END-ROUTINE — source: COPAUA0C.cbl:1016-1025
    // =================================================================================================
    private void EndRoutine9990()
    {
        Terminate9000();                                     // PERFORM 9000-TERMINATE. source: :1019
        throw new EndRoutineSignal();                        // EXEC CICS RETURN (hard exit). source: :1021
    }

    // =================================================================================================
    //  STRING / reply-buffer emulation (W02-PUT-BUFFER X(200) + non-reset WS-RESP-LENGTH pointer, FB-6)
    // =================================================================================================
    private readonly char[] _w02PutBuffer = InitBuffer();    // X(200) — spaces

    private static char[] InitBuffer()
    {
        var b = new char[200];
        Array.Fill(b, ' ');
        return b;
    }

    /// <summary>
    /// Emulates <c>STRING ... DELIMITED BY SIZE INTO W02-PUT-BUFFER WITH POINTER WS-RESP-LENGTH</c> for the
    /// 6-field reply (each followed by a literal ',', trailing comma after the amount). Writes into the
    /// 200-byte buffer starting at the 1-based pointer; on overflow the COBOL STRING stops silently. The
    /// pointer is advanced and persisted (NOT reset per message — FB-6). source: :722-731
    /// </summary>
    private void StringIntoReply()
    {
        int pos = _wsRespLength;                             // WITH POINTER WS-RESP-LENGTH (1-based)
        AppendString(ref pos, Fixed(_rl.CardNum, 16));      // PA-RL-CARD-NUM X(16)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_rl.TransactionId, 15)); // PA-RL-TRANSACTION-ID X(15)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_rl.AuthIdCode, 6));    // PA-RL-AUTH-ID-CODE X(6)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_rl.AuthRespCode, 2));  // PA-RL-AUTH-RESP-CODE X(2)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_rl.AuthRespReason, 4)); // PA-RL-AUTH-RESP-REASON X(4)
        AppendChar(ref pos, ',');
        AppendString(ref pos, _wsApprovedAmtDis);           // WS-APPROVED-AMT-DIS -zzzzzzzzz9.99 (14 chars)
        AppendChar(ref pos, ',');                            // trailing comma
        _wsRespLength = (short)pos;                          // pointer ends at built-length+1 (persisted)
    }

    private void AppendString(ref int pos, string s)
    {
        foreach (char c in s) AppendChar(ref pos, c);
    }

    private void AppendChar(ref int pos, char c)
    {
        // COBOL STRING with no ON OVERFLOW: when the pointer exceeds the receiver, the move stops silently
        // and the pointer is NOT advanced further. source: :722-731 (overflow = silent stop).
        if (pos >= 1 && pos <= _w02PutBuffer.Length)
        {
            _w02PutBuffer[pos - 1] = c;
            pos++;
        }
        else
        {
            // overflow: stop writing; the COBOL pointer is left at its overflow value (do not advance).
        }
    }

    /// <summary>Returns the first <paramref name="length"/> bytes of W02-PUT-BUFFER (the MQPUT1 payload).</summary>
    private string ReplyBufferToLength(short length)
    {
        int n = Math.Clamp(length, 0, _w02PutBuffer.Length);
        return new string(_w02PutBuffer, 0, n);
    }

    // =================================================================================================
    //  Small helpers — flag setters/getters, numeric conversion, IMS/SQL status, key builders.
    // =================================================================================================

    // ---- WS-OPTIONS / open option for MQOO-INPUT-SHARED (metadata only). ----
    private const int MqooInputShared = 0x00000002;

    // ---- IMS status sentinels. ----
    private const string ImsOk = "  ";        // spaces
    private const string ImsGe = "GE";        // segment not found

    private static bool IsStatusOk(string s) => s == "  " || s == "FW";       // 88 STATUS-OK
    private static bool IsSegmentNotFound(string s) => s == "GE";             // 88 SEGMENT-NOT-FOUND

    /// <summary>Maps a repository two-char FileStatus to the DL/I status the COBOL EVALUATEs.</summary>
    private static string SqlToIms(string fileStatus) => fileStatus switch
    {
        FileStatus.Ok => ImsOk,               // '00' -> spaces
        FileStatus.RecordNotFound => ImsGe,   // '23' -> GE
        FileStatus.DuplicateKeyError => "II", // '22' -> II (duplicate insert)
        _ => "AO",                            // any other -> a non-OK, non-GE status (critical)
    };

    /// <summary>Maps a two-char FileStatus to a CICS RESP-like numeric for the ERR-CODE-1 display.</summary>
    private static int RespToCode(string fileStatus) => fileStatus switch
    {
        FileStatus.Ok => (int)Resp.Normal,
        FileStatus.RecordNotFound => (int)Resp.NotFnd,
        FileStatus.EndOfFile => (int)Resp.EndFile,
        FileStatus.DuplicateKey or FileStatus.DuplicateKeyError => (int)Resp.DupRec,
        _ => (int)Resp.Error,
    };

    // ---- Decision/state flag setters (88-level SET TO TRUE). ----
    private void SetApproveAuth() => _declineFlg = 'A';
    private void SetDeclineAuth() => _declineFlg = 'D';
    private bool IsDeclineAuth() => _declineFlg == 'D';

    private void SetAuthRespApproved() => _authRespFlg = 'A';
    private void SetAuthRespDeclined() => _authRespFlg = 'D';
    private bool IsAuthRespApproved() => _authRespFlg == 'A';
    private bool IsAuthRespDeclined() => _authRespFlg == 'D';

    private void SetInsufficientFund() => _declineReasonFlg = 'I';
    private bool IsInsufficientFund() => _declineReasonFlg == 'I';
    // Dead reason flags: CARD-NOT-ACTIVE/ACCOUNT-CLOSED/CARD-FRAUD/MERCHANT-FRAUD are declared but never
    // SET anywhere in COPAUA0C (FB-3), so their EVALUATE arms can never fire. source: :140-145,707-714.
    private static bool IsCardNotActive() => false;
    private static bool IsAccountClosed() => false;
    private static bool IsCardFraud() => false;
    private static bool IsMerchantFraud() => false;

    private void SetCardFoundXref() => _xrefReadFlg = 'Y';
    private void SetCardNfoundXref() => _xrefReadFlg = 'N';
    private bool IsCardFoundXref() => _xrefReadFlg == 'Y';
    private bool IsCardNfoundXref() => _xrefReadFlg == 'N';

    private void SetFoundAcctInMstr() => _acctMasterReadFlg = 'Y';
    private void SetNfoundAcctInMstr() => _acctMasterReadFlg = 'N';
    private bool IsFoundAcctInMstr() => _acctMasterReadFlg == 'Y';
    private bool IsNfoundAcctInMstr() => _acctMasterReadFlg == 'N';

    private void SetFoundCustInMstr() => _custMasterReadFlg = 'Y';
    private void SetNfoundCustInMstr() => _custMasterReadFlg = 'N';
    private bool IsNfoundCustInMstr() => _custMasterReadFlg == 'N';

    private void SetFoundPautSmrySeg() => _pautSmrySegFlg = 'Y';
    private void SetNfoundPautSmrySeg() => _pautSmrySegFlg = 'N';
    private bool IsFoundPautSmrySeg() => _pautSmrySegFlg == 'Y';
    private bool IsNfoundPautSmrySeg() => _pautSmrySegFlg == 'N';

    private void SetNoMoreMsgAvailable() => _msgAvailableFlg = 'N';
    private bool IsNoMoreMsgAvailable() => _msgAvailableFlg == 'N';

    private void SetLoopEnd() => _msgLoopFlg = 'E';
    private bool IsLoopEnd() => _msgLoopFlg == 'E';

    private void SetRequestMqOpen() => _requestMqFlg = 'O';
    private void SetRequestMqClse() => _requestMqFlg = 'C';
    private bool IsRequestMqOpen() => _requestMqFlg == 'O';

    private void SetImsPsbSchd() => _imsPsbSchdFlg = 'Y';
    private void SetImsPsbNotSchd() => _imsPsbSchdFlg = 'N';

    // ---- Numeric conversions (display->numeric with COBOL de-edit semantics). ----

    /// <summary>FUNCTION NUMVAL — parse a signed/spaced plain numeric; non-numeric → 0. source: :376-377</summary>
    private static decimal NumVal(string? v)
    {
        string t = (v ?? "").Trim();
        if (t.Length == 0) return 0m;
        // NUMVAL accepts a leading/trailing sign, spaces and one decimal point; strip anything else.
        bool neg = t.Contains('-');
        var sb = new StringBuilder();
        bool dot = false;
        foreach (char c in t)
        {
            if (c is >= '0' and <= '9') sb.Append(c);
            else if (c == '.' && !dot) { dot = true; sb.Append('.'); }
        }
        if (sb.Length == 0 || (sb.Length == 1 && sb[0] == '.')) return 0m;
        if (!decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n))
            return 0m;
        return neg ? -n : n;
    }

    /// <summary>De-edits a display string into an N-digit unsigned integer (COBOL MOVE alpha→9(N)).</summary>
    private static int ToNumDigits(string? v, int width)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        var sb = new StringBuilder();
        foreach (char c in v) if (c is >= '0' and <= '9') sb.Append(c);
        if (sb.Length == 0) return 0;
        // Keep the low-order `width` digits (zoned numeric receiver truncates high-order on overflow).
        string digits = sb.ToString();
        if (digits.Length > width) digits = digits.Substring(digits.Length - width);
        return int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static int ToNum2(string? v) => ToNumDigits(v, 2);
    private static int ToNum5(string? v) => ToNumDigits(v, 5);
    private static int ToNum6(string? v) => ToNumDigits(v, 6);

    // ---- COMP / COMP-3 arithmetic with the field's truncation/overflow. ----

    /// <summary>ADD into an S9(9)V99 COMP-3 receiver (truncate toward zero, silent overflow).</summary>
    private static decimal Add9v2(decimal acc, decimal add) => Decimals.Store(acc + add, 9, 2, signed: true);

    /// <summary>ADD 1 into an S9(4) COMP counter (wraps at 9999, signed binary halfword).</summary>
    private static int AddCount(int acc, int add)
    {
        int n = acc + add;
        // S9(4) COMP holds -9999..9999 by digit capacity; high-order overflow drops modulo 10^4.
        int mag = Math.Abs(n) % 10000;
        return n < 0 ? -mag : mag;
    }

    // ---- Key / display builders. ----

    /// <summary>WS-CODE-DISPLAY 9(9): a binary code rendered as 9 zoned digits (leading zeros). source: :276-279</summary>
    private static string CodeDisplay(int code)
    {
        int mag = Math.Abs(code) % 1000000000;  // 9 digits
        return mag.ToString("D9", CultureInfo.InvariantCulture);
    }

    /// <summary>The X(11) RIDFLD form of an account id (zoned 11-digit), used as the error event key.</summary>
    private static string AcctIdKey(long acctId) => Pic(acctId, 11);

    /// <summary>The 9(9) display form of a customer id, used as the error event key.</summary>
    private static string CustIdKey(long custId) => Pic(custId, 9);

    private static string Pic(long value, int width)
    {
        long mag = Math.Abs(value) % (long)Decimals.Pow10(width);
        return mag.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    /// <summary>
    /// Builds the 8-byte PAUT9CTS child sequence key as a fixed-width zero-padded decimal concatenation of
    /// the two 9s-complement components (date-9C = 5 digits, time-9C = 9 digits = 14 chars). Because both
    /// are non-negative fixed-width values, ascending lexicographic order of this string equals ascending
    /// (AUTH_DATE_9C, AUTH_TIME_9C) — exactly the IX_PAUT_DETAIL_SEQ / IMS twin-chain newest-first order.
    /// source: :913-919; IMS_SCHEMA.md §2/§3.7.
    /// </summary>
    private static string BuildAuthKey(int authDate9c, long authTime9c)
    {
        int d = (int)(Math.Abs((long)authDate9c) % 100000L);          // 5 digits
        long t = Math.Abs(authTime9c) % 1000000000L;                  // 9 digits
        return d.ToString("D5", CultureInfo.InvariantCulture)
             + t.ToString("D9", CultureInfo.InvariantCulture);
    }

    /// <summary>Right-pads/truncates a value to a fixed COBOL X(width) field (spaces on the right).</summary>
    private static string Fixed(string? v, int width)
    {
        v ??= "";
        return v.Length >= width ? v.Substring(0, width) : v.PadRight(width, ' ');
    }

    /// <summary>
    /// UNSTRING receiver: takes the next CSV part (or leaves the field unchanged when fewer commas were
    /// present — faithful), right-truncates it to the receiver width (COBOL UNSTRING truncates an
    /// over-long value to the receiver size), and applies it. source: :354-374
    /// </summary>
    private static void Unstring(string[] parts, ref int index, int width, Action<string> set)
    {
        if (index >= parts.Length)
        {
            // Fewer delimiters than receivers: the trailing receivers are UNCHANGED (do not call set).
            index++;
            return;
        }
        string value = parts[index];
        if (value.Length > width) value = value.Substring(0, width);
        set(value);
        index++;
    }

    // ---- INITIALIZE / clone helpers for the IMS summary/detail io-areas. ----

    /// <summary>
    /// INITIALIZE PENDING-AUTH-SUMMARY REPLACING NUMERIC DATA BY ZERO: all numeric subfields 0, all
    /// alphanumeric fields SPACES. source: :802-803
    /// </summary>
    private static PautSummary NewInitializedSummary() => new()
    {
        AcctId = 0,
        CustId = 0,
        AuthStatus = " ",
        AccountStatus1 = "  ",
        AccountStatus2 = "  ",
        AccountStatus3 = "  ",
        AccountStatus4 = "  ",
        AccountStatus5 = "  ",
        CreditLimit = 0m,
        CashLimit = 0m,
        CreditBalance = 0m,
        CashBalance = 0m,
        ApprovedAuthCnt = 0,
        DeclinedAuthCnt = 0,
        ApprovedAuthAmt = 0m,
        DeclinedAuthAmt = 0m,
    };

    /// <summary>Snapshots the working PA-detail io-area into a row for the INSERT (the COBOL FROM area).</summary>
    private PautDetail ClonePaDetail() => new()
    {
        AcctId = _paDetail.AcctId,
        AuthKey = _paDetail.AuthKey,
        AuthDate9c = _paDetail.AuthDate9c,
        AuthTime9c = _paDetail.AuthTime9c,
        AuthOrigDate = _paDetail.AuthOrigDate,
        AuthOrigTime = _paDetail.AuthOrigTime,
        CardNum = _paDetail.CardNum,
        AuthType = _paDetail.AuthType,
        CardExpiryDate = _paDetail.CardExpiryDate,
        MessageType = _paDetail.MessageType,
        MessageSource = _paDetail.MessageSource,
        AuthIdCode = _paDetail.AuthIdCode,
        AuthRespCode = _paDetail.AuthRespCode,
        AuthRespReason = _paDetail.AuthRespReason,
        ProcessingCode = _paDetail.ProcessingCode,
        TransactionAmt = _paDetail.TransactionAmt,
        ApprovedAmt = _paDetail.ApprovedAmt,
        MerchantCatagoryCode = _paDetail.MerchantCatagoryCode,
        AcqrCountryCode = _paDetail.AcqrCountryCode,
        PosEntryMode = _paDetail.PosEntryMode,
        MerchantId = _paDetail.MerchantId,
        MerchantName = _paDetail.MerchantName,
        MerchantCity = _paDetail.MerchantCity,
        MerchantState = _paDetail.MerchantState,
        MerchantZip = _paDetail.MerchantZip,
        TransactionId = _paDetail.TransactionId,
        MatchStatus = _paDetail.MatchStatus,
        AuthFraud = _paDetail.AuthFraud,
        FraudRptDate = _paDetail.FraudRptDate,
    };

    // ---- CICS FORMATTIME emulation. ----

    /// <summary>
    /// CICS FORMATTIME YYDDD into an X(6) field — the 5-char "YYDDD" (2-digit year + 3-digit day-of-year)
    /// plus a trailing position; the COBOL only consumes the first 5 chars (WS-CUR-DATE-X6(1:5)). source: :862-868
    /// </summary>
    private static string FormatTimeYyddd(DateTime now)
    {
        int yy = now.Year % 100;
        int ddd = now.DayOfYear;
        return yy.ToString("D2", CultureInfo.InvariantCulture)
             + ddd.ToString("D3", CultureInfo.InvariantCulture)
             + " ";
    }

    // ---- ERROR-LOG-RECORD layout (122 bytes, CCPAUERY). ----

    /// <summary>Builds the fixed-offset ERROR-LOG-RECORD written to the CSSL TD queue. source: CCPAUERY; :996-1006</summary>
    private string BuildErrorLogRecord(string errDate, string errTime)
    {
        var sb = new StringBuilder(119);
        sb.Append(Fixed(errDate, 6));          // ERR-DATE X(6)
        sb.Append(Fixed(errTime, 6));          // ERR-TIME X(6)
        sb.Append(Fixed(WsCicsTranid, 8));     // ERR-APPLICATION X(8)  <- WS-CICS-TRANID
        sb.Append(Fixed(WsPgmAuth, 8));        // ERR-PROGRAM X(8)      <- WS-PGM-AUTH
        sb.Append(Fixed(_errLocation, 4));     // ERR-LOCATION X(4)
        sb.Append(_errLevel == '\0' ? ' ' : _errLevel);     // ERR-LEVEL X(1)
        sb.Append(_errSubsystem == '\0' ? ' ' : _errSubsystem); // ERR-SUBSYSTEM X(1)
        sb.Append(Fixed(_errCode1, 9));        // ERR-CODE-1 X(9)
        sb.Append(Fixed(_errCode2, 9));        // ERR-CODE-2 X(9)
        sb.Append(Fixed(_errMessage, 50));     // ERR-MESSAGE X(50)
        sb.Append(Fixed(_errEventKey, 20));    // ERR-EVENT-KEY X(20)
        return sb.ToString();
    }

    // =================================================================================================
    //  PENDING-AUTH-REQUEST / PENDING-AUTH-RESPONSE staging (CCPAURQY / CCPAURLY).
    // =================================================================================================

    /// <summary>PENDING-AUTH-REQUEST (copybook CCPAURQY) — the parsed CSV request. source: CCPAURQY.cpy.</summary>
    private sealed class PendingAuthRequest
    {
        public string AuthDate = "";            // PA-RQ-AUTH-DATE X(6)
        public string AuthTime = "";            // PA-RQ-AUTH-TIME X(6)
        public string CardNum = "";             // PA-RQ-CARD-NUM X(16)
        public string AuthType = "";            // PA-RQ-AUTH-TYPE X(4)
        public string CardExpiryDate = "";      // PA-RQ-CARD-EXPIRY-DATE X(4)
        public string MessageType = "";         // PA-RQ-MESSAGE-TYPE X(6)
        public string MessageSource = "";       // PA-RQ-MESSAGE-SOURCE X(6)
        public int ProcessingCode;              // PA-RQ-PROCESSING-CODE 9(6)
        public decimal TransactionAmt;          // PA-RQ-TRANSACTION-AMT +9(10).99
        public string MerchantCatagoryCode = ""; // PA-RQ-MERCHANT-CATAGORY-CODE X(4)
        public string AcqrCountryCode = "";     // PA-RQ-ACQR-COUNTRY-CODE X(3)
        public int PosEntryMode;                // PA-RQ-POS-ENTRY-MODE 9(2)
        public string MerchantId = "";          // PA-RQ-MERCHANT-ID X(15)
        public string MerchantName = "";        // PA-RQ-MERCHANT-NAME X(22)
        public string MerchantCity = "";        // PA-RQ-MERCHANT-CITY X(13)
        public string MerchantState = "";       // PA-RQ-MERCHANT-STATE X(2)
        public string MerchantZip = "";         // PA-RQ-MERCHANT-ZIP X(9)
        public string TransactionId = "";       // PA-RQ-TRANSACTION-ID X(15)
    }

    /// <summary>PENDING-AUTH-RESPONSE (copybook CCPAURLY) — the reply fields. source: CCPAURLY.cpy.</summary>
    private sealed class PendingAuthResponse
    {
        public string CardNum = "";             // PA-RL-CARD-NUM X(16)
        public string TransactionId = "";       // PA-RL-TRANSACTION-ID X(15)
        public string AuthIdCode = "";          // PA-RL-AUTH-ID-CODE X(6)
        public string AuthRespCode = "";        // PA-RL-AUTH-RESP-CODE X(2)
        public string AuthRespReason = "";      // PA-RL-AUTH-RESP-REASON X(4)
        public decimal ApprovedAmt;             // PA-RL-APPROVED-AMT +9(10).99
    }
}
