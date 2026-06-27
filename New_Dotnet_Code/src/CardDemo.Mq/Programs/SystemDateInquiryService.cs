using System.Globalization;
using System.Text;
using CardDemo.Runtime;

namespace CardDemo.Mq.Programs;

/// <summary>
/// Faithful .NET port of the optional VSAM/MQ COBOL program <c>CODATE01</c> — the CardDemo
/// <b>System-Date inquiry service</b> (CICS transaction <c>CDRD</c>, MQ-triggered server, no BMS screen).
/// It is started by the CICS-MQ trigger monitor, learns its request queue from the MQ Trigger Message
/// (<c>MQTM</c>) via <c>EXEC CICS RETRIEVE</c>, opens an error queue (<c>CARD.DEMO.ERROR</c>), the
/// trigger-supplied request queue (for GET) and the hard-coded reply queue (<c>CARD.DEMO.REPLY.DATE</c>,
/// for PUT), then <b>drains</b> the request queue: for every request it obtains the current CICS date and
/// time (<c>ASKTIME</c>/<c>FORMATTIME</c>), builds a fixed free-text reply
/// <c>'SYSTEM DATE : MM-DD-YYYY' + 'SYSTEM TIME : HH:MM:SS'</c>, and PUTs it to the reply queue, echoing
/// the request's saved MsgId/CorrelId. It <b>ignores the request body entirely</b> — it always answers
/// with the current date/time. It loops until the request queue is empty (the 5 s GET wait expires →
/// <c>MQRC-NO-MSG-AVAILABLE</c>), taking a CICS <c>SYNCPOINT</c> before each subsequent GET. Any MQ/CICS
/// failure PUTs a diagnostic block to the error queue and terminates. source: CODATE01.cbl:2 (PROGRAM-ID
/// CODATE01 IS INITIAL), :127-169, :339-364.
/// </summary>
/// <remarks>
/// <para><b>Structure.</b> CODATE01 is a "called service" / responder rather than a screen program: there
/// is no <c>EXEC CICS SEND/RECEIVE MAP</c>, no EIBAID/PFKey, no COMMAREA flow (the LINKAGE SECTION is
/// empty). Its online surface is the MQ request→reply envelope, so the port is an <see cref="IMqServer"/>
/// in <c>CardDemo.Mq</c> (per CODATE01.md §1/§5 and MQ_SHIM.md §6.2), driven by the in-proc
/// <see cref="MqBroker"/>. Each COBOL paragraph is one method carrying its original name and a
/// <c>// source: CODATE01.cbl:NNN</c> citation; statement order, the <c>EVALUATE</c>/<c>PERFORM</c>
/// control flow and every faithful bug are preserved verbatim.</para>
///
/// <para><b>No relational table is touched.</b> This program performs <b>zero</b> VSAM/file/DB/IMS
/// access — there are no <c>EXEC CICS READ/WRITE</c>, no <c>EXEC SQL</c>, no <c>EXEC DLI</c> verbs. The
/// only collaborators are the in-proc MQ shim (<see cref="MqBroker"/>) and an injected
/// <see cref="IClock"/> standing in for CICS <c>ASKTIME</c>/<c>FORMATTIME</c>. No repository is wired.
/// The dead <c>LIT-ACCTFILENAME = 'ACCTDAT '</c> WORKING-STORAGE constant is a vestigial copy-paste from
/// the COACCT01 sibling and is never referenced (FB-10). source: CODATE01.cbl:30-45,114-120.</para>
///
/// <para><b>EXEC CICS / MQ mapping.</b> The MQ verbs map to the <see cref="MqBroker"/> shim (MQOPEN →
/// <see cref="MqBroker.Open"/>; MQGET → <see cref="MqBroker.Get"/>; MQPUT → <see cref="MqBroker.Put"/>;
/// MQCLOSE → <see cref="MqBroker.Close"/>). <c>ASKTIME</c>/<c>FORMATTIME</c> map to <see cref="IClock"/>:
/// <c>FORMATTIME MMDDYYYY DATESEP('-')</c> → <c>MM-dd-yyyy</c>, <c>TIME TIMESEP</c> → <c>HH:mm:ss</c>,
/// both off the same instant. <c>SYNCPOINT</c> is the per-message commit boundary (a no-op in-proc). The
/// <c>CARD.DEMO.ERROR</c> PUT of the <c>MQ-ERR-DISPLAY</c> block is sent both to the in-proc error queue
/// and the injected <see cref="IErrorLog"/>. There is no money/decimal arithmetic in this program. source:
/// CODATE01.cbl:343-353, :275-277.</para>
///
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="number">
/// <item>FB-1 — request body read but never used: <c>3000-GET-REQUEST</c> copies the GET buffer into
/// <c>REQUEST-MESSAGE</c> and <c>REQUEST-MSG-COPY</c> (<c>WS-FUNC</c>/<c>WS-KEY</c>/<c>WS-FILLER</c>) but
/// no field is ever inspected — the reply is always the current date/time. source: :318,:322,:339-364.</item>
/// <item>FB-2 — <c>MQMD-REPLYTOQ</c> captured and ignored: the request's ReplyToQ is saved to
/// <c>MQ-QUEUE-REPLY</c>/<c>SAVE-REPLY2Q</c> but the reply PUT always targets the pre-opened literal
/// <c>CARD.DEMO.REPLY.DATE</c> handle (<c>OUTPUT-QUEUE-HANDLE</c>). source: :315,:320,:383-390.</item>
/// <item>FB-3 — wrong handle variable: <c>MQGET</c>/<c>MQPUT</c>/<c>MQCLOSE</c> pass <c>MQ-HCONN</c> (left
/// at its <c>VALUE 0</c> initializer) while <c>MQOPEN</c> passes <c>QMGR-HANDLE-CONN</c> (also 0). Modeled
/// as the single ambient broker connection. source: :182 vs :301-302,:383-384,:461.</item>
/// <item>FB-4 — flag/queue naming inversion: opening the <i>input</i> queue does <c>SET REPLY-QUEUE-OPEN</c>;
/// opening the <i>reply/output</i> queue does <c>SET RESP-QUEUE-OPEN</c>; termination closes the input
/// queue when <c>REPLY-QUEUE-OPEN</c> and the output queue when <c>RESP-QUEUE-OPEN</c>. Internally
/// consistent; preserved verbatim. source: :194,:228,:444-449.</item>
/// <item>FB-5 — the reply <c>STRING</c> emits no separator between the date value and the next label:
/// <c>'SYSTEM DATE : ' + WS-MMDDYYYY + 'SYSTEM TIME : ' + WS-TIME</c> yields
/// <c>...MM-DD-YYYYSYSTEM TIME :...</c> (date value butts against the next label). source: :355-360.</item>
/// <item>FB-6 — RESP2 display move is a no-op self-move in the RETRIEVE error path:
/// <c>MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD</c> (intended <c>-CD-D</c>), so the STRING emits an
/// unpopulated (0) RESP2 display field. source: :150-151.</item>
/// <item>FB-7 — <c>5100-CLOSE-OUTPUT-QUEUE</c> error path names the wrong queue:
/// <c>MOVE INPUT-QUEUE-NAME TO MQ-APPL-QUEUE-NAME</c> (the input, not the reply, queue). source: :496.</item>
/// <item>FB-8 — termination recursion on close failure: 5000/5200-CLOSE-* call 8000-TERMINATION on a close
/// error (5200 also 9000-ERROR), which re-walks the closes (flags unreset); modeled idempotently (a
/// re-entry short-circuits to EXEC CICS RETURN). source: :476,:521-522,:444-452.</item>
/// <item>FB-9 — README vs code payload divergence: the README's structured <c>DATE-RESPONSE-MSG</c> is NOT
/// emitted; the code emits the free-text form above. The code is honored. source: :355-360.</item>
/// <item>FB-10 — dead WORKING-STORAGE: <c>LIT-ACCTFILENAME ('ACCTDAT ')</c>, <c>WS-RESP-CD</c>,
/// <c>WS-REAS-CD</c>, <c>MQ-MSG-COUNT</c> (incremented, never read), <c>SAVE-REPLY2Q</c> (set, never used).
/// No file access or external counter is introduced. source: :114-120,:324,:320.</item>
/// </list>
/// </remarks>
public sealed class SystemDateInquiryService : IMqServer
{
    // =================================================================================================
    //  Injected collaborators (the MQ shim + diagnostic error sink + clock). NO repository — CODATE01
    //  touches no relational table (it only answers with the current date/time). source: CODATE01.cbl
    //  (no file/DB/IMS verbs present).
    // =================================================================================================
    private readonly IErrorLog _errorLog;
    private readonly IClock _clock;

    // The broker for the current Handle() invocation. Under CICS the HCONN comes from the CICS-MQ
    // adapter (FB-3: MQOPEN/MQCLOSE use QMGR-HANDLE-CONN, MQGET/MQPUT use MQ-HCONN — both 0); modeled
    // here as one ambient in-proc connection.
    private MqBroker _mq = null!;

    // The three opened queue handles (HOBJ analogues).
    private MqQueueHandle? _inputHandle;     // INPUT-QUEUE-HANDLE  (request queue, GET)
    private MqQueueHandle? _outputHandle;    // OUTPUT-QUEUE-HANDLE (reply queue, PUT)
    private MqQueueHandle? _errorHandle;     // ERROR-QUEUE-HANDLE  (CARD.DEMO.ERROR, PUT)

    // =================================================================================================
    //  WORKING-STORAGE flags — source: CODATE01.cbl:13-23
    // =================================================================================================
    // WS-MQ-MSG-FLAG X(1) VALUE 'N'; 88 NO-MORE-MSGS VALUE 'Y'. source: :13-14
    private char _wsMqMsgFlag = 'N';

    // FB-4: the open/close flag names are swapped vs. their intuitive meaning but internally consistent.
    //   WS-RESP-QUEUE-STS  88 RESP-QUEUE-OPEN  -> set when the OUTPUT/reply queue opens. source: :16-17
    //   WS-ERR-QUEUE-STS   88 ERR-QUEUE-OPEN   -> set when the ERROR queue opens.        source: :19-20
    //   WS-REPLY-QUEUE-STS 88 REPLY-QUEUE-OPEN -> set when the INPUT/request queue opens. source: :22-23
    private char _wsRespQueueSts = 'N';      // 88 RESP-QUEUE-OPEN  = 'Y'  (output/reply queue)
    private char _wsErrQueueSts = 'N';       // 88 ERR-QUEUE-OPEN   = 'Y'  (error queue)
    private char _wsReplyQueueSts = 'N';     // 88 REPLY-QUEUE-OPEN = 'Y'  (input/request queue)

    // WS-CICS-RESP-CDS — RETRIEVE RESP/RESP2 + their display copies. source: :26-30
    private int _wsCicsResp1Cd;              // S9(8) COMP
    private int _wsCicsResp2Cd;              // S9(8) COMP
    private int _wsCicsResp1CdD;             // 9(8)  (display copy)
    private int _wsCicsResp2CdD;             // 9(8)  (display copy — left 0 by FB-6)

    // =================================================================================================
    //  DATE FIELDS (WS-DATE-TIME) — source: CODATE01.cbl:35-38
    // =================================================================================================
    private long _wsAbsTime;                 // WS-ABS-TIME S9(15) COMP-3 (ASKTIME absolute time)
    private string _wsMmddyyyy = "";         // WS-MMDDYYYY X(10) (FORMATTIME MMDDYYYY DATESEP('-') -> MM-DD-YYYY)
    private string _wsTime = "";             // WS-TIME     X(8)  (FORMATTIME TIME TIMESEP -> HH:MM:SS)

    // =================================================================================================
    //  MQ work fields — source: CODATE01.cbl:42-67
    // =================================================================================================
    private string _mqQueue = "";            // MQ-QUEUE X(48)
    private string _mqQueueReply = "";       // MQ-QUEUE-REPLY X(48)  (REPLYTOQ — captured, FB-2)
    private int _mqConditionCode;            // MQ-CONDITION-CODE (MQCC-*)
    private int _mqReasonCode;               // MQ-REASON-CODE    (MQRC-*)
    private string _mqBuffer = "";           // MQ-BUFFER X(1000)
    private int _mqBufferLength;             // MQ-BUFFER-LENGTH
    private byte[] _mqCorrelId = MqConstants.MqciNone;  // MQ-CORRELID X(24)
    private byte[] _mqMsgId = MqConstants.MqmiNone;     // MQ-MSG-ID   X(24)
    private int _mqMsgCount;                  // MQ-MSG-COUNT 9(9)  (FB-10: dead counter)
    private byte[] _saveCorelid = MqConstants.MqciNone; // SAVE-CORELID X(24)
    private byte[] _saveMsgid = MqConstants.MqmiNone;   // SAVE-MSGID   X(24)
    private string _saveReply2q = "";        // SAVE-REPLY2Q X(48)  (FB-2/FB-10: captured, never used for PUT)

    // MQ-ERR-DISPLAY (the 80-ish byte error block written to CARD.DEMO.ERROR). source: :58-67
    private string _mqErrorPara = "";        // MQ-ERROR-PARA X(25)
    private string _mqApplReturnMessage = "";// MQ-APPL-RETURN-MESSAGE X(25)
    private int _mqApplConditionCode;        // MQ-APPL-CONDITION-CODE 9(2)
    private int _mqApplReasonCode;           // MQ-APPL-REASON-CODE 9(5)
    private string _mqApplQueueName = "";    // MQ-APPL-QUEUE-NAME X(48)

    // =================================================================================================
    //  QUEUE-INFO + message buffers — source: CODATE01.cbl:92-112
    // =================================================================================================
    private string _inputQueueName = "";     // INPUT-QUEUE-NAME  X(48)  (= MQTM-QNAME)
    private string _replyQueueName = "";     // REPLY-QUEUE-NAME  X(48)  (literal CARD.DEMO.REPLY.DATE)
    private string _errorQueueName = "";     // ERROR-QUEUE-NAME  X(48)  (literal CARD.DEMO.ERROR)
    private string _requestMessage = "";     // REQUEST-MESSAGE   X(1000)
    private string _replyMessage = "";       // REPLY-MESSAGE     X(1000)
    private string _errorMessage = "";       // ERROR-MESSAGE     X(1000)

    // REQUEST-MSG-COPY overlay: WS-FUNC X(4) + WS-KEY 9(11) + WS-FILLER X(985). source: :109-112
    // (Parsed but never inspected — FB-1.)
    private string _wsFunc = "";             // WS-FUNC X(4)
    private long _wsKey;                     // WS-KEY  9(11)

    // =================================================================================================
    //  WS-VARIABLES — source: CODATE01.cbl:114-120  (all dead — FB-10)
    // =================================================================================================
    private const string LitAcctFilename = "ACCTDAT ";   // LIT-ACCTFILENAME X(8) (never referenced). :115-116
    private int _wsRespCd;                                // WS-RESP-CD S9(9) COMP (unused). :117-118
    private int _wsReasCd;                                // WS-REAS-CD S9(9) COMP (unused). :119-120

    /// <summary>Signals the COBOL 8000-TERMINATION hard exit (PERFORM 8000-TERMINATION then EXEC CICS
    /// RETURN + GOBACK). Unwinds to <see cref="Handle"/> and ends the transaction.</summary>
    private sealed class TerminationSignal : Exception { }

    // Re-entrancy guard for 8000-TERMINATION (FB-8: a close error re-PERFORMs 8000-TERMINATION; the
    // queue flags are not reset before the re-call, so model termination as idempotent — once entered,
    // a re-entry short-circuits to the EXEC CICS RETURN rather than looping the closes).
    private bool _terminating;

    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Wires the date-inquiry server over the MQ shim and a clock. No relational connection is required —
    /// CODATE01 touches no table. The <paramref name="db"/> overload matches the other VSAM-MQ handlers'
    /// constructor shape (and is accepted/ignored) so the dispatcher can wire all three uniformly.
    /// </summary>
    /// <param name="clock">Clock for ASKTIME/FORMATTIME (defaults to the system clock).</param>
    /// <param name="errorLog">The CARD.DEMO.ERROR diagnostic stand-in (defaults to a discard sink).</param>
    public SystemDateInquiryService(IClock? clock = null, IErrorLog? errorLog = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _errorLog = errorLog ?? NullErrorLog.Instance;
    }

    /// <summary>The request queue this server drains (the dispatcher key — MQ_SHIM.md §6.4).</summary>
    public string RequestQueue => MqQueues.RequestDate;

    // =================================================================================================
    //  1000-CONTROL — source: CODATE01.cbl:127-169
    // =================================================================================================
    /// <summary>
    /// Entry point / driver. Opens the error queue, retrieves the trigger queue name, opens the request
    /// and reply queues, primes the first GET and drains the request queue until NO-MORE-MSGS, then
    /// terminates. A critical failure performs 8000-TERMINATION which raises <see cref="TerminationSignal"/>
    /// and unwinds here (EXEC CICS RETURN). Returns the number of request messages processed.
    /// </summary>
    public int Handle(TriggerMessage trigger, MqBroker mq)
    {
        _mq = mq;
        try
        {
            Control1000(trigger);
        }
        catch (TerminationSignal)
        {
            // 8000-TERMINATION already closed the open queues; EXEC CICS RETURN + GOBACK end the txn.
        }
        return _mqMsgCount;   // FB-10: the COBOL never surfaces MQ-MSG-COUNT; returned here for the shim only.
    }

    private void Control1000(TriggerMessage trigger)
    {
        // MOVE SPACES TO INPUT-QUEUE-NAME, QMGR-NAME, QUEUE-MESSAGE. source: :129-132
        _inputQueueName = "";
        // INITIALIZE MQ-ERR-DISPLAY. source: :134
        InitializeMqErrDisplay();

        // PERFORM 2100-OPEN-ERROR-QUEUE (open CARD.DEMO.ERROR first, so failures can be reported). :136
        OpenErrorQueue2100();

        // EXEC CICS RETRIEVE INTO(MQTM) RESP/RESP2. The shim always supplies the trigger. source: :140-144
        _wsCicsResp1Cd = (int)Resp.Normal;
        _wsCicsResp2Cd = 0;

        if (_wsCicsResp1Cd == (int)Resp.Normal)                 // IF WS-CICS-RESP1-CD = DFHRESP(NORMAL). :145
        {
            _inputQueueName = trigger.QueueName;                // MOVE MQTM-QNAME TO INPUT-QUEUE-NAME. :146
            _replyQueueName = MqQueues.ReplyDate;               // MOVE 'CARD.DEMO.REPLY.DATE' TO ...    . :147
        }
        else
        {
            _mqErrorPara = "CICS RETRIEVE";                     // MOVE 'CICS RETRIEVE' TO MQ-ERROR-PARA. :149
            _wsCicsResp1CdD = _wsCicsResp1Cd;                   // MOVE WS-CICS-RESP1-CD TO ...-CD-D. :150
            // FB-6: self-move (intended -CD-D), so WS-CICS-RESP2-CD-D stays 0. source: :151
            _wsCicsResp2Cd = _wsCicsResp2Cd;
            _mqApplReturnMessage =                              // STRING 'RESP: '... 'END'. source: :152-155
                "RESP: " + Disp8(_wsCicsResp1CdD) + Disp8(_wsCicsResp2CdD) + "END";
            Error9000();                                        // PERFORM 9000-ERROR. source: :157
            Termination8000();                                 // PERFORM 8000-TERMINATION. source: :158
        }

        OpenInputQueue2300();                                  // PERFORM 2300-OPEN-INPUT-QUEUE. source: :161
        OpenOutputQueue2400();                                 // PERFORM 2400-OPEN-OUTPUT-QUEUE. source: :162
        GetRequest3000();                                     // PERFORM 3000-GET-REQUEST (prime). source: :163

        // PERFORM 4000-MAIN-PROCESS UNTIL NO-MORE-MSGS. source: :164-165
        while (!IsNoMoreMsgs())
        {
            MainProcess4000();
        }

        Termination8000();                                    // PERFORM 8000-TERMINATION. source: :167
    }

    // =================================================================================================
    //  2300-OPEN-INPUT-QUEUE — source: CODATE01.cbl:171-202
    // =================================================================================================
    /// <summary>Open the trigger-supplied request queue for GET (MQOO-INPUT-SHARED + SAVE-ALL-CONTEXT +
    /// FAIL-IF-QUIESCING). On success SET REPLY-QUEUE-OPEN (FB-4: the input queue sets the reply flag).</summary>
    private void OpenInputQueue2300()
    {
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE INPUT-QUEUE-NAME TO MQOD-OBJECTNAME. source: :175-176
        // COMPUTE MQ-OPTIONS = MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :178-180
        int options = MqooInputShared | MqooSaveAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... (FB-3: QMGR-HANDLE-CONN here). The in-proc Open
        // always succeeds. source: :182-187
        _inputHandle = _mq.Open(_inputQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :189
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :190
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO INPUT-QUEUE-HANDLE (handle already captured above).
                SetReplyQueueOpen();                          // SET REPLY-QUEUE-OPEN TO TRUE (FB-4). :194
                break;
            default:                                           // WHEN OTHER. source: :195
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "INP MQOPEN ERR";       // source: :199
                Error9000();                                  // PERFORM 9000-ERROR. source: :200
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :201
                break;
        }
    }

    // =================================================================================================
    //  2400-OPEN-OUTPUT-QUEUE — source: CODATE01.cbl:204-236
    // =================================================================================================
    /// <summary>Open CARD.DEMO.REPLY.DATE for PUT (MQOO-OUTPUT + PASS-ALL-CONTEXT + FAIL-IF-QUIESCING).
    /// On success SET RESP-QUEUE-OPEN.</summary>
    private void OpenOutputQueue2400()
    {
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE REPLY-QUEUE-NAME TO MQOD-OBJECTNAME. source: :209-210
        // COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :212-214
        int options = MqooOutput | MqooPassAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... source: :216-221
        _outputHandle = _mq.Open(_replyQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :223
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :224
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO OUTPUT-QUEUE-HANDLE (handle already captured above).
                SetRespQueueOpen();                           // SET RESP-QUEUE-OPEN TO TRUE. :228
                break;
            default:                                           // WHEN OTHER. source: :229
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _replyQueueName;
                _mqApplReturnMessage = "OUT MQOPEN ERR";       // source: :233
                Error9000();                                  // PERFORM 9000-ERROR. source: :234
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :235
                break;
        }
    }

    // =================================================================================================
    //  2100-OPEN-ERROR-QUEUE — source: CODATE01.cbl:238-271
    // =================================================================================================
    /// <summary>Open CARD.DEMO.ERROR for PUT (the error sink). On failure DISPLAY the block and TERMINATE
    /// (no 9000-ERROR here — you cannot write errors when the error queue itself failed to open).</summary>
    private void OpenErrorQueue2100()
    {
        _errorQueueName = MqQueues.Error;                     // MOVE 'CARD.DEMO.ERROR' TO ERROR-QUEUE-NAME. :243
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE ERROR-QUEUE-NAME TO MQOD-OBJECTNAME. source: :244-245
        // COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :247-249
        int options = MqooOutput | MqooPassAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... source: :251-256
        _errorHandle = _mq.Open(_errorQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :258
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :259
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO ERROR-QUEUE-HANDLE (handle already captured above).
                SetErrQueueOpen();                            // SET ERR-QUEUE-OPEN TO TRUE. :263
                break;
            default:                                           // WHEN OTHER. source: :264
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "ERR MQOPEN ERR";       // source: :268
                DisplayMqErrDisplay();                        // DISPLAY MQ-ERR-DISPLAY (not enqueued). :269
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :270
                break;
        }
    }

    // =================================================================================================
    //  4000-MAIN-PROCESS — source: CODATE01.cbl:274-280
    // =================================================================================================
    /// <summary>Per-message loop body (performed UNTIL NO-MORE-MSGS): SYNCPOINT the previous message's
    /// unit of work, then GET the next request.</summary>
    private void MainProcess4000()
    {
        // EXEC CICS SYNCPOINT END-EXEC (commit previous message's UOW). In-proc the PUT is already
        // committed on the shared connection; this is the per-message boundary. source: :275-277
        GetRequest3000();                                    // PERFORM 3000-GET-REQUEST. source: :279
    }

    // =================================================================================================
    //  3000-GET-REQUEST — source: CODATE01.cbl:283-337
    // =================================================================================================
    /// <summary>GET one request message (5 s wait, any next msg) and dispatch it; on the empty queue set
    /// NO-MORE-MSGS, on any other GET failure write the error and terminate. The request body is captured
    /// but never inspected (FB-1).</summary>
    private void GetRequest3000()
    {
        // MOVE 5000 TO MQGMO-WAITINTERVAL; MOVE SPACES TO MQ-CORRELID/MQ-MSG-ID; MOVE INPUT-QUEUE-NAME
        // TO MQ-QUEUE; MOVE INPUT-QUEUE-HANDLE TO MQ-HOBJ; MOVE 1000 TO MQ-BUFFER-LENGTH. source: :286-291
        _mqCorrelId = MqConstants.MqciNone;
        _mqMsgId = MqConstants.MqmiNone;
        _mqQueue = _inputQueueName;
        _mqBufferLength = 1000;

        // MOVE MQMI-NONE TO MQMD-MSGID; MOVE MQCI-NONE TO MQMD-CORRELID (take any next msg). source: :292-293
        // INITIALIZE REQUEST-MSG-COPY REPLACING NUMERIC BY ZEROES -> WS-FUNC spaces, WS-KEY 0. source: :294
        _wsFunc = "";
        _wsKey = 0;

        // COMPUTE MQGMO-OPTIONS = MQGMO-SYNCPOINT + FAIL-IF-QUIESCING + CONVERT + WAIT. source: :296-299
        // CALL 'MQGET' USING MQ-HCONN ... (FB-3: MQ-HCONN, not QMGR-HANDLE-CONN). source: :301-309
        MqResult r = _mq.Get(_inputHandle!, out MqMessage? msg);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        if (_mqConditionCode == MqConstants.MqccOk && msg is not null)   // IF MQ-CONDITION-CODE = MQCC-OK. :312
        {
            _mqMsgId = msg.MsgId;                             // MOVE MQMD-MSGID TO MQ-MSG-ID. source: :313
            _mqCorrelId = msg.CorrelId;                       // MOVE MQMD-CORRELID TO MQ-CORRELID. :314
            _mqQueueReply = msg.ReplyToQueue;                 // MOVE MQMD-REPLYTOQ TO MQ-QUEUE-REPLY (FB-2). :315
            _mqApplConditionCode = _mqConditionCode;          // source: :316
            _mqApplReasonCode = _mqReasonCode;                // source: :317
            _requestMessage = Fixed(msg.Body, 1000);          // MOVE MQ-BUFFER TO REQUEST-MESSAGE. source: :318
            _saveCorelid = _mqCorrelId;                       // MOVE MQ-CORRELID TO SAVE-CORELID. :319
            _saveReply2q = _mqQueueReply;                     // MOVE MQ-QUEUE-REPLY TO SAVE-REPLY2Q (FB-2). :320
            _saveMsgid = _mqMsgId;                            // MOVE MQ-MSG-ID TO SAVE-MSGID. :321

            // MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY (overlay X(1000) onto FUNC/KEY/FILLER). FB-1:
            // parsed, then never inspected. source: :322
            OverlayRequestMsgCopy(_requestMessage);

            ProcessRequestReply4000();                        // PERFORM 4000-PROCESS-REQUEST-REPLY. :323
            _mqMsgCount++;                                    // ADD 1 TO MQ-MSG-COUNT (FB-10: dead). :324
        }
        else                                                  // ELSE. source: :325
        {
            if (_mqReasonCode == MqConstants.MqrcNoMsgAvailable)   // IF MQ-REASON-CODE = MQRC-NO-MSG-AVAILABLE. :326
            {
                SetNoMoreMsgs();                              // SET NO-MORE-MSGS TO TRUE. source: :327
            }
            else                                              // ELSE. source: :329
            {
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "INP MQGET ERR:";      // source: :333
                Error9000();                                  // PERFORM 9000-ERROR. source: :334
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :335
            }
        }
    }

    // =================================================================================================
    //  4000-PROCESS-REQUEST-REPLY — source: CODATE01.cbl:339-364  — the only business logic
    // =================================================================================================
    /// <summary>Build the date/time reply: ASKTIME the current absolute time, FORMATTIME it into
    /// MM-DD-YYYY / HH:MM:SS, STRING the free-text reply (FB-5: no separator between the date value and the
    /// next label), then PUT it. The request content is ignored — the answer is always "now".</summary>
    private void ProcessRequestReply4000()
    {
        _replyMessage = "";                                  // MOVE SPACES TO REPLY-MESSAGE. source: :340
        // INITIALIZE WS-DATE-TIME REPLACING NUMERIC BY ZEROES -> WS-ABS-TIME 0, WS-MMDDYYYY/WS-TIME spaces.
        // source: :341
        _wsAbsTime = 0;
        _wsMmddyyyy = "";
        _wsTime = "";

        // EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME). The IClock supplies "now"; ABSTIME is the CICS absolute
        // time (ms since 1900-01-01 00:00:00). source: :343-345
        DateTime now = _clock.Now;
        _wsAbsTime = CicsAbsTime(now);

        // EXEC CICS FORMATTIME ABSTIME(WS-ABS-TIME) MMDDYYYY(WS-MMDDYYYY) DATESEP('-') TIME(WS-TIME) TIMESEP.
        // -> WS-MMDDYYYY = MM-DD-YYYY (month-day-year, hyphen sep); WS-TIME = HH:MM:SS (colon sep). :347-353
        _wsMmddyyyy = now.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);   // X(10)
        _wsTime = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);         // X(8)

        // STRING 'SYSTEM DATE : ' WS-MMDDYYYY 'SYSTEM TIME : ' WS-TIME DELIMITED BY SIZE INTO REPLY-MESSAGE.
        // FB-5: no delimiter between the date VALUE and the next LABEL, so the date butts directly against
        // 'SYSTEM TIME : '. The X(10)/X(8) fields contribute their full fixed width. source: :355-360
        _replyMessage = "SYSTEM DATE : " + Fixed(_wsMmddyyyy, 10)
                      + "SYSTEM TIME : " + Fixed(_wsTime, 8);

        PutReply4100();                                      // PERFORM 4100-PUT-REPLY. source: :361
    }

    // =================================================================================================
    //  4100-PUT-REPLY — source: CODATE01.cbl:366-403
    // =================================================================================================
    /// <summary>PUT the built reply to CARD.DEMO.REPLY.DATE (the pre-opened OUTPUT-QUEUE-HANDLE — FB-2:
    /// never the request's ReplyToQ), echoing the saved MsgId/CorrelId.</summary>
    private void PutReply4100()
    {
        _mqBuffer = Fixed(_replyMessage, 1000);              // MOVE REPLY-MESSAGE TO MQ-BUFFER. source: :371
        _mqBufferLength = 1000;                              // MOVE 1000 TO MQ-BUFFER-LENGTH. source: :372

        var reply = new MqMessage
        {
            Body = _mqBuffer,
            MsgId = _saveMsgid,                              // MOVE SAVE-MSGID TO MQMD-MSGID. source: :373
            CorrelId = _saveCorelid,                         // MOVE SAVE-CORELID TO MQMD-CORRELID. :374
            Format = MqConstants.MqfmtString,                // MOVE MQFMT-STRING TO MQMD-FORMAT. :375
            CodedCharSetId = MqConstants.MqccsiQMgr,         // COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR. :377
        };

        // COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING. :379-381
        // CALL 'MQPUT' USING MQ-HCONN OUTPUT-QUEUE-HANDLE ... (FB-3/FB-2). source: :383-390
        MqResult r = _mq.Put(_outputHandle!, reply);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :392
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :393
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :396
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _replyQueueName;
                _mqApplReturnMessage = "MQPUT ERR";          // source: :400
                Error9000();                                 // PERFORM 9000-ERROR. source: :401
                Termination8000();                           // PERFORM 8000-TERMINATION. source: :402
                break;
        }
    }

    // =================================================================================================
    //  9000-ERROR — source: CODATE01.cbl:405-441
    // =================================================================================================
    /// <summary>PUT the MQ-ERR-DISPLAY block to CARD.DEMO.ERROR. On a PUT failure DISPLAY the block and
    /// terminate (no recursion into 9000-ERROR).</summary>
    private void Error9000()
    {
        _errorMessage = FormatMqErrDisplay();                // MOVE MQ-ERR-DISPLAY TO ERROR-MESSAGE. source: :409
        _mqBuffer = Fixed(_errorMessage, 1000);              // MOVE ERROR-MESSAGE TO MQ-BUFFER. source: :410
        _mqBufferLength = 1000;                              // MOVE 1000 TO MQ-BUFFER-LENGTH. source: :411

        _errorLog.Write(_errorMessage);                      // diagnostic sink (MQ_SHIM.md §5.4)

        var err = new MqMessage
        {
            Body = _mqBuffer,
            Format = MqConstants.MqfmtString,                // MOVE MQFMT-STRING TO MQMD-FORMAT. source: :412
            CodedCharSetId = MqConstants.MqccsiQMgr,         // COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR. :414
        };

        // COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING. :416-418
        // CALL 'MQPUT' USING MQ-HCONN ERROR-QUEUE-HANDLE ... source: :420-427
        MqResult r = _errorHandle is null ? MqResult.Success : _mq.Put(_errorHandle, err);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :429
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :430
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :433
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "MQPUT ERR";          // source: :437
                DisplayMqErrDisplay();                       // DISPLAY MQ-ERR-DISPLAY. source: :438
                Termination8000();                           // PERFORM 8000-TERMINATION (no 9000 recursion). :439
                break;
        }
    }

    // =================================================================================================
    //  8000-TERMINATION — source: CODATE01.cbl:442-454
    // =================================================================================================
    /// <summary>Close the open queues (FB-4 flag/queue mapping), EXEC CICS RETURN + GOBACK. FB-8: a close
    /// error re-PERFORMs 8000-TERMINATION; modeled idempotently — a re-entry short-circuits to the RETURN.</summary>
    private void Termination8000()
    {
        if (_terminating)
        {
            // FB-8: re-entry from a close-error PERFORM 8000-TERMINATION. The COBOL would re-walk the
            // closes (flags unreset); model termination as idempotent and go straight to EXEC CICS RETURN.
            throw new TerminationSignal();
        }
        _terminating = true;

        if (IsReplyQueueOpen())                              // IF REPLY-QUEUE-OPEN. source: :444
        {
            CloseInputQueue5000();                           // PERFORM 5000-CLOSE-INPUT-QUEUE (FB-4). :445
        }
        if (IsRespQueueOpen())                               // IF RESP-QUEUE-OPEN. source: :447
        {
            CloseOutputQueue5100();                          // PERFORM 5100-CLOSE-OUTPUT-QUEUE. source: :448
        }
        if (IsErrQueueOpen())                                // IF ERR-QUEUE-OPEN. source: :450
        {
            CloseErrorQueue5200();                           // PERFORM 5200-CLOSE-ERROR-QUEUE. source: :451
        }

        // EXEC CICS RETURN END-EXEC; GOBACK. source: :453-454
        throw new TerminationSignal();
    }

    // =================================================================================================
    //  5000-CLOSE-INPUT-QUEUE — source: CODATE01.cbl:456-477
    // =================================================================================================
    private void CloseInputQueue5000()
    {
        _mqQueue = _inputQueueName;                          // MOVE INPUT-QUEUE-NAME TO MQ-QUEUE. source: :457
        // MOVE INPUT-QUEUE-HANDLE TO MQ-HOBJ; COMPUTE MQ-OPTIONS = MQCO-NONE. source: :458-459
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :461-465
        MqResult r = _inputHandle is null ? MqResult.Success : _mq.Close(_inputHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :467
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :468
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :471
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :475
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-8). source: :476
                break;
        }
    }

    // =================================================================================================
    //  5100-CLOSE-OUTPUT-QUEUE — source: CODATE01.cbl:478-499
    // =================================================================================================
    private void CloseOutputQueue5100()
    {
        _mqQueue = _replyQueueName;                          // MOVE REPLY-QUEUE-NAME TO MQ-QUEUE. source: :479
        // MOVE OUTPUT-QUEUE-HANDLE TO MQ-HOBJ; COMPUTE MQ-OPTIONS = MQCO-NONE. source: :480-481
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :483-487
        MqResult r = _outputHandle is null ? MqResult.Success : _mq.Close(_outputHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :489
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :490
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :493
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // FB-7: the output/reply close error names INPUT-QUEUE-NAME (sic — not REPLY-QUEUE-NAME).
                _mqApplQueueName = _inputQueueName;          // source: :496
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :497
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-8). source: :498
                break;
        }
    }

    // =================================================================================================
    //  5200-CLOSE-ERROR-QUEUE — source: CODATE01.cbl:501-523
    // =================================================================================================
    private void CloseErrorQueue5200()
    {
        _mqQueue = _errorQueueName;                          // MOVE ERROR-QUEUE-NAME TO MQ-QUEUE. source: :502
        // MOVE ERROR-QUEUE-HANDLE TO MQ-HOBJ; COMPUTE MQ-OPTIONS = MQCO-NONE. source: :503-504
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :506-510
        MqResult r = _errorHandle is null ? MqResult.Success : _mq.Close(_errorHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :512
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :513
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :516
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :520
                Error9000();                                 // PERFORM 9000-ERROR (FB-8). source: :521
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-8). source: :522
                break;
        }
    }

    // =================================================================================================
    //  REQUEST-MSG-COPY overlay — source: CODATE01.cbl:109-112,322  (FB-1: parsed, never inspected)
    // =================================================================================================
    /// <summary>
    /// MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY: re-overlays the X(1000) buffer onto WS-FUNC X(4),
    /// WS-KEY 9(11), WS-FILLER X(985). WS-FUNC is the first 4 chars; WS-KEY is the next 11 chars parsed
    /// as an unsigned 11-digit zoned numeric. Neither field is ever read (FB-1) — the overlay is a faithful
    /// no-op effect.
    /// </summary>
    private void OverlayRequestMsgCopy(string buffer)
    {
        string b = Fixed(buffer, 1000);
        _wsFunc = b.Substring(0, 4);                         // WS-FUNC X(4)
        _wsKey = ParseKey11(b.Substring(4, 11));             // WS-KEY 9(11)
    }

    // =================================================================================================
    //  MQ-ERR-DISPLAY helpers — source: CODATE01.cbl:58-67,134,409,438
    // =================================================================================================
    private void InitializeMqErrDisplay()
    {
        _mqErrorPara = "";
        _mqApplReturnMessage = "";
        _mqApplConditionCode = 0;
        _mqApplReasonCode = 0;
        _mqApplQueueName = "";
    }

    /// <summary>Serializes MQ-ERR-DISPLAY to its fixed-offset byte image (X(25)+SP(2)+X(25)+SP(2)+9(2)+
    /// SP(2)+9(5)+SP(2)+X(48)). source: :58-67.</summary>
    private string FormatMqErrDisplay()
    {
        var sb = new StringBuilder(115);
        sb.Append(Fixed(_mqErrorPara, 25));                  // MQ-ERROR-PARA X(25)
        sb.Append("  ");                                     // FILLER X(2)
        sb.Append(Fixed(_mqApplReturnMessage, 25));          // MQ-APPL-RETURN-MESSAGE X(25)
        sb.Append("  ");                                     // FILLER X(2)
        sb.Append(Pic9(_mqApplConditionCode, 2));            // MQ-APPL-CONDITION-CODE 9(2)
        sb.Append("  ");                                     // FILLER X(2)
        sb.Append(Pic9(_mqApplReasonCode, 5));               // MQ-APPL-REASON-CODE 9(5)
        sb.Append("  ");                                     // FILLER X(2)
        sb.Append(Fixed(_mqApplQueueName, 48));              // MQ-APPL-QUEUE-NAME X(48)
        return sb.ToString();
    }

    private void DisplayMqErrDisplay() => _errorLog.Write(FormatMqErrDisplay());

    // =================================================================================================
    //  88-level flag setters / testers (FB-4 naming preserved).
    // =================================================================================================
    private void SetNoMoreMsgs() => _wsMqMsgFlag = 'Y';
    private bool IsNoMoreMsgs() => _wsMqMsgFlag == 'Y';

    private void SetRespQueueOpen() => _wsRespQueueSts = 'Y';   // output/reply queue (FB-4)
    private bool IsRespQueueOpen() => _wsRespQueueSts == 'Y';

    private void SetErrQueueOpen() => _wsErrQueueSts = 'Y';
    private bool IsErrQueueOpen() => _wsErrQueueSts == 'Y';

    private void SetReplyQueueOpen() => _wsReplyQueueSts = 'Y'; // input/request queue (FB-4)
    private bool IsReplyQueueOpen() => _wsReplyQueueSts == 'Y';

    // =================================================================================================
    //  Time + string formatting helpers.
    // =================================================================================================

    /// <summary>
    /// CICS <c>ASKTIME ABSTIME</c>: the absolute time in milliseconds since 1900-01-01 00:00:00 (the CICS
    /// epoch), as the <c>WS-ABS-TIME S9(15) COMP-3</c> field carries it. Computed from the injected clock.
    /// (Only the formatted MM-DD-YYYY / HH:MM:SS values reach the reply; this raw value is internal.)
    /// </summary>
    private static long CicsAbsTime(DateTime now)
    {
        var epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (long)(now - epoch).TotalMilliseconds;
    }

    /// <summary>WS-CICS-RESP1/2-CD-D 9(8) display image used in the RETRIEVE error STRING.</summary>
    private static string Disp8(int value) => Pic9(value, 8);

    /// <summary>An unsigned PIC 9(width) zoned image: <paramref name="width"/> zero-padded digit bytes,
    /// low-order on silent overflow, no sign character.</summary>
    private static string Pic9(long value, int width)
    {
        long modulus = (long)Decimals.Pow10(width);
        long mag = ((value % modulus) + modulus) % modulus;
        return mag.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    private static string Pic9(int value, int width) => Pic9((long)value, width);

    /// <summary>
    /// Parses the 11-byte WS-KEY 9(11) view of the buffer: an unsigned zoned numeric. Non-digit bytes are
    /// de-edited (keeping the digit characters, low-order 11). Spaces/zeros → 0. (FB-1: the result is never
    /// read.)
    /// </summary>
    private static long ParseKey11(string raw)
    {
        var sb = new StringBuilder(11);
        foreach (char c in raw) if (c is >= '0' and <= '9') sb.Append(c);
        if (sb.Length == 0) return 0;
        string digits = sb.ToString();
        if (digits.Length > 11) digits = digits.Substring(digits.Length - 11);
        return long.Parse(digits, CultureInfo.InvariantCulture);
    }

    /// <summary>Right-pads/truncates a value to a fixed COBOL X(width) field (spaces on the right).</summary>
    private static string Fixed(string? v, int width)
    {
        v ??= "";
        return v.Length >= width ? v.Substring(0, width) : v.PadRight(width, ' ');
    }

    // =================================================================================================
    //  MQOO-* open-option flags (CMQV) referenced by the MQOPEN COMPUTEs (metadata only). source: §4.
    // =================================================================================================
    private const int MqooInputShared = 0x00000002;       // MQOO-INPUT-SHARED
    private const int MqooOutput = 0x00000010;            // MQOO-OUTPUT
    private const int MqooPassAllContext = 0x00000200;    // MQOO-PASS-ALL-CONTEXT
    private const int MqooSaveAllContext = 0x00000800;    // MQOO-SAVE-ALL-CONTEXT
    private const int MqooFailIfQuiescing = 0x00002000;   // MQOO-FAIL-IF-QUIESCING
}
