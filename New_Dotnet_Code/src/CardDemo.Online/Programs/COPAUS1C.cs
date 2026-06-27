using System.Globalization;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the optional online CICS/IMS/BMS COBOL program <c>COPAUS1C</c> — the
/// <b>Detail View of an Authorization Message</b> (CICS transaction <c>CPVD</c>, BMS map <c>COPAU1A</c> /
/// mapset <c>COPAU01</c>). Given an account id (<c>CDEMO-ACCT-ID</c>) and a selected 8-byte authorization
/// key (<c>CDEMO-CPVD-PAU-SELECTED</c>) passed in the COMMAREA from the summary screen COPAUS0C, it reads
/// the IMS pending-authorization <i>summary</i> root (PAUTSUM0) to establish parentage and the keyed
/// <i>detail</i> child (PAUTDTL1), formats every detail field onto the display-only map, and SENDs it.
/// PF8 advances to the next authorization detail under the same account (forward GNP); PF5 toggles the
/// fraud flag (LINK COPAUS2C → on success REPL the IMS detail, on failure ROLLBACK); PF3 returns to the
/// summary screen COPAUS0C. source: COPAUS1C.cbl:1-6,156-206,291-358.
/// </summary>
/// <remarks>
/// <para><b>Structure.</b> Each COBOL paragraph is one method carrying its original name and a
/// <c>// source: COPAUS1C.cbl:NNN</c> citation; statement order, the EVALUATE/PERFORM control flow, the
/// COMMAREA field usage and every faithful bug are preserved verbatim.</para>
/// <para><b>DL/I → repository mapping (IMS_SCHEMA.md §3).</b> SCHD = begin/ensure unit of work
/// (<see cref="SqliteTransaction"/>); GU(PAUTSUM0 by acct) = <see cref="PautSummaryRepository.ReadByKey"/>;
/// qualified GNP(PAUTDTL1 WHERE PAUT9CTS=key) = <see cref="PautDetailRepository.StartParentScanAt"/> +
/// <see cref="PautDetailRepository.ReadNextInParent"/> (reposition at-or-after the key); unqualified GNP =
/// the next <see cref="PautDetailRepository.ReadNextInParent"/> on the same cursor; REPL(PAUTDTL1) =
/// <see cref="PautDetailRepository.Update"/>; SYNCPOINT = COMMIT; SYNCPOINT ROLLBACK = ROLLBACK. DIBSTAT
/// '  '/'FW' → AUTHS-NOT-EOF ('00'); 'GE'/'GB' (RecordNotFound '23' / EndOfFile '10') → AUTHS-EOF; other →
/// error message + SEND. The PF5 fraud LINK target COPAUS2C is <see cref="Copaus2c"/> (writes AUTHFRDS).</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="number">
/// <item>FB-1 — the auth-time display reuses the <c>'00:00:00'</c> VALUE init of WS-AUTH-TIME: POPULATE
/// overlays only the digit pairs (1:2/4:2/7:2), leaving the ':' separators from the initial VALUE. Modeled
/// by seeding the buffer and overlaying. source: :54,303-306.</item>
/// <item>FB-2 — WS-IMS-PSB-SCHD-FLG has no VALUE clause; IMS-PSB-SCHD is only ever SET 'Y' on a successful
/// SCHD, and tested (then reset + SYNCPOINT) in PROCESS-ENTER-KEY/PF8. No defensive init added. source:
/// :89-91,218-221,276-279,590-591.</item>
/// <item>FB-3 — on an unexpected DIBSTAT the read paragraph SENDs the screen <i>inline</i> then returns;
/// the caller continues to POPULATE (no-op under ERR-FLG-ON) and MAIN issues a <b>second</b> SEND →
/// the screen is sent twice on an error turn. Not collapsed. source: :453-461,177,183,294,202.</item>
/// <item>FB-4 — card-expiry display positional-slices the raw 4 bytes (xx/yy) with no validation. source:
/// :336-338.</item>
/// <item>FB-5 — stray <c>DISPLAY 'RPT DT:'</c> in UPDATE-AUTH-DETAILS routed to a log line. source: :523.</item>
/// <item>FB-6 — MARK-AUTH-FRAUD does not gate on AUTHS-EOF/ERR-FLG: it toggles + LINKs even when the read
/// found nothing/errored, and omits the IMS-PSB-SCHD syncpoint reset that ENTER/PF8 do. source: :230-266.</item>
/// <item>FB-7 — WS-RESP-CD/WS-REAS-CD declared but never used (dead). source: :47-48.</item>
/// <item>FB-8 — truncated/misspelled reason texts ('INSUFFICNT FUND','EXCED DAILY LMT') are intentional
/// data. source: :60,63.</item>
/// </list>
/// </remarks>
public sealed class Copaus1c : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COPAUS1C.cbl:32-54
    // =============================================================================================
    private const string WS_PGM_AUTH_DTL = "COPAUS1C";   // 05 WS-PGM-AUTH-DTL   X(08) VALUE 'COPAUS1C'. :33
    private const string WS_PGM_AUTH_SMRY = "COPAUS0C";  // 05 WS-PGM-AUTH-SMRY  X(08) VALUE 'COPAUS0C'. :34
    private const string WS_PGM_AUTH_FRAUD = "COPAUS2C"; // 05 WS-PGM-AUTH-FRAUD X(08) VALUE 'COPAUS2C'. :35
    private const string WS_CICS_TRANID = "CPVD";        // 05 WS-CICS-TRANID    X(04) VALUE 'CPVD'.     :36

    private string _wsMessage = "";       // 05 WS-MESSAGE X(80) VALUE SPACES. :37

    // 05 WS-ERR-FLG X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. :38-40
    private bool _errFlgOn;
    private bool ErrFlgOff => !_errFlgOn;

    // 05 WS-AUTHS-EOF X(01) VALUE 'N'. 88 AUTHS-EOF='Y' / AUTHS-NOT-EOF='N'. :41-43
    private bool _authsEof;

    // 05 WS-SEND-ERASE-FLG X(01) VALUE 'Y'. 88 SEND-ERASE-YES='Y' / SEND-ERASE-NO='N'. :44-46
    private bool _sendEraseYes = true;

    // 05 WS-RESP-CD / WS-REAS-CD S9(09) COMP VALUE ZEROS — FB-7: declared, never used. :47-48
    private int _wsRespCd;
    private int _wsReasCd;

    private long _wsAcctId;               // 05 WS-ACCT-ID  9(11). :50 (<- CDEMO-ACCT-ID)
    private string _wsAuthKey = "";       // 05 WS-AUTH-KEY X(08). :51 (<- CDEMO-CPVD-PAU-SELECTED)
    // 05 WS-AUTH-AMT PIC -zzzzzzz9.99 (edited). :52
    // 05 WS-AUTH-DATE X(08) VALUE '00/00/00'. :53
    private string _wsAuthDate = "00/00/00";
    // 05 WS-AUTH-TIME X(08) VALUE '00:00:00' — persists ':' separators (FB-1). :54
    private string _wsAuthTime = "00:00:00";

    // =============================================================================================
    //  WS-IMS-VARIABLES — source: COPAUS1C.cbl:75-91
    // =============================================================================================
    // 05 IMS-RETURN-CODE X(02): STATUS-OK='  '|'FW', SEGMENT-NOT-FOUND='GE', END-OF-DB='GB',
    //    PSB-SCHEDULED-MORE-THAN-ONCE='TC'. (others inert). :79-88
    private string _imsReturnCode = "  ";

    // 05 WS-IMS-PSB-SCHD-FLG X(1) — NO VALUE (FB-2). 88 IMS-PSB-SCHD='Y'/IMS-PSB-NOT-SCHD='N'. :89-91
    // Initial content is undefined/spaces -> not 'Y'.
    private string _imsPsbSchdFlg = " ";
    private bool ImsPsbSchd => _imsPsbSchdFlg == "Y";

    // =============================================================================================
    //  PENDING-AUTH-SUMMARY / PENDING-AUTH-DETAILS io-areas (COPY CIPAUSMY / CIPAUDTY). :141-146
    // =============================================================================================
    private PautSummary? _summaryRec;
    private PautDetail _detailRec = new();

    // PA-AUTHORIZATION-KEY / PA-ACCT-ID set before each DL/I call. :436-437
    private long _paAcctId;
    private string _paAuthorizationKey = "";

    // =============================================================================================
    //  Decline-reason lookup table (SEARCH ALL, ASCENDING KEY) — source: COPAUS1C.cbl:56-73
    //  10 hard-coded rows: 4-char code + verbatim 16-char description (FB-8 spellings preserved).
    // =============================================================================================
    private static readonly (string Code, string Desc)[] DeclineReasonTab =
    {
        ("0000", "APPROVED        "), // :58
        ("3100", "INVALID CARD    "), // :59
        ("4100", "INSUFFICNT FUND"),  // :60 (FB-8 sic; note source literal is 19 chars -> X(16) keeps 'INSUFFICNT FUND')
        ("4200", "CARD NOT ACTIVE "), // :61
        ("4300", "ACCOUNT CLOSED  "), // :62
        ("4400", "EXCED DAILY LMT"),  // :63 (FB-8 sic)
        ("5100", "CARD FRAUD      "), // :64
        ("5200", "MERCHANT FRAUD  "), // :65
        ("5300", "LOST CARD       "), // :66
        ("9000", "UNKNOWN         "), // :67
    };

    // =============================================================================================
    //  WS-FRAUD-DATA — COMMAREA passed to COPAUS2C on LINK. source: COPAUS1C.cbl:93-104
    // =============================================================================================
    private readonly WsFraudData _fraudData = new();

    // =============================================================================================
    //  COMMAREA (typed view) + CPVD extension + per-turn map + DB. source: :109-120,171-172,202-205
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    // CDEMO-CPVD-INFO (appended after COCOM01Y). Only the selected key is read by this program; the rest
    // are screen-paging state owned by COPAUS0C, carried through unchanged. source: :110-120
    private string _cpvdPauSelFlg = "";   // CDEMO-CPVD-PAU-SEL-FLG  X(01). :111
    private string _cpvdPauSelected = ""; // CDEMO-CPVD-PAU-SELECTED X(08). :112  (the selected auth key)

    // CCDA-* (COTTL01Y / CSMSG01Y).
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         "; // CSMSG01Y :20-21

    private readonly RelationalDb _db;
    private PautSummaryRepository _summaryRepo = null!;
    private PautDetailRepository _detailRepo = null!;

    // Unit of work opened by SCHEDULE-PSB; COMMIT on SYNCPOINT, ROLLBACK on SYNCPOINT ROLLBACK.
    private SqliteTransaction? _uow;

    /// <summary>Log sink for the stray DISPLAY (FB-5). Defaults to the console-region log (Console.Error).</summary>
    private readonly Action<string> _log;

    /// <summary>Factory-friendly constructor: takes the shared relational DB (repositories created in <see cref="Handle"/>).</summary>
    public Copaus1c(RelationalDb db) : this(db, null) { }

    /// <summary>Constructor with an injectable log sink for the stray DISPLAY (FB-5).</summary>
    public Copaus1c(RelationalDb db, Action<string>? log)
    {
        _db = db;
        _log = log ?? (s => Console.Error.WriteLine(s));
    }

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Copaus1c() : this(null!, null) { }

    /// <inheritdoc/>
    public string ProgramName => WS_PGM_AUTH_DTL; // PROGRAM-ID. COPAUS1C. :23

    /// <inheritdoc/>
    public string TransId => WS_CICS_TRANID;      // CSD: CPVD -> COPAUS1C. :36

    // =============================================================================================
    //  MAIN-PARA — source: COPAUS1C.cbl:156-206
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        _map = BuildMap();
        if (_db is not null)
        {
            _summaryRepo = new PautSummaryRepository(_db.Connection);
            _detailRepo = new PautDetailRepository(_db.Connection);
        }

        _errFlgOn = false;            // SET ERR-FLG-OFF    TO TRUE. :159
        _sendEraseYes = true;         // SET SEND-ERASE-YES TO TRUE. :160

        _wsMessage = "";                                       // MOVE SPACES TO WS-MESSAGE. :162
        _map.Field("ERRMSG").SetValue("", setMdt: false);      //              ERRMSGO OF COPAU1AO. :163

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 — cold start. :165-169
            _commArea = ctx.CommArea ?? new CardDemoCommArea();  // INITIALIZE CARDDEMO-COMMAREA. :166
            ResetCommArea();
            _commArea.ToProgram = WS_PGM_AUTH_SMRY;              // MOVE WS-PGM-AUTH-SMRY TO CDEMO-TO-PROGRAM. :168
            ReturnToPrevScreen(ctx);                             // PERFORM RETURN-TO-PREV-SCREEN. :169
            return; // XCTL terminates this task.
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. :171
            _commArea = ctx.CommArea!;
            RestoreCpvdInfo();
            _cpvdFraudData = "";                                 // MOVE SPACES TO CDEMO-CPVD-FRAUD-DATA. :172

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. :173
                _commArea.SetReenter();                          // SET CDEMO-PGM-REENTER TO TRUE. :174
                ProcessEnterKey(ctx);                            // PERFORM PROCESS-ENTER-KEY. :175
                SendAuthviewScreen(ctx);                         // PERFORM SEND-AUTHVIEW-SCREEN. :177
            }
            else
            {
                ReceiveAuthviewScreen(ctx);                      // PERFORM RECEIVE-AUTHVIEW-SCREEN. :179
                // EVALUATE EIBAID. :180-198
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);                    // WHEN DFHENTER. :181-182
                        SendAuthviewScreen(ctx);                 // :183
                        break;
                    case AidKey.Pf3:
                        _commArea.ToProgram = WS_PGM_AUTH_SMRY;  // WHEN DFHPF3 — back to summary. :184-185
                        ReturnToPrevScreen(ctx);                 // PERFORM RETURN-TO-PREV-SCREEN. :186
                        return;                                   // XCTL terminates this task.
                    case AidKey.Pf5:
                        MarkAuthFraud(ctx);                      // WHEN DFHPF5. :187-188
                        SendAuthviewScreen(ctx);                 // :189
                        break;
                    case AidKey.Pf8:
                        ProcessPf8Key(ctx);                      // WHEN DFHPF8. :190-191
                        SendAuthviewScreen(ctx);                 // :192
                        break;
                    default:
                        ProcessEnterKey(ctx);                    // WHEN OTHER. :193-194
                        _wsMessage = CCDA_MSG_INVALID_KEY;       // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. :196
                        SendAuthviewScreen(ctx);                 // :197
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID('CPVD') COMMAREA(CARDDEMO-COMMAREA). :202-205
        SaveCpvdInfo();
        ctx.ReturnTransId(WS_CICS_TRANID, _commArea);
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COPAUS1C.cbl:208-228
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        MoveLowValuesToMapOut();   // MOVE LOW-VALUES TO COPAU1AO. :210

        // IF CDEMO-ACCT-ID IS NUMERIC AND CDEMO-CPVD-PAU-SELECTED NOT = SPACES AND LOW-VALUES. :211-212
        if (IsNumeric(_commArea.AcctId) && NotSpacesOrLow(_cpvdPauSelected))
        {
            _wsAcctId = _commArea.AcctId;        // MOVE CDEMO-ACCT-ID TO WS-ACCT-ID. :213
            _wsAuthKey = _cpvdPauSelected;       // MOVE CDEMO-CPVD-PAU-SELECTED TO WS-AUTH-KEY. :214-215
            ReadAuthRecord(ctx);                 // PERFORM READ-AUTH-RECORD. :216

            if (ImsPsbSchd)
            {
                // IF IMS-PSB-SCHD — reset + commit. :218-221
                _imsPsbSchdFlg = "N";            // SET IMS-PSB-NOT-SCHD TO TRUE.
                TakeSyncpoint();                 // PERFORM TAKE-SYNCPOINT.
            }
        }
        else
        {
            _errFlgOn = true;                    // SET ERR-FLG-ON TO TRUE. :223-224
        }

        PopulateAuthDetails();                   // PERFORM POPULATE-AUTH-DETAILS. :227
    }

    // =============================================================================================
    //  MARK-AUTH-FRAUD (PF5 — toggle fraud) — source: COPAUS1C.cbl:230-266
    //  FB-6: does NOT gate on AUTHS-EOF/ERR-FLG; no IMS-PSB-SCHD syncpoint reset here.
    // =============================================================================================
    private void MarkAuthFraud(CicsContext ctx)
    {
        _wsAcctId = _commArea.AcctId;            // MOVE CDEMO-ACCT-ID TO WS-ACCT-ID. :231
        _wsAuthKey = _cpvdPauSelected;           // MOVE CDEMO-CPVD-PAU-SELECTED TO WS-AUTH-KEY. :232

        ReadAuthRecord(ctx);                     // PERFORM READ-AUTH-RECORD. :234

        // Toggle. :236-242
        if (_detailRec.AuthFraud == "F")         // IF PA-FRAUD-CONFIRMED ('F').
        {
            _detailRec.AuthFraud = "R";          // SET PA-FRAUD-REMOVED TO TRUE ('R').
            _fraudData.FrdAction = "R";          // SET WS-REMOVE-FRAUD TO TRUE ('R').
        }
        else
        {
            _detailRec.AuthFraud = "F";          // SET PA-FRAUD-CONFIRMED TO TRUE ('F').
            _fraudData.FrdAction = "F";          // SET WS-REPORT-FRAUD TO TRUE ('F').
        }

        _fraudData.AuthRecord = ClonePaDetail(); // MOVE PENDING-AUTH-DETAILS TO WS-FRAUD-AUTH-RECORD. :244
        _fraudData.FrdAcctId = _commArea.AcctId; // MOVE CDEMO-ACCT-ID TO WS-FRD-ACCT-ID. :245
        _fraudData.FrdCustId = _commArea.CustId; // MOVE CDEMO-CUST-ID TO WS-FRD-CUST-ID. :246

        // EXEC CICS LINK PROGRAM('COPAUS2C') COMMAREA(WS-FRAUD-DATA) NOHANDLE. :248-252
        bool linkNormal = LinkCopaus2c();

        if (linkNormal)                          // IF EIBRESP = DFHRESP(NORMAL). :253
        {
            if (_fraudData.IsUpdtSuccess)        // IF WS-FRD-UPDT-SUCCESS. :254
                UpdateAuthDetails(ctx);          // PERFORM UPDATE-AUTH-DETAILS. :255
            else
            {
                _wsMessage = _fraudData.FrdActMsg; // MOVE WS-FRD-ACT-MSG TO WS-MESSAGE. :257
                RollBack();                      // PERFORM ROLL-BACK. :258
            }
        }
        else
        {
            RollBack();                          // ELSE (LINK failed) — PERFORM ROLL-BACK. :260-262
        }

        _cpvdPauSelected = _detailRec.AuthKey;   // MOVE PA-AUTHORIZATION-KEY TO CDEMO-CPVD-PAU-SELECTED. :264
        PopulateAuthDetails();                   // PERFORM POPULATE-AUTH-DETAILS. :265
    }

    // =============================================================================================
    //  PROCESS-PF8-KEY (advance to next auth) — source: COPAUS1C.cbl:268-289
    // =============================================================================================
    private void ProcessPf8Key(CicsContext ctx)
    {
        _wsAcctId = _commArea.AcctId;            // MOVE CDEMO-ACCT-ID TO WS-ACCT-ID. :270
        _wsAuthKey = _cpvdPauSelected;           // MOVE CDEMO-CPVD-PAU-SELECTED TO WS-AUTH-KEY. :271

        ReadAuthRecord(ctx);                     // PERFORM READ-AUTH-RECORD (reposition at current key). :273
        ReadNextAuthRecord(ctx);                 // PERFORM READ-NEXT-AUTH-RECORD (GNP next). :274

        if (ImsPsbSchd)
        {
            // IF IMS-PSB-SCHD. :276-279
            _imsPsbSchdFlg = "N";                // SET IMS-PSB-NOT-SCHD TO TRUE.
            TakeSyncpoint();                     // PERFORM TAKE-SYNCPOINT.
        }

        if (_authsEof)
        {
            // IF AUTHS-EOF. :281-284
            _sendEraseYes = false;               // SET SEND-ERASE-NO TO TRUE.
            _wsMessage = "Already at the last Authorization...";
        }
        else
        {
            _cpvdPauSelected = _detailRec.AuthKey; // MOVE PA-AUTHORIZATION-KEY TO CDEMO-CPVD-PAU-SELECTED. :286
            PopulateAuthDetails();               // PERFORM POPULATE-AUTH-DETAILS. :287
        }
    }

    // =============================================================================================
    //  POPULATE-AUTH-DETAILS (format detail -> map output) — source: COPAUS1C.cbl:291-358
    // =============================================================================================
    private void PopulateAuthDetails()
    {
        if (!ErrFlgOff) return; // IF ERR-FLG-OFF (else leave the map cleared). :294

        PautDetail pa = _detailRec;

        SetOut("CARDNUM", Fixed(pa.CardNum, 16));            // MOVE PA-CARD-NUM TO CARDNUMO. :295

        // Auth date MM/DD/YY from PA-AUTH-ORIG-DATE (YYMMDD): split (1:2)/(3:2)/(5:2) -> YY/MM/DD. :297-300
        string od = Fixed(pa.AuthOrigDate, 6);
        string yy = od.Substring(0, 2);
        string mm = od.Substring(2, 2);
        string dd = od.Substring(4, 2);
        _wsAuthDate = $"{mm}/{dd}/{yy}";                     // MOVE WS-CURDATE-MM-DD-YY TO WS-AUTH-DATE.
        SetOut("AUTHDT", _wsAuthDate);                       // MOVE WS-AUTH-DATE TO AUTHDTO. :301

        // Auth time HH:MM:SS — overlay digit pairs onto the '00:00:00' init, keeping ':' (FB-1). :303-306
        string ot = Fixed(pa.AuthOrigTime, 6);
        char[] t = _wsAuthTime.ToCharArray();                // persisted '00:00:00' (or last turn's value).
        t[0] = ot[0]; t[1] = ot[1];                          // WS-AUTH-TIME(1:2) <- PA-AUTH-ORIG-TIME(1:2).
        t[3] = ot[2]; t[4] = ot[3];                          // (4:2) <- (3:2).
        t[6] = ot[4]; t[7] = ot[5];                          // (7:2) <- (5:2).
        _wsAuthTime = new string(t);
        SetOut("AUTHTM", _wsAuthTime);                       // MOVE WS-AUTH-TIME TO AUTHTMO. :306

        // Amount — PA-APPROVED-AMT into PIC -zzzzzzz9.99, truncate toward zero. :308-309
        string wsAuthAmt = EditedNumeric.Format(pa.ApprovedAmt, "-zzzzzzz9.99");
        SetOut("AUTHAMT", wsAuthAmt);                        // MOVE WS-AUTH-AMT TO AUTHAMTO.

        // Response indicator. :311-317
        if (pa.AuthRespCode == "00")
        {
            SetOut("AUTHRSP", "A");                           // MOVE 'A' TO AUTHRSPO.
            _map.Field("AUTHRSP").ColorOverride = BmsColor.Green; // MOVE DFHGREEN TO AUTHRSPC.
        }
        else
        {
            SetOut("AUTHRSP", "D");                           // MOVE 'D' TO AUTHRSPO.
            _map.Field("AUTHRSP").ColorOverride = BmsColor.Red;   // MOVE DFHRED TO AUTHRSPC.
        }

        // Decline-reason text (SEARCH ALL binary search on DECL-CODE = PA-AUTH-RESP-REASON). :319-328
        string reason = Fixed(pa.AuthRespReason, 4);
        string authRsn = SearchDeclineReason(reason, out bool found);
        if (found)
        {
            // WHEN found -> '<reason>-<desc>' in 20-char AUTHRSNO. :324-327
            SetOut("AUTHRSN", BuildAuthRsn(reason, authRsn));
        }
        else
        {
            // AT END -> '9999' + '-' + 'ERROR'. :320-323
            SetOut("AUTHRSN", BuildAuthRsn("9999", "ERROR"));
        }

        SetOut("AUTHCD", Fixed6Num(pa.ProcessingCode));      // MOVE PA-PROCESSING-CODE TO AUTHCDO. :331
        SetOut("POSEMD", Fixed2Num(pa.PosEntryMode));        // MOVE PA-POS-ENTRY-MODE TO POSEMDO. :332
        SetOut("AUTHSRC", Fixed(pa.MessageSource, 6));       // MOVE PA-MESSAGE-SOURCE TO AUTHSRCO. :333
        SetOut("MCCCD", Fixed(pa.MerchantCatagoryCode, 4));  // MOVE PA-MERCHANT-CATAGORY-CODE TO MCCCDO. :334

        // Card expiry MM/YY — positional slice of the raw 4 bytes 'xx/yy' (FB-4). :336-338
        string ce = Fixed(pa.CardExpiryDate, 4);
        SetOut("CRDEXP", ce.Substring(0, 2) + "/" + ce.Substring(2, 2));

        SetOut("AUTHTYP", Fixed(pa.AuthType, 4));            // MOVE PA-AUTH-TYPE TO AUTHTYPO. :340
        SetOut("TRNID", Fixed(pa.TransactionId, 15));        // MOVE PA-TRANSACTION-ID TO TRNIDO. :341
        SetOut("AUTHMTC", Fixed(pa.MatchStatus, 1));         // MOVE PA-MATCH-STATUS TO AUTHMTCO. :342

        // Fraud status. :344-350
        if (pa.AuthFraud == "F" || pa.AuthFraud == "R")
        {
            // '<F/R>-<rpt-date>' into 10-char AUTHFRDO. :345-347
            string frd = Fixed(pa.AuthFraud, 1) + "-" + Fixed(pa.FraudRptDate, 8);
            SetOut("AUTHFRD", Fixed(frd, 10));
        }
        else
        {
            SetOut("AUTHFRD", "-");                           // MOVE '-' TO AUTHFRDO. :349
        }

        SetOut("MERNAME", Fixed(pa.MerchantName, 22));       // MOVE PA-MERCHANT-NAME TO MERNAMEO. :352
        SetOut("MERID", Fixed(pa.MerchantId, 15));           // MOVE PA-MERCHANT-ID TO MERIDO. :353
        SetOut("MERCITY", Fixed(pa.MerchantCity, 13));       // MOVE PA-MERCHANT-CITY TO MERCITYO. :354
        SetOut("MERST", Fixed(pa.MerchantState, 2));         // MOVE PA-MERCHANT-STATE TO MERSTO. :355
        SetOut("MERZIP", Fixed(pa.MerchantZip, 9));          // MOVE PA-MERCHANT-ZIP TO MERZIPO. :356
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN (XCTL out) — source: COPAUS1C.cbl:360-370
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        _commArea.FromTranId = WS_CICS_TRANID;   // MOVE WS-CICS-TRANID TO CDEMO-FROM-TRANID. :362
        _commArea.FromProgram = WS_PGM_AUTH_DTL; // MOVE WS-PGM-AUTH-DTL TO CDEMO-FROM-PROGRAM. :363
        _commArea.SetFirstEntry();               // MOVE ZEROS TO CDEMO-PGM-CONTEXT. :364 / SET CDEMO-PGM-ENTER. :365

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). :367-370
        SaveCpvdInfo();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-AUTHVIEW-SCREEN — source: COPAUS1C.cbl:373-396
    // =============================================================================================
    private void SendAuthviewScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                              // PERFORM POPULATE-HEADER-INFO. :375

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false); // MOVE WS-MESSAGE TO ERRMSGO. :377
        _map.Field("CARDNUM").CursorLength = -1;              // MOVE -1 TO CARDNUML (cursor to CARDNUM). :378

        // SEND ERASE when SEND-ERASE-YES, else SEND without ERASE (CURSOR only). :380-395
        ctx.SendMap(MapName, MapsetName, _map, new SendMapOptions
        {
            Erase = _sendEraseYes,
            FreeKb = true,   // DFHMSD CTRL=(ALARM,FREEKB).
            Cursor = -1,     // CURSOR.
        });
    }

    // =============================================================================================
    //  RECEIVE-AUTHVIEW-SCREEN — source: COPAUS1C.cbl:398-406
    // =============================================================================================
    private void ReceiveAuthviewScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COPAU1A') MAPSET('COPAU01') INTO(COPAU1AI) NOHANDLE. :400-405
        // The screen is display-only; the inbound field contents are unused, only EIBAID matters.
        ctx.ReceiveMap(MapName, MapsetName, _map);
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COPAUS1C.cbl:409-429
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        DateTime now = ctx.Clock.Now;                         // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. :411

        SetOut("TITLE01", CCDA_TITLE01);                      // MOVE CCDA-TITLE01 TO TITLE01O. :413
        SetOut("TITLE02", CCDA_TITLE02);                      // MOVE CCDA-TITLE02 TO TITLE02O. :414
        SetOut("TRNNAME", WS_CICS_TRANID);                    // MOVE WS-CICS-TRANID TO TRNNAMEO. :415
        SetOut("PGMNAME", WS_PGM_AUTH_DTL);                   // MOVE WS-PGM-AUTH-DTL TO PGMNAMEO. :416

        // CURDATE = MM/DD/YY (year = last two digits). :418-422
        SetOut("CURDATE", $"{now.Month:D2}/{now.Day:D2}/{(now.Year % 100):D2}");
        // CURTIME = HH:MM:SS. :424-428
        SetOut("CURTIME", $"{now.Hour:D2}:{now.Minute:D2}:{now.Second:D2}");
    }

    // =============================================================================================
    //  READ-AUTH-RECORD (GU summary + keyed GNP detail) — source: COPAUS1C.cbl:431-491
    // =============================================================================================
    private void ReadAuthRecord(CicsContext ctx)
    {
        SchedulePsb(ctx);                                     // PERFORM SCHEDULE-PSB. :433

        _paAcctId = _wsAcctId;                                // MOVE WS-ACCT-ID TO PA-ACCT-ID. :436
        _paAuthorizationKey = _wsAuthKey;                     // MOVE WS-AUTH-KEY TO PA-AUTHORIZATION-KEY. :437

        // EXEC DLI GU SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) WHERE(ACCNTID = PA-ACCT-ID). :439-443
        string st = _summaryRepo.ReadByKey(_paAcctId, out _summaryRec);
        _imsReturnCode = DibstatFromStatus(st);               // MOVE DIBSTAT TO IMS-RETURN-CODE. :445

        // EVALUATE TRUE. :446-462
        if (IsStatusOk(_imsReturnCode))
            _authsEof = false;                                // STATUS-OK -> SET AUTHS-NOT-EOF. :447-448
        else if (IsSegmentNotFound(_imsReturnCode) || IsEndOfDb(_imsReturnCode))
            _authsEof = true;                                 // GE/GB -> SET AUTHS-EOF. :449-451
        else
        {
            _errFlgOn = true;                                 // MOVE 'Y' TO WS-ERR-FLG. :453
            _wsMessage = " System error while reading Auth Summary: Code:" + _imsReturnCode; // :455-460
            SendAuthviewScreen(ctx);                          // PERFORM SEND-AUTHVIEW-SCREEN (FB-3 inline). :461
        }

        if (!_authsEof)
        {
            // EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(...) WHERE(PAUT9CTS = PA-AUTHORIZATION-KEY). :464-469
            // Qualified GNP = reposition at-or-after the key under this parent.
            _detailRepo.StartParentScanAt(_paAcctId, _paAuthorizationKey);
            string dst = _detailRepo.ReadNextInParent(out PautDetail? d);
            if (dst == FileStatus.Ok && d is not null) _detailRec = d;
            _imsReturnCode = DibstatFromStatus(dst);          // MOVE DIBSTAT TO IMS-RETURN-CODE. :471

            // EVALUATE TRUE. :472-488
            if (IsStatusOk(_imsReturnCode))
                _authsEof = false;                            // STATUS-OK -> SET AUTHS-NOT-EOF. :473-474
            else if (IsSegmentNotFound(_imsReturnCode) || IsEndOfDb(_imsReturnCode))
                _authsEof = true;                             // GE/GB -> SET AUTHS-EOF. :475-477
            else
            {
                _errFlgOn = true;                             // MOVE 'Y' TO WS-ERR-FLG. :479
                _wsMessage = " System error while reading Auth Details: Code:" + _imsReturnCode; // :481-486
                SendAuthviewScreen(ctx);                      // PERFORM SEND-AUTHVIEW-SCREEN (FB-3 inline). :487
            }
        }
    }

    // =============================================================================================
    //  READ-NEXT-AUTH-RECORD (unqualified GNP — advance) — source: COPAUS1C.cbl:493-518
    // =============================================================================================
    private void ReadNextAuthRecord(CicsContext ctx)
    {
        // EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS) (no WHERE — next child). :495-498
        // Advance the same parent-scoped cursor positioned by the prior READ-AUTH-RECORD.
        string st = _detailRepo.ReadNextInParent(out PautDetail? d);
        if (st == FileStatus.Ok && d is not null) _detailRec = d;
        _imsReturnCode = DibstatFromStatus(st);               // MOVE DIBSTAT TO IMS-RETURN-CODE. :500

        // EVALUATE TRUE. :501-517
        if (IsStatusOk(_imsReturnCode))
            _authsEof = false;                                // STATUS-OK -> SET AUTHS-NOT-EOF. :502-503
        else if (IsSegmentNotFound(_imsReturnCode) || IsEndOfDb(_imsReturnCode))
            _authsEof = true;                                 // GE/GB -> SET AUTHS-EOF. :504-506
        else
        {
            _errFlgOn = true;                                 // MOVE 'Y' TO WS-ERR-FLG. :508
            _wsMessage = " System error while reading next Auth: Code:" + _imsReturnCode; // :510-515
            SendAuthviewScreen(ctx);                          // PERFORM SEND-AUTHVIEW-SCREEN (FB-3 inline). :516
        }
    }

    // =============================================================================================
    //  UPDATE-AUTH-DETAILS (REPL detail with fraud flag) — source: COPAUS1C.cbl:520-552
    // =============================================================================================
    private void UpdateAuthDetails(CicsContext ctx)
    {
        // MOVE WS-FRAUD-AUTH-RECORD TO PENDING-AUTH-DETAILS (copy the fraud-stamped record back). :522
        _detailRec = _fraudData.AuthRecord;

        _log("RPT DT: " + _detailRec.FraudRptDate);           // DISPLAY 'RPT DT: ' PA-FRAUD-RPT-DATE (FB-5). :523

        // EXEC DLI REPL SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS). :525-528
        string st = _detailRepo.Update(_detailRec);
        _imsReturnCode = DibstatFromStatus(st);               // MOVE DIBSTAT TO IMS-RETURN-CODE. :530

        // EVALUATE TRUE. :531-551
        if (IsStatusOk(_imsReturnCode))
        {
            TakeSyncpoint();                                  // PERFORM TAKE-SYNCPOINT. :533
            _wsMessage = _detailRec.AuthFraud == "R"          // IF PA-FRAUD-REMOVED. :534
                ? "AUTH FRAUD REMOVED..."                     // :535
                : "AUTH MARKED FRAUD...";                     // :537
        }
        else
        {
            RollBack();                                       // PERFORM ROLL-BACK. :540
            _errFlgOn = true;                                 // MOVE 'Y' TO WS-ERR-FLG. :542
            _wsMessage = " System error while FRAUD Tagging, ROLLBACK||" + _imsReturnCode; // :544-549
            SendAuthviewScreen(ctx);                          // PERFORM SEND-AUTHVIEW-SCREEN (FB-3 inline). :550
        }
    }

    // =============================================================================================
    //  TAKE-SYNCPOINT — source: COPAUS1C.cbl:557-560
    // =============================================================================================
    private void TakeSyncpoint()
    {
        // EXEC CICS SYNCPOINT -> COMMIT. :558-559
        if (_uow is not null)
        {
            _uow.Commit();
            _uow.Dispose();
            _uow = null;
        }
    }

    // =============================================================================================
    //  ROLL-BACK — source: COPAUS1C.cbl:565-569
    // =============================================================================================
    private void RollBack()
    {
        // EXEC CICS SYNCPOINT ROLLBACK -> ROLLBACK. :566-568
        if (_uow is not null)
        {
            _uow.Rollback();
            _uow.Dispose();
            _uow = null;
        }
    }

    // =============================================================================================
    //  SCHEDULE-PSB — source: COPAUS1C.cbl:574-603
    // =============================================================================================
    private void SchedulePsb(CicsContext ctx)
    {
        // EXEC DLI SCHD PSB((PSB-NAME)) NODHABEND; MOVE DIBSTAT TO IMS-RETURN-CODE. :575-579
        _imsReturnCode = ScheduleUnitOfWork();

        if (IsPsbScheduledMoreThanOnce(_imsReturnCode))
        {
            // 'TC' -> EXEC DLI TERM; re-EXEC DLI SCHD. :580-589
            TermUnitOfWork();
            _imsReturnCode = ScheduleUnitOfWork();
        }

        if (IsStatusOk(_imsReturnCode))
        {
            _imsPsbSchdFlg = "Y";                             // SET IMS-PSB-SCHD TO TRUE. :590-591
        }
        else
        {
            _errFlgOn = true;                                 // MOVE 'Y' TO WS-ERR-FLG. :593
            _wsMessage = " System error while scheduling PSB: Code:" + _imsReturnCode; // :595-600
            SendAuthviewScreen(ctx);                          // PERFORM SEND-AUTHVIEW-SCREEN (FB-3 inline). :601
        }
    }

    /// <summary>SCHD = open/ensure the unit of work. Returns the DIBSTAT-equivalent: '  ' on success, 'TC' if already scheduled.</summary>
    private string ScheduleUnitOfWork()
    {
        if (_uow is not null) return "TC"; // PSB-SCHEDULED-MORE-THAN-ONCE.
        if (_db is null) return "  ";
        try
        {
            _uow = _db.Connection.BeginTransaction();
            return "  ";                   // STATUS-OK.
        }
        catch
        {
            return "TE";                   // COULD-NOT-SCHEDULE-PSB.
        }
    }

    /// <summary>TERM = close the unit of work (rollback any pending work) before re-SCHD. source: :581-582.</summary>
    private void TermUnitOfWork()
    {
        if (_uow is not null)
        {
            _uow.Rollback();
            _uow.Dispose();
            _uow = null;
        }
    }

    // =============================================================================================
    //  CDEMO-CPVD-INFO (de)serialize — carried across turns in the COMMAREA. source: :110-120,171
    // =============================================================================================
    // COPAUS1C reads CDEMO-ACCT-ID / CDEMO-CUST-ID from the base COMMAREA (real fields) and the CPVD
    // extension's CDEMO-CPVD-PAU-SELECTED. The CPVD extension is appended after COCOM01Y; since the typed
    // CardDemoCommArea models only the base 160 bytes, the program-private CPVD fields this program touches
    // (PAU-SEL-FLG + PAU-SELECTED) are packed into the customer-name slots so they round-trip each turn.
    private string _cpvdFraudData = ""; // CDEMO-CPVD-FRAUD-DATA X(100) — cleared each turn, otherwise unused. :120,172

    private void SaveCpvdInfo()
    {
        string packed = Fixed(_cpvdPauSelFlg, 1) + Fixed(_cpvdPauSelected, 8);
        packed = Fixed(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCpvdInfo()
    {
        string packed = Fixed(_commArea.CustFName, 25) + Fixed(_commArea.CustMName, 25) + Fixed(_commArea.CustLName, 25);
        packed = Fixed(packed, 75);
        char sf = packed[0];
        _cpvdPauSelFlg = sf == ' ' || sf == '\0' ? "" : sf.ToString();
        _cpvdPauSelected = packed.Substring(1, 8).TrimEnd();
    }

    /// <summary>INITIALIZE CARDDEMO-COMMAREA (cold start) — zero/space the base + CPVD fields. :166</summary>
    private void ResetCommArea()
    {
        _commArea.FromTranId = ""; _commArea.FromProgram = "";
        _commArea.ToTranId = ""; _commArea.ToProgram = "";
        _commArea.UserId = ""; _commArea.UserType = ""; _commArea.PgmContext = 0;
        _commArea.CustId = 0; _commArea.CustFName = ""; _commArea.CustMName = ""; _commArea.CustLName = "";
        _commArea.AcctId = 0; _commArea.AcctStatus = ""; _commArea.CardNum = 0;
        _commArea.LastMap = ""; _commArea.LastMapSet = "";
        _cpvdPauSelFlg = ""; _cpvdPauSelected = ""; _cpvdFraudData = "";
    }

    // =============================================================================================
    //  LINK to COPAUS2C — source: COPAUS1C.cbl:248-252
    // =============================================================================================
    /// <summary>
    /// EXEC CICS LINK PROGRAM('COPAUS2C') COMMAREA(WS-FRAUD-DATA) NOHANDLE — synchronous nested call to the
    /// fraud DB2 worker (<see cref="Copaus2c"/>), which shares this task's connection/unit of work. Returns
    /// whether EIBRESP would be NORMAL (true unless the linked program itself failed catastrophically).
    /// </summary>
    private bool LinkCopaus2c()
    {
        if (_db is null) return true; // headless: NORMAL with no work.
        try
        {
            var sub = new Copaus2c(_db, null);
            sub.Run(_fraudData);
            return true; // EIBRESP = DFHRESP(NORMAL).
        }
        catch
        {
            return false; // abnormal LINK -> ROLL-BACK path.
        }
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics + DIBSTAT mapping
    // =============================================================================================

    /// <summary>Sets a named output field's symbolic ...O value. Null/empty clears it.</summary>
    private void SetOut(string name, string? value) => _map.Field(name).SetValue(value, setMdt: false);

    /// <summary>MOVE LOW-VALUES TO COPAU1AO — blank every named output field + clear per-turn overrides. :210</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    /// <summary>
    /// SEARCH ALL WS-DECLINE-REASON-TAB on DECL-CODE = reason. Returns the 16-char DECL-DESC and sets
    /// <paramref name="found"/>; on miss returns "" with found=false (the AT END path). The table is
    /// pre-sorted ascending (binary-search semantics; a sorted lookup is equivalent). source: :319-328.
    /// </summary>
    private static string SearchDeclineReason(string reason, out bool found)
    {
        foreach ((string code, string desc) in DeclineReasonTab)
        {
            if (code == reason) { found = true; return desc; }
        }
        found = false;
        return "";
    }

    /// <summary>
    /// Builds AUTHRSNO (20 chars): code (4) into (1:4), '-' at (5:1), desc into (6:) — the COBOL overlays
    /// onto a low-value/space-cleared 20-char field. source: :321-327.
    /// </summary>
    private static string BuildAuthRsn(string code, string desc)
    {
        char[] buf = new string(' ', 20).ToCharArray();
        string c = Fixed(code, 4);
        for (int i = 0; i < 4; i++) buf[i] = c[i];   // MOVE code TO AUTHRSNO (first 4) — or PA-AUTH-RESP-REASON.
        buf[4] = '-';                                 // MOVE '-' TO AUTHRSNO(5:1).
        string d = desc ?? "";
        for (int i = 0; i < d.Length && 5 + i < 20; i++) buf[5 + i] = d[i]; // MOVE desc TO AUTHRSNO(6:).
        return new string(buf);
    }

    /// <summary>True when a 9(11) value is numeric — COMMAREA numeric reads are always digit-valued here.</summary>
    private static bool IsNumeric(long value) => value >= 0;

    /// <summary>
    /// True when NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "X NOT = SPACES AND LOW-VALUES"
    /// guard expands to (X NOT= all-SPACES) AND (X NOT= all-LOW-VALUES). A mixed space/NUL buffer is
    /// neither entirely spaces nor entirely low-values, so it passes (returns true).
    /// </summary>
    private static bool NotSpacesOrLow(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false; // empty == all-SPACES (and all-LOW-VALUES) -> fails guard.
        bool allSpaces = s.All(c => c == ' ');
        bool allLow = s.All(c => c == '\0');
        return !allSpaces && !allLow;
    }

    /// <summary>Right-pads/truncates to a fixed COBOL X(width) field with spaces.</summary>
    private static string Fixed(string? v, int width)
    {
        v ??= "";
        return v.Length >= width ? v.Substring(0, width) : v.PadRight(width, ' ');
    }

    /// <summary>PA-PROCESSING-CODE 9(06) -> AUTHCDO X(6): zero-padded 6-digit DISPLAY (low digits on overflow).</summary>
    private static string Fixed6Num(int value)
    {
        long mag = Math.Abs((long)value) % 1000000L;
        return mag.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>PA-POS-ENTRY-MODE 9(02) -> POSEMDO X(4): the 2-digit value left-justified into 4 (COBOL alphanumeric MOVE).</summary>
    private static string Fixed2Num(int value)
    {
        long mag = Math.Abs((long)value) % 100L;
        return Fixed(mag.ToString("D2", CultureInfo.InvariantCulture), 4);
    }

    /// <summary>Snapshots the working PA-detail io-area (PENDING-AUTH-DETAILS) into a fresh record (COBOL FROM/MOVE).</summary>
    private PautDetail ClonePaDetail() => new()
    {
        AcctId = _detailRec.AcctId,
        AuthKey = _detailRec.AuthKey,
        AuthDate9c = _detailRec.AuthDate9c,
        AuthTime9c = _detailRec.AuthTime9c,
        AuthOrigDate = _detailRec.AuthOrigDate,
        AuthOrigTime = _detailRec.AuthOrigTime,
        CardNum = _detailRec.CardNum,
        AuthType = _detailRec.AuthType,
        CardExpiryDate = _detailRec.CardExpiryDate,
        MessageType = _detailRec.MessageType,
        MessageSource = _detailRec.MessageSource,
        AuthIdCode = _detailRec.AuthIdCode,
        AuthRespCode = _detailRec.AuthRespCode,
        AuthRespReason = _detailRec.AuthRespReason,
        ProcessingCode = _detailRec.ProcessingCode,
        TransactionAmt = _detailRec.TransactionAmt,
        ApprovedAmt = _detailRec.ApprovedAmt,
        MerchantCatagoryCode = _detailRec.MerchantCatagoryCode,
        AcqrCountryCode = _detailRec.AcqrCountryCode,
        PosEntryMode = _detailRec.PosEntryMode,
        MerchantId = _detailRec.MerchantId,
        MerchantName = _detailRec.MerchantName,
        MerchantCity = _detailRec.MerchantCity,
        MerchantState = _detailRec.MerchantState,
        MerchantZip = _detailRec.MerchantZip,
        TransactionId = _detailRec.TransactionId,
        MatchStatus = _detailRec.MatchStatus,
        AuthFraud = _detailRec.AuthFraud,
        FraudRptDate = _detailRec.FraudRptDate,
    };

    // ---- DIBSTAT <- repository FileStatus mapping (IMS_SCHEMA.md §3) ----

    /// <summary>Maps a repository FileStatus to the IMS DIBSTAT 2-char code the EVALUATEs branch on.</summary>
    private static string DibstatFromStatus(string fileStatus) => fileStatus switch
    {
        FileStatus.Ok => "  ",                 // '  ' STATUS-OK (segment returned).
        FileStatus.RecordNotFound => "GE",     // '23' -> GE SEGMENT-NOT-FOUND.
        FileStatus.EndOfFile => "GB",          // '10' -> GB END-OF-DB.
        _ => "AI",                             // any other -> an unexpected DIBSTAT (error path).
    };

    private static bool IsStatusOk(string c) => c == "  " || c == "FW";       // 88 STATUS-OK. :80
    private static bool IsSegmentNotFound(string c) => c == "GE";             // 88 SEGMENT-NOT-FOUND. :81
    private static bool IsEndOfDb(string c) => c == "GB";                     // 88 END-OF-DB. :84
    private static bool IsPsbScheduledMoreThanOnce(string c) => c == "TC";    // 88 PSB-SCHEDULED-MORE-THAN-ONCE. :86

    // =============================================================================================
    //  BMS map builder — COPAU1A in mapset COPAU01 (24x80). source: SCREEN_COPAU01.md / bms/COPAU01.bms
    //  Display-only screen: every field ASKIP (no input). No IC (default cursor (1,1); program MOVEs
    //  -1 TO CARDNUML to drop the cursor on CARDNUM at SEND).
    // =============================================================================================
    /// <summary>The DFHMDI map name.</summary>
    public const string MapName = "COPAU1A";

    /// <summary>The DFHMSD mapset name.</summary>
    public const string MapsetName = "COPAU01";

    /// <summary>
    /// Constructs the <c>COPAU1A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> in BMS
    /// declaration order, with its Row/Col/Length/attribute/colour and initial literal. All data fields are
    /// ASKIP (display-only). source: SCREEN_COPAU01.md fields #1-54.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (#1-10) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                        // #1
            Out("TRNNAME", 1, 7, 4, BmsColor.Blue),                      // #2
            Out("TITLE01", 1, 21, 40, BmsColor.Yellow),                  // #3
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                       // #4
            OutInit("CURDATE", 1, 71, 8, BmsColor.Blue, "mm/dd/yy"),     // #5
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                        // #6
            Out("PGMNAME", 2, 7, 8, BmsColor.Blue),                      // #7
            Out("TITLE02", 2, 21, 40, BmsColor.Yellow),                  // #8
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                       // #9
            OutInit("CURTIME", 2, 71, 8, BmsColor.Blue, "hh:mm:ss"),     // #10

            // ----- 'View Authorization Details' bright heading (#11) -----
            LitBrt(4, 27, 26, BmsColor.Neutral, "View Authorization Details"), // #11

            // ----- detail body labels + output fields (#12-41) -----
            Lit(7, 2, 7, BmsColor.Turquoise, "Card #:"),                 // #12
            Out("CARDNUM", 7, 11, 16, BmsColor.Pink),                    // #13
            Lit(7, 31, 10, BmsColor.Turquoise, "Auth Date:"),           // #14
            OutInit("AUTHDT", 7, 43, 10, BmsColor.Pink, " "),            // #15
            Lit(7, 56, 10, BmsColor.Turquoise, "Auth Time:"),           // #16
            OutInit("AUTHTM", 7, 68, 10, BmsColor.Pink, " "),           // #17
            Lit(9, 2, 10, BmsColor.Turquoise, "Auth Resp:"),            // #18
            OutInit("AUTHRSP", 9, 14, 1, BmsColor.Pink, " "),          // #19
            Lit(9, 18, 12, BmsColor.Turquoise, "Resp Reason:"),        // #20
            OutInit("AUTHRSN", 9, 32, 20, BmsColor.Blue, " "),         // #21
            Lit(9, 56, 10, BmsColor.Turquoise, "Auth Code:"),          // #22
            OutInit("AUTHCD", 9, 68, 6, BmsColor.Blue, " "),           // #23
            Lit(11, 2, 7, BmsColor.Turquoise, "Amount:"),              // #24
            OutInit("AUTHAMT", 11, 11, 12, BmsColor.Blue, " "),        // #25
            Lit(11, 29, 15, BmsColor.Turquoise, "POS Entry Mode:"),    // #26
            OutInit("POSEMD", 11, 46, 4, BmsColor.Blue, " "),          // #27
            Lit(11, 56, 10, BmsColor.Turquoise, "Source   :"),         // #28
            OutInit("AUTHSRC", 11, 68, 10, BmsColor.Blue, " "),        // #29
            Lit(13, 2, 9, BmsColor.Turquoise, "MCC Code:"),            // #30
            OutInit("MCCCD", 13, 13, 4, BmsColor.Blue, " "),           // #31
            Lit(13, 25, 15, BmsColor.Turquoise, "Card Exp. Date:"),    // #32
            OutInit("CRDEXP", 13, 42, 5, BmsColor.Blue, " "),          // #33
            Lit(13, 52, 10, BmsColor.Turquoise, "Auth Type:"),         // #34
            OutInit("AUTHTYP", 13, 64, 14, BmsColor.Blue, " "),        // #35
            Lit(15, 2, 18, BmsColor.Turquoise, "Tran Id:"),            // #36
            OutInit("TRNID", 15, 12, 15, BmsColor.Blue, " "),          // #37
            Lit(15, 31, 13, BmsColor.Turquoise, "Match Status:"),      // #38
            OutInit("AUTHMTC", 15, 46, 1, BmsColor.Red, " "),          // #39
            Lit(15, 52, 13, BmsColor.Turquoise, "Fraud Status:"),      // #40
            OutInit("AUTHFRD", 15, 67, 10, BmsColor.Red, " "),         // #41

            // ----- merchant separator (#42, default protection) + merchant fields (#43-52) -----
            LitDefault(17, 2, 76, BmsColor.Neutral,
                "Merchant Details " + new string('-', 59)),             // #42 (17 + 59 = 76)
            Lit(19, 2, 5, BmsColor.Turquoise, "Name:"),                // #43
            OutInit("MERNAME", 19, 9, 25, BmsColor.Blue, " "),         // #44
            Lit(19, 41, 12, BmsColor.Turquoise, "Merchant ID:"),       // #45
            OutInit("MERID", 19, 55, 15, BmsColor.Blue, " "),          // #46
            Lit(21, 2, 5, BmsColor.Turquoise, "City:"),                // #47
            OutInit("MERCITY", 21, 9, 25, BmsColor.Blue, " "),         // #48
            Lit(21, 41, 6, BmsColor.Turquoise, "State:"),              // #49
            OutInit("MERST", 21, 49, 2, BmsColor.Blue, " "),           // #50
            Lit(21, 55, 4, BmsColor.Turquoise, "Zip:"),                // #51
            OutInit("MERZIP", 21, 61, 10, BmsColor.Blue, " "),         // #52

            // ----- error line (#53) + PF-key footer (#54) -----
            OutBrtFset("ERRMSG", 23, 1, 78, BmsColor.Red),             // #53 ASKIP,BRT,FSET
            Lit(24, 1, 45, BmsColor.Yellow, " F3=Back  F5=Mark/Remove Fraud  F8=Next Auth"), // #54
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;                 // ASKIP,NORM
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;              // ASKIP,BRT
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // ASKIP,BRT,FSET
    private static BmsAttribute ProtNorm => BmsAttribute.Protected | BmsAttribute.Normal;             // default-protected literal

    // ---- field factory helpers ----

    /// <summary>Unnamed literal with ATTRB=(ASKIP,NORM) + colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed bright literal (ASKIP,BRT) — the heading.</summary>
    private static ScreenField LitBrt(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = AskipBrt, Color = color, Value = text };

    /// <summary>Unnamed literal with no ATTRB= operand (defaults protected, NORM) — the merchant separator (#42).</summary>
    private static ScreenField LitDefault(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = ProtNorm, Color = color, Value = text };

    /// <summary>Named ASKIP,NORM output field (display only).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = Askip, Color = color };

    /// <summary>Named ASKIP,NORM output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = initial };

    /// <summary>Named ASKIP,BRT,FSET output field — the error line (#53).</summary>
    private static ScreenField OutBrtFset(string name, int row, int col, int len, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = AskipBrtFset, Color = color };
}
