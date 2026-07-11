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
public sealed class AuthorizationDecisionService : IMqServer
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
    private const string ProgramId = "COPAUA0C";                                       // :33  WS-PGM-AUTH PIC X(8)
    private const string TranId = "CP00";                                              // :34  WS-CICS-TRANID PIC X(4)
    private const string CardXrefFileName = "CCXREF  ";                                // :39  WS-CCXREF-FILE PIC X(8)  (logical, info only)
    private const short MessageProcessLimit = 500;                                     // :40  WS-REQSTS-PROCESS-LIMIT PIC S9(4) COMP

    private short _messagesProcessed;                                                 // :42 WS-MSG-PROCESSED S9(4) COMP
    private string _requestQueueName = "";                                            // :43 WS-REQUEST-QNAME X(48)
    private string _replyQueueName = "";                                              // :44 WS-REPLY-QNAME X(48)
    private byte[] _savedCorrelId = MqConstants.MqciNone;                             // :45 WS-SAVE-CORRELID X(24)

    // WS-RESP-LENGTH PIC S9(4) VALUE 1 — the STRING pointer. NOT reset per message (FB-6). source: :46
    private short _replyLength = 1;                                                    // WS-RESP-LENGTH

    private int _waitInterval;                                                         // :60 WS-WAIT-INTERVAL ms (set 5000)
    private decimal _availableAmount;                                                  // :62 WS-AVAILABLE-AMT S9(9)V99 COMP-3
    private string _transactionAmountText = "";                                       // :63 WS-TRANSACTION-AMT-AN X(13)
    private decimal _transactionAmount;                                                // :64 WS-TRANSACTION-AMT S9(10)V99
    private decimal _approvedAmount;                                                   // :65 WS-APPROVED-AMT S9(10)V99
    private string _approvedAmountDisplay = "";                                       // :66 WS-APPROVED-AMT-DIS -zzzzzzzzz9.99
    private string _triggerData = "";                                                // :67 WS-TRIGGER-DATA X(64)

    // MQ work fields (W01 = request GET; W02 = reply PUT). source: :99-108
    private string _getBuffer = "";             // W01-GET-BUFFER X(500) — the raw GET payload (sub-stringed by DATALEN)
    private int _getDataLength;                  // W01-DATALEN — length of the message actually returned by GET

    // MQGET / MQPUT completion + reason (WS-COMPCODE / WS-REASON). source: :58-59
    private int _completionCode;                 // WS-COMPCODE
    private int _reasonCode;                      // WS-REASON

    // Time work fields. source: :50-56
    private string _currentDateText = "";       // WS-CUR-DATE-X6 — FORMATTIME YYDDD (8500) / YYMMDD (9500)
    private string _currentTimeText = "";       // WS-CUR-TIME-X6 — FORMATTIME TIME (HHMMSS)
    private int _currentTimeNumeric;            // WS-CUR-TIME-N6 9(6) numeric HHMMSS
    private int _currentTimeMillis;             // WS-CUR-TIME-MS S9(8) COMP — milliseconds 0..999
    private int _julianDate;                    // WS-YYDDD 9(5)
    private long _timeWithMillis;               // WS-TIME-WITH-MS S9(9) COMP-3

    // =================================================================================================
    //  WS-SWITCHES — source: COPAUA0C.cbl:110-145
    // =================================================================================================
    private char _authResponseFlag;             // WS-AUTH-RESP-FLG — 'A' approved / 'D' declined
    private char _loopFlag = 'N';               // WS-LOOP-FLG — 88 WS-LOOP-END = 'E'
    private char _messageAvailableFlag = 'M';   // WS-MSG-AVAILABLE-FLG — 88 NO-MORE-MSG-AVAILABLE = 'N' / MORE = 'M'
    private char _requestMqFlag = 'C';          // WS-REQUEST-MQ-FLG — 88 WS-REQUEST-MQ-OPEN = 'O' / CLSE = 'C'
    private char _xrefReadFlag;                  // WS-XREF-READ-FLG — 88 CARD-FOUND-XREF = 'Y' / NFOUND = 'N'
    private char _accountReadFlag;              // WS-ACCT-READ-FLG — 88 FOUND-ACCT-IN-MSTR = 'Y' / NFOUND = 'N'
    private char _customerReadFlag;             // WS-CUST-READ-FLG — 88 FOUND-CUST-IN-MSTR = 'Y' / NFOUND = 'N'
    private char _summarySegmentFlag;           // WS-PAUT-SMRY-SEG-FLG — 88 FOUND-PAUT-SMRY-SEG = 'Y' / NFOUND = 'N'
    private char _decisionFlag;                 // WS-DECISION-FLG — 88 APPROVE-AUTH = 'A' / DECLINE-AUTH = 'D'
    private char _declineReasonFlag;            // WS-DECLINE-REASON-FLG — 88 INSUFFICIENT-FUND='I' / CARD-NOT-ACTIVE='A' / ...

    // IMS scheduling flag (WS-IMS-PSB-SCHD-FLG). source: :95-97
    private char _imsPsbScheduledFlag;          // 88 IMS-PSB-SCHD = 'Y' / NOT = 'N'

    // =================================================================================================
    //  Staging records (the parsed request, the built reply, the IMS segments). The COBOL keeps one of
    //  each in WORKING-STORAGE; here they are mutable fields reused across the drain loop (the COBOL
    //  does NOT re-INITIALIZE them between messages either — fields persist their prior values). source:
    //  CCPAURQY / CCPAURLY / CIPAUSMY / CIPAUDTY.
    // =================================================================================================
    private readonly PendingAuthRequest _request = new();      // PENDING-AUTH-REQUEST  (CCPAURQY)
    private readonly PendingAuthResponse _response = new();      // PENDING-AUTH-RESPONSE (CCPAURLY)
    private PautSummary _authSummary = new();                // PENDING-AUTH-SUMMARY  (CIPAUSMY)
    private readonly PautDetail _authDetail = new();         // PENDING-AUTH-DETAILS  (CIPAUDTY)

    // VSAM record buffers (read from the masters). source: CVACT03Y / CVACT01Y / CVCUS01Y.
    private CardXref? _cardXref;
    private Account? _account;
    // Customer is read but never consumed (FB-2); kept for parity/side-effect only.

    // =================================================================================================
    //  ERROR-LOG-RECORD — source: CCPAUERY.cpy (ERR-* fields, 122 bytes).
    // =================================================================================================
    private string _errorLocation = "";         // ERR-LOCATION
    private char _errorLevel;                    // ERR-LEVEL — 'L'/'I'/'W'/'C'
    private char _errorSubsystem;               // ERR-SUBSYSTEM — 'A'/'C'/'I'/'D'/'M'/'F'
    private string _errorCode1 = "";            // ERR-CODE-1
    private string _errorCode2 = "";            // ERR-CODE-2
    private string _errorMessage = "";          // ERR-MESSAGE
    private string _errorEventKey = "";         // ERR-EVENT-KEY

    /// <summary>Signals the COBOL 9990-END-ROUTINE hard exit (critical error → TERM + EXEC CICS RETURN).</summary>
    private sealed class EndRoutineSignal : Exception { }

    // -------------------------------------------------------------------------------------------------

    /// <summary>Wires the auth processor over the relational DB, an error sink, and a clock.</summary>
    /// <param name="db">The relational connection backing the VSAM→SQL reads and the IMS-table writes.</param>
    /// <param name="errorLog">The CSSL TD-queue stand-in (defaults to a discard sink).</param>
    /// <param name="clock">Clock for ASKTIME/FORMATTIME (defaults to the system clock).</param>
    public AuthorizationDecisionService(RelationalDb db, IErrorLog? errorLog = null, IClock? clock = null)
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
            Initialize(trigger);          // PERFORM 1000-INITIALIZE
            MainProcess();                // PERFORM 2000-MAIN-PROCESS
            Terminate();                  // PERFORM 9000-TERMINATE
        }
        catch (EndRoutineSignal)
        {
            // 9990-END-ROUTINE already performed 9000-TERMINATE; EXEC CICS RETURN ends the transaction.
        }
        // EXEC CICS RETURN. source: :226-227
        return _messagesProcessed;
    }

    // =================================================================================================
    //  1000-INITIALIZE — source: COPAUA0C.cbl:230-250
    // =================================================================================================
    private void Initialize(TriggerMessage trigger)  // COBOL paragraph: 1000-INITIALIZE
    {
        // EXEC CICS RETRIEVE INTO(MQTM) NOHANDLE; if NORMAL move MQTM-QNAME/TRIGGERDATA. source: :233-240
        // The shim always supplies the trigger (RETRIEVE succeeds).
        _requestQueueName = trigger.QueueName;
        _triggerData = trigger.TriggerData;

        _waitInterval = 5000;                                 // MOVE 5000 TO WS-WAIT-INTERVAL. source: :242

        OpenRequestQueue();                                 // PERFORM 1100-OPEN-REQUEST-QUEUE. source: :244
        ReadRequestMq();                                   // PERFORM 3100-READ-REQUEST-MQ (prime). source: :246
    }

    // =================================================================================================
    //  1100-OPEN-REQUEST-QUEUE — source: COPAUA0C.cbl:255-287
    // =================================================================================================
    private void OpenRequestQueue()  // COBOL paragraph: 1100-OPEN-REQUEST-QUEUE
    {
        // MQOD-OBJECTTYPE = MQOT-Q; MQOD-OBJECTNAME = WS-REQUEST-QNAME; WS-OPTIONS = MQOO-INPUT-SHARED.
        // CALL 'MQOPEN'. source: :257-268
        _requestHandle = _mq.Open(_requestQueueName, MqooInputShared);
        _completionCode = MqConstants.MqccOk;                       // the in-proc Open always succeeds
        _reasonCode = MqConstants.MqrcNone;

        if (_completionCode == MqConstants.MqccOk)                  // IF WS-COMPCODE = MQCC-OK. source: :270
        {
            SetRequestMqOpen();                                // SET WS-REQUEST-MQ-OPEN TO TRUE
        }
        else
        {
            _errorLocation = "M001";                             // source: :273-282
            _errorLevel = 'C'; _errorSubsystem = 'M';
            _errorCode1 = CodeDisplay(_completionCode);
            _errorCode2 = CodeDisplay(_reasonCode);
            _errorMessage = "REQ MQ OPEN ERROR";
            LogError();
        }
    }

    // =================================================================================================
    //  1200-SCHEDULE-PSB — source: COPAUA0C.cbl:292-321
    // =================================================================================================
    private void SchedulePsb()  // COBOL paragraph: 1200-SCHEDULE-PSB
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
            _errorLocation = "I001";                             // source: :311-316
            _errorLevel = 'C'; _errorSubsystem = 'I';
            _errorCode1 = imsReturnCode;
            _errorMessage = "IMS SCHD FAILED";
            LogError();
        }
    }

    // =================================================================================================
    //  2000-MAIN-PROCESS — source: COPAUA0C.cbl:323-348
    // =================================================================================================
    private void MainProcess()  // COBOL paragraph: 2000-MAIN-PROCESS
    {
        // PERFORM UNTIL NO-MORE-MSG-AVAILABLE OR WS-LOOP-END
        while (!IsNoMoreMsgAvailable() && !IsLoopEnd())
        {
            ExtractRequestMessage();                           // PERFORM 2100-EXTRACT-REQUEST-MSG. source: :328
            ProcessAuthorization();                                 // PERFORM 5000-PROCESS-AUTH. source: :330

            _messagesProcessed++;                                 // ADD 1 TO WS-MSG-PROCESSED. source: :332

            // EXEC CICS SYNCPOINT (COMMIT). In-proc the repository writes are already committed; the
            // syncpoint boundary is the implicit per-statement commit on the shared connection. source: :334-336
            SetImsPsbNotSchd();                                // SET IMS-PSB-NOT-SCHD TO TRUE. source: :337

            if (_messagesProcessed > MessageProcessLimit)        // IF WS-MSG-PROCESSED > 500. source: :339
            {
                SetLoopEnd();                                  // SET WS-LOOP-END TO TRUE
            }
            else
            {
                ReadRequestMq();                           // PERFORM 3100-READ-REQUEST-MQ (next). source: :342
            }
        }
    }

    // =================================================================================================
    //  2100-EXTRACT-REQUEST-MSG — source: COPAUA0C.cbl:351-383
    // =================================================================================================
    private void ExtractRequestMessage()  // COBOL paragraph: 2100-EXTRACT-REQUEST-MSG
    {
        // UNSTRING W01-GET-BUFFER(1:W01-DATALEN) DELIMITED BY ',' INTO the 18 request receivers.
        // COBOL UNSTRING: a value longer than its receiver truncates to the receiver size; fewer commas
        // leave trailing receivers UNCHANGED (their prior value persists — faithful). source: :354-374
        string buffer = _getDataLength <= 0
            ? ""
            : _getBuffer.Substring(0, Math.Min(_getDataLength, _getBuffer.Length));
        string[] parts = buffer.Split(',');

        int i = 0;
        Unstring(parts, ref i, 6, v => _request.AuthDate = v);                 // 1  PA-RQ-AUTH-DATE X(6)
        Unstring(parts, ref i, 6, v => _request.AuthTime = v);                 // 2  PA-RQ-AUTH-TIME X(6)
        Unstring(parts, ref i, 16, v => _request.CardNum = v);                 // 3  PA-RQ-CARD-NUM X(16)
        Unstring(parts, ref i, 4, v => _request.AuthType = v);                 // 4  PA-RQ-AUTH-TYPE X(4)
        Unstring(parts, ref i, 4, v => _request.CardExpiryDate = v);           // 5  PA-RQ-CARD-EXPIRY-DATE X(4)
        Unstring(parts, ref i, 6, v => _request.MessageType = v);              // 6  PA-RQ-MESSAGE-TYPE X(6)
        Unstring(parts, ref i, 6, v => _request.MessageSource = v);            // 7  PA-RQ-MESSAGE-SOURCE X(6)
        Unstring(parts, ref i, 6, v => _request.ProcessingCode = ToNum6(v));   // 8  PA-RQ-PROCESSING-CODE 9(6)
        Unstring(parts, ref i, 13, v => _transactionAmountText = v);         // 9  -> WS-TRANSACTION-AMT-AN X(13)
        Unstring(parts, ref i, 4, v => _request.MerchantCatagoryCode = v);     // 10 PA-RQ-MERCHANT-CATAGORY-CODE X(4)
        Unstring(parts, ref i, 3, v => _request.AcqrCountryCode = v);          // 11 PA-RQ-ACQR-COUNTRY-CODE X(3)
        Unstring(parts, ref i, 2, v => _request.PosEntryMode = ToNum2(v));     // 12 PA-RQ-POS-ENTRY-MODE 9(2)
        Unstring(parts, ref i, 15, v => _request.MerchantId = v);              // 13 PA-RQ-MERCHANT-ID X(15)
        Unstring(parts, ref i, 22, v => _request.MerchantName = v);            // 14 PA-RQ-MERCHANT-NAME X(22)
        Unstring(parts, ref i, 13, v => _request.MerchantCity = v);            // 15 PA-RQ-MERCHANT-CITY X(13)
        Unstring(parts, ref i, 2, v => _request.MerchantState = v);            // 16 PA-RQ-MERCHANT-STATE X(2)
        Unstring(parts, ref i, 9, v => _request.MerchantZip = v);              // 17 PA-RQ-MERCHANT-ZIP X(9)
        Unstring(parts, ref i, 15, v => _request.TransactionId = v);           // 18 PA-RQ-TRANSACTION-ID X(15)

        // COMPUTE PA-RQ-TRANSACTION-AMT = FUNCTION NUMVAL(WS-TRANSACTION-AMT-AN). source: :376-377
        // PA-RQ-TRANSACTION-AMT is edited PIC +9(10).99 (10 int digits, 2 frac, signed). Store truncates.
        _request.TransactionAmt = Decimals.Store(NumVal(_transactionAmountText), 10, 2, signed: true);

        // MOVE PA-RQ-TRANSACTION-AMT TO WS-TRANSACTION-AMT (S9(10)V99 working copy). source: :379
        _transactionAmount = Decimals.Store(_request.TransactionAmt, 10, 2, signed: true);
    }

    // =================================================================================================
    //  3100-READ-REQUEST-MQ — source: COPAUA0C.cbl:386-435
    // =================================================================================================
    private void ReadRequestMq()  // COBOL paragraph: 3100-READ-REQUEST-MQ
    {
        // MQGMO-OPTIONS = NO-SYNCPOINT + WAIT + CONVERT + FAIL-IF-QUIESCING; WAITINTERVAL = 5000.
        // MD MSGID=MQMI-NONE, CORRELID=MQCI-NONE, FORMAT=MQFMT-STRING; W01-BUFFLEN = 500.
        // CALL 'MQGET'. source: :389-409
        MqResult r = _mq.Get(_requestHandle!, out MqMessage? msg);
        _completionCode = r.CompletionCode;
        _reasonCode = r.ReasonCode;

        if (_completionCode == MqConstants.MqccOk && msg is not null)   // IF WS-COMPCODE = MQCC-OK. source: :410
        {
            // GET buffer is X(500): the COBOL truncates a longer payload to 500 bytes; DATALEN is the
            // returned length (capped at the 500-byte buffer). source: :398,406
            _getBuffer = msg.Body.Length > 500 ? msg.Body.Substring(0, 500) : msg.Body;
            _getDataLength = Math.Min(msg.Body.Length, 500);

            _savedCorrelId = msg.CorrelId;                          // MOVE MQMD-CORRELID -> WS-SAVE-CORRELID. :411
            _replyQueueName = msg.ReplyToQueue;                        // MOVE MQMD-REPLYTOQ -> WS-REPLY-QNAME. :413
        }
        else
        {
            if (_reasonCode == MqConstants.MqrcNoMsgAvailable)         // IF WS-REASON = MQRC-NO-MSG-AVAILABLE. :416
            {
                SetNoMoreMsgAvailable();                            // SET NO-MORE-MSG-AVAILABLE TO TRUE
            }
            else
            {
                _errorLocation = "M003";                             // source: :419-429
                _errorLevel = 'C'; _errorSubsystem = 'C';              // FB-4: subsystem set CICS, not MQ
                _errorCode1 = CodeDisplay(_completionCode);
                _errorCode2 = CodeDisplay(_reasonCode);
                _errorMessage = "FAILED TO READ REQUEST MQ";
                _errorEventKey = _authDetail.CardNum;                  // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
                LogError();
            }
        }
    }

    // =================================================================================================
    //  5000-PROCESS-AUTH — source: COPAUA0C.cbl:438-469
    // =================================================================================================
    private void ProcessAuthorization()  // COBOL paragraph: 5000-PROCESS-AUTH
    {
        SetApproveAuth();                                          // SET APPROVE-AUTH TO TRUE. source: :441

        SchedulePsb();                                        // PERFORM 1200-SCHEDULE-PSB. source: :443

        SetCardFoundXref();                                       // SET CARD-FOUND-XREF (optimistic). :445
        SetFoundAcctInMstr();                                     // SET FOUND-ACCT-IN-MSTR (optimistic). :446

        ReadXrefRecord();                                    // PERFORM 5100-READ-XREF-RECORD. source: :448

        if (IsCardFoundXref())                                    // IF CARD-FOUND-XREF. source: :450
        {
            ReadAccountRecord();                               // PERFORM 5200-READ-ACCT-RECORD. source: :451
            ReadCustomerRecord();                               // PERFORM 5300-READ-CUST-RECORD. source: :452
            ReadAuthSummary();                               // PERFORM 5500-READ-AUTH-SUMMRY. source: :454
            ReadProfileData();                              // PERFORM 5600-READ-PROFILE-DATA. source: :456
        }

        MakeDecision();                                     // PERFORM 6000-MAKE-DECISION. source: :459
        SendResponse();                                    // PERFORM 7100-SEND-RESPONSE. source: :461

        if (IsCardFoundXref())                                  // IF CARD-FOUND-XREF. source: :463
        {
            WriteAuthToDb();                               // PERFORM 8000-WRITE-AUTH-TO-DB. source: :464
        }
    }

    // =================================================================================================
    //  5100-READ-XREF-RECORD — source: COPAUA0C.cbl:472-517
    // =================================================================================================
    private void ReadXrefRecord()  // COBOL paragraph: 5100-READ-XREF-RECORD
    {
        // MOVE PA-RQ-CARD-NUM TO XREF-CARD-NUM; EXEC CICS READ DATASET(CCXREF) RIDFLD(XREF-CARD-NUM).
        // source: :475-485
        string xrefCardNum = _request.CardNum;
        string resp = _xref.ReadByKey(xrefCardNum, out _cardXref);

        switch (resp)                                              // EVALUATE WS-RESP-CD. source: :487
        {
            case FileStatus.Ok:                                   // WHEN DFHRESP(NORMAL). source: :488
                SetCardFoundXref();
                break;

            case FileStatus.RecordNotFound:                       // WHEN DFHRESP(NOTFND). source: :490
                SetCardNfoundXref();
                SetNfoundAcctInMstr();                            // SET NFOUND-ACCT-IN-MSTR TO TRUE. :492
                _errorLocation = "A001";                            // source: :494-500
                _errorLevel = 'W'; _errorSubsystem = 'A';
                _errorMessage = "CARD NOT FOUND IN XREF";
                _errorEventKey = xrefCardNum;
                LogError();
                break;

            default:                                              // WHEN OTHER. source: :501
                _errorLocation = "C001";                            // source: :502-512
                _errorLevel = 'C'; _errorSubsystem = 'C';
                _errorCode1 = CodeDisplay(RespToCode(resp));
                _errorCode2 = CodeDisplay(0);
                _errorMessage = "FAILED TO READ XREF FILE";
                _errorEventKey = xrefCardNum;
                LogError();
                break;
        }
    }

    // =================================================================================================
    //  5200-READ-ACCT-RECORD — source: COPAUA0C.cbl:520-565
    // =================================================================================================
    private void ReadAccountRecord()  // COBOL paragraph: 5200-READ-ACCT-RECORD
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
                _errorLocation = "A002";                            // source: :541-547
                _errorLevel = 'W'; _errorSubsystem = 'A';
                _errorMessage = "ACCT NOT FOUND IN XREF";          // FB-5: text says XREF; it is ACCT
                _errorEventKey = AcctIdKey(acctId);
                LogError();
                break;

            default:                                              // WHEN OTHER. source: :549
                _errorLocation = "C002";                            // source: :550-560
                _errorLevel = 'C'; _errorSubsystem = 'C';
                _errorCode1 = CodeDisplay(RespToCode(resp));
                _errorCode2 = CodeDisplay(0);
                _errorMessage = "FAILED TO READ ACCT FILE";
                _errorEventKey = AcctIdKey(acctId);
                LogError();
                break;
        }
    }

    // =================================================================================================
    //  5300-READ-CUST-RECORD — source: COPAUA0C.cbl:568-613  (FB-2: result never used by the decision)
    // =================================================================================================
    private void ReadCustomerRecord()  // COBOL paragraph: 5300-READ-CUST-RECORD
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
                _errorLocation = "A003";                            // source: :589-595
                _errorLevel = 'W'; _errorSubsystem = 'A';
                _errorMessage = "CUST NOT FOUND IN XREF";          // FB-5: text says XREF; it is CUST
                _errorEventKey = CustIdKey(custId);
                LogError();
                break;

            default:                                              // WHEN OTHER. source: :597
                _errorLocation = "C003";                            // source: :598-608
                _errorLevel = 'C'; _errorSubsystem = 'C';
                _errorCode1 = CodeDisplay(RespToCode(resp));
                _errorCode2 = CodeDisplay(0);
                _errorMessage = "FAILED TO READ CUST FILE";
                _errorEventKey = CustIdKey(custId);
                LogError();
                break;
        }
    }

    // =================================================================================================
    //  5500-READ-AUTH-SUMMRY — source: COPAUA0C.cbl:616-644
    // =================================================================================================
    private void ReadAuthSummary()  // COBOL paragraph: 5500-READ-AUTH-SUMMRY
    {
        // MOVE XREF-ACCT-ID TO PA-ACCT-ID; EXEC DLI GU SEGMENT(PAUTSUM0) WHERE(ACCNTID = PA-ACCT-ID).
        // MOVE DIBSTAT TO IMS-RETURN-CODE. source: :619-626
        long acctId = _cardXref?.AcctId ?? 0L;
        _authSummary.AcctId = acctId;
        string status = _summary.ReadByKey(acctId, out PautSummary? found);
        string imsReturnCode = SqlToIms(status);                 // '00' -> '  '; '23' -> 'GE'

        // EVALUATE TRUE. source: :627
        if (IsStatusOk(imsReturnCode))                           // WHEN STATUS-OK. source: :628
        {
            _authSummary = found!;                                 // INTO(PENDING-AUTH-SUMMARY)
            SetFoundPautSmrySeg();
        }
        else if (IsSegmentNotFound(imsReturnCode))               // WHEN SEGMENT-NOT-FOUND. source: :630
        {
            SetNfoundPautSmrySeg();
        }
        else                                                     // WHEN OTHER. source: :632
        {
            _errorLocation = "I002";                                // source: :633-639
            _errorLevel = 'C'; _errorSubsystem = 'I';
            _errorCode1 = imsReturnCode;
            _errorMessage = "IMS GET SUMMARY FAILED";
            _errorEventKey = _authDetail.CardNum;                    // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
            LogError();
        }
    }

    // =================================================================================================
    //  5600-READ-PROFILE-DATA — source: COPAUA0C.cbl:647-654  (CONTINUE — stub; fraud profile not impl.)
    // =================================================================================================
    private void ReadProfileData()  // COBOL paragraph: 5600-READ-PROFILE-DATA
    {
        // CONTINUE. source: :650
    }

    // =================================================================================================
    //  6000-MAKE-DECISION — source: COPAUA0C.cbl:657-735
    // =================================================================================================
    private void MakeDecision()  // COBOL paragraph: 6000-MAKE-DECISION
    {
        // Echo into reply. source: :660-662
        _response.CardNum = _request.CardNum;                               // PA-RQ-CARD-NUM -> PA-RL-CARD-NUM
        _response.TransactionId = _request.TransactionId;                  // PA-RQ-TRANSACTION-ID -> PA-RL-TRANSACTION-ID
        _response.AuthIdCode = _request.AuthTime;                          // PA-RQ-AUTH-TIME -> PA-RL-AUTH-ID-CODE

        // Decline if above available limit; use IMS summary if present, else account master. source: :665-683
        if (IsFoundPautSmrySeg())                                // IF FOUND-PAUT-SMRY-SEG. source: :665
        {
            // COMPUTE WS-AVAILABLE-AMT = PA-CREDIT-LIMIT - PA-CREDIT-BALANCE (S9(9)V99 receiver). :666-667
            _availableAmount = Decimals.Store(
                _authSummary.CreditLimit - _authSummary.CreditBalance, 9, 2, signed: true);
            if (_transactionAmount > _availableAmount)             // IF WS-TRANSACTION-AMT > WS-AVAILABLE-AMT. :668
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
                _availableAmount = Decimals.Store(acctCreditLimit - acctCurrBal, 9, 2, signed: true);
                if (_transactionAmount > _availableAmount)         // source: :676
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
            _response.AuthRespCode = "05";                            // MOVE '05' TO PA-RL-AUTH-RESP-CODE. :688
            _response.ApprovedAmt = 0m;                               // MOVE 0 TO PA-RL-APPROVED-AMT
            _approvedAmount = 0m;                               //         WS-APPROVED-AMT. :689-690
        }
        else
        {
            SetAuthRespApproved();                             // SET AUTH-RESP-APPROVED TO TRUE. :692
            _response.AuthRespCode = "00";                            // MOVE '00' TO PA-RL-AUTH-RESP-CODE. :693
            _response.ApprovedAmt = _request.TransactionAmt;              // MOVE PA-RQ-TRANSACTION-AMT -> PA-RL-APPROVED-AMT
            _approvedAmount = _request.TransactionAmt;              //         WS-APPROVED-AMT. :694-695
        }

        _response.AuthRespReason = "0000";                            // MOVE '0000' TO PA-RL-AUTH-RESP-REASON. :698
        if (IsAuthRespDeclined())                                // IF AUTH-RESP-DECLINED. source: :699
        {
            // EVALUATE TRUE. 4200/4300/5100/5200 are dead (FB-3). source: :700-717
            if (IsCardNfoundXref() || IsNfoundAcctInMstr() || IsNfoundCustInMstr())
                _response.AuthRespReason = "3100";
            else if (IsInsufficientFund())
                _response.AuthRespReason = "4100";
            else if (IsCardNotActive())
                _response.AuthRespReason = "4200";
            else if (IsAccountClosed())
                _response.AuthRespReason = "4300";
            else if (IsCardFraud())
                _response.AuthRespReason = "5100";
            else if (IsMerchantFraud())
                _response.AuthRespReason = "5200";
            else
                _response.AuthRespReason = "9000";
        }

        // MOVE WS-APPROVED-AMT TO WS-APPROVED-AMT-DIS (edit into -zzzzzzzzz9.99). source: :720
        _approvedAmountDisplay = EditedNumeric.Format(_approvedAmount, "-ZZZZZZZZZ9.99");

        // STRING the 6-field CSV (each followed by ',', trailing comma included) WITH POINTER
        // WS-RESP-LENGTH. The pointer is NOT reset per message (FB-6). source: :722-731
        StringIntoReply();
    }

    // =================================================================================================
    //  7100-SEND-RESPONSE — source: COPAUA0C.cbl:738-783
    // =================================================================================================
    private void SendResponse()  // COBOL paragraph: 7100-SEND-RESPONSE
    {
        // Reply OD: OBJECTTYPE=MQOT-Q, OBJECTNAME=WS-REPLY-QNAME.
        // Reply MD: MSGTYPE=MQMT-REPLY, CORRELID=WS-SAVE-CORRELID, MSGID=MQMI-NONE, REPLYTOQ/QMGR=SPACES,
        // PERSISTENCE=MQPER-NOT-PERSISTENT, EXPIRY=50, FORMAT=MQFMT-STRING.
        // PMO=MQPMO-NO-SYNCPOINT+MQPMO-DEFAULT-CONTEXT; W02-BUFFLEN = WS-RESP-LENGTH. CALL 'MQPUT1'.
        // source: :741-766
        var reply = new MqMessage
        {
            // W02-BUFFLEN = WS-RESP-LENGTH bytes of W02-PUT-BUFFER (the cumulative-pointer length, FB-6).
            Body = ReplyBufferToLength(_replyLength),
            MsgType = MqConstants.MqmtReply,
            CorrelId = _savedCorrelId,
            MsgId = MqConstants.MqmiNone,
            ReplyToQueue = "",
            ReplyToQMgr = "",
            Persistence = MqConstants.MqperNotPersistent,
            Expiry = MqConstants.AuthReplyExpiry,            // 50 = 5.0 s
            Format = MqConstants.MqfmtString,
        };

        MqResult r = _mq.Put1(_replyQueueName, reply);          // MQPUT1 by name (open-put-close). :758-766
        _completionCode = r.CompletionCode;
        _reasonCode = r.ReasonCode;

        if (_completionCode != MqConstants.MqccOk)                // IF WS-COMPCODE NOT = MQCC-OK. source: :767
        {
            _errorLocation = "M004";                            // source: :768-778
            _errorLevel = 'C'; _errorSubsystem = 'M';
            _errorCode1 = CodeDisplay(_completionCode);
            _errorCode2 = CodeDisplay(_reasonCode);
            _errorMessage = "FAILED TO PUT ON REPLY MQ";
            _errorEventKey = _authDetail.CardNum;                 // MOVE PA-CARD-NUM TO ERR-EVENT-KEY
            LogError();
        }
    }

    // =================================================================================================
    //  8000-WRITE-AUTH-TO-DB — source: COPAUA0C.cbl:786-795
    // =================================================================================================
    private void WriteAuthToDb()  // COBOL paragraph: 8000-WRITE-AUTH-TO-DB
    {
        UpdateSummary();                                  // PERFORM 8400-UPDATE-SUMMARY. source: :790
        InsertAuth();                                    // PERFORM 8500-INSERT-AUTH. source: :791
    }

    // =================================================================================================
    //  8400-UPDATE-SUMMARY — source: COPAUA0C.cbl:798-851
    // =================================================================================================
    private void UpdateSummary()  // COBOL paragraph: 8400-UPDATE-SUMMARY
    {
        if (IsNfoundPautSmrySeg())                            // IF NFOUND-PAUT-SMRY-SEG. source: :801
        {
            // INITIALIZE PENDING-AUTH-SUMMARY REPLACING NUMERIC DATA BY ZERO (numerics 0, chars spaces).
            // Then overlay acct/cust id. source: :802-806
            _authSummary = NewInitializedSummary();
            _authSummary.AcctId = _cardXref?.AcctId ?? 0L;     // MOVE XREF-ACCT-ID TO PA-ACCT-ID
            _authSummary.CustId = _cardXref?.CustId ?? 0L;     // MOVE XREF-CUST-ID TO PA-CUST-ID
        }

        // Always copy the account limits — FB-9: unconditional, even on a failed account read (stale).
        // S9(9)V99 receivers truncate. source: :810-811
        _authSummary.CreditLimit = Decimals.Store(_account?.CreditLimit ?? 0m, 9, 2, signed: true);
        _authSummary.CashLimit = Decimals.Store(_account?.CashCreditLimit ?? 0m, 9, 2, signed: true);

        if (IsAuthRespApproved())                            // IF AUTH-RESP-APPROVED. source: :813
        {
            _authSummary.ApprovedAuthCnt = AddCount(_authSummary.ApprovedAuthCnt, 1);             // :814
            _authSummary.ApprovedAuthAmt = Add9v2(_authSummary.ApprovedAuthAmt, _approvedAmount); // :815
            _authSummary.CreditBalance = Add9v2(_authSummary.CreditBalance, _approvedAmount);      // :817
            _authSummary.CashBalance = 0m;                                                       // FB-8. :818
        }
        else
        {
            _authSummary.DeclinedAuthCnt = AddCount(_authSummary.DeclinedAuthCnt, 1);             // :820
            // FB-1: PA-TRANSACTION-AMT (the DETAIL field) is not populated until 8500, so this adds its
            // prior value (0 on a fresh summary, the previous message's amount on an existing one). :821
            _authSummary.DeclinedAuthAmt = Add9v2(_authSummary.DeclinedAuthAmt, _authDetail.TransactionAmt);
        }

        string status;
        if (IsFoundPautSmrySeg())                            // IF FOUND-PAUT-SMRY-SEG -> REPL. source: :824
        {
            status = _summary.Update(_authSummary);            // EXEC DLI REPL SEGMENT(PAUTSUM0). :825-828
        }
        else
        {
            status = _summary.Insert(_authSummary);            // EXEC DLI ISRT SEGMENT(PAUTSUM0). :830-833
        }
        string imsReturnCode = SqlToIms(status);             // MOVE DIBSTAT TO IMS-RETURN-CODE. :835

        if (IsStatusOk(imsReturnCode))                       // IF STATUS-OK -> CONTINUE. source: :837-838
        {
            // CONTINUE
        }
        else
        {
            _errorLocation = "I003";                           // source: :840-846
            _errorLevel = 'C'; _errorSubsystem = 'I';
            _errorCode1 = imsReturnCode;
            _errorMessage = "IMS UPDATE SUMRY FAILED";
            _errorEventKey = _authDetail.CardNum;
            LogError();
        }
    }

    // =================================================================================================
    //  8500-INSERT-AUTH — source: COPAUA0C.cbl:854-936
    // =================================================================================================
    private void InsertAuth()  // COBOL paragraph: 8500-INSERT-AUTH
    {
        // EXEC CICS ASKTIME / FORMATTIME YYDDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6) MILLISECONDS(...).
        // source: :857-866
        DateTime now = _clock.Now;
        _currentDateText = FormatTimeYyddd(now);                 // 5-digit YYDDD + a trailing char (X(6))
        _currentTimeText = now.ToString("HHmmss", CultureInfo.InvariantCulture);
        _currentTimeMillis = now.Millisecond;                      // 0..999

        _julianDate = ToNum5(_currentDateText.Substring(0, 5));     // MOVE WS-CUR-DATE-X6(1:5) TO WS-YYDDD. :868
        _currentTimeNumeric = ToNum6(_currentTimeText);                 // MOVE WS-CUR-TIME-X6 TO WS-CUR-TIME-N6. :869

        // COMPUTE WS-TIME-WITH-MS = (WS-CUR-TIME-N6 * 1000) + WS-CUR-TIME-MS. source: :871-872
        _timeWithMillis = (long)_currentTimeNumeric * 1000 + _currentTimeMillis;

        // 9s-complement descending keys — preserve verbatim (do NOT "fix"). source: :874-875
        _authDetail.AuthDate9c = (int)(99999 - _julianDate);      // COMPUTE PA-AUTH-DATE-9C = 99999 - WS-YYDDD
        _authDetail.AuthTime9c = 999999999 - _timeWithMillis;    // COMPUTE PA-AUTH-TIME-9C = 999999999 - ...

        // Copy all request fields into the detail segment. source: :877-895
        _authDetail.AuthOrigDate = _request.AuthDate;
        _authDetail.AuthOrigTime = _request.AuthTime;
        _authDetail.CardNum = _request.CardNum;
        _authDetail.AuthType = _request.AuthType;
        _authDetail.CardExpiryDate = _request.CardExpiryDate;
        _authDetail.MessageType = _request.MessageType;
        _authDetail.MessageSource = _request.MessageSource;
        _authDetail.ProcessingCode = _request.ProcessingCode;
        _authDetail.TransactionAmt = Decimals.Store(_request.TransactionAmt, 10, 2, signed: true);  // PA-TRANSACTION-AMT
        _authDetail.MerchantCatagoryCode = _request.MerchantCatagoryCode;
        _authDetail.AcqrCountryCode = _request.AcqrCountryCode;
        _authDetail.PosEntryMode = _request.PosEntryMode;
        _authDetail.MerchantId = _request.MerchantId;
        _authDetail.MerchantName = _request.MerchantName;
        _authDetail.MerchantCity = _request.MerchantCity;
        _authDetail.MerchantState = _request.MerchantState;
        _authDetail.MerchantZip = _request.MerchantZip;
        _authDetail.TransactionId = _request.TransactionId;

        // Copy reply fields. source: :897-900
        _authDetail.AuthIdCode = _response.AuthIdCode;
        _authDetail.AuthRespCode = _response.AuthRespCode;
        _authDetail.AuthRespReason = _response.AuthRespReason;
        _authDetail.ApprovedAmt = Decimals.Store(_response.ApprovedAmt, 10, 2, signed: true);  // PA-APPROVED-AMT

        if (IsAuthRespApproved())                            // IF AUTH-RESP-APPROVED. source: :902
            _authDetail.MatchStatus = "P";                    // SET PA-MATCH-PENDING TO TRUE
        else
            _authDetail.MatchStatus = "D";                    // SET PA-MATCH-AUTH-DECLINED TO TRUE

        _authDetail.AuthFraud = " ";                           // MOVE SPACE TO PA-AUTH-FRAUD (X(1)). source: :908
        _authDetail.FraudRptDate = "        ";                 // MOVE SPACE TO PA-FRAUD-RPT-DATE (X(8) spaces)

        _authDetail.AcctId = _cardXref?.AcctId ?? 0L;          // MOVE XREF-ACCT-ID TO PA-ACCT-ID. source: :911

        // The 8-byte PAUT9CTS child sequence key = date-9C (5 digits) + time-9C (9 digits), 9s-complement,
        // so ascending key order == newest-first (matches IX_PAUT_DETAIL_SEQ and the COPAUS0C/CBPAUP0C
        // scan order). Built as a fixed-width zero-padded decimal concatenation. source: :913-919; IMS_SCHEMA.md §2/§3.7
        _authDetail.AuthKey = BuildAuthKey(_authDetail.AuthDate9c, _authDetail.AuthTime9c);

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
            _errorLocation = "I004";                           // source: :925-931
            _errorLevel = 'C'; _errorSubsystem = 'I';
            _errorCode1 = imsReturnCode;
            _errorMessage = "IMS INSERT DETL FAILED";
            _errorEventKey = _authDetail.CardNum;
            LogError();
        }
    }

    // =================================================================================================
    //  9000-TERMINATE — source: COPAUA0C.cbl:940-951
    // =================================================================================================
    private void Terminate()  // COBOL paragraph: 9000-TERMINATE
    {
        // IF IMS-PSB-SCHD EXEC DLI TERM (normally a no-op — 2000 sets NOT-SCHD after each message).
        // source: :943-945  (the relational unit of work ends implicitly).
        CloseRequestQueue();                             // PERFORM 9100-CLOSE-REQUEST-QUEUE. source: :947
    }

    // =================================================================================================
    //  9100-CLOSE-REQUEST-QUEUE — source: COPAUA0C.cbl:953-980
    // =================================================================================================
    private void CloseRequestQueue()  // COBOL paragraph: 9100-CLOSE-REQUEST-QUEUE
    {
        if (IsRequestMqOpen() && _requestHandle is not null)  // IF WS-REQUEST-MQ-OPEN. source: :955
        {
            MqResult r = _mq.Close(_requestHandle);          // CALL 'MQCLOSE' ... MQCO-NONE. :956-961
            _completionCode = r.CompletionCode;
            _reasonCode = r.ReasonCode;

            if (_completionCode == MqConstants.MqccOk)           // IF WS-COMPCODE = MQCC-OK. source: :963
            {
                SetRequestMqClse();
            }
            else
            {
                _errorLocation = "M005";                       // source: :966-975
                _errorLevel = 'W'; _errorSubsystem = 'M';
                _errorCode1 = CodeDisplay(_completionCode);
                _errorCode2 = CodeDisplay(_reasonCode);
                _errorMessage = "FAILED TO CLOSE REQUEST MQ";
                LogError();
            }
        }
    }

    // =================================================================================================
    //  9500-LOG-ERROR — source: COPAUA0C.cbl:983-1013
    // =================================================================================================
    private void LogError()  // COBOL paragraph: 9500-LOG-ERROR
    {
        // EXEC CICS ASKTIME / FORMATTIME YYMMDD(WS-CUR-DATE-X6) TIME(WS-CUR-TIME-X6). source: :986-994
        DateTime now = _clock.Now;
        string errDate = now.ToString("yyMMdd", CultureInfo.InvariantCulture);
        string errTime = now.ToString("HHmmss", CultureInfo.InvariantCulture);

        // Build the 119-byte ERROR-LOG-RECORD (fixed offsets) and WRITEQ TD QUEUE('CSSL'). source: :996-1006
        string record = BuildErrorLogRecord(errDate, errTime);
        _errorLog.Write(record);                             // NOHANDLE — failures ignored

        if (_errorLevel == 'C')                                // IF ERR-CRITICAL. source: :1008
        {
            EndRoutine();                                // PERFORM 9990-END-ROUTINE
        }
    }

    // =================================================================================================
    //  9990-END-ROUTINE — source: COPAUA0C.cbl:1016-1025
    // =================================================================================================
    private void EndRoutine()  // COBOL paragraph: 9990-END-ROUTINE
    {
        Terminate();                                     // PERFORM 9000-TERMINATE. source: :1019
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
        int pos = _replyLength;                             // WITH POINTER WS-RESP-LENGTH (1-based)
        AppendString(ref pos, Fixed(_response.CardNum, 16));      // PA-RL-CARD-NUM X(16)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_response.TransactionId, 15)); // PA-RL-TRANSACTION-ID X(15)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_response.AuthIdCode, 6));    // PA-RL-AUTH-ID-CODE X(6)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_response.AuthRespCode, 2));  // PA-RL-AUTH-RESP-CODE X(2)
        AppendChar(ref pos, ',');
        AppendString(ref pos, Fixed(_response.AuthRespReason, 4)); // PA-RL-AUTH-RESP-REASON X(4)
        AppendChar(ref pos, ',');
        AppendString(ref pos, _approvedAmountDisplay);           // WS-APPROVED-AMT-DIS -zzzzzzzzz9.99 (14 chars)
        AppendChar(ref pos, ',');                            // trailing comma
        _replyLength = (short)pos;                          // pointer ends at built-length+1 (persisted)
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
    private void SetApproveAuth() => _decisionFlag = 'A';
    private void SetDeclineAuth() => _decisionFlag = 'D';
    private bool IsDeclineAuth() => _decisionFlag == 'D';

    private void SetAuthRespApproved() => _authResponseFlag = 'A';
    private void SetAuthRespDeclined() => _authResponseFlag = 'D';
    private bool IsAuthRespApproved() => _authResponseFlag == 'A';
    private bool IsAuthRespDeclined() => _authResponseFlag == 'D';

    private void SetInsufficientFund() => _declineReasonFlag = 'I';
    private bool IsInsufficientFund() => _declineReasonFlag == 'I';
    // Dead reason flags: CARD-NOT-ACTIVE/ACCOUNT-CLOSED/CARD-FRAUD/MERCHANT-FRAUD are declared but never
    // SET anywhere in COPAUA0C (FB-3), so their EVALUATE arms can never fire. source: :140-145,707-714.
    private static bool IsCardNotActive() => false;
    private static bool IsAccountClosed() => false;
    private static bool IsCardFraud() => false;
    private static bool IsMerchantFraud() => false;

    private void SetCardFoundXref() => _xrefReadFlag = 'Y';
    private void SetCardNfoundXref() => _xrefReadFlag = 'N';
    private bool IsCardFoundXref() => _xrefReadFlag == 'Y';
    private bool IsCardNfoundXref() => _xrefReadFlag == 'N';

    private void SetFoundAcctInMstr() => _accountReadFlag = 'Y';
    private void SetNfoundAcctInMstr() => _accountReadFlag = 'N';
    private bool IsFoundAcctInMstr() => _accountReadFlag == 'Y';
    private bool IsNfoundAcctInMstr() => _accountReadFlag == 'N';

    private void SetFoundCustInMstr() => _customerReadFlag = 'Y';
    private void SetNfoundCustInMstr() => _customerReadFlag = 'N';
    private bool IsNfoundCustInMstr() => _customerReadFlag == 'N';

    private void SetFoundPautSmrySeg() => _summarySegmentFlag = 'Y';
    private void SetNfoundPautSmrySeg() => _summarySegmentFlag = 'N';
    private bool IsFoundPautSmrySeg() => _summarySegmentFlag == 'Y';
    private bool IsNfoundPautSmrySeg() => _summarySegmentFlag == 'N';

    private void SetNoMoreMsgAvailable() => _messageAvailableFlag = 'N';
    private bool IsNoMoreMsgAvailable() => _messageAvailableFlag == 'N';

    private void SetLoopEnd() => _loopFlag = 'E';
    private bool IsLoopEnd() => _loopFlag == 'E';

    private void SetRequestMqOpen() => _requestMqFlag = 'O';
    private void SetRequestMqClse() => _requestMqFlag = 'C';
    private bool IsRequestMqOpen() => _requestMqFlag == 'O';

    private void SetImsPsbSchd() => _imsPsbScheduledFlag = 'Y';
    private void SetImsPsbNotSchd() => _imsPsbScheduledFlag = 'N';

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
        AcctId = _authDetail.AcctId,
        AuthKey = _authDetail.AuthKey,
        AuthDate9c = _authDetail.AuthDate9c,
        AuthTime9c = _authDetail.AuthTime9c,
        AuthOrigDate = _authDetail.AuthOrigDate,
        AuthOrigTime = _authDetail.AuthOrigTime,
        CardNum = _authDetail.CardNum,
        AuthType = _authDetail.AuthType,
        CardExpiryDate = _authDetail.CardExpiryDate,
        MessageType = _authDetail.MessageType,
        MessageSource = _authDetail.MessageSource,
        AuthIdCode = _authDetail.AuthIdCode,
        AuthRespCode = _authDetail.AuthRespCode,
        AuthRespReason = _authDetail.AuthRespReason,
        ProcessingCode = _authDetail.ProcessingCode,
        TransactionAmt = _authDetail.TransactionAmt,
        ApprovedAmt = _authDetail.ApprovedAmt,
        MerchantCatagoryCode = _authDetail.MerchantCatagoryCode,
        AcqrCountryCode = _authDetail.AcqrCountryCode,
        PosEntryMode = _authDetail.PosEntryMode,
        MerchantId = _authDetail.MerchantId,
        MerchantName = _authDetail.MerchantName,
        MerchantCity = _authDetail.MerchantCity,
        MerchantState = _authDetail.MerchantState,
        MerchantZip = _authDetail.MerchantZip,
        TransactionId = _authDetail.TransactionId,
        MatchStatus = _authDetail.MatchStatus,
        AuthFraud = _authDetail.AuthFraud,
        FraudRptDate = _authDetail.FraudRptDate,
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
        sb.Append(Fixed(TranId, 8));     // ERR-APPLICATION X(8)  <- WS-CICS-TRANID
        sb.Append(Fixed(ProgramId, 8));        // ERR-PROGRAM X(8)      <- WS-PGM-AUTH
        sb.Append(Fixed(_errorLocation, 4));     // ERR-LOCATION X(4)
        sb.Append(_errorLevel == '\0' ? ' ' : _errorLevel);     // ERR-LEVEL X(1)
        sb.Append(_errorSubsystem == '\0' ? ' ' : _errorSubsystem); // ERR-SUBSYSTEM X(1)
        sb.Append(Fixed(_errorCode1, 9));        // ERR-CODE-1 X(9)
        sb.Append(Fixed(_errorCode2, 9));        // ERR-CODE-2 X(9)
        sb.Append(Fixed(_errorMessage, 50));     // ERR-MESSAGE X(50)
        sb.Append(Fixed(_errorEventKey, 20));    // ERR-EVENT-KEY X(20)
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
