using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the optional online CICS COBOL program <c>COPAUS0C</c> — the "Pending
/// Authorization Summary / View Authorizations" paged list (TRANSID <c>CPVS</c>, BMS map <c>COPAU0A</c> /
/// mapset <c>COPAU00</c>), part of the IMS/DB2/MQ authorization module.
/// </summary>
/// <remarks>
/// <para>
/// COPAUS0C accepts an 11-digit account id on a 24x80 BMS screen, resolves the account through the card
/// cross-reference alternate index (<c>CXACAIX</c> = CARD_XREF by acct id), the account master
/// (<c>ACCTDAT</c> = ACCOUNT) and the customer master (<c>CUSTDAT</c> = CUSTOMER) to paint the header
/// block (name/address/phone, credit/cash limits), reads the IMS <b>pending-authorization summary</b> root
/// segment (<c>PAUTSUM0</c> = PAUT_SUMMARY) for that account, and pages through its
/// <b>pending-authorization detail</b> child segments (<c>PAUTDTL1</c> = PAUT_DETAIL), showing up to
/// <b>5 authorizations per screen</b> with PF7 (backward) / PF8 (forward). Typing <c>S</c> next to a row
/// XCTLs to the detail program (<c>COPAUS1C</c>). PF3 XCTLs to the menu (<c>COMEN01C</c>). It is
/// pseudo-conversational: every path ends with <c>EXEC CICS RETURN TRANSID('CPVS') COMMAREA(...)</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COPAUS0C.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-private <c>CDEMO-CPVS-INFO</c> trailer), and every
/// faithful bug are preserved verbatim. Money MOVEs into the edited screen fields truncate toward zero
/// (no rounding).
/// </para>
/// <para><b>VSAM / IMS DL/I → repository mapping.</b></para>
/// <list type="bullet">
/// <item>CICS <c>READ DATASET(CXACAIX) RIDFLD(acctId)</c> -> <see cref="CardXrefRepository.ReadByAltKey"/>
/// (first card on the path). source: COPAUS0C.cbl:812-862</item>
/// <item>CICS <c>READ DATASET(ACCTDAT) RIDFLD(acctId)</c> -> <see cref="AccountRepository.ReadByKey"/>.
/// source: COPAUS0C.cbl:865-912</item>
/// <item>CICS <c>READ DATASET(CUSTDAT) RIDFLD(custId)</c> -> <see cref="CustomerRepository.ReadByKey"/>.
/// source: COPAUS0C.cbl:915-963</item>
/// <item>DL/I <c>GU PAUTSUM0 WHERE(ACCNTID=PA-ACCT-ID)</c> -> <see cref="PautSummaryRepository.ReadByKey"/>
/// (STATUS-OK -> FOUND; GE/'23' -> not-found; else error). source: COPAUS0C.cbl:966-997</item>
/// <item>DL/I <c>GNP PAUTDTL1</c> (forward child scan) -> <see cref="PautDetailRepository.StartParentScan"/>
/// + <see cref="PautDetailRepository.ReadNextInParent"/>. source: COPAUS0C.cbl:457-486</item>
/// <item>DL/I <c>GNP PAUTDTL1 WHERE(PAUT9CTS=key)</c> (reposition) ->
/// <see cref="PautDetailRepository.StartParentScanAt"/> + ReadNextInParent (seek at-or-after the saved
/// 8-byte AUTH_KEY = 9s-complement, so ascending = newest first). source: COPAUS0C.cbl:488-519</item>
/// <item>DL/I <c>SCHD</c>/<c>TERM</c> + <c>EXEC CICS SYNCPOINT</c> -> the <c>WS-IMS-PSB-SCHD-FLG</c> state
/// machine: SCHD opens a logical read unit-of-work (the GNP cursor); on each SEND when a PSB is scheduled
/// it "syncpoints" + unschedules (ends the cursor). 'TC' (scheduled-more-than-once) -> TERM then re-SCHD.
/// source: COPAUS0C.cbl:684-688,1001-1031</item>
/// </list>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — Dead <c>SEND-PAULST-SCREEN</c> after <c>RETURN-TO-PREV-SCREEN</c> (PF3): the paragraph does
/// an unconditional XCTL, so the trailing PERFORM SEND can never run. Modeled: the XCTL records the outcome
/// and the subsequent SEND is guarded (unreachable). source: COPAUS0C.cbl:235-238,674-677</item>
/// <item>FB-2 — PF8 forward-paging reposition + look-ahead interplay: <c>PROCESS-PAGE-FORWARD</c> peeks one
/// extra GNP purely to set NEXT-PAGE-YES/NO and discards it; PF8 then saves CDEMO-CPVS-PAUKEY-LAST (the last
/// DISPLAYED key) and repositions to it, so the boundary handling differs between the first page and PF8
/// pages. The exact reposition+lookahead sequence is preserved; the cursor is not "optimised".
/// source: COPAUS0C.cbl:424-452,488-519,391-407</item>
/// <item>FB-3 — NOTFND on XREF/ACCT/CUST does NOT set WS-ERR-FLG (only WHEN OTHER does). It SENDs the
/// "not found" screen but, because ERR-FLG stays off, <c>GATHER-ACCOUNT-DETAILS</c> keeps going (reads ACCT
/// with a stale XREF-ACCT-ID, then CUST, then the summary), and <c>GATHER-DETAILS</c> still INITIALIZEs and
/// PROCESS-PAGE-FORWARDs on garbage — so a single not-found screen is SENT multiple times.
/// source: COPAUS0C.cbl:832-845,882-895,933-946,349-356,750-755</item>
/// <item>FB-4 — Multiple <c>SEND-PAULST-SCREEN</c> per task turn: error paragraphs SEND inline and the
/// caller SENDs again at the end of MAIN-PARA, so the map is SENT two+ times before the single RETURN; each
/// SEND also runs the SYNCPOINT/unschedule logic. Every PERFORM SEND is kept.
/// source: COPAUS0C.cbl:681-709 + every inline PERFORM SEND-PAULST-SCREEN</item>
/// <item>FB-5 — Shared date work-fields clobbered between header and grid: <c>POPULATE-AUTH-LIST</c> reuses
/// the global WS-CURDATE-YY/-MM/-DD (CSDAT01Y) to format each row's date, overwriting the header values;
/// because <c>POPULATE-HEADER-INFO</c> runs again inside SEND-PAULST-SCREEN, the header date is recomputed
/// and correct at SEND time. The same work-fields are shared faithfully. source: COPAUS0C.cbl:531-534,690,729-740</item>
/// <item>FB-6 — Verbatim message text incl. leading spaces and the spelling <c>repos.</c> in the IMS
/// reposition error. source: COPAUS0C.cbl:477,510,989,1023</item>
/// <item>FB-7 — Page-anchor array <c>CDEMO-CPVS-PAUKEY-PREV-PG OCCURS 20</c> has no bounds check; paging past
/// page 20 would overflow adjacent COMMAREA storage. Reproduced (allow the overflow) — realistic data pages
/// 5-at-a-time so it is a latent bug only. source: COPAUS0C.cbl:120,368,440</item>
/// <item>FB-8 — <c>ACCSTAT</c> map field (Acct Status) is never populated by this program; left blank to
/// match. source: COPAU00.bms:114-117 (no MOVE in cbl)</item>
/// </list>
/// </remarks>
public sealed class PendingAuthSummaryProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COPAUS0C.cbl:32-60
    // =============================================================================================
    private const string WS_PGM_AUTH_SMRY = "COPAUS0C"; // 05 WS-PGM-AUTH-SMRY PIC X(08) VALUE 'COPAUS0C'. :33
    private const string WS_PGM_AUTH_DTL = "COPAUS1C";  // 05 WS-PGM-AUTH-DTL  PIC X(08) VALUE 'COPAUS1C'. :34
    private const string WS_PGM_MENU = "COMEN01C";      // 05 WS-PGM-MENU      PIC X(08) VALUE 'COMEN01C'. :35
    private const string WS_CICS_TRANID = "CPVS";       // 05 WS-CICS-TRANID   PIC X(04) VALUE 'CPVS'.     :36

    // 05 WS-ACCTFILENAME / WS-CUSTFILENAME / WS-CARDXREFNAME-ACCT-PATH (DDnames). :38-41
    private const string WS_ACCTFILENAME = "ACCTDAT ";
    private const string WS_CUSTFILENAME = "CUSTDAT ";
    private const string WS_CARDXREFNAME_ACCT_PATH = "CXACAIX ";
    // WS-CARDFILENAME 'CARDDAT' and WS-CCXREF-FILE 'CCXREF' are declared but NEVER used. :40,42

    private string _wsMessage = "";          // 05 WS-MESSAGE PIC X(80) VALUE SPACES. :37

    // 05 WS-ACCT-ID PIC X(11). LOW-VALUES sentinel modeled as null. :44
    private string? _wsAcctId = "";
    // 05 WS-AUTH-KEY-SAVE PIC X(08). LOW-VALUES modeled as "". :45
    private string _wsAuthKeySave = "";
    // 05 WS-AUTH-APRV-STAT PIC X(01). :46
    private string _wsAuthAprvStat = "";

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. :47-48 ; WS-RESP-CD-DIS / WS-REAS-CD-DIS PIC 9(09). :49-50
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-IDX PIC S9(04) COMP. :52 (WS-REC-COUNT/WS-PAGE-NUM declared but unused.)
    private int _wsIdx;

    // 05 WS-AUTH-DATE PIC X(08) VALUE '00/00/00'. :59 ; WS-AUTH-TIME PIC X(08) VALUE '00:00:00'. :60
    private string _wsAuthDate = "00/00/00";
    private string _wsAuthTime = "00:00:00";

    // Shared CSDAT01Y date work-fields (WS-CURDATE-YY/-MM/-DD). FB-5: reused by header AND grid. :531-534,736-740
    private string _wsCurdateYy = "";
    private string _wsCurdateMm = "";
    private string _wsCurdateDd = "";

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared header / messages.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  WS-IMS-VARIABLES — source: COPAUS0C.cbl:74-90
    // =============================================================================================
    // 88 STATUS-OK VALUE '  ','FW' ; SEGMENT-NOT-FOUND 'GE' ; END-OF-DB 'GB' ;
    // PSB-SCHEDULED-MORE-THAN-ONCE 'TC'. DIBSTAT held in IMS-RETURN-CODE X(02). :78-87
    private string _imsReturnCode = "  ";
    private bool StatusOk => _imsReturnCode == "  " || _imsReturnCode == "FW"; // 88 STATUS-OK
    private bool SegmentNotFound => _imsReturnCode == "GE";                    // 88 SEGMENT-NOT-FOUND
    private bool EndOfDb => _imsReturnCode == "GB";                            // 88 END-OF-DB
    private bool PsbScheduledMoreThanOnce => _imsReturnCode == "TC";           // 88 PSB-SCHEDULED-MORE-THAN-ONCE

    // 05 WS-IMS-PSB-SCHD-FLG: 88 IMS-PSB-SCHD='Y' / IMS-PSB-NOT-SCHD='N'. :88-90
    private char _imsPsbSchdFlg = 'N';
    private bool ImsPsbSchd => _imsPsbSchdFlg == 'Y';   // 88 IMS-PSB-SCHD
    private void SetImsPsbSchd() => _imsPsbSchdFlg = 'Y';
    private void SetImsPsbNotSchd() => _imsPsbSchdFlg = 'N';

    // =============================================================================================
    //  WS-SWITCHES — source: COPAUS0C.cbl:93-114
    // =============================================================================================
    // 05 WS-PAUT-SMRY-SEG-FLG: 88 FOUND-PAUT-SMRY-SEG='Y' / NFOUND-PAUT-SMRY-SEG='N'. :103-105
    private char _pautSmrySegFlg = 'N';
    private bool FoundPautSmrySeg => _pautSmrySegFlg == 'Y';
    private void SetFoundPautSmrySeg() => _pautSmrySegFlg = 'Y';
    private void SetNfoundPautSmrySeg() => _pautSmrySegFlg = 'N';

    // 05 WS-ERR-FLG PIC X(1) VALUE 'N': 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. :106-108
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON
    private bool ErrFlgOff => !_errFlgOn; // 88 ERR-FLG-OFF

    // 05 WS-AUTHS-EOF PIC X(1) VALUE 'N': 88 AUTHS-EOF='Y' / AUTHS-NOT-EOF='N'. :109-111
    private bool _authsEof;
    private bool AuthsEof => _authsEof;        // 88 AUTHS-EOF
    private bool AuthsNotEof => !_authsEof;    // 88 AUTHS-NOT-EOF
    private void SetAuthsEof() => _authsEof = true;
    private void SetAuthsNotEof() => _authsEof = false;

    // 05 WS-SEND-ERASE-FLG PIC X(1) VALUE 'Y': 88 SEND-ERASE-YES='Y' / SEND-ERASE-NO='N'. :112-114
    private bool _sendEraseYes = true;
    private bool SendEraseYes => _sendEraseYes; // 88 SEND-ERASE-YES
    private void SetSendEraseYes() => _sendEraseYes = true;
    private void SetSendEraseNo() => _sendEraseYes = false;

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CPVS-INFO (program-private paging state). source: COPAUS0C.cbl:117-126
    // =============================================================================================
    // 10 CDEMO-CPVS-PAU-SEL-FLG    PIC X(01). :118
    private string _cpvsPauSelFlg = "";
    // 10 CDEMO-CPVS-PAU-SELECTED   PIC X(08). :119
    private string _cpvsPauSelected = "";
    // 10 CDEMO-CPVS-PAUKEY-PREV-PG PIC X(08) OCCURS 20. :120 (1-based indices 1..20)
    private readonly string[] _cpvsPaukeyPrevPg = new string[21]; // [0] unused
    // 10 CDEMO-CPVS-PAUKEY-LAST    PIC X(08). LOW-VALUES modeled as "". :121
    private string _cpvsPaukeyLast = "";
    // 10 CDEMO-CPVS-PAGE-NUM       PIC S9(04) COMP. :122
    private int _cpvsPageNum;
    // 10 CDEMO-CPVS-NEXT-PAGE-FLG  PIC X(01) VALUE 'N': 88 NEXT-PAGE-YES='Y' / NEXT-PAGE-NO='N'. :123-125
    private char _cpvsNextPageFlg = 'N';
    private bool NextPageYes => _cpvsNextPageFlg == 'Y'; // 88 NEXT-PAGE-YES
    private void SetNextPageYes() => _cpvsNextPageFlg = 'Y';
    private void SetNextPageNo() => _cpvsNextPageFlg = 'N';
    // 10 CDEMO-CPVS-AUTH-KEYS      PIC X(08) OCCURS 5. :126 (1-based indices 1..5)
    private readonly string[] _cpvsAuthKeys = new string[6]; // [0] unused

    // =============================================================================================
    //  Record images read from the files / IMS segments.
    // =============================================================================================
    private CardXref? _cardXref;                 // CARD-XREF-RECORD (CVACT03Y)
    private Account? _account;                    // ACCOUNT-RECORD (CVACT01Y)
    private Customer? _customer;                  // CUSTOMER-RECORD (CVCUS01Y)
    private PautSummary? _summary;                // PENDING-AUTH-SUMMARY (CIPAUSMY)
    private PautDetail? _detail;                  // PENDING-AUTH-DETAILS (CIPAUDTY) — the current GNP row

    // The IMS "current parentage" for GNP — set by the last GU summary (CDEMO-ACCT-ID). :971
    private long _pautParentAcctId;

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: COPAUS0C.cbl:172-174,200
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private CardXrefRepository _xrefs = null!;
    private AccountRepository _accounts = null!;
    private CustomerRepository _customers = null!;
    private PautSummaryRepository _pautSummaries = null!;
    private PautDetailRepository _pautDetails = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. Per-table repositories are created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here). The PAUT_DETAIL
    /// repository holds the GNP cursor across the paragraph PERFORMs within one turn.
    /// </summary>
    public PendingAuthSummaryProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public PendingAuthSummaryProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGM_AUTH_SMRY; // PROGRAM-ID. COPAUS0C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_CICS_TRANID;       // CSD: CPVS -> COPAUS0C. source: :36

    // =============================================================================================
    //  MAIN-PARA — source: COPAUS0C.cbl:178-257
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COPAU00 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null)
        {
            _xrefs = new CardXrefRepository(_db.Connection);
            _accounts = new AccountRepository(_db.Connection);
            _customers = new CustomerRepository(_db.Connection);
            _pautSummaries = new PautSummaryRepository(_db.Connection);
            _pautDetails = new PautDetailRepository(_db.Connection);
        }

        // SET ERR-FLG-OFF / AUTHS-NOT-EOF / NEXT-PAGE-NO / SEND-ERASE-YES TO TRUE. source: :181-184
        _errFlgOn = false;
        SetAuthsNotEof();
        SetNextPageNo();
        SetSendEraseYes();

        // MOVE SPACES TO WS-MESSAGE ERRMSGO OF COPAU0AO. source: :186
        _wsMessage = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        // MOVE -1 TO ACCTIDL OF COPAU0AI. source: :188 (cursor -> ACCTID field)
        _map.Field("ACCTID").CursorLength = -1;

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 — first entry, no commarea. source: :190-198
            _commArea = new CardDemoCommArea(); // INITIALIZE CARDDEMO-COMMAREA. :191
            _commArea.ToProgram = WS_PGM_AUTH_SMRY; // MOVE WS-PGM-AUTH-SMRY TO CDEMO-TO-PROGRAM. :192
            _commArea.SetReenter();                 // SET CDEMO-PGM-REENTER TO TRUE. :194
            MoveLowValuesToMapOut();                // MOVE LOW-VALUES TO COPAU0AO. :195
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :196
            InitCpvsInfo();                         // CDEMO-CPVS-INFO is INITIALIZEd with the commarea.
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :198
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :200
            _commArea = ctx.CommArea!;
            RestoreCpvsInfo(ctx);

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER — arriving from another program (menu / back from detail). :202-219
                _commArea.SetReenter();   // SET CDEMO-PGM-REENTER TO TRUE. :203
                MoveLowValuesToMapOut();  // MOVE LOW-VALUES TO COPAU0AO. :205

                // IF CDEMO-ACCT-ID IS NUMERIC -> move to WS-ACCT-ID + ACCTIDO; ELSE blank. source: :207-213
                if (CdemoAcctIdIsNumeric())
                {
                    _wsAcctId = Zoned(_commArea.AcctId, 11);              // MOVE CDEMO-ACCT-ID TO WS-ACCT-ID. :208
                    _map.Field("ACCTID").SetValue(_wsAcctId, setMdt: false); // ... ACCTIDO. :209
                }
                else
                {
                    _map.Field("ACCTID").SetValue(" ", setMdt: false);   // MOVE SPACE TO ACCTIDO. :211
                    _wsAcctId = null;                                     // MOVE LOW-VALUES TO WS-ACCT-ID. :212
                }

                GatherDetails(ctx);       // PERFORM GATHER-DETAILS. :215
                SetSendEraseYes();        // SET SEND-ERASE-YES TO TRUE. :217
                SendPaulstScreen(ctx);    // PERFORM SEND-PAULST-SCREEN. :219
            }
            else
            {
                // ELSE — true reentry after our own RETURN. source: :221-251
                ReceivePaulstScreen(ctx); // PERFORM RECEIVE-PAULST-SCREEN. :222

                // EVALUATE EIBAID. source: :224-250
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);  // WHEN DFHENTER -> PERFORM PROCESS-ENTER-KEY. :225-226
                        if (ctx.Outcome is not null) break; // XCTL to COPAUS1C taken inside PROCESS-ENTER-KEY.

                        // IF WS-ACCT-ID = LOW-VALUES -> SPACE else show it. source: :228-232
                        if (_wsAcctId is null)
                            _map.Field("ACCTID").SetValue(" ", setMdt: false);
                        else
                            _map.Field("ACCTID").SetValue(_wsAcctId, setMdt: false);

                        SendPaulstScreen(ctx); // PERFORM SEND-PAULST-SCREEN. :234
                        break;

                    case AidKey.Pf3:
                        _commArea.ToProgram = WS_PGM_MENU; // MOVE WS-PGM-MENU TO CDEMO-TO-PROGRAM. :236
                        ReturnToPrevScreen(ctx);           // PERFORM RETURN-TO-PREV-SCREEN (XCTL). :237
                        // FB-1: PERFORM SEND-PAULST-SCREEN is dead after the unconditional XCTL. :238
                        if (ctx.Outcome is null) SendPaulstScreen(ctx);
                        break;

                    case AidKey.Pf7:
                        ProcessPf7Key(ctx);    // WHEN DFHPF7 -> PERFORM PROCESS-PF7-KEY. :239-240
                        if (ctx.Outcome is null) SendPaulstScreen(ctx); // PERFORM SEND-PAULST-SCREEN. :241
                        break;

                    case AidKey.Pf8:
                        ProcessPf8Key(ctx);    // WHEN DFHPF8 -> PERFORM PROCESS-PF8-KEY. :242-243
                        if (ctx.Outcome is null) SendPaulstScreen(ctx); // PERFORM SEND-PAULST-SCREEN. :244
                        break;

                    default:
                        // WHEN OTHER. source: :245-249
                        _errFlgOn = true;                          // MOVE 'Y' TO WS-ERR-FLG. :246
                        _map.Field("ACCTID").CursorLength = -1;    // MOVE -1 TO ACCTIDL. :247
                        _wsMessage = CCDA_MSG_INVALID_KEY;         // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. :248
                        SendPaulstScreen(ctx);                     // PERFORM SEND-PAULST-SCREEN. :249
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-CICS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :254-257
        if (ctx.Outcome is null)
        {
            SaveCpvsInfo(ctx);
            ctx.ReturnTransId(WS_CICS_TRANID, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COPAUS0C.cbl:261-338
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        string acctidi = _map.Field("ACCTID").Value; // ACCTIDI OF COPAU0AI (raw keyed value)

        // IF ACCTIDI = SPACES OR LOW-VALUES. source: :264-271
        if (IsSpacesOrLowValues(acctidi))
        {
            _wsAcctId = null;                              // MOVE LOW-VALUES TO WS-ACCT-ID. :265
            _errFlgOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. :267
            _wsMessage = "Please enter Acct Id...";        // source: :268-269
            _map.Field("ACCTID").CursorLength = -1;        // MOVE -1 TO ACCTIDL. :271
        }
        else
        {
            // IF ACCTIDI IS NOT NUMERIC. source: :273-281
            if (!IsNumericX(acctidi, 11))
            {
                _wsAcctId = null;                          // MOVE LOW-VALUES TO WS-ACCT-ID. :274
                _errFlgOn = true;                          // MOVE 'Y' TO WS-ERR-FLG. :276
                _wsMessage = "Acct Id must be Numeric ..."; // source: :277-278
                _map.Field("ACCTID").CursorLength = -1;    // MOVE -1 TO ACCTIDL. :280
            }
            else
            {
                // MOVE ACCTIDI TO WS-ACCT-ID CDEMO-ACCT-ID. source: :283-284
                _wsAcctId = PadX(acctidi, 11);
                _commArea.AcctId = ParseLong(_wsAcctId);

                // EVALUATE TRUE — first non-blank/non-low selection row wins. source: :285-309
                if (NotSpacesOrLow(SelIn(1)))
                {
                    _cpvsPauSelFlg = SelIn(1); _cpvsPauSelected = AuthKey(1); // :286-289
                }
                else if (NotSpacesOrLow(SelIn(2)))
                {
                    _cpvsPauSelFlg = SelIn(2); _cpvsPauSelected = AuthKey(2); // :290-293
                }
                else if (NotSpacesOrLow(SelIn(3)))
                {
                    _cpvsPauSelFlg = SelIn(3); _cpvsPauSelected = AuthKey(3); // :294-297
                }
                else if (NotSpacesOrLow(SelIn(4)))
                {
                    _cpvsPauSelFlg = SelIn(4); _cpvsPauSelected = AuthKey(4); // :298-301
                }
                else if (NotSpacesOrLow(SelIn(5)))
                {
                    _cpvsPauSelFlg = SelIn(5); _cpvsPauSelected = AuthKey(5); // :302-305
                }
                else
                {
                    _cpvsPauSelFlg = "";   // WHEN OTHER -> MOVE SPACES. :306-308
                    _cpvsPauSelected = "";
                }

                // IF (SEL-FLG NOT = SPACES AND LOW-VALUES) AND (SELECTED NOT = SPACES AND LOW-VALUES). :310-332
                if (NotSpacesOrLow(_cpvsPauSelFlg) && NotSpacesOrLow(_cpvsPauSelected))
                {
                    // EVALUATE CDEMO-CPVS-PAU-SEL-FLG. source: :313-331
                    string flg = _cpvsPauSelFlg.Length > 0 ? _cpvsPauSelFlg.Substring(0, 1) : "";
                    if (flg == "S" || flg == "s")
                    {
                        // WHEN 'S'/'s' — XCTL to the detail program. source: :314-325
                        _commArea.ToProgram = WS_PGM_AUTH_DTL; // MOVE WS-PGM-AUTH-DTL  TO CDEMO-TO-PROGRAM. :316
                        _commArea.FromTranId = WS_CICS_TRANID; // MOVE WS-CICS-TRANID   TO CDEMO-FROM-TRANID. :317
                        _commArea.FromProgram = WS_PGM_AUTH_SMRY; // MOVE WS-PGM-AUTH-SMRY TO CDEMO-FROM-PROGRAM. :318
                        _commArea.PgmContext = 0;              // MOVE 0 TO CDEMO-PGM-CONTEXT. :319
                        _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER TO TRUE. :320
                        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). :322-325
                        SaveCpvsInfo(ctx);
                        ctx.Xctl(WS_PGM_AUTH_DTL, _commArea);
                        return;
                    }
                    else
                    {
                        // WHEN OTHER. source: :326-330
                        _wsMessage = "Invalid selection. Valid value is S"; // :327-329
                        _map.Field("ACCTID").CursorLength = -1;            // MOVE -1 TO ACCTIDL. :330
                    }
                }
            }
        }

        // ALWAYS (fallthrough): PERFORM GATHER-DETAILS. source: :337
        GatherDetails(ctx);
    }

    // =============================================================================================
    //  GATHER-DETAILS — source: COPAUS0C.cbl:342-358
    // =============================================================================================
    private void GatherDetails(CicsContext ctx)
    {
        _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :345
        _cpvsPageNum = 0;                        // MOVE 0 TO CDEMO-CPVS-PAGE-NUM. :347

        // IF WS-ACCT-ID NOT = LOW-VALUES. source: :349-357
        if (_wsAcctId is not null)
        {
            GatherAccountDetails(ctx);          // PERFORM GATHER-ACCOUNT-DETAILS. :350
            InitializeAuthData();               // PERFORM INITIALIZE-AUTH-DATA. :352

            if (FoundPautSmrySeg)               // IF FOUND-PAUT-SMRY-SEG. :354
                ProcessPageForward(ctx);        // PERFORM PROCESS-PAGE-FORWARD. :355
        }
    }

    // =============================================================================================
    //  PROCESS-PF7-KEY (page backward) — source: COPAUS0C.cbl:362-385
    // =============================================================================================
    private void ProcessPf7Key(CicsContext ctx)
    {
        // IF CDEMO-CPVS-PAGE-NUM > 1. source: :365
        if (_cpvsPageNum > 1)
        {
            _cpvsPageNum = _cpvsPageNum - 1; // COMPUTE CDEMO-CPVS-PAGE-NUM = CDEMO-CPVS-PAGE-NUM - 1. :366

            // MOVE CDEMO-CPVS-PAUKEY-PREV-PG(CDEMO-CPVS-PAGE-NUM) TO WS-AUTH-KEY-SAVE. :368
            _wsAuthKeySave = PrevPg(_cpvsPageNum);
            GetAuthSummary(ctx);             // PERFORM GET-AUTH-SUMMARY. :370
            if (ctx.Outcome is not null) return;

            SetSendEraseNo();                // SET SEND-ERASE-NO TO TRUE. :372
            SetNextPageYes();                // SET NEXT-PAGE-YES TO TRUE. :374
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :375

            InitializeAuthData();            // PERFORM INITIALIZE-AUTH-DATA. :377
            ProcessPageForward(ctx);         // PERFORM PROCESS-PAGE-FORWARD. :379
        }
        else
        {
            _wsMessage = "You are already at the top of the page..."; // :381-382
            SetSendEraseNo();                // SET SEND-ERASE-NO TO TRUE. :383
        }
    }

    // =============================================================================================
    //  PROCESS-PF8-KEY (page forward) — source: COPAUS0C.cbl:388-412
    // =============================================================================================
    private void ProcessPf8Key(CicsContext ctx)
    {
        // IF CDEMO-CPVS-PAUKEY-LAST = SPACES OR LOW-VALUES. source: :391-398
        if (IsSpacesOrLowValues(_cpvsPaukeyLast))
        {
            _wsAuthKeySave = ""; // MOVE LOW-VALUES TO WS-AUTH-KEY-SAVE. :392
        }
        else
        {
            _wsAuthKeySave = _cpvsPaukeyLast; // MOVE CDEMO-CPVS-PAUKEY-LAST TO WS-AUTH-KEY-SAVE. :394
            GetAuthSummary(ctx);              // PERFORM GET-AUTH-SUMMARY. :396
            if (ctx.Outcome is not null) return;
            RepositionAuthorizations(ctx);    // PERFORM REPOSITION-AUTHORIZATIONS. :397
            if (ctx.Outcome is not null) return;
        }

        _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :400
        SetSendEraseNo();                        // SET SEND-ERASE-NO TO TRUE. :402

        // IF NEXT-PAGE-YES. source: :404-411
        if (NextPageYes)
        {
            InitializeAuthData();    // PERFORM INITIALIZE-AUTH-DATA. :405
            ProcessPageForward(ctx); // PERFORM PROCESS-PAGE-FORWARD. :407
        }
        else
        {
            _wsMessage = "You are already at the bottom of the page..."; // :409-410
        }
    }

    // =============================================================================================
    //  PROCESS-PAGE-FORWARD — source: COPAUS0C.cbl:415-454
    // =============================================================================================
    private void ProcessPageForward(CicsContext ctx)
    {
        // IF ERR-FLG-OFF. source: :418
        if (ErrFlgOff)
        {
            _wsIdx = 1;                 // MOVE 1 TO WS-IDX. :420
            _cpvsPaukeyLast = "";       // MOVE LOW-VALUES TO CDEMO-CPVS-PAUKEY-LAST. :422

            // PERFORM UNTIL WS-IDX > 5 OR AUTHS-EOF OR ERR-FLG-ON. source: :424-443
            while (!(_wsIdx > 5 || AuthsEof || ErrFlgOn))
            {
                // IF EIBAID = DFHPF7 AND WS-IDX = 1 -> REPOSITION else GET-AUTHORIZATIONS. source: :425-429
                if (ctx.EibAid == AidKey.Pf7 && _wsIdx == 1)
                    RepositionAuthorizations(ctx);
                else
                    GetAuthorizations(ctx);
                if (ctx.Outcome is not null) return; // an inline SEND error path already RETURNed.

                // IF AUTHS-NOT-EOF AND ERR-FLG-OFF. source: :430-442
                if (AuthsNotEof && ErrFlgOff)
                {
                    PopulateAuthList();          // PERFORM POPULATE-AUTH-LIST. :431
                    _wsIdx = _wsIdx + 1;         // COMPUTE WS-IDX = WS-IDX + 1. :432

                    // MOVE PA-AUTHORIZATION-KEY TO CDEMO-CPVS-PAUKEY-LAST. :434-435
                    _cpvsPaukeyLast = PaAuthorizationKey();

                    if (_wsIdx == 2)             // IF WS-IDX = 2. :436
                    {
                        _cpvsPageNum = _cpvsPageNum + 1; // COMPUTE CDEMO-CPVS-PAGE-NUM = + 1. :437-438
                        // MOVE PA-AUTHORIZATION-KEY TO CDEMO-CPVS-PAUKEY-PREV-PG(CDEMO-CPVS-PAGE-NUM). :439-440
                        SetPrevPg(_cpvsPageNum, PaAuthorizationKey());
                    }
                }
            }

            // After loop, peek one extra row to set the "more pages" flag (FB-2 look-ahead). source: :445-452
            if (AuthsNotEof && ErrFlgOff)
            {
                GetAuthorizations(ctx);  // PERFORM GET-AUTHORIZATIONS. :446
                if (ctx.Outcome is not null) return;
                if (AuthsNotEof && ErrFlgOff) // IF AUTHS-NOT-EOF AND ERR-FLG-OFF. :447
                    SetNextPageYes();    // SET NEXT-PAGE-YES TO TRUE. :448
                else
                    SetNextPageNo();     // SET NEXT-PAGE-NO TO TRUE. :450
            }
        }
    }

    // =============================================================================================
    //  GET-AUTHORIZATIONS — DL/I GNP PAUTDTL1 (forward). source: COPAUS0C.cbl:457-486
    // =============================================================================================
    private void GetAuthorizations(CicsContext ctx)
    {
        // EXEC DLI GNP USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS). :461-464
        string st = _pautDetails.ReadNextInParent(out _detail);
        _imsReturnCode = DliStatus(st); // MOVE DIBSTAT TO IMS-RETURN-CODE. :466

        // EVALUATE TRUE. source: :467-484
        if (StatusOk)
        {
            SetAuthsNotEof();    // WHEN STATUS-OK -> SET AUTHS-NOT-EOF TO TRUE. :468-469
        }
        else if (SegmentNotFound || EndOfDb)
        {
            SetAuthsEof();       // WHEN SEGMENT-NOT-FOUND / END-OF-DB -> SET AUTHS-EOF TO TRUE. :470-472
        }
        else
        {
            // WHEN OTHER. source: :473-483
            _errFlgOn = true;    // MOVE 'Y' TO WS-ERR-FLG. :474
            // FB-6: leading space + verbatim text. :476-481
            _wsMessage = " System error while reading AUTH Details: Code:" + _imsReturnCode;
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :482
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :483
        }
    }

    // =============================================================================================
    //  REPOSITION-AUTHORIZATIONS — DL/I GNP WHERE(PAUT9CTS=key). source: COPAUS0C.cbl:488-519
    // =============================================================================================
    private void RepositionAuthorizations(CicsContext ctx)
    {
        // MOVE WS-AUTH-KEY-SAVE TO PA-AUTHORIZATION-KEY. :491
        // EXEC DLI GNP ... SEGMENT(PAUTDTL1) WHERE(PAUT9CTS = PA-AUTHORIZATION-KEY). :493-497
        // Reposition the parent-scoped GNP cursor at-or-after the saved 8-byte key, then read it.
        _pautDetails.StartParentScanAt(_pautParentAcctId, PadX(_wsAuthKeySave, 8));
        string st = _pautDetails.ReadNextInParent(out _detail);
        _imsReturnCode = DliStatus(st); // MOVE DIBSTAT TO IMS-RETURN-CODE. :499

        // EVALUATE TRUE. source: :500-517
        if (StatusOk)
        {
            SetAuthsNotEof();    // WHEN STATUS-OK. :501-502
        }
        else if (SegmentNotFound || EndOfDb)
        {
            SetAuthsEof();       // WHEN SEGMENT-NOT-FOUND / END-OF-DB. :503-505
        }
        else
        {
            // WHEN OTHER. source: :506-516
            _errFlgOn = true;    // MOVE 'Y' TO WS-ERR-FLG. :507
            // FB-6: leading space + the misspelling 'repos.'. :509-514
            _wsMessage = " System error while repos. AUTH Details: Code:" + _imsReturnCode;
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :515
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :516
        }
    }

    // =============================================================================================
    //  POPULATE-AUTH-LIST — source: COPAUS0C.cbl:522-605
    // =============================================================================================
    private void PopulateAuthList()
    {
        PautDetail d = _detail ?? new PautDetail();

        // MOVE PA-APPROVED-AMT TO WS-AUTH-AMT (edited -zzzzzzz9.99). :525
        string wsAuthAmt = EditAmt(d.ApprovedAmt, "-ZZZZZZZ9.99");

        // Build WS-AUTH-TIME hh:mm:ss from PA-AUTH-ORIG-TIME(1:2)(3:2)(5:2) (the ':' are the field's VALUE). :527-529
        string origTime = PadX(d.AuthOrigTime, 6);
        _wsAuthTime = origTime.Substring(0, 2) + ":" + origTime.Substring(2, 2) + ":" + origTime.Substring(4, 2);

        // Build date: PA-AUTH-ORIG-DATE(1:2/3:2/5:2) -> WS-CURDATE-YY/-MM/-DD; WS-AUTH-DATE = mm/dd/yy. :531-534
        string origDate = PadX(d.AuthOrigDate, 6);
        _wsCurdateYy = origDate.Substring(0, 2); // MOVE PA-AUTH-ORIG-DATE(1:2) TO WS-CURDATE-YY. :531
        _wsCurdateMm = origDate.Substring(2, 2); // MOVE (3:2) TO WS-CURDATE-MM. :532
        _wsCurdateDd = origDate.Substring(4, 2); // MOVE (5:2) TO WS-CURDATE-DD. :533
        _wsAuthDate = $"{_wsCurdateMm}/{_wsCurdateDd}/{_wsCurdateYy}"; // MOVE WS-CURDATE-MM-DD-YY TO WS-AUTH-DATE. :534

        // IF PA-AUTH-RESP-CODE = '00' -> 'A' else 'D'. source: :536-540
        _wsAuthAprvStat = PadX(d.AuthRespCode, 2) == "00" ? "A" : "D";

        // EVALUATE WS-IDX (1..5) — stamp the row's fields and save the auth key. source: :542-605
        int n = _wsIdx;
        if (n is < 1 or > 5) return; // WHEN OTHER -> CONTINUE. :603-604

        SetAuthKey(n, PaAuthorizationKey());                                  // CDEMO-CPVS-AUTH-KEYS(n). :544-545,...
        _map.Field($"TRNID{n:D2}").SetValue(PadX(d.TransactionId, 15), setMdt: false); // PA-TRANSACTION-ID -> TRNIDnnI. :547
        _map.Field($"PDATE{n:D2}").SetValue(_wsAuthDate, setMdt: false);      // WS-AUTH-DATE -> PDATEnnI. :548
        _map.Field($"PTIME{n:D2}").SetValue(_wsAuthTime, setMdt: false);      // WS-AUTH-TIME -> PTIMEnnI. :549
        _map.Field($"PTYPE{n:D2}").SetValue(PadX(d.AuthType, 4), setMdt: false); // PA-AUTH-TYPE -> PTYPEnnI. :550
        _map.Field($"PAPRV{n:D2}").SetValue(_wsAuthAprvStat, setMdt: false);  // WS-AUTH-APRV-STAT -> PAPRVnnI. :551
        _map.Field($"PSTAT{n:D2}").SetValue(PadX(d.MatchStatus, 1), setMdt: false); // PA-MATCH-STATUS -> PSTATnnI. :552
        _map.Field($"PAMT00{n}").SetValue(wsAuthAmt, setMdt: false);          // WS-AUTH-AMT -> PAMT00nI. :553
        // MOVE DFHBMUNP TO SELnnnnA — unprotect the (populated) selection field. :554
        SetSelAttribute(n, unprotect: true);
    }

    // =============================================================================================
    //  INITIALIZE-AUTH-DATA — clear the 5 grid rows. source: COPAUS0C.cbl:608-662
    // =============================================================================================
    private void InitializeAuthData()
    {
        // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > 5. source: :611-661
        for (_wsIdx = 1; _wsIdx <= 5; _wsIdx++)
        {
            int n = _wsIdx;
            // MOVE DFHBMPRO TO SELnnnnA — protect the (empty) selection field. :614,623,...
            SetSelAttribute(n, unprotect: false);
            // MOVE SPACES TO the row's seven data fields. :615-621,...
            _map.Field($"TRNID{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PDATE{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PTIME{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PTYPE{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PAPRV{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PSTAT{n:D2}").SetValue(" ", setMdt: false);
            _map.Field($"PAMT00{n}").SetValue(" ", setMdt: false);
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COPAUS0C.cbl:665-677
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :668-670
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_CICS_TRANID;    // MOVE WS-CICS-TRANID  TO CDEMO-FROM-TRANID. :671
        _commArea.FromProgram = WS_PGM_AUTH_SMRY;  // MOVE WS-PGM-AUTH-SMRY TO CDEMO-FROM-PROGRAM. :672
        _commArea.PgmContext = 0;                  // MOVE ZEROS TO CDEMO-PGM-CONTEXT. :673

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :674-677
        SaveCpvsInfo(ctx);
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-PAULST-SCREEN — source: COPAUS0C.cbl:681-709
    // =============================================================================================
    private void SendPaulstScreen(CicsContext ctx)
    {
        // FB-4: every error path performs this; the SYNCPOINT/unschedule runs each time.
        // IF IMS-PSB-SCHD -> SET IMS-PSB-NOT-SCHD; EXEC CICS SYNCPOINT. source: :684-688
        if (ImsPsbSchd)
        {
            SetImsPsbNotSchd();        // SET IMS-PSB-NOT-SCHD TO TRUE. :685
            // EXEC CICS SYNCPOINT — commit + end the IMS read unit-of-work (GNP cursor). :686-687
            _pautDetails.EndParentScan();
        }

        PopulateHeaderInfo(ctx);                                  // PERFORM POPULATE-HEADER-INFO. :690
        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false); // MOVE WS-MESSAGE TO ERRMSGO. :692

        // IF SEND-ERASE-YES -> SEND ... ERASE CURSOR; ELSE SEND ... CURSOR (no erase). source: :694-708
        ctx.SendMap("COPAU0A", "COPAU00", _map, new SendMapOptions
        {
            Erase = SendEraseYes,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO ACCTIDL placed throughout.
        });
    }

    // =============================================================================================
    //  RECEIVE-PAULST-SCREEN — source: COPAUS0C.cbl:712-722
    // =============================================================================================
    private void ReceivePaulstScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COPAU0A') MAPSET('COPAU00') INTO(COPAU0AI) RESP RESP2. source: :715-721
        ctx.ReceiveMap("COPAU0A", "COPAU00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COPAUS0C.cbl:726-747
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :729
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. :731
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. :732
        _map.Field("TRNNAME").SetValue(WS_CICS_TRANID, setMdt: false);     // MOVE WS-CICS-TRANID   TO TRNNAMEO. :733
        _map.Field("PGMNAME").SetValue(WS_PGM_AUTH_SMRY, setMdt: false);   // MOVE WS-PGM-AUTH-SMRY TO PGMNAMEO. :734

        // FB-5: recompute the shared WS-CURDATE work-fields from the current date (overwriting any row values). :736-738
        _wsCurdateMm = Two(now.Month);                 // MOVE WS-CURDATE-MONTH TO WS-CURDATE-MM. :736
        _wsCurdateDd = Two(now.Day);                   // MOVE WS-CURDATE-DAY   TO WS-CURDATE-DD. :737
        _wsCurdateYy = Four(now.Year).Substring(2, 2); // MOVE WS-CURDATE-YEAR(3:2) TO WS-CURDATE-YY. :738

        // MOVE WS-CURDATE-MM-DD-YY TO CURDATEO. source: :740
        _map.Field("CURDATE").SetValue($"{_wsCurdateMm}/{_wsCurdateDd}/{_wsCurdateYy}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :742-746
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  GATHER-ACCOUNT-DETAILS — source: COPAUS0C.cbl:750-808
    // =============================================================================================
    private void GatherAccountDetails(CicsContext ctx)
    {
        GetCardXrefByAcct(ctx);   // PERFORM GETCARDXREF-BYACCT. :753
        if (ctx.Outcome is not null) return;
        GetAcctDataByAcct(ctx);   // PERFORM GETACCTDATA-BYACCT. :754
        if (ctx.Outcome is not null) return;
        GetCustDataByCust(ctx);   // PERFORM GETCUSTDATA-BYCUST. :755
        if (ctx.Outcome is not null) return;

        Customer c = _customer ?? new Customer();
        Account a = _account ?? new Account();

        _map.Field("CUSTID").SetValue(Zoned(c.CustId, 9), setMdt: false); // MOVE CUST-ID TO CUSTIDO. :757

        // STRING first(DELIM SPACES) ' ' middle(1:1) ' ' last(DELIM SPACES) INTO CNAMEO. source: :758-764
        _map.Field("CNAME").SetValue(
            DelimBySpaces(c.FirstName) + " " + Substr(c.MiddleName, 1, 1) + " " + DelimBySpaces(c.LastName),
            setMdt: false);

        // STRING addr-line-1(DELIM '  ') ',' addr-line-2(DELIM '  ') INTO ADDR001O. source: :766-770
        _map.Field("ADDR001").SetValue(
            DelimByTwoSpaces(c.AddrLine1) + "," + DelimByTwoSpaces(c.AddrLine2), setMdt: false);

        // STRING addr-line-3(DELIM '  ') ',' state-cd ',' zip(1:5) INTO ADDR002O. source: :771-777
        _map.Field("ADDR002").SetValue(
            DelimByTwoSpaces(c.AddrLine3) + "," + PadX(c.AddrStateCd, 2) + "," + Substr(c.AddrZip, 1, 5),
            setMdt: false);

        _map.Field("PHONE1").SetValue(PadX(c.PhoneNum1, 15), setMdt: false); // MOVE CUST-PHONE-NUM-1 TO PHONE1O. :779
        // MOVE ACCT-CREDIT-LIMIT TO WS-DISPLAY-AMT12 (-zzzzzzz9.99) TO CREDLIMO. :780-781
        _map.Field("CREDLIM").SetValue(EditAmt(a.CreditLimit, "-ZZZZZZZ9.99"), setMdt: false);
        // MOVE ACCT-CASH-CREDIT-LIMIT TO WS-DISPLAY-AMT9 (-zzzz9.99) TO CASHLIMO. :782-783
        _map.Field("CASHLIM").SetValue(EditAmt(a.CashCreditLimit, "-ZZZZ9.99"), setMdt: false);

        GetAuthSummary(ctx);      // PERFORM GET-AUTH-SUMMARY. :785
        if (ctx.Outcome is not null) return;

        // IF FOUND-PAUT-SMRY-SEG -> paint counts/balances/amounts; ELSE MOVE ZERO. source: :787-807
        if (FoundPautSmrySeg)
        {
            PautSummary s = _summary ?? new PautSummary();
            _map.Field("APPRCNT").SetValue(DisplayCount(s.ApprovedAuthCnt), setMdt: false); // PA-APPROVED-AUTH-CNT. :788-789
            _map.Field("DECLCNT").SetValue(DisplayCount(s.DeclinedAuthCnt), setMdt: false); // PA-DECLINED-AUTH-CNT. :790-791
            _map.Field("CREDBAL").SetValue(EditAmt(s.CreditBalance, "-ZZZZZZZ9.99"), setMdt: false); // PA-CREDIT-BALANCE. :792-793
            _map.Field("CASHBAL").SetValue(EditAmt(s.CashBalance, "-ZZZZ9.99"), setMdt: false);      // PA-CASH-BALANCE. :794-795
            _map.Field("APPRAMT").SetValue(EditAmt(s.ApprovedAuthAmt, "-ZZZZ9.99"), setMdt: false);  // PA-APPROVED-AUTH-AMT. :796-797
            _map.Field("DECLAMT").SetValue(EditAmt(s.DeclinedAuthAmt, "-ZZZZ9.99"), setMdt: false);  // PA-DECLINED-AUTH-AMT. :798-799
        }
        else
        {
            // MOVE ZERO TO APPRCNTO DECLCNTO CREDBALO CASHBALO APPRAMTO DECLAMTO. source: :800-807
            // The receiving BMS symbolic fields are alphanumeric (COPAU00.cpy: APPRCNTO/DECLCNTO PIC X(3),
            // CREDBALO X(12), CASHBALO X(9), APPRAMTO/DECLAMTO X(10)). MOVE of the figurative constant ZERO
            // into an alphanumeric item fills the ENTIRE field with character '0' (not a single '0'), so each
            // field renders as its full width of '0's.
            _map.Field("APPRCNT").SetValue(new string('0', 3), setMdt: false);
            _map.Field("DECLCNT").SetValue(new string('0', 3), setMdt: false);
            _map.Field("CREDBAL").SetValue(new string('0', 12), setMdt: false);
            _map.Field("CASHBAL").SetValue(new string('0', 9), setMdt: false);
            _map.Field("APPRAMT").SetValue(new string('0', 10), setMdt: false);
            _map.Field("DECLAMT").SetValue(new string('0', 10), setMdt: false);
        }
    }

    // =============================================================================================
    //  GETCARDXREF-BYACCT — READ CXACAIX (CARD_XREF by acct id alt index). source: COPAUS0C.cbl:812-862
    // =============================================================================================
    private void GetCardXrefByAcct(CicsContext ctx)
    {
        // MOVE WS-ACCT-ID TO WS-CARD-RID-ACCT-ID-X. :817
        // EXEC CICS READ DATASET(CXACAIX) RIDFLD(WS-CARD-RID-ACCT-ID-X) INTO(CARD-XREF-RECORD) RESP. :818-826
        long acctIdKey = ParseLong(_wsAcctId ?? "");
        string fileStatus = _xrefs.ReadByAltKey(acctIdKey, out _cardXref);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :828-861
        if (_wsRespCd == (int)Resp.Normal)
        {
            // WHEN NORMAL -> MOVE XREF-CUST-ID / XREF-CARD-NUM TO CDEMO-CUST-ID / CDEMO-CARD-NUM. :829-831
            _commArea.CustId = _cardXref?.CustId ?? 0;
            _commArea.CardNum = ParseLong(_cardXref?.XrefCardNum ?? "0");
        }
        else if (_wsRespCd == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :832-845 — FB-3: does NOT set WS-ERR-FLG.
            _wsRespCdDis = _wsRespCd;  // MOVE WS-RESP-CD TO WS-RESP-CD-DIS. :833
            _wsReasCdDis = _wsReasCd;  // MOVE WS-REAS-CD TO WS-REAS-CD-DIS. :834
            // STRING 'Account:' WS-ACCT-ID ' not found in XREF file. Resp:' resp ' Reas:' reas. :836-843
            _wsMessage = Truncate80(
                "Account:" + PadX(_wsAcctId, 11) + " not found in XREF file. Resp:" + Disp9(_wsRespCdDis) +
                " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :844
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :845
        }
        else
        {
            // WHEN OTHER. source: :846-861
            _errFlgOn = true;          // MOVE 'Y' TO WS-ERR-FLG. :847
            _wsRespCdDis = _wsRespCd;  // :848
            _wsReasCdDis = _wsReasCd;  // :849
            // STRING 'Account:' WS-CARD-RID-ACCT-ID-X ' System error while reading XREF file. Resp:' ... ' Reas:' ... :851-858
            _wsMessage = Truncate80(
                "Account:" + Zoned(acctIdKey, 11) + " System error while reading XREF file. Resp:" +
                Disp9(_wsRespCdDis) + " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :859
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :860
        }
    }

    // =============================================================================================
    //  GETACCTDATA-BYACCT — READ ACCTDAT (ACCOUNT by acct id). source: COPAUS0C.cbl:865-912
    // =============================================================================================
    private void GetAcctDataByAcct(CicsContext ctx)
    {
        // MOVE XREF-ACCT-ID TO WS-CARD-RID-ACCT-ID (numeric redefine); READ uses the -X form over the same
        // bytes, so the key is the cross-ref acct id. FB-3: on a prior XREF NOTFND, XREF-ACCT-ID is stale. :868
        long acctIdKey = _cardXref?.AcctId ?? 0;
        // EXEC CICS READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X) INTO(ACCOUNT-RECORD) RESP. :869-877
        string fileStatus = _accounts.ReadByKey(acctIdKey, out _account);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :879-911
        if (_wsRespCd == (int)Resp.Normal)
        {
            // WHEN NORMAL -> CONTINUE. :880-881
        }
        else if (_wsRespCd == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :882-895 — FB-3: does NOT set WS-ERR-FLG.
            _wsRespCdDis = _wsRespCd;  // :883
            _wsReasCdDis = _wsReasCd;  // :884
            // STRING 'Account:' WS-CARD-RID-ACCT-ID-X ' not found in ACCT file. Resp:' ... ' Reas:' ... :886-893
            _wsMessage = Truncate80(
                "Account:" + Zoned(acctIdKey, 11) + " not found in ACCT file. Resp:" + Disp9(_wsRespCdDis) +
                " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :894
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :895
        }
        else
        {
            // WHEN OTHER. source: :896-911
            _errFlgOn = true;          // MOVE 'Y' TO WS-ERR-FLG. :897
            _wsRespCdDis = _wsRespCd;  // :898
            _wsReasCdDis = _wsReasCd;  // :899
            _wsMessage = Truncate80(
                "Account:" + Zoned(acctIdKey, 11) + " System error while reading ACCT file. Resp:" +
                Disp9(_wsRespCdDis) + " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :909
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :910
        }
    }

    // =============================================================================================
    //  GETCUSTDATA-BYCUST — READ CUSTDAT (CUSTOMER by cust id). source: COPAUS0C.cbl:915-963
    // =============================================================================================
    private void GetCustDataByCust(CicsContext ctx)
    {
        // MOVE XREF-CUST-ID TO WS-CARD-RID-CUST-ID. :918
        long custIdKey = _cardXref?.CustId ?? 0;
        // EXEC CICS READ DATASET(CUSTDAT) RIDFLD(WS-CARD-RID-CUST-ID-X) INTO(CUSTOMER-RECORD) RESP. :920-928
        string fileStatus = _customers.ReadByKey(custIdKey, out _customer);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :930-962
        if (_wsRespCd == (int)Resp.Normal)
        {
            // WHEN NORMAL -> CONTINUE. :931-932
        }
        else if (_wsRespCd == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :933-946 — FB-3: does NOT set WS-ERR-FLG.
            _wsRespCdDis = _wsRespCd;  // :934
            _wsReasCdDis = _wsReasCd;  // :935
            // STRING 'Customer:' WS-CARD-RID-CUST-ID-X ' not found in CUST file. Resp:' ... ' Reas:' ... :937-944
            _wsMessage = Truncate80(
                "Customer:" + Zoned(custIdKey, 9) + " not found in CUST file. Resp:" + Disp9(_wsRespCdDis) +
                " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :945
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :946
        }
        else
        {
            // WHEN OTHER. source: :947-962
            _errFlgOn = true;          // MOVE 'Y' TO WS-ERR-FLG. :948
            _wsRespCdDis = _wsRespCd;  // :949
            _wsReasCdDis = _wsReasCd;  // :950
            _wsMessage = Truncate80(
                "Customer:" + Zoned(custIdKey, 9) + " System error while reading CUST file. Resp:" +
                Disp9(_wsRespCdDis) + " Reas:" + Disp9(_wsReasCdDis));
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :960
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :961
        }
    }

    // =============================================================================================
    //  GET-AUTH-SUMMARY — DL/I GU PAUTSUM0 WHERE(ACCNTID=PA-ACCT-ID). source: COPAUS0C.cbl:966-997
    // =============================================================================================
    private void GetAuthSummary(CicsContext ctx)
    {
        SchedulePsb(ctx);  // PERFORM SCHEDULE-PSB. :969
        if (ctx.Outcome is not null) return;

        // MOVE CDEMO-ACCT-ID TO PA-ACCT-ID. (the commented-out alt uses XREF-ACCT-ID.) :971-972
        long paAcctId = _commArea.AcctId;
        _pautParentAcctId = paAcctId; // GU establishes parentage for the subsequent GNP. (IMS_SCHEMA §3.1)

        // EXEC DLI GU USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY)
        //     WHERE(ACCNTID = PA-ACCT-ID). :973-977
        string st = _pautSummaries.ReadByKey(paAcctId, out _summary);
        // SQLite repo returns '23' on not-found; the DL/I status is 'GE' (SEGMENT-NOT-FOUND). Map it.
        _imsReturnCode = st == FileStatus.Ok ? "  " : (st == FileStatus.RecordNotFound ? "GE" : "AI");

        // Position the GNP cursor on this parent's child twin chain (the GU set the IMS position). :973
        _pautDetails.StartParentScan(paAcctId);

        // EVALUATE TRUE. source: :980-996
        if (StatusOk)
        {
            SetFoundPautSmrySeg();   // WHEN STATUS-OK -> SET FOUND-PAUT-SMRY-SEG TO TRUE. :981-982
        }
        else if (SegmentNotFound)
        {
            SetNfoundPautSmrySeg();  // WHEN SEGMENT-NOT-FOUND -> SET NFOUND-PAUT-SMRY-SEG TO TRUE. :983-984
        }
        else
        {
            // WHEN OTHER. source: :985-996
            _errFlgOn = true;        // MOVE 'Y' TO WS-ERR-FLG. :986
            // FB-6: leading space + verbatim text. :988-993
            _wsMessage = " System error while reading AUTH Summary: Code:" + _imsReturnCode;
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :994
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :995
        }
    }

    // =============================================================================================
    //  SCHEDULE-PSB — DL/I SCHD PSB('PSBPAUTB') NODHABEND. source: COPAUS0C.cbl:1001-1031
    // =============================================================================================
    private void SchedulePsb(CicsContext ctx)
    {
        // EXEC DLI SCHD PSB((PSB-NAME)) NODHABEND. :1002-1005
        // Modeled: opening the IMS read unit-of-work always succeeds here ('  '); 'TC' (scheduled twice)
        // would TERM + re-SCHD. In this re-host a re-SCHD is a no-op (one shared connection per turn).
        _imsReturnCode = "  ";

        // IF PSB-SCHEDULED-MORE-THAN-ONCE -> TERM then re-SCHD + recapture status. source: :1007-1016
        if (PsbScheduledMoreThanOnce)
        {
            // EXEC DLI TERM ; EXEC DLI SCHD ... ; MOVE DIBSTAT TO IMS-RETURN-CODE. :1008-1015
            _pautDetails.EndParentScan();
            _imsReturnCode = "  ";
        }

        // IF STATUS-OK -> SET IMS-PSB-SCHD; ELSE error. source: :1017-1030
        if (StatusOk)
        {
            SetImsPsbSchd(); // SET IMS-PSB-SCHD TO TRUE. :1018
        }
        else
        {
            _errFlgOn = true; // MOVE 'Y' TO WS-ERR-FLG. :1020
            // FB-6: leading space + verbatim text. :1022-1027
            _wsMessage = " System error while scheduling PSB: Code:" + _imsReturnCode;
            _map.Field("ACCTID").CursorLength = -1; // MOVE -1 TO ACCTIDL. :1028
            SendPaulstScreen(ctx);                  // PERFORM SEND-PAULST-SCREEN. :1029
        }
    }

    // =============================================================================================
    //  WS-RESP-CD-DIS / WS-REAS-CD-DIS — PIC 9(09) display copies for the messages. source: :49-50
    // =============================================================================================
    private int _wsRespCdDis;
    private int _wsReasCdDis;

    // =============================================================================================
    //  Symbolic-map input readers + per-row helpers.
    // =============================================================================================
    private string SelIn(int n) => _map.Field($"SEL{n:D4}").Value; // SEL0001I..SEL0005I

    /// <summary>CDEMO-CPVS-AUTH-KEYS(n) read (selection lookup; set during the previous turn). source: :288-305</summary>
    private string AuthKey(int n) => _cpvsAuthKeys[n] ?? "";
    private void SetAuthKey(int n, string v) => _cpvsAuthKeys[n] = v;

    /// <summary>CDEMO-CPVS-PAUKEY-PREV-PG(n) — FB-7: no bound check (indices 1..20; overflow is latent). source: :120,368,440</summary>
    private string PrevPg(int n) => (n >= 1 && n < _cpvsPaukeyPrevPg.Length) ? (_cpvsPaukeyPrevPg[n] ?? "") : "";
    private void SetPrevPg(int n, string v)
    {
        if (n >= 1 && n < _cpvsPaukeyPrevPg.Length) _cpvsPaukeyPrevPg[n] = v;
        // n > 20 would overflow into adjacent COMMAREA storage in COBOL — silently dropped here (FB-7).
    }

    /// <summary>PA-AUTHORIZATION-KEY of the current GNP row (the CHAR(8) AUTH_KEY). source: CIPAUDTY.</summary>
    private string PaAuthorizationKey() => PadX(_detail?.AuthKey ?? "", 8);

    /// <summary>MOVE DFHBMUNP/DFHBMPRO TO SELnnnnA — toggle a row's selection field protected/unprotected.</summary>
    private void SetSelAttribute(int n, bool unprotect)
    {
        ScreenField f = _map.Field($"SEL{n:D4}");
        if (unprotect)
            // DFHBMUNP — unprotect + reset MDT (selectable row with data). source: :554
            f.AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Normal;
        else
            // DFHBMPRO — protect (empty row, not selectable). source: :614
            f.AttributeOverride = BmsAttribute.Protected | BmsAttribute.Normal;
    }

    /// <summary>MOVE LOW-VALUES TO COPAU0AO — blank every named output field + clear per-turn overrides. source: :195,205</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  Money / count edit.
    // =============================================================================================
    /// <summary>
    /// MOVE numeric TO an edited PIC (<c>-ZZZZZZZ9.99</c> or <c>-ZZZZ9.99</c>): truncate toward zero (no
    /// rounding), zero-suppress, leading floating sign. Delegates to <see cref="EditedNumeric"/>.
    /// </summary>
    private static string EditAmt(decimal value, string picture) => EditedNumeric.Format(value, picture);

    /// <summary>MOVE count TO WS-DISPLAY-COUNT PIC 9(03): low-order 3 digits, zero-padded. source: :58,788-791</summary>
    private static string DisplayCount(int value)
    {
        int mag = value < 0 ? -value : value;
        return (mag % 1000).ToString("D3");
    }

    // =============================================================================================
    //  DL/I status mapper — repository FileStatus -> the 2-char DIBSTAT the EVALUATEs test.
    // =============================================================================================
    private static string DliStatus(string fileStatus) => fileStatus switch
    {
        FileStatus.Ok => "  ",          // row returned -> spaces (STATUS-OK)
        FileStatus.EndOfFile => "GB",   // cursor exhausted -> END-OF-DB
        FileStatus.RecordNotFound => "GE", // no row -> SEGMENT-NOT-FOUND
        _ => "AI",                      // any other -> WHEN OTHER (system error)
    };

    // =============================================================================================
    //  CICS RESP mapper — repository FileStatus -> the RESP the EVALUATE WS-RESP-CD branches on.
    // =============================================================================================
    private void SetResp(string fileStatus)
    {
        _wsRespCd = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  CDEMO-CPVS-INFO transport across the pseudo-conversational turn.
    //  The typed CardDemoCommArea carries CARDDEMO-COMMAREA; the program-private CPVS trailer (which is
    //  larger than the unused COMMAREA slots) is stashed in a per-class static keyed by the COMMAREA
    //  image so it survives the RETURN/RECEIVE round trip in the console runtime (same approach as
    //  COCRDUPC/COACTUPC). source: COPAUS0C.cbl:117-126,200,254-257
    // =============================================================================================
    private static readonly Dictionary<string, CpvsInfo> _cpvsStore = new(StringComparer.Ordinal);

    private sealed record CpvsInfo(
        string SelFlg, string Selected, string[] PrevPg, string PaukeyLast,
        int PageNum, char NextPageFlg, string[] AuthKeys);

    private void InitCpvsInfo()
    {
        // INITIALIZE CDEMO-CPVS-INFO (with the commarea) — all spaces/zeroes, NEXT-PAGE-FLG VALUE 'N'.
        _cpvsPauSelFlg = "";
        _cpvsPauSelected = "";
        Array.Fill(_cpvsPaukeyPrevPg, "");
        _cpvsPaukeyLast = "";
        _cpvsPageNum = 0;
        _cpvsNextPageFlg = 'N';
        Array.Fill(_cpvsAuthKeys, "");
    }

    private void RestoreCpvsInfo(CicsContext ctx)
    {
        string key = ctx.CommArea!.ToImage();
        if (_cpvsStore.TryGetValue(key, out CpvsInfo? t))
        {
            _cpvsPauSelFlg = t.SelFlg;
            _cpvsPauSelected = t.Selected;
            Array.Copy(t.PrevPg, _cpvsPaukeyPrevPg, Math.Min(t.PrevPg.Length, _cpvsPaukeyPrevPg.Length));
            _cpvsPaukeyLast = t.PaukeyLast;
            _cpvsPageNum = t.PageNum;
            _cpvsNextPageFlg = t.NextPageFlg;
            Array.Copy(t.AuthKeys, _cpvsAuthKeys, Math.Min(t.AuthKeys.Length, _cpvsAuthKeys.Length));
        }
        else
        {
            // No stashed trailer (e.g. carried by an external caller's commarea) — INITIALIZE-equivalent.
            InitCpvsInfo();
        }
    }

    private void SaveCpvsInfo(CicsContext ctx)
    {
        string key = _commArea.ToImage();
        _cpvsStore[key] = new CpvsInfo(
            _cpvsPauSelFlg, _cpvsPauSelected,
            (string[])_cpvsPaukeyPrevPg.Clone(), _cpvsPaukeyLast,
            _cpvsPageNum, _cpvsNextPageFlg, (string[])_cpvsAuthKeys.Clone());
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
    /// COBOL class test <c>field IS NUMERIC</c> on an X(width) field: the field is the fixed COBOL width, so
    /// a partial entry has trailing spaces and every character of the full width must be a digit '0'-'9'.
    /// </summary>
    private static bool IsNumericX(string? value, int width)
    {
        string v = PadX(value, width);
        foreach (char c in v)
            if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>
    /// IF CDEMO-ACCT-ID IS NUMERIC — the typed 9(11) COMMAREA field is always a non-negative integer, so it
    /// is numeric unless it round-trips to a value that would not fit; treated as numeric here. source: :207
    /// </summary>
    private bool CdemoAcctIdIsNumeric() => _commArea.AcctId >= 0;

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

    /// <summary>Renders a 9(09) display value (WS-RESP-CD-DIS / WS-REAS-CD-DIS) zero-padded to 9 digits. source: :49-50</summary>
    private static string Disp9(int value) => Zoned(value, 9);

    /// <summary>Substring(start 1-based, len) over a fixed COBOL field, space-padded; COBOL ref-mod semantics.</summary>
    private static string Substr(string? value, int start, int len)
    {
        string v = PadX(value, start - 1 + len);
        return v.Substring(start - 1, len);
    }

    /// <summary>STRING ... DELIMITED BY SPACES — the run of characters up to (but not incl.) the first space.</summary>
    private static string DelimBySpaces(string? value)
    {
        string v = value ?? "";
        int sp = v.IndexOf(' ');
        return sp < 0 ? v : v.Substring(0, sp);
    }

    /// <summary>STRING ... DELIMITED BY '  ' — the run of characters up to the first two-space sequence.</summary>
    private static string DelimByTwoSpaces(string? value)
    {
        string v = value ?? "";
        int sp = v.IndexOf("  ", StringComparison.Ordinal);
        return sp < 0 ? v : v.Substring(0, sp);
    }

    /// <summary>Truncates a built message to the X(80) WS-MESSAGE width (STRING DELIMITED BY SIZE). source: :37</summary>
    private static string Truncate80(string s) => s.Length > 80 ? s[..80] : s;

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
    //  BMS map builder — COPAU0A in mapset COPAU00 (24x80).
    //  source: app/bms/COPAU00.bms:19-514 / SCREEN_COPAU00.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COPAU00.bms:26.</summary>
    public const string MapName = "COPAU0A";

    /// <summary>The DFHMSD mapset name. source: COPAU00.bms:19.</summary>
    public const string MapsetName = "COPAU00";

    /// <summary>
    /// Constructs the <c>COPAU0A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The keyable fields are <c>ACCTID</c>
    /// (5,19) L11 and the five per-row selection fields <c>SEL0001..SEL0005</c>; no <c>IC</c> is coded, so
    /// CICS defaults the cursor to the first unprotected field (<c>ACCTID</c>). No PICIN/PICOUT clauses
    /// appear in this map (the amounts are pre-edited by the program before the MOVE to the screen field).
    /// Note the source-order quirk: the row-5 data fields precede <c>SEL0005</c> (bms:453-496), and the
    /// Time rule (15,37) is one column left of the Time header (14,38) — both reproduced.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                                 // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                    // bms:34-37
            Out("TITLE01", 1, 21, 40, AskipFset, BmsColor.Yellow),                // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                                // bms:42-46
            OutInit("CURDATE", 1, 71, 8, AskipFset, BmsColor.Blue, "mm/dd/yy"),   // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                                 // bms:52-56
            Out("PGMNAME", 2, 7, 8, AskipFset, BmsColor.Blue),                    // bms:57-60
            Out("TITLE02", 2, 21, 40, AskipFset, BmsColor.Yellow),                // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                                // bms:65-69
            OutInit("CURTIME", 2, 71, 8, AskipFset, BmsColor.Blue, "hh:mm:ss"),   // bms:70-74

            // ----- screen sub-title (no ATTRB -> protected display literal) (bms:75-78) -----
            LitAttr(3, 30, 19, ProtDefault, BmsColor.Neutral, "View Authorizations"), // bms:75-78

            // ----- account search line (bms:79-91) -----
            Lit(5, 3, 15, BmsColor.Turquoise, "Search Acct Id:"),                 // bms:79-83
            // ACCTID: ATTRB=(FSET,NORM,UNPROT) GREEN UNDERLINE — the searched account (first unprotected).
            new ScreenField
            {
                Name = "ACCTID", Row = 5, Col = 19, Length = 11,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                    // bms:84-88
            Stopper(5, 31),                                                       // bms:89-91

            // ----- customer / account detail block (bms:92-142) -----
            LitDefault(6, 3, 6, BmsColor.Default, "Name: "),                      // bms:92-95
            Out("CNAME", 6, 10, 25, Askip, BmsColor.Blue),                        // bms:96-99
            LitDefault(6, 44, 13, BmsColor.Default, "Customer Id: "),             // bms:100-102
            Out("CUSTID", 6, 58, 9, Askip, BmsColor.Blue),                        // bms:103-106
            Out("ADDR001", 7, 10, 25, Askip, BmsColor.Blue),                      // bms:107-110
            LitDefault(7, 44, 13, BmsColor.Default, "Acct Status: "),             // bms:111-113
            Out("ACCSTAT", 7, 58, 1, Askip, BmsColor.Blue),                       // bms:114-117 (FB-8: never populated)
            Out("ADDR002", 8, 10, 25, Askip, BmsColor.Blue),                      // bms:118-121
            LitDefault(9, 10, 3, BmsColor.Default, "PH:"),                        // bms:122-124
            Out("PHONE1", 9, 15, 13, Askip, BmsColor.Blue),                       // bms:125-128
            LitDefault(9, 44, 13, BmsColor.Default, "Approval # : "),             // bms:129-131
            Out("APPRCNT", 9, 58, 3, Askip, BmsColor.Blue),                       // bms:132-135
            LitDefault(9, 64, 10, BmsColor.Default, "Decline #:"),                // bms:136-138
            Out("DECLCNT", 9, 76, 3, Askip, BmsColor.Blue),                       // bms:139-142

            // ----- limits / balances block (bms:143-196) -----
            LitDefault(11, 6, 11, BmsColor.Default, "Credit Lim:"),               // bms:143-146
            OutInit("CREDLIM", 11, 19, 12, AskipFset, BmsColor.Blue, " "),        // bms:147-151
            LitDefault(11, 35, 9, BmsColor.Default, "Cash Lim:"),                 // bms:152-155
            OutInit("CASHLIM", 11, 46, 9, AskipFset, BmsColor.Blue, " "),         // bms:156-160
            LitDefault(11, 58, 9, BmsColor.Default, "Appr Amt:"),                 // bms:161-164
            OutInit("APPRAMT", 11, 69, 10, AskipFset, BmsColor.Blue, " "),        // bms:165-169
            LitDefault(12, 6, 11, BmsColor.Default, "Credit Bal:"),               // bms:170-173
            OutInit("CREDBAL", 12, 19, 12, AskipFset, BmsColor.Blue, " "),        // bms:174-178
            LitDefault(12, 35, 9, BmsColor.Default, "Cash Bal:"),                 // bms:179-182
            OutInit("CASHBAL", 12, 46, 9, AskipFset, BmsColor.Blue, " "),         // bms:183-187
            LitDefault(12, 58, 9, BmsColor.Default, "Decl Amt:"),                 // bms:188-191
            OutInit("DECLAMT", 12, 69, 10, AskipFset, BmsColor.Blue, " "),        // bms:192-196

            // ----- list column headers (row 14) (bms:197-236) -----
            LitAttr(14, 2, 3, Askip, BmsColor.Neutral, "Sel"),                    // bms:197-201
            LitAttr(14, 8, 16, Askip, BmsColor.Neutral, " Transaction ID "),      // bms:202-206
            LitAttr(14, 27, 8, Askip, BmsColor.Neutral, "  Date  "),              // bms:207-211
            LitAttr(14, 38, 8, Askip, BmsColor.Neutral, "  Time  "),              // bms:212-216
            LitAttr(14, 49, 5, Askip, BmsColor.Neutral, "Type "),                 // bms:217-221
            LitAttr(14, 56, 3, Askip, BmsColor.Neutral, "A/D"),                   // bms:222-226
            LitAttr(14, 61, 3, Askip, BmsColor.Neutral, "STS"),                   // bms:227-231
            LitAttr(14, 67, 12, Askip, BmsColor.Neutral, "   Amount   "),         // bms:232-236

            // ----- separator rules (row 15) — Time rule at col 37 (one left of header). (bms:237-276) -----
            LitAttr(15, 2, 3, Askip, BmsColor.Neutral, "---"),                    // bms:237-241
            LitAttr(15, 8, 16, Askip, BmsColor.Neutral, "----------------"),      // bms:242-246
            LitAttr(15, 27, 8, Askip, BmsColor.Neutral, "--------"),              // bms:247-251
            LitAttr(15, 37, 8, Askip, BmsColor.Neutral, "--------"),              // bms:252-256
            LitAttr(15, 49, 4, Askip, BmsColor.Neutral, "----"),                  // bms:257-261
            LitAttr(15, 56, 3, Askip, BmsColor.Neutral, "---"),                   // bms:262-266
            LitAttr(15, 61, 3, Askip, BmsColor.Neutral, "---"),                   // bms:267-271
            LitAttr(15, 67, 12, Askip, BmsColor.Neutral, "------------"),         // bms:272-276
        };

        // ----- detail rows 1..4 (bms:277-452) -----
        for (int n = 1; n <= 4; n++)
        {
            int row = 15 + n; // rows 16..19
            fields.Add(RowSel($"SEL{n:D4}", row));             // SEL000n (row,3) L1. bms:277,321,365,409
            fields.Add(Stopper(row, 5));                       // (row,5) L0 stopper. bms:283,327,371,415
            fields.Add(RowOut($"TRNID{n:D2}", row, 8, 16));    // TRNIDnn (row,8) L16. bms:286,...
            fields.Add(RowOut($"PDATE{n:D2}", row, 27, 8));    // PDATEnn (row,27) L8. bms:291,...
            fields.Add(RowOut($"PTIME{n:D2}", row, 38, 8));    // PTIMEnn (row,38) L8. bms:296,...
            fields.Add(RowOut($"PTYPE{n:D2}", row, 49, 4));    // PTYPEnn (row,49) L4. bms:301,...
            fields.Add(RowOut($"PAPRV{n:D2}", row, 58, 1));    // PAPRVnn (row,58) L1. bms:306,...
            fields.Add(RowOut($"PSTAT{n:D2}", row, 63, 1));    // PSTATnn (row,63) L1. bms:311,...
            fields.Add(RowOut($"PAMT00{n}", row, 67, 12));     // PAMT00n (row,67) L12. bms:316,...
        }

        // ----- detail row 5 (bms:453-496): the data fields come BEFORE SEL0005 in BMS source order. -----
        fields.Add(RowOut("TRNID05", 20, 8, 16));  // bms:453-457
        fields.Add(RowOut("PDATE05", 20, 27, 8));  // bms:458-462
        fields.Add(RowOut("PTIME05", 20, 38, 8));  // bms:463-467
        fields.Add(RowOut("PTYPE05", 20, 49, 4));  // bms:468-472
        fields.Add(RowOut("PAPRV05", 20, 58, 1));  // bms:473-477
        fields.Add(RowOut("PSTAT05", 20, 63, 1));  // bms:478-482
        fields.Add(RowOut("PAMT005", 20, 67, 12)); // bms:483-487
        fields.Add(RowSel("SEL0005", 20));         // SEL0005 (20,3) L1. bms:488-493
        fields.Add(Stopper(20, 5));                // (20,5) L0 stopper. bms:494-496

        // ----- instruction + error + footer (bms:497-512) -----
        fields.Add(LitAttr(22, 12, 52, AskipBrt, BmsColor.Neutral,
            "Type 'S' to View Authorization details from the list"));         // bms:497-502
        fields.Add(Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red));     // bms:503-506
        fields.Add(LitAttr(24, 1, 48, Askip, BmsColor.Yellow,
            "ENTER=Continue  F3=Back  F7=Backward  F8=Forward"));             // bms:507-512

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)
    private static BmsAttribute ProtDefault => BmsAttribute.Protected | BmsAttribute.Normal;      // no ATTRB on a heading -> protected display

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) and the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with no ATTRB clause (COLOR=DEFAULT label) -> protected display, default colour.</summary>
    private static ScreenField LitDefault(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = ProtDefault, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT or protected heading).</summary>
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

    /// <summary>Per-row data field (TRNIDnn/PDATEnn/PTIMEnn/PTYPEnn/PAPRVnn/PSTATnn/PAMT00n): ATTRB=(ASKIP,FSET,NORM) BLUE INITIAL ' '.</summary>
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
