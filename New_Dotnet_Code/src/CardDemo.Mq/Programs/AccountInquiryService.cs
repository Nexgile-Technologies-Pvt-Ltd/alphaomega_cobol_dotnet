using System.Globalization;
using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Mq.Programs;

/// <summary>
/// Faithful .NET port of the optional VSAM/MQ COBOL program <c>COACCT01</c> — the CardDemo
/// <b>Account-Inquiry service</b> (CICS transaction <c>CDRA</c>, MQ-triggered server, no BMS screen).
/// It is started by the CICS-MQ trigger monitor, learns its request queue from the MQ Trigger Message
/// (<c>MQTM</c>) via <c>EXEC CICS RETRIEVE</c>, opens an error queue (<c>CARD.DEMO.ERROR</c>), the
/// trigger-supplied request queue (for GET) and the hard-coded reply queue (<c>CARD.DEMO.REPLY.ACCT</c>,
/// for PUT), then <b>drains</b> the request queue: for each request it expects function <c>INQA</c> + an
/// 11-digit account id, performs a keyed READ of the <c>ACCTDAT</c> VSAM file (the relational
/// <b>ACCOUNT</b> table), formats a labeled fixed-width text snapshot of the account into the reply, and
/// PUTs it to the reply queue. Invalid function/key and not-found requests get an
/// <c>INVALID REQUEST PARAMETERS ...</c> text reply; any other read error is written to the MQ error
/// queue and the program terminates. source: COACCT01.cbl:2 (PROGRAM-ID COACCT01 IS INITIAL), :178-220.
/// </summary>
/// <remarks>
/// <para><b>Structure.</b> COACCT01 is a "called service" / responder rather than a screen program:
/// there is no <c>EXEC CICS SEND/RECEIVE MAP</c>, no EIBAID/PFKey and no COMMAREA flow. Its online
/// surface is the MQ request→reply envelope, so the port is an <see cref="IMqServer"/> in
/// <c>CardDemo.Mq</c> (per COACCT01.md §1 and MQ_SHIM.md §6.2), driven by the in-proc
/// <see cref="MqBroker"/>. Each COBOL paragraph is one method carrying its original name and a
/// <c>// source: COACCT01.cbl:NNN</c> citation; statement order, the <c>EVALUATE</c>/<c>PERFORM</c>
/// control flow and every faithful bug are preserved verbatim.</para>
///
/// <para><b>EXEC SQL / EXEC CICS / MQ mapping.</b> The single <c>EXEC CICS READ DATASET(ACCTDAT)</c>
/// becomes <see cref="AccountRepository.ReadByKey(long, out Account?)"/> returning the two-char
/// <see cref="FileStatus"/> the COBOL <c>EVALUATE WS-RESP-CD</c> branches on (Ok '00' → DFHRESP(NORMAL);
/// RecordNotFound '23' → DFHRESP(NOTFND); anything else → the WHEN OTHER abend path). The MQ verbs map to
/// the <see cref="MqBroker"/> shim (MQOPEN → <see cref="MqBroker.Open"/>; MQGET → <see cref="MqBroker.Get"/>;
/// MQPUT → <see cref="MqBroker.Put"/>; MQCLOSE → <see cref="MqBroker.Close"/>). The <c>CARD.DEMO.ERROR</c>
/// PUT of the <c>MQ-ERR-DISPLAY</c> block is sent both to the in-proc error queue and the injected
/// <see cref="IErrorLog"/>. Money is COBOL fixed-point: the reply numeric fields are non-edited DISPLAY,
/// re-serialized as zoned decimal (12 raw digit bytes, embedded sign on the last byte) — never as a
/// decimal string. source: COACCT01.cbl:140-166, :390-460.</para>
///
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="number">
/// <item>FB-1 — open/close flag-name vs queue swap: opening the <i>input</i> (request) queue does
/// <c>SET REPLY-QUEUE-OPEN</c>; opening the <i>output</i> (reply) queue does <c>SET RESP-QUEUE-OPEN</c>;
/// at termination <c>IF REPLY-QUEUE-OPEN PERFORM 5000-CLOSE-INPUT-QUEUE</c>. Internally consistent;
/// preserved verbatim. source: :245,:279,:540-545.</item>
/// <item>FB-2 — MQGET/MQPUT use <c>MQ-HCONN</c> while MQOPEN/MQCLOSE use <c>QMGR-HANDLE-CONN</c>; both 0.
/// Modeled as the single ambient broker connection. source: :233,:352,:479.</item>
/// <item>FB-3 — no-op self-move <c>MOVE WS-CICS-RESP2-CD TO WS-CICS-RESP2-CD</c> (intended <c>-CD-D</c>),
/// so the RETRIEVE error string carries an unpopulated (0) RESP2 display field. source: :201-203.</item>
/// <item>FB-4 — <c>ADD 1 TO MQ-MSG-COUNT</c> is a dead counter, never read out. source: :375.</item>
/// <item>FB-5 — <c>MQMD-REPLYTOQ</c> is captured into <c>MQ-QUEUE-REPLY</c>/<c>SAVE-REPLY2Q</c> but the
/// PUT always targets the pre-opened literal <c>CARD.DEMO.REPLY.ACCT</c>. source: :366,:371,:480.</item>
/// <item>FB-6 — termination recursion on close error: 5000/5100-CLOSE-* call 8000-TERMINATION on a close
/// failure, which can re-enter the same close; modeled idempotently (a closed queue does not loop).
/// source: :572,:594,:540-548.</item>
/// <item>FB-7 — <c>'CICS RETREIVE'</c> misspelling in the RETRIEVE error label. source: :200.</item>
/// <item>FB-8 — <c>ACCT-ADDR-ZIP</c> is read into the record but never moved into the reply. source: :407-425.</item>
/// <item>FB-9 — README documents an <c>'ACCT'</c> request/response layout; the code uses
/// <c>WS-FUNC = 'INQA'</c> + 11-digit key and a free-text labeled reply. The code is honored. source: :393.</item>
/// </list>
/// </remarks>
public sealed class AccountInquiryService : IMqServer
{
    // =================================================================================================
    //  Injected collaborators (the relational data layer + MQ shim + diagnostic error sink).
    // =================================================================================================
    private readonly AccountRepository _accounts;
    private readonly IErrorLog _errorLog;

    // The broker for the current Handle() invocation. Under CICS the HCONN comes from the CICS-MQ
    // adapter (FB-2: MQOPEN/MQCLOSE use QMGR-HANDLE-CONN, MQGET/MQPUT use MQ-HCONN — both 0); modeled
    // here as one ambient in-proc connection.
    private MqBroker _mq = null!;

    // The three opened queue handles (HOBJ analogues).
    private MqQueueHandle? _inputHandle;     // INPUT-QUEUE-HANDLE  (request queue, GET)
    private MqQueueHandle? _outputHandle;    // OUTPUT-QUEUE-HANDLE (reply queue, PUT)
    private MqQueueHandle? _errorHandle;     // ERROR-QUEUE-HANDLE  (CARD.DEMO.ERROR, PUT)

    // =================================================================================================
    //  WORKING-STORAGE flags — source: COACCT01.cbl:13-23
    // =================================================================================================
    // WS-MQ-MSG-FLAG X(1) VALUE 'N'; 88 NO-MORE-MSGS VALUE 'Y'. source: :13-14
    private char _wsMqMsgFlag = 'N';

    // FB-1: the open/close flag names are swapped vs. their intuitive meaning but internally consistent.
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
    private int _wsCicsResp2CdD;             // 9(8)  (display copy — left 0 by FB-3)

    // =================================================================================================
    //  MQ work fields — source: COACCT01.cbl:42-67
    // =================================================================================================
    private string _mqQueue = "";            // MQ-QUEUE X(48)
    private string _mqQueueReply = "";       // MQ-QUEUE-REPLY X(48)  (REPLYTOQ — captured, FB-5)
    private int _mqConditionCode;            // MQ-CONDITION-CODE (MQCC-*)
    private int _mqReasonCode;               // MQ-REASON-CODE    (MQRC-*)
    private string _mqBuffer = "";           // MQ-BUFFER X(1000)
    private int _mqBufferLength;             // MQ-BUFFER-LENGTH
    private byte[] _mqCorrelId = MqConstants.MqciNone;  // MQ-CORRELID X(24)
    private byte[] _mqMsgId = MqConstants.MqmiNone;     // MQ-MSG-ID   X(24)
    private int _mqMsgCount;                  // MQ-MSG-COUNT 9(9)  (FB-4: dead counter)
    private byte[] _saveCorelid = MqConstants.MqciNone; // SAVE-CORELID X(24)
    private byte[] _saveMsgid = MqConstants.MqmiNone;   // SAVE-MSGID   X(24)
    private string _saveReply2q = "";        // SAVE-REPLY2Q X(48)  (FB-5: captured, never used for PUT)

    // MQ-ERR-DISPLAY (the 80-ish byte error block written to CARD.DEMO.ERROR). source: :58-67
    private string _mqErrorPara = "";        // MQ-ERROR-PARA X(25)
    private string _mqApplReturnMessage = "";// MQ-APPL-RETURN-MESSAGE X(25)
    private int _mqApplConditionCode;        // MQ-APPL-CONDITION-CODE 9(2)
    private int _mqApplReasonCode;           // MQ-APPL-REASON-CODE 9(5)
    private string _mqApplQueueName = "";    // MQ-APPL-QUEUE-NAME X(48)

    // =================================================================================================
    //  QUEUE-INFO + message buffers — source: COACCT01.cbl:92-112
    // =================================================================================================
    private string _inputQueueName = "";     // INPUT-QUEUE-NAME  X(48)  (= MQTM-QNAME)
    private string _replyQueueName = "";     // REPLY-QUEUE-NAME  X(48)  (literal CARD.DEMO.REPLY.ACCT)
    private string _errorQueueName = "";     // ERROR-QUEUE-NAME  X(48)  (literal CARD.DEMO.ERROR)
    private string _requestMessage = "";     // REQUEST-MESSAGE   X(1000)
    private string _replyMessage = "";       // REPLY-MESSAGE     X(1000)
    private string _errorMessage = "";       // ERROR-MESSAGE     X(1000)

    // REQUEST-MSG-COPY overlay: WS-FUNC X(4) + WS-KEY 9(11) + WS-FILLER X(985). source: :109-112
    private string _wsFunc = "";             // WS-FUNC X(4)
    private long _wsKey;                     // WS-KEY  9(11)

    // =================================================================================================
    //  WS-VARIABLES — source: COACCT01.cbl:114-128
    // =================================================================================================
    private const string LitAcctFilename = "ACCTDAT ";   // LIT-ACCTFILENAME X(8) (trailing space). :115-116
    private int _wsRespCd;                                // WS-RESP-CD S9(9) COMP (CICS READ RESP)
    private int _wsReasCd;                                // WS-REAS-CD S9(9) COMP (CICS READ RESP2)
    private long _wsCardRidAcctId;                        // WS-CARD-RID-ACCT-ID 9(11) (RIDFLD numeric). :126

    // ACCOUNT-RECORD (COPY CVACT01Y) — the READ target. source: :171
    private Account? _accountRecord;

    /// <summary>Signals the COBOL 8000-TERMINATION hard exit (PERFORM 8000-TERMINATION then EXEC CICS
    /// RETURN + GOBACK). Unwinds to <see cref="Handle"/> and ends the transaction.</summary>
    private sealed class TerminationSignal : Exception { }

    // Re-entrancy guard for 8000-TERMINATION (FB-6: a close error re-PERFORMs 8000-TERMINATION; the
    // queue flags are not reset before the re-call, so model termination as idempotent — once entered,
    // a re-entry short-circuits to the EXEC CICS RETURN rather than looping the closes).
    private bool _terminating;

    // -------------------------------------------------------------------------------------------------

    /// <summary>Wires the account-inquiry server over the relational DB and a diagnostic error sink.</summary>
    /// <param name="db">The relational connection backing the VSAM→SQL ACCOUNT read.</param>
    /// <param name="errorLog">The CARD.DEMO.ERROR diagnostic stand-in (defaults to a discard sink).</param>
    public AccountInquiryService(RelationalDb db, IErrorLog? errorLog = null)
    {
        _accounts = new AccountRepository(db);
        _errorLog = errorLog ?? NullErrorLog.Instance;
    }

    /// <summary>The request queue this server drains (the dispatcher key — MQ_SHIM.md §6.4).</summary>
    public string RequestQueue => MqQueues.RequestAcct;

    // =================================================================================================
    //  1000-CONTROL — source: COACCT01.cbl:178-220
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
        return _mqMsgCount;   // FB-4: the COBOL never surfaces MQ-MSG-COUNT; returned here for the shim only.
    }

    private void Control1000(TriggerMessage trigger)
    {
        // MOVE SPACES TO INPUT-QUEUE-NAME, QMGR-NAME, QUEUE-MESSAGE. source: :180-183
        _inputQueueName = "";
        // INITIALIZE MQ-ERR-DISPLAY. source: :185
        InitializeMqErrDisplay();

        // PERFORM 2100-OPEN-ERROR-QUEUE (open CARD.DEMO.ERROR first). source: :187
        OpenErrorQueue2100();

        // EXEC CICS RETRIEVE INTO(MQTM) RESP/RESP2. The shim always supplies the trigger. source: :191-195
        _wsCicsResp1Cd = (int)Resp.Normal;
        _wsCicsResp2Cd = 0;

        if (_wsCicsResp1Cd == (int)Resp.Normal)                 // IF WS-CICS-RESP1-CD = DFHRESP(NORMAL). :196
        {
            _inputQueueName = trigger.QueueName;                // MOVE MQTM-QNAME TO INPUT-QUEUE-NAME. :197
            _replyQueueName = MqQueues.ReplyAcct;               // MOVE 'CARD.DEMO.REPLY.ACCT' TO ...   . :198
        }
        else
        {
            // FB-7: 'CICS RETREIVE' (sic) error label. source: :200
            _mqErrorPara = "CICS RETREIVE";
            _wsCicsResp1CdD = _wsCicsResp1Cd;                   // MOVE WS-CICS-RESP1-CD TO ...-CD-D. :201
            // FB-3: self-move (intended -CD-D), so WS-CICS-RESP2-CD-D stays 0. source: :202
            _wsCicsResp2Cd = _wsCicsResp2Cd;
            _mqApplReturnMessage =                              // STRING 'RESP: '... 'END'. source: :203-206
                "RESP: " + Disp8(_wsCicsResp1CdD) + Disp8(_wsCicsResp2CdD) + "END";
            Error9000();                                        // PERFORM 9000-ERROR. source: :208
            Termination8000();                                 // PERFORM 8000-TERMINATION. source: :209
        }

        OpenInputQueue2300();                                  // PERFORM 2300-OPEN-INPUT-QUEUE. source: :212
        OpenOutputQueue2400();                                 // PERFORM 2400-OPEN-OUTPUT-QUEUE. source: :213
        GetRequest3000();                                     // PERFORM 3000-GET-REQUEST (prime). source: :214

        // PERFORM 4000-MAIN-PROCESS UNTIL NO-MORE-MSGS. source: :215-216
        while (!IsNoMoreMsgs())
        {
            MainProcess4000();
        }

        Termination8000();                                    // PERFORM 8000-TERMINATION. source: :218
    }

    // =================================================================================================
    //  2300-OPEN-INPUT-QUEUE — source: COACCT01.cbl:222-253
    // =================================================================================================
    /// <summary>Open the trigger-supplied request queue for GET (MQOO-INPUT-SHARED + SAVE-ALL-CONTEXT
    /// + FAIL-IF-QUIESCING). On success SET REPLY-QUEUE-OPEN (FB-1: the input queue sets the reply flag).</summary>
    private void OpenInputQueue2300()
    {
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE INPUT-QUEUE-NAME TO MQOD-OBJECTNAME. source: :226-227
        // COMPUTE MQ-OPTIONS = MQOO-INPUT-SHARED + MQOO-SAVE-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :229-231
        int options = MqooInputShared | MqooSaveAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... (FB-2: QMGR-HANDLE-CONN here). The in-proc Open
        // always succeeds. source: :233-238
        _inputHandle = _mq.Open(_inputQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :240
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :241
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO INPUT-QUEUE-HANDLE (handle already captured above).
                SetReplyQueueOpen();                          // SET REPLY-QUEUE-OPEN TO TRUE (FB-1). :245
                break;
            default:                                           // WHEN OTHER. source: :246
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "INP MQOPEN ERR";       // source: :250
                Error9000();                                  // PERFORM 9000-ERROR. source: :251
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :252
                break;
        }
    }

    // =================================================================================================
    //  2400-OPEN-OUTPUT-QUEUE — source: COACCT01.cbl:255-287
    // =================================================================================================
    /// <summary>Open CARD.DEMO.REPLY.ACCT for PUT (MQOO-OUTPUT + PASS-ALL-CONTEXT + FAIL-IF-QUIESCING).
    /// On success SET RESP-QUEUE-OPEN.</summary>
    private void OpenOutputQueue2400()
    {
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE REPLY-QUEUE-NAME TO MQOD-OBJECTNAME. source: :260-261
        // COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :263-265
        int options = MqooOutput | MqooPassAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... source: :267-272
        _outputHandle = _mq.Open(_replyQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :274
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :275
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO OUTPUT-QUEUE-HANDLE (handle already captured above).
                SetRespQueueOpen();                           // SET RESP-QUEUE-OPEN TO TRUE. :279
                break;
            default:                                           // WHEN OTHER. source: :280
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _replyQueueName;
                _mqApplReturnMessage = "OUT MQOPEN ERR";       // source: :284
                Error9000();                                  // PERFORM 9000-ERROR. source: :285
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :286
                break;
        }
    }

    // =================================================================================================
    //  2100-OPEN-ERROR-QUEUE — source: COACCT01.cbl:289-322
    // =================================================================================================
    /// <summary>Open CARD.DEMO.ERROR for PUT (the error sink). On failure DISPLAY the block and TERMINATE
    /// (no 9000-ERROR here — you cannot write errors when the error queue itself failed to open).</summary>
    private void OpenErrorQueue2100()
    {
        _errorQueueName = MqQueues.Error;                     // MOVE 'CARD.DEMO.ERROR' TO ERROR-QUEUE-NAME. :294
        // MOVE SPACES TO MQOD-OBJECTQMGRNAME; MOVE ERROR-QUEUE-NAME TO MQOD-OBJECTNAME. source: :295-296
        // COMPUTE MQ-OPTIONS = MQOO-OUTPUT + MQOO-PASS-ALL-CONTEXT + MQOO-FAIL-IF-QUIESCING. :298-300
        int options = MqooOutput | MqooPassAllContext | MqooFailIfQuiescing;

        // CALL 'MQOPEN' USING QMGR-HANDLE-CONN ... source: :302-307
        _errorHandle = _mq.Open(_errorQueueName, options);
        _mqConditionCode = MqConstants.MqccOk;
        _mqReasonCode = MqConstants.MqrcNone;

        switch (_mqConditionCode)                              // EVALUATE MQ-CONDITION-CODE. source: :309
        {
            case MqConstants.MqccOk:                           // WHEN MQCC-OK. source: :310
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // MOVE MQ-HOBJ TO ERROR-QUEUE-HANDLE (handle already captured above).
                SetErrQueueOpen();                            // SET ERR-QUEUE-OPEN TO TRUE. :314
                break;
            default:                                           // WHEN OTHER. source: :315
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "ERR MQOPEN ERR";       // source: :319
                DisplayMqErrDisplay();                        // DISPLAY MQ-ERR-DISPLAY (not enqueued). :320
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :321
                break;
        }
    }

    // =================================================================================================
    //  4000-MAIN-PROCESS — source: COACCT01.cbl:325-331
    // =================================================================================================
    /// <summary>Per-message loop body (performed UNTIL NO-MORE-MSGS): SYNCPOINT the previous message's
    /// unit of work, then GET the next request.</summary>
    private void MainProcess4000()
    {
        // EXEC CICS SYNCPOINT END-EXEC (commit previous message's UOW). The in-proc repository read +
        // PUT are already committed on the shared connection; this is the per-message boundary. source: :326-328
        GetRequest3000();                                    // PERFORM 3000-GET-REQUEST. source: :330
    }

    // =================================================================================================
    //  3000-GET-REQUEST — source: COACCT01.cbl:334-388
    // =================================================================================================
    /// <summary>GET one request message (5 s wait, any next msg) and dispatch it; on the empty queue set
    /// NO-MORE-MSGS, on any other GET failure write the error and terminate.</summary>
    private void GetRequest3000()
    {
        // MOVE 5000 TO MQGMO-WAITINTERVAL; MOVE SPACES TO MQ-CORRELID/MQ-MSG-ID; MOVE INPUT-QUEUE-NAME
        // TO MQ-QUEUE; MOVE INPUT-QUEUE-HANDLE TO MQ-HOBJ; MOVE 1000 TO MQ-BUFFER-LENGTH. source: :337-342
        _mqCorrelId = MqConstants.MqciNone;
        _mqMsgId = MqConstants.MqmiNone;
        _mqQueue = _inputQueueName;
        _mqBufferLength = 1000;

        // MOVE MQMI-NONE TO MQMD-MSGID; MOVE MQCI-NONE TO MQMD-CORRELID (take any next msg). source: :343-344
        // INITIALIZE REQUEST-MSG-COPY REPLACING NUMERIC BY ZEROES -> WS-FUNC spaces, WS-KEY 0. source: :345
        _wsFunc = "";
        _wsKey = 0;

        // COMPUTE MQGMO-OPTIONS = MQGMO-SYNCPOINT + FAIL-IF-QUIESCING + CONVERT + WAIT. source: :347-350
        // CALL 'MQGET' USING MQ-HCONN ... (FB-2: MQ-HCONN, not QMGR-HANDLE-CONN). source: :352-360
        MqResult r = _mq.Get(_inputHandle!, out MqMessage? msg);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        if (_mqConditionCode == MqConstants.MqccOk && msg is not null)   // IF MQ-CONDITION-CODE = MQCC-OK. :363
        {
            _mqMsgId = msg.MsgId;                             // MOVE MQMD-MSGID TO MQ-MSG-ID. source: :364
            _mqCorrelId = msg.CorrelId;                       // MOVE MQMD-CORRELID TO MQ-CORRELID. :365
            _mqQueueReply = msg.ReplyToQueue;                 // MOVE MQMD-REPLYTOQ TO MQ-QUEUE-REPLY (FB-5). :366
            _mqApplConditionCode = _mqConditionCode;          // source: :367
            _mqApplReasonCode = _mqReasonCode;                // source: :368
            _requestMessage = Fixed(msg.Body, 1000);          // MOVE MQ-BUFFER TO REQUEST-MESSAGE. source: :369
            _saveCorelid = _mqCorrelId;                       // MOVE MQ-CORRELID TO SAVE-CORELID. :370
            _saveReply2q = _mqQueueReply;                     // MOVE MQ-QUEUE-REPLY TO SAVE-REPLY2Q (FB-5). :371
            _saveMsgid = _mqMsgId;                            // MOVE MQ-MSG-ID TO SAVE-MSGID. :372

            // MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY (overlay X(1000) onto FUNC/KEY/FILLER). source: :373
            OverlayRequestMsgCopy(_requestMessage);

            ProcessRequestReply4000();                        // PERFORM 4000-PROCESS-REQUEST-REPLY. :374
            _mqMsgCount++;                                    // ADD 1 TO MQ-MSG-COUNT (FB-4: dead). :375
        }
        else                                                  // ELSE. source: :376
        {
            if (_mqReasonCode == MqConstants.MqrcNoMsgAvailable)   // IF MQ-REASON-CODE = MQRC-NO-MSG-AVAILABLE. :377
            {
                SetNoMoreMsgs();                              // SET NO-MORE-MSGS TO TRUE. source: :378
            }
            else                                              // ELSE. source: :380
            {
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "INP MQGET ERR:";      // source: :384
                Error9000();                                  // PERFORM 9000-ERROR. source: :385
                Termination8000();                            // PERFORM 8000-TERMINATION. source: :386
            }
        }
    }

    // =================================================================================================
    //  4000-PROCESS-REQUEST-REPLY — source: COACCT01.cbl:390-460  — core business logic
    // =================================================================================================
    /// <summary>Build the reply for one parsed request: on a valid INQA+key inquiry read the ACCOUNT
    /// (NORMAL → labeled snapshot; NOTFND → invalid-params text; OTHER → write error + terminate);
    /// otherwise reply with the invalid-params + function text.</summary>
    private void ProcessRequestReply4000()
    {
        _replyMessage = "";                                  // MOVE SPACES TO REPLY-MESSAGE. source: :391
        // INITIALIZE WS-DATE-TIME REPLACING NUMERIC BY ZEROES (WS-DATE-TIME is otherwise unused). source: :392

        // IF WS-FUNC = 'INQA' AND WS-KEY > ZEROES. source: :393
        if (_wsFunc == "INQA" && _wsKey > 0)
        {
            _wsKey = Store11(_wsKey);                         // (the numeric WS-KEY is an 11-digit field)
            _wsCardRidAcctId = _wsKey;                        // MOVE WS-KEY TO WS-CARD-RID-ACCT-ID. source: :394

            // EXEC CICS READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X) KEYLENGTH(11)
            //   INTO(ACCOUNT-RECORD) RESP(WS-RESP-CD) RESP2(WS-REAS-CD). source: :396-404
            // The X(11) RIDFLD is the zoned image of WS-CARD-RID-ACCT-ID; for SQL we pass the integer.
            string resp = _accounts.ReadByKey(_wsCardRidAcctId, out _accountRecord);
            _wsRespCd = RespFromStatus(resp);
            _wsReasCd = 0;

            switch (resp)                                     // EVALUATE WS-RESP-CD. source: :406
            {
                case FileStatus.Ok:                           // WHEN DFHRESP(NORMAL). source: :407
                    BuildAccountReply(_accountRecord!);       // MOVE 11 fields into WS-ACCT-RESPONSE. :408-425
                    _replyMessage = _wsAcctResponse;          // MOVE WS-ACCT-RESPONSE TO REPLY-MESSAGE. :426
                    PutReply4100();                           // PERFORM 4100-PUT-REPLY. source: :427
                    break;

                case FileStatus.RecordNotFound:               // WHEN DFHRESP(NOTFND). source: :428
                    // STRING 'INVALID REQUEST PARAMETERS ' 'ACCT ID : ' WS-KEY DELIMITED BY SIZE. :429-434
                    _replyMessage = "INVALID REQUEST PARAMETERS " + "ACCT ID : " + Key11(_wsKey);
                    PutReply4100();                           // PERFORM 4100-PUT-REPLY. source: :435
                    break;

                default:                                      // WHEN OTHER. source: :437
                    _mqApplConditionCode = _wsRespCd;         // source: :439
                    _mqApplReasonCode = _wsReasCd;            // source: :440
                    _mqApplQueueName = _inputQueueName;       // source: :441
                    _mqApplReturnMessage = "ERROR WHILE READING ACCTFILE";  // source: :442-443
                    Error9000();                              // PERFORM 9000-ERROR. source: :444
                    Termination8000();                        // PERFORM 8000-TERMINATION. source: :445
                    break;
            }
        }
        else                                                  // ELSE. source: :448
        {
            // STRING 'INVALID REQUEST PARAMETERS ' 'ACCT ID : ' WS-KEY 'FUNCTION : ' WS-FUNC. :449-455
            _replyMessage = "INVALID REQUEST PARAMETERS " + "ACCT ID : " + Key11(_wsKey)
                          + "FUNCTION : " + Fixed(_wsFunc, 4);
            PutReply4100();                                   // PERFORM 4100-PUT-REPLY. source: :456
        }
    }

    // =================================================================================================
    //  4100-PUT-REPLY — source: COACCT01.cbl:462-499
    // =================================================================================================
    /// <summary>PUT the built reply to CARD.DEMO.REPLY.ACCT (the pre-opened OUTPUT-QUEUE-HANDLE — FB-5:
    /// never the request's ReplyToQ), echoing the saved MsgId/CorrelId.</summary>
    private void PutReply4100()
    {
        _mqBuffer = Fixed(_replyMessage, 1000);              // MOVE REPLY-MESSAGE TO MQ-BUFFER. source: :467
        _mqBufferLength = 1000;                              // MOVE 1000 TO MQ-BUFFER-LENGTH. source: :468

        var reply = new MqMessage
        {
            Body = _mqBuffer,
            MsgId = _saveMsgid,                              // MOVE SAVE-MSGID TO MQMD-MSGID. source: :469
            CorrelId = _saveCorelid,                         // MOVE SAVE-CORELID TO MQMD-CORRELID. :470
            Format = MqConstants.MqfmtString,                // MOVE MQFMT-STRING TO MQMD-FORMAT. :471
            CodedCharSetId = MqConstants.MqccsiQMgr,         // COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR. :473
        };

        // COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING. :475-477
        // CALL 'MQPUT' USING MQ-HCONN OUTPUT-QUEUE-HANDLE ... (FB-2/FB-5). source: :479-486
        MqResult r = _mq.Put(_outputHandle!, reply);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :488
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :489
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :492
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _replyQueueName;
                _mqApplReturnMessage = "MQPUT ERR";          // source: :496
                Error9000();                                 // PERFORM 9000-ERROR. source: :497
                Termination8000();                           // PERFORM 8000-TERMINATION. source: :498
                break;
        }
    }

    // =================================================================================================
    //  9000-ERROR — source: COACCT01.cbl:501-537
    // =================================================================================================
    /// <summary>PUT the MQ-ERR-DISPLAY block to CARD.DEMO.ERROR. On a PUT failure DISPLAY the block and
    /// terminate (no recursion into 9000-ERROR).</summary>
    private void Error9000()
    {
        _errorMessage = FormatMqErrDisplay();                // MOVE MQ-ERR-DISPLAY TO ERROR-MESSAGE. source: :505
        _mqBuffer = Fixed(_errorMessage, 1000);              // MOVE ERROR-MESSAGE TO MQ-BUFFER. source: :506
        _mqBufferLength = 1000;                              // MOVE 1000 TO MQ-BUFFER-LENGTH. source: :507

        _errorLog.Write(_errorMessage);                      // diagnostic sink (MQ_SHIM.md §5.4)

        var err = new MqMessage
        {
            Body = _mqBuffer,
            Format = MqConstants.MqfmtString,                // MOVE MQFMT-STRING TO MQMD-FORMAT. source: :508
            CodedCharSetId = MqConstants.MqccsiQMgr,         // COMPUTE MQMD-CODEDCHARSETID = MQCCSI-Q-MGR. :510
        };

        // COMPUTE MQPMO-OPTIONS = MQPMO-SYNCPOINT + MQPMO-DEFAULT-CONTEXT + MQPMO-FAIL-IF-QUIESCING. :512-514
        // CALL 'MQPUT' USING MQ-HCONN ERROR-QUEUE-HANDLE ... source: :516-523
        MqResult r = _errorHandle is null ? MqResult.Success : _mq.Put(_errorHandle, err);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :525
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :526
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :529
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "MQPUT ERR";          // source: :533
                DisplayMqErrDisplay();                       // DISPLAY MQ-ERR-DISPLAY. source: :534
                Termination8000();                           // PERFORM 8000-TERMINATION (no 9000 recursion). :535
                break;
        }
    }

    // =================================================================================================
    //  8000-TERMINATION — source: COACCT01.cbl:538-550
    // =================================================================================================
    /// <summary>Close the open queues (FB-1 flag/queue mapping), EXEC CICS RETURN + GOBACK. FB-6: a close
    /// error re-PERFORMs 8000-TERMINATION; modeled idempotently — a re-entry short-circuits to the RETURN.</summary>
    private void Termination8000()
    {
        if (_terminating)
        {
            // FB-6: re-entry from a close-error PERFORM 8000-TERMINATION. The COBOL would re-walk the
            // closes (flags unreset); model termination as idempotent and go straight to EXEC CICS RETURN.
            throw new TerminationSignal();
        }
        _terminating = true;

        if (IsReplyQueueOpen())                              // IF REPLY-QUEUE-OPEN. source: :540
        {
            CloseInputQueue5000();                           // PERFORM 5000-CLOSE-INPUT-QUEUE (FB-1). :541
        }
        if (IsRespQueueOpen())                               // IF RESP-QUEUE-OPEN. source: :543
        {
            CloseOutputQueue5100();                          // PERFORM 5100-CLOSE-OUTPUT-QUEUE. source: :544
        }
        if (IsErrQueueOpen())                                // IF ERR-QUEUE-OPEN. source: :546
        {
            CloseErrorQueue5200();                           // PERFORM 5200-CLOSE-ERROR-QUEUE. source: :547
        }

        // EXEC CICS RETURN END-EXEC; GOBACK. source: :549-550
        throw new TerminationSignal();
    }

    // =================================================================================================
    //  5000-CLOSE-INPUT-QUEUE — source: COACCT01.cbl:552-573
    // =================================================================================================
    private void CloseInputQueue5000()
    {
        _mqQueue = _inputQueueName;                          // MOVE INPUT-QUEUE-NAME TO MQ-QUEUE. source: :553
        // COMPUTE MQ-OPTIONS = MQCO-NONE. source: :555
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :557-561
        MqResult r = _inputHandle is null ? MqResult.Success : _mq.Close(_inputHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :563
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :564
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :567
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :571
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-6). source: :572
                break;
        }
    }

    // =================================================================================================
    //  5100-CLOSE-OUTPUT-QUEUE — source: COACCT01.cbl:574-595
    // =================================================================================================
    private void CloseOutputQueue5100()
    {
        _mqQueue = _replyQueueName;                          // MOVE REPLY-QUEUE-NAME TO MQ-QUEUE. source: :575
        // COMPUTE MQ-OPTIONS = MQCO-NONE. source: :577
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :579-583
        MqResult r = _outputHandle is null ? MqResult.Success : _mq.Close(_outputHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :585
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :586
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :589
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                // Copy/paste: uses INPUT-QUEUE-NAME in the error message (harmless). source: :592
                _mqApplQueueName = _inputQueueName;
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :593
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-6). source: :594
                break;
        }
    }

    // =================================================================================================
    //  5200-CLOSE-ERROR-QUEUE — source: COACCT01.cbl:597-619
    // =================================================================================================
    private void CloseErrorQueue5200()
    {
        _mqQueue = _errorQueueName;                          // MOVE ERROR-QUEUE-NAME TO MQ-QUEUE. source: :598
        // COMPUTE MQ-OPTIONS = MQCO-NONE. source: :600
        // CALL 'MQCLOSE' USING MQ-HCONN MQ-HOBJ ... source: :602-606
        MqResult r = _errorHandle is null ? MqResult.Success : _mq.Close(_errorHandle);
        _mqConditionCode = r.CompletionCode;
        _mqReasonCode = r.ReasonCode;

        switch (_mqConditionCode)                            // EVALUATE MQ-CONDITION-CODE. source: :608
        {
            case MqConstants.MqccOk:                         // WHEN MQCC-OK. source: :609
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                break;
            default:                                         // WHEN OTHER. source: :612
                _mqApplConditionCode = _mqConditionCode;
                _mqApplReasonCode = _mqReasonCode;
                _mqApplQueueName = _errorQueueName;
                _mqApplReturnMessage = "MQCLOSE ERR";        // source: :616
                Error9000();                                 // PERFORM 9000-ERROR. source: :617
                Termination8000();                           // PERFORM 8000-TERMINATION (FB-6). source: :618
                break;
        }
    }

    // =================================================================================================
    //  WS-ACCT-RESPONSE — the labeled reply block (built by BuildAccountReply). source: :130-169
    // =================================================================================================
    private string _wsAcctResponse = "";

    /// <summary>
    /// MOVEs the 11 surfaced account fields into the <c>WS-ACCT-RESPONSE</c> group and re-serializes it to
    /// its exact byte image. Labels are the literal <c>05</c>-level VALUE clauses; numeric values are
    /// <b>non-edited DISPLAY</b> (<c>9(11)</c> and <c>S9(10)V99</c>) so they emit zoned-decimal digit bytes
    /// (no decimal point, embedded sign on the last byte). FB-8: <c>ACCT-ADDR-ZIP</c> is intentionally not
    /// moved. source: COACCT01.cbl:407-425, layout :130-169; §6 byte-parity.
    /// </summary>
    private void BuildAccountReply(Account a)
    {
        var sb = new StringBuilder(220);
        sb.Append("ACCOUNT ID : ");                          // WS-ACCT-LBL X(13). source: :132-133
        sb.Append(Pic9(a.AcctId, 11));                       // WS-ACCT-ID 9(11) <- ACCT-ID. :134,:408
        sb.Append("ACCOUNT STATUS : ");                      // WS-STATUS-LBL X(17). source: :135-136
        sb.Append(Fixed(a.ActiveStatus, 1));                 // WS-ACCT-ACTIVE-STATUS X(1). :137,:409-410
        sb.Append("BALANCE : ");                             // WS-CURR-BAL-LBL X(10). source: :138-139
        sb.Append(PicS9V99(a.CurrBal));                      // WS-ACCT-CURR-BAL S9(10)V99 <- ACCT-CURR-BAL. :140,:411
        sb.Append("CREDIT LIMIT : ");                        // WS-CRDT-LMT-LBL X(15). source: :142-143
        sb.Append(PicS9V99(a.CreditLimit));                  // WS-ACCT-CREDIT-LIMIT <- ACCT-CREDIT-LIMIT. :144,:412-413
        sb.Append("CASH LIMIT : ");                          // WS-CASH-LIMIT-LBL X(13). source: :146-147
        sb.Append(PicS9V99(a.CashCreditLimit));              // WS-ACCT-CASH-CREDIT-LIMIT. :148,:414-415
        sb.Append("OPEN DATE : ");                           // WS-OPEN-DATE-LBL X(12). source: :150-151
        sb.Append(Fixed(a.OpenDate, 10));                    // WS-ACCT-OPEN-DATE X(10). :152,:416
        sb.Append("EXPR DATE : ");                           // WS-EXPR-DATE-LBL X(12). source: :153-154
        sb.Append(Fixed(a.ExpirationDate, 10));              // WS-ACCT-EXPIRAION-DATE X(10) (sic). :155,:417-418
        sb.Append("REIS DATE : ");                           // WS-REISSUE-DT-LBL X(12). source: :156-157
        sb.Append(Fixed(a.ReissueDate, 10));                 // WS-ACCT-REISSUE-DATE X(10). :158,:419-420
        sb.Append("CREDIT BAL : ");                          // WS-CURR-CYC-CREDIT-LBL X(13). source: :159-160
        sb.Append(PicS9V99(a.CurrCycCredit));                // WS-ACCT-CURR-CYC-CREDIT. :161,:421-422
        sb.Append("DEBIT BAL : ");                           // WS-CURR-CYC-DEBIT-LBL X(12). source: :163-164
        sb.Append(PicS9V99(a.CurrCycDebit));                 // WS-ACCT-CURR-CYC-DEBIT. :165,:423-424
        sb.Append("GROUP ID : ");                            // WS-ACCT-GRP-LBL X(11). source: :167-168
        sb.Append(Fixed(a.GroupId, 10));                     // WS-ACCT-GROUP-ID X(10). :169,:425
        _wsAcctResponse = sb.ToString();
        // FB-8: ACCT-ADDR-ZIP read but never surfaced (absent from the move list :407-425).
    }

    // =================================================================================================
    //  REQUEST-MSG-COPY overlay — source: COACCT01.cbl:109-112,373
    // =================================================================================================
    /// <summary>
    /// MOVE REQUEST-MESSAGE TO REQUEST-MSG-COPY: re-overlays the X(1000) buffer onto WS-FUNC X(4),
    /// WS-KEY 9(11), WS-FILLER X(985). WS-FUNC is the first 4 chars; WS-KEY is the next 11 chars parsed
    /// as an unsigned 11-digit zoned numeric (non-digit content de-edits per the COBOL MOVE alpha→9(11),
    /// keeping the low-order digits). source: :109-112,:373.
    /// </summary>
    private void OverlayRequestMsgCopy(string buffer)
    {
        string b = Fixed(buffer, 1000);
        _wsFunc = b.Substring(0, 4);                         // WS-FUNC X(4)
        _wsKey = ParseKey11(b.Substring(4, 11));             // WS-KEY 9(11)
    }

    // =================================================================================================
    //  MQ-ERR-DISPLAY helpers — source: COACCT01.cbl:58-67,185,505,534
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
    //  88-level flag setters / testers (FB-1 naming preserved).
    // =================================================================================================
    private void SetNoMoreMsgs() => _wsMqMsgFlag = 'Y';
    private bool IsNoMoreMsgs() => _wsMqMsgFlag == 'Y';

    private void SetRespQueueOpen() => _wsRespQueueSts = 'Y';   // output/reply queue (FB-1)
    private bool IsRespQueueOpen() => _wsRespQueueSts == 'Y';

    private void SetErrQueueOpen() => _wsErrQueueSts = 'Y';
    private bool IsErrQueueOpen() => _wsErrQueueSts == 'Y';

    private void SetReplyQueueOpen() => _wsReplyQueueSts = 'Y'; // input/request queue (FB-1)
    private bool IsReplyQueueOpen() => _wsReplyQueueSts == 'Y';

    // =================================================================================================
    //  Numeric / string formatting helpers (zoned-decimal reply serialization + COBOL field widths).
    // =================================================================================================

    /// <summary>Maps the ACCOUNT repository two-char FileStatus to the CICS RESP value the COBOL EVALUATEs
    /// (Ok→NORMAL, RecordNotFound→NOTFND, anything else→a non-NORMAL/NOTFND code that hits WHEN OTHER).</summary>
    private static int RespFromStatus(string fileStatus) => fileStatus switch
    {
        FileStatus.Ok => (int)Resp.Normal,
        FileStatus.RecordNotFound => (int)Resp.NotFnd,
        _ => (int)Resp.Error,
    };

    /// <summary>WS-KEY 9(11): clamp a long into the unsigned 11-digit field (silent high-order overflow).</summary>
    private static long Store11(long v) => ((v % 100000000000L) + 100000000000L) % 100000000000L;

    /// <summary>
    /// Parses the 11-byte WS-KEY 9(11) view of the buffer: an unsigned zoned numeric. Non-digit bytes are
    /// de-edited (COBOL MOVE alpha→9(11) keeps the digit characters, low-order 11). Spaces/zeros → 0.
    /// </summary>
    private static long ParseKey11(string raw)
    {
        // COBOL group MOVE REQUEST-MESSAGE -> WS-KEY (PIC 9(11) DISPLAY): the 11 bytes land verbatim and a
        // numeric reference reads each byte's zoned LOW-ORDER nibble as its digit, IN PLACE — it does NOT
        // de-edit/strip non-digit bytes. For a normal ASCII-digit request key '0'..'9' this equals the keyed
        // account id; a non-digit byte contributes its low nibble at its own position (faithful), rather than
        // being dropped (which would shift later digits left and change the numeric value). source: :109-112,:373.
        long v = 0;
        foreach (char c in raw) v = v * 10 + (c & 0x0F);
        return v % 100000000000L; // 9(11) holds 11 digits
    }

    /// <summary>The X(11) RIDFLD / WS-KEY zoned image of an account id: 11 zero-padded digit bytes (no sign).
    /// Used by the STRING'd INVALID-REQUEST replies, where WS-KEY 9(11) contributes its full fixed width.</summary>
    private static string Key11(long v) => Pic9(v, 11);

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
    /// Serializes an <c>S9(10)V99</c> non-edited DISPLAY field to its 12-byte zoned-decimal image (10 int +
    /// 2 frac digit bytes, NO decimal point, the trailing digit carrying the embedded sign overpunch) using
    /// the project's <see cref="ZonedDecimalCodec"/> ASCII (GnuCOBOL) convention — the same codec the
    /// golden round-trip diffs against. Truncate toward zero, silent high-order overflow. source: §6.
    /// </summary>
    private static string PicS9V99(decimal value)
    {
        var bytes = new byte[12];
        ZonedDecimalCodec.Encode(value, bytes, totalDigits: 12, scale: 2, signed: true, HostKind.Ascii);
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>WS-CICS-RESP1/2-CD-D 9(8) display image used in the RETRIEVE error STRING.</summary>
    private static string Disp8(int value) => Pic9(value, 8);

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
