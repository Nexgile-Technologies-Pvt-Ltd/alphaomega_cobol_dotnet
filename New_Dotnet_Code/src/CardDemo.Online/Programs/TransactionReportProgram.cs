using System.Globalization;
using System.Text;
using CardDemo.Data;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>CORPT00C</c> — the "Transaction Reports" screen
/// (TRANSID <c>CR00</c>, BMS map <c>CORPT0A</c> / mapset <c>CORPT00</c>). The operator chooses one of three
/// report time-ranges — <b>Monthly</b> (current calendar month), <b>Yearly</b> (current calendar year), or
/// <b>Custom</b> (a user-entered start/end date range) — and, after a <c>Y/N</c> confirmation, the program
/// builds an entire JCL job-stream in working storage and writes it line-by-line to the CICS extra-partition
/// TDQ <c>JOBS</c> (wired to the internal reader). The actual transaction report is produced asynchronously
/// by the submitted batch job (<c>PROC=TRANREPT</c>); <c>CORPT00C</c> itself does <b>no</b> transaction-file
/// I/O at run time — it only emits JCL. It is pseudo-conversational: each ENTER re-drives <c>CR00</c> into
/// this same program; PF3 returns to the previous menu.
/// </summary>
/// <remarks>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name (including the source's misspelling
/// <c>WIRTE-JOBSUB-TDQ</c>) and a <c>// source: CORPT00C.cbl:NNN</c> citation. Statement order, the
/// <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage (<see cref="CardDemoCommArea"/>),
/// and every faithful bug are preserved verbatim. Money: there is none to truncate here (the program emits
/// no amounts); the date math is pure integer Gregorian day arithmetic.
/// </para>
/// <para><b>The SEND-then-RETURN termination idiom (FB-1).</b> <c>SEND-TRNRPT-SCREEN</c> ends with
/// <c>GO TO RETURN-TO-CICS</c>, which issues <c>EXEC CICS RETURN</c> — so the <em>first</em>
/// <c>PERFORM SEND-TRNRPT-SCREEN</c> in any paragraph effectively terminates the program; the PERFORM never
/// returns to its caller and the rest of <c>PROCESS-ENTER-KEY</c> / the validation chain after it does NOT
/// execute. The port reproduces this exactly: <see cref="SendTrnrptScreen"/> records the terminating RETURN
/// outcome on the context, and every caller short-circuits with <c>if (ctx.Outcome is not null) return;</c>
/// after a <c>PERFORM SEND-TRNRPT-SCREEN</c>. The later <c>IF NOT ERR-FLG-ON</c> guards are therefore largely
/// redundant on the failure paths but are reproduced faithfully.</para>
/// <para><b>TDQ <c>JOBS</c> → internal-reader sink.</b> <c>EXEC CICS WRITEQ TD QUEUE('JOBS')</c> appends one
/// 80-byte fixed record per call. There is no LE/CICS TDQ in .NET; the assembled JCL lines are captured into
/// <see cref="JobsQueue"/> (one 80-char line per WRITEQ), modelling the job-submit queue for characterization
/// per the port spec §9(a). Each WRITEQ succeeds (NORMAL); the "Unable to Write TDQ (JOBS)..." error branch
/// is reproduced but, like the COBOL on a healthy region, is not reached.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — No early return after a validation <c>PERFORM SEND-TRNRPT-SCREEN</c>, but SEND-TRNRPT-SCREEN
/// itself RETURNs to CICS (<c>GO TO RETURN-TO-CICS</c>). See the termination-idiom note above. source:
/// CORPT00C.cbl:556-591,258-456</item>
/// <item>FB-2 — Date-part validations re-echo NUMVAL-C results into the INPUT fields, then compare them as
/// text; month/day <c>00</c> pass the <c>NOT &gt; '12'</c>/<c>'31'</c> + NUMERIC checks and are accepted (only
/// CSUTLDTC may reject them). No <c>&gt;= 1</c> lower bound is added. source: CORPT00C.cbl:305-371</item>
/// <item>FB-3 — <c>/*EOF</c> and trailing blank/low-value JCL lines are WRITTEN to the TDQ before the loop
/// stops: the loop sets END-LOOP-YES <em>before</em> calling WIRTE-JOBSUB-TDQ, but the write still runs in
/// that same iteration. source: CORPT00C.cbl:498-508</item>
/// <item>FB-4 — The confirm error and success/confirm messages STRING <c>WS-REPORT-NAME</c> / <c>CONFIRMI</c>
/// <c>DELIMITED BY SPACE</c>. Kept verbatim. source: CORPT00C.cbl:449-451,465-470,484-490</item>
/// <item>FB-5 — <c>WS-MESSAGE</c> (X80) is wider than <c>ERRMSGO</c> (X78), so <c>MOVE WS-MESSAGE TO ERRMSGO</c>
/// silently drops the last 2 chars (78-char clamp; all current messages fit). source: CORPT00C.cbl:39,560</item>
/// <item>FB-6 — Dead working storage / dead branch: <c>WS-TRANSACT-EOF</c> (set, never tested),
/// <c>WS-REC-COUNT</c>, <c>WS-TRAN-AMT</c>, <c>WS-TRAN-DATE</c>, the copied <c>TRAN-RECORD</c>, and the
/// no-ERASE arm of SEND-TRNRPT-SCREEN (<c>SEND-ERASE-NO</c> never set) — carried inert. source:
/// CORPT00C.cbl:44-46,56,77-78,146,562-578</item>
/// <item>FB-7 — <c>DISPLAY 'PROCESS ENTER KEY'</c> and the WRITEQ-failure <c>DISPLAY 'RESP:'...</c> are debug
/// traces to the region log/SYSOUT, not the screen — treated as no-ops. source: CORPT00C.cbl:210,529</item>
/// <item>FB-8 — Monthly end-of-month math mutates <c>WS-CURDATE</c> in place (day=01, bump month/year) so
/// <c>WS-CURDATE-N</c> is the FIRST of NEXT month; subtracting one integer day yields the original month's last
/// day. Reproduced exactly. source: CORPT00C.cbl:223-234</item>
/// </list>
/// </remarks>
public sealed class TransactionReportProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: CORPT00C.cbl:36-79
    // =============================================================================================
    private const string WS_PGMNAME = "CORPT00C";       // 05 WS-PGMNAME PIC X(08) VALUE 'CORPT00C'. source: :37
    private const string WS_TRANID = "CR00";            // 05 WS-TRANID  PIC X(04) VALUE 'CR00'.     source: :38
    private const string WS_TRANSACT_FILE = "TRANSACT"; // 05 WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT' (dead, FB-6). source: :40

    private string _wsMessage = "";                     // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :39

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :41-43
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;

    // 05 WS-TRANSACT-EOF PIC X(01) VALUE 'N'. 88 TRANSACT-EOF='Y'/TRANSACT-NOT-EOF='N' (dead, FB-6). source: :44-46
    private char _wsTransactEof = 'N';

    // 05 WS-SEND-ERASE-FLG PIC X(01) VALUE 'Y'. 88 SEND-ERASE-YES='Y'/SEND-ERASE-NO='N'. source: :47-49
    // Set to Y once at :167 and never changed -> SEND always ERASEs; the no-ERASE arm is dead (FB-6).
    private char _wsSendEraseFlg = 'Y';
    private bool SendEraseYes => _wsSendEraseFlg == 'Y';

    // 05 WS-END-LOOP PIC X(01) VALUE 'N'. 88 END-LOOP-YES='Y'/END-LOOP-NO='N'. source: :50-52
    private bool _endLoopYes;

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :54-55
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-REC-COUNT PIC S9(04) COMP (declared, dead, FB-6). source: :56
    private int _wsRecCount;
    // 05 WS-IDX PIC S9(04) COMP — JCL-line loop subscript. source: :57
    private int _wsIdx;
    // 05 WS-REPORT-NAME PIC X(10) VALUE SPACES — 'Monthly'/'Yearly'/'Custom'. source: :58
    private string _wsReportName = "";

    // 05 WS-START-DATE group = YYYY '-' MM '-' DD (10 chars, ISO). source: :60-65
    private string _wsStartDateYyyy = "    ";
    private string _wsStartDateMm = "  ";
    private string _wsStartDateDd = "  ";
    private string WsStartDate => _wsStartDateYyyy + "-" + _wsStartDateMm + "-" + _wsStartDateDd;
    // 05 WS-END-DATE group = YYYY '-' MM '-' DD (10 chars). source: :66-71
    private string _wsEndDateYyyy = "    ";
    private string _wsEndDateMm = "  ";
    private string _wsEndDateDd = "  ";
    private string WsEndDate => _wsEndDateYyyy + "-" + _wsEndDateMm + "-" + _wsEndDateDd;

    // 05 WS-DATE-FORMAT PIC X(10) VALUE 'YYYY-MM-DD' — mask passed to CSUTLDTC. source: :72
    private const string WS_DATE_FORMAT = "YYYY-MM-DD";

    // 05 WS-NUM-99 PIC 99 VALUE 0, WS-NUM-9999 PIC 9999 VALUE 0 — NUMVAL-C scratch. source: :74-75
    private int _wsNum99;
    private int _wsNum9999;

    // 05 WS-TRAN-AMT PIC +99999999.99 / WS-TRAN-DATE PIC X(08) VALUE '00/00/00' (dead, FB-6). source: :77-78
    private string _wsTranDate = "00/00/00";
    // 05 JCL-RECORD PIC X(80) VALUE ' ' — one 80-byte JCL line buffer written to TDQ. source: :79
    private string _jclRecord = new string(' ', 80);

    // CSUTLDTC-PARM (call-area for the date validator). source: :129-136
    private string _csutldtcResult = new string(' ', 80);

    // CCDA-* (COTTL01Y / CSMSG01Y) — shared header titles + the invalid-key message.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";  // COTTL01Y CCDA-TITLE01
    private const string CCDA_TITLE02 = "              CardDemo                  ";  // COTTL01Y CCDA-TITLE02
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...";          // CSMSG01Y

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map. source: :154-157,176,199-202
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    // The relational DB is accepted for factory parity but CORPT00C performs NO file/SQL I/O (spec §2);
    // no repositories are created.
    private readonly RelationalDb? _db;

    /// <summary>
    /// The captured <c>JOBS</c> TDQ image — one 80-char JCL line per <c>EXEC CICS WRITEQ TD QUEUE('JOBS')</c>,
    /// in write order (the .NET stand-in for the internal-reader sink; port spec §9). Cleared each task.
    /// </summary>
    public List<string> JobsQueue { get; } = new();

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB for parity with the other handlers.
    /// CORPT00C performs no file/SQL I/O, so no repositories are created (no DB is opened here).
    /// </summary>
    public TransactionReportProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public TransactionReportProgram() => _db = null;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. CORPT00C. source: :24

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CR00 -> CORPT00C. source: CSD_TRANSACTIONS.md:80; cbl:38

    // =============================================================================================
    //  MAIN-PARA — source: CORPT00C.cbl:163-202
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY CORPT00 re-initialised per turn).
        _map = BuildMap();
        JobsQueue.Clear();

        _errFlgOn = false;          // SET ERR-FLG-OFF       TO TRUE. source: :165
        _wsTransactEof = 'N';       // SET TRANSACT-NOT-EOF  TO TRUE. source: :166
        _wsSendEraseFlg = 'Y';      // SET SEND-ERASE-YES    TO TRUE. source: :167

        _wsMessage = "";                                       // MOVE SPACES TO WS-MESSAGE. source: :169
        _map.Field("ERRMSG").SetValue("", setMdt: false);      //               ERRMSGO OF CORPT0AO. source: :170

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :172-174
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
            return; // XCTL terminates this task.
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :176
            _commArea = ctx.CommArea!;

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :177
                _commArea.SetReenter();                  // SET CDEMO-PGM-REENTER TO TRUE. source: :178
                MoveLowValuesToMapOut();                 // MOVE LOW-VALUES TO CORPT0AO. source: :179
                _map.Field("MONTHLY").CursorLength = -1; // MOVE -1 TO MONTHLYL OF CORPT0AI. source: :180
                SendTrnrptScreen(ctx);                   // PERFORM SEND-TRNRPT-SCREEN. source: :181
                return;                                  // SEND-TRNRPT-SCREEN RETURNs to CICS (FB-1).
            }
            else
            {
                ReceiveTrnrptScreen(ctx);                // PERFORM RECEIVE-TRNRPT-SCREEN. source: :183
                // EVALUATE EIBAID. source: :184-195
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);            // WHEN DFHENTER. source: :185-186
                        if (ctx.Outcome is not null) return; // a SEND/RETURN inside terminated the task.
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3. source: :187-189
                        _commArea.ToProgram = "COMEN01C"; // MOVE 'COMEN01C' TO CDEMO-TO-PROGRAM. source: :188
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :189
                        return;                            // XCTL terminates this task.
                    default:
                        // WHEN OTHER. source: :190-194
                        _errFlgOn = true;                          // MOVE 'Y' TO WS-ERR-FLG. source: :191
                        _map.Field("MONTHLY").CursorLength = -1;   // MOVE -1 TO MONTHLYL. source: :192
                        _wsMessage = CCDA_MSG_INVALID_KEY;         // MOVE CCDA-MSG-INVALID-KEY. source: :193
                        SendTrnrptScreen(ctx);                     // PERFORM SEND-TRNRPT-SCREEN. source: :194
                        return;                                    // SEND-TRNRPT-SCREEN RETURNs to CICS (FB-1).
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :199-202
        // (Reached only when PROCESS-ENTER-KEY ran to completion without any SEND-TRNRPT-SCREEN — which it
        //  never does, since its success tail always SENDs; kept for structural fidelity to MAIN-PARA.)
        ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: CORPT00C.cbl:208-456
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // DISPLAY 'PROCESS ENTER KEY' — debug trace to SYSOUT, not the screen (FB-7). source: :210

        string monthly = _map.Field("MONTHLY").Value;
        string yearly = _map.Field("YEARLY").Value;
        string custom = _map.Field("CUSTOM").Value;

        // EVALUATE TRUE over which report-type field is non-blank (first match wins). source: :212-443
        if (NotSpacesOrLow(monthly))
        {
            // WHEN MONTHLYI NOT = SPACES AND LOW-VALUES (Monthly). source: :213-238
            _wsReportName = "Monthly";                                  // MOVE 'Monthly' TO WS-REPORT-NAME. source: :214

            // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :215
            DateTime now = ctx.Clock.Now;
            int year = now.Year;
            int month = now.Month;
            int day = now.Day;

            // Start date = first of current month. source: :217-221
            _wsStartDateYyyy = Four(year);
            _wsStartDateMm = Two(month);
            _wsStartDateDd = "01";
            string startDate = WsStartDate;
            SetParmStartDate(startDate);                                // PARM-START-DATE-1 / -2. source: :220-221

            // End date = last day of current month = (first of next month) - 1 day (FB-8). source: :223-230
            day = 1;                                                    // MOVE 1 TO WS-CURDATE-DAY. source: :223
            month += 1;                                                 // ADD 1 TO WS-CURDATE-MONTH. source: :224
            if (month > 12)                                            // IF WS-CURDATE-MONTH > 12. source: :225
            {
                year += 1;                                              // ADD 1 TO WS-CURDATE-YEAR. source: :226
                month = 1;                                              // MOVE 1 TO WS-CURDATE-MONTH. source: :227
            }
            // COMPUTE WS-CURDATE-N = DATE-OF-INTEGER(INTEGER-OF-DATE(WS-CURDATE-N) - 1).
            // WS-CURDATE-N redefines the (now next-month, day=01) WS-CURDATE as 9(08) YYYYMMDD. source: :229-230
            DateTime firstOfNext = new DateTime(year, month, day);
            DateTime lastOfMonth = firstOfNext.AddDays(-1);
            year = lastOfMonth.Year;
            month = lastOfMonth.Month;
            day = lastOfMonth.Day;

            _wsEndDateYyyy = Four(year);                                // source: :232
            _wsEndDateMm = Two(month);                                  // source: :233
            _wsEndDateDd = Two(day);                                    // source: :234
            SetParmEndDate(WsEndDate);                                  // PARM-END-DATE-1 / -2. source: :235-236

            SubmitJobToIntrdr(ctx);                                     // PERFORM SUBMIT-JOB-TO-INTRDR. source: :238
            if (ctx.Outcome is not null) return;
        }
        else if (NotSpacesOrLow(yearly))
        {
            // WHEN YEARLYI NOT = SPACES AND LOW-VALUES (Yearly). source: :239-255
            _wsReportName = "Yearly";                                   // MOVE 'Yearly' TO WS-REPORT-NAME. source: :240

            DateTime now = ctx.Clock.Now;                              // MOVE FUNCTION CURRENT-DATE. source: :241
            int year = now.Year;

            // Start = Jan 1 of current year. source: :243-248
            _wsStartDateYyyy = Four(year);
            _wsEndDateYyyy = Four(year);                                // WS-START-DATE-YYYY = WS-END-DATE-YYYY. source: :243-244
            _wsStartDateMm = "01";                                      // source: :245-246
            _wsStartDateDd = "01";
            SetParmStartDate(WsStartDate);                             // PARM-START-DATE-1 / -2. source: :247-248

            // End = Dec 31 of current year. source: :250-253
            _wsEndDateMm = "12";
            _wsEndDateDd = "31";
            SetParmEndDate(WsEndDate);                                 // PARM-END-DATE-1 / -2. source: :252-253

            SubmitJobToIntrdr(ctx);                                     // PERFORM SUBMIT-JOB-TO-INTRDR. source: :255
            if (ctx.Outcome is not null) return;
        }
        else if (NotSpacesOrLow(custom))
        {
            // WHEN CUSTOMI NOT = SPACES AND LOW-VALUES (Custom). source: :256-436
            ProcessCustomReport(ctx);
            if (ctx.Outcome is not null) return;
        }
        else
        {
            // WHEN OTHER (no report type selected). source: :437-442
            _wsMessage = "Select a report type to print report...";    // source: :438-439
            _errFlgOn = true;                                          // MOVE 'Y' TO WS-ERR-FLG. source: :440
            _map.Field("MONTHLY").CursorLength = -1;                   // MOVE -1 TO MONTHLYL. source: :441
            SendTrnrptScreen(ctx);                                     // PERFORM SEND-TRNRPT-SCREEN. source: :442
            if (ctx.Outcome is not null) return;
        }

        // Success tail — IF NOT ERR-FLG-ON. source: :445-456
        if (!ErrFlgOn)
        {
            InitializeAllFields();                                     // PERFORM INITIALIZE-ALL-FIELDS. source: :447
            _map.Field("ERRMSG").ColorOverride = BmsColor.Green;       // MOVE DFHGREEN TO ERRMSGC. source: :448
            // STRING WS-REPORT-NAME (DELIM SPACE) ' report submitted for printing ...' (DELIM SIZE). source: :449-452
            _wsMessage = DelimBySpace(_wsReportName) + " report submitted for printing ...";
            _map.Field("MONTHLY").CursorLength = -1;                   // MOVE -1 TO MONTHLYL. source: :453
            SendTrnrptScreen(ctx);                                     // PERFORM SEND-TRNRPT-SCREEN. source: :454
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Custom report path — the CUSTOMI branch of PROCESS-ENTER-KEY. source: CORPT00C.cbl:256-436
    // ---------------------------------------------------------------------------------------------
    private void ProcessCustomReport(CicsContext ctx)
    {
        string sdtMm = _map.Field("SDTMM").Value;
        string sdtDd = _map.Field("SDTDD").Value;
        string sdtYyyy = _map.Field("SDTYYYY").Value;
        string edtMm = _map.Field("EDTMM").Value;
        string edtDd = _map.Field("EDTDD").Value;
        string edtYyyy = _map.Field("EDTYYYY").Value;

        // Empty-field guard (EVALUATE TRUE, first matching wins). source: :258-303
        if (IsSpacesOrLowValues(sdtMm))
        {
            _wsMessage = "Start Date - Month can NOT be empty...";     // source: :261-262
            _errFlgOn = true;                                          // source: :263
            _map.Field("SDTMM").CursorLength = -1;                     // MOVE -1 TO SDTMML. source: :264
            SendTrnrptScreen(ctx); return;                            // source: :265 (FB-1)
        }
        else if (IsSpacesOrLowValues(sdtDd))
        {
            _wsMessage = "Start Date - Day can NOT be empty...";       // source: :268-269
            _errFlgOn = true;                                          // source: :270
            _map.Field("SDTDD").CursorLength = -1;                     // MOVE -1 TO SDTDDL. source: :271
            SendTrnrptScreen(ctx); return;                            // source: :272 (FB-1)
        }
        else if (IsSpacesOrLowValues(sdtYyyy))
        {
            _wsMessage = "Start Date - Year can NOT be empty...";      // source: :275-276
            _errFlgOn = true;                                          // source: :277
            _map.Field("SDTYYYY").CursorLength = -1;                   // MOVE -1 TO SDTYYYYL. source: :278
            SendTrnrptScreen(ctx); return;                            // source: :279 (FB-1)
        }
        else if (IsSpacesOrLowValues(edtMm))
        {
            _wsMessage = "End Date - Month can NOT be empty...";       // source: :282-283
            _errFlgOn = true;                                          // source: :284
            _map.Field("EDTMM").CursorLength = -1;                     // MOVE -1 TO EDTMML. source: :285
            SendTrnrptScreen(ctx); return;                            // source: :286 (FB-1)
        }
        else if (IsSpacesOrLowValues(edtDd))
        {
            _wsMessage = "End Date - Day can NOT be empty...";         // source: :289-290
            _errFlgOn = true;                                          // source: :291
            _map.Field("EDTDD").CursorLength = -1;                     // MOVE -1 TO EDTDDL. source: :292
            SendTrnrptScreen(ctx); return;                            // source: :293 (FB-1)
        }
        else if (IsSpacesOrLowValues(edtYyyy))
        {
            _wsMessage = "End Date - Year can NOT be empty...";        // source: :296-297
            _errFlgOn = true;                                          // source: :298
            _map.Field("EDTYYYY").CursorLength = -1;                   // MOVE -1 TO EDTYYYYL. source: :299
            SendTrnrptScreen(ctx); return;                            // source: :300 (FB-1)
        }
        // WHEN OTHER -> CONTINUE. source: :301-302

        // NUMVAL-C normalization of all six date parts; re-echo zero-padded into the INPUT fields (FB-2).
        // source: :305-327
        _wsNum99 = NumValC2(sdtMm);   sdtMm = Pic2(_wsNum99);   _map.Field("SDTMM").SetValue(sdtMm, setMdt: false);     // :305-307
        _wsNum99 = NumValC2(sdtDd);   sdtDd = Pic2(_wsNum99);   _map.Field("SDTDD").SetValue(sdtDd, setMdt: false);     // :309-311
        _wsNum9999 = NumValC4(sdtYyyy); sdtYyyy = Pic4(_wsNum9999); _map.Field("SDTYYYY").SetValue(sdtYyyy, setMdt: false); // :313-315
        _wsNum99 = NumValC2(edtMm);   edtMm = Pic2(_wsNum99);   _map.Field("EDTMM").SetValue(edtMm, setMdt: false);     // :317-319
        _wsNum99 = NumValC2(edtDd);   edtDd = Pic2(_wsNum99);   _map.Field("EDTDD").SetValue(edtDd, setMdt: false);     // :321-323
        _wsNum9999 = NumValC4(edtYyyy); edtYyyy = Pic4(_wsNum9999); _map.Field("EDTYYYY").SetValue(edtYyyy, setMdt: false); // :325-327

        // Range/numeric validations (each independent IF, no ELSE, no early exit — FB-1 makes the first
        // failing SEND terminate the task). String comparisons '> 12' / '> 31' are alphanumeric on the field.
        if (!IsNumeric2(sdtMm) || string.CompareOrdinal(sdtMm, "12") > 0)
        {
            _wsMessage = "Start Date - Not a valid Month...";          // source: :331-332
            _errFlgOn = true;                                          // source: :333
            _map.Field("SDTMM").CursorLength = -1;                     // MOVE -1 TO SDTMML. source: :334
            SendTrnrptScreen(ctx); return;                            // source: :335 (FB-1)
        }
        if (!IsNumeric2(sdtDd) || string.CompareOrdinal(sdtDd, "31") > 0)
        {
            _wsMessage = "Start Date - Not a valid Day...";            // source: :340-341
            _errFlgOn = true;                                          // source: :342
            _map.Field("SDTDD").CursorLength = -1;                     // MOVE -1 TO SDTDDL. source: :343
            SendTrnrptScreen(ctx); return;                            // source: :344 (FB-1)
        }
        if (!IsNumeric4(sdtYyyy))
        {
            _wsMessage = "Start Date - Not a valid Year...";           // source: :348-349
            _errFlgOn = true;                                          // source: :350
            _map.Field("SDTYYYY").CursorLength = -1;                   // MOVE -1 TO SDTYYYYL. source: :351
            SendTrnrptScreen(ctx); return;                            // source: :352 (FB-1)
        }
        if (!IsNumeric2(edtMm) || string.CompareOrdinal(edtMm, "12") > 0)
        {
            _wsMessage = "End Date - Not a valid Month...";            // source: :357-358
            _errFlgOn = true;                                          // source: :359
            _map.Field("EDTMM").CursorLength = -1;                     // MOVE -1 TO EDTMML. source: :360
            SendTrnrptScreen(ctx); return;                            // source: :361 (FB-1)
        }
        if (!IsNumeric2(edtDd) || string.CompareOrdinal(edtDd, "31") > 0)
        {
            _wsMessage = "End Date - Not a valid Day...";              // source: :366-367
            _errFlgOn = true;                                          // source: :368
            _map.Field("EDTDD").CursorLength = -1;                     // MOVE -1 TO EDTDDL. source: :369
            SendTrnrptScreen(ctx); return;                            // source: :370 (FB-1)
        }
        if (!IsNumeric4(edtYyyy))
        {
            _wsMessage = "End Date - Not a valid Year...";             // source: :374-375
            _errFlgOn = true;                                          // source: :376
            _map.Field("EDTYYYY").CursorLength = -1;                   // MOVE -1 TO EDTYYYYL. source: :377
            SendTrnrptScreen(ctx); return;                            // source: :378 (FB-1)
        }

        // Build dates from inputs. source: :381-386
        _wsStartDateYyyy = sdtYyyy;
        _wsStartDateMm = sdtMm;
        _wsStartDateDd = sdtDd;
        _wsEndDateYyyy = edtYyyy;
        _wsEndDateMm = edtMm;
        _wsEndDateDd = edtDd;

        // CSUTLDTC start-date validation. source: :388-406
        // MOVE WS-START-DATE TO CSUTLDTC-DATE; WS-DATE-FORMAT TO CSUTLDTC-DATE-FORMAT; SPACES TO RESULT.
        _csutldtcResult = DateValidationUtility(WsStartDate, WS_DATE_FORMAT);       // CALL 'CSUTLDTC'. source: :392-394
        if (SevCd(_csutldtcResult) == "0000")
        {
            // CONTINUE. source: :396-397
        }
        else if (MsgNum(_csutldtcResult) != "2513")
        {
            _wsMessage = "Start Date - Not a valid date...";           // source: :400-401
            _errFlgOn = true;                                          // source: :402
            _map.Field("SDTMM").CursorLength = -1;                     // MOVE -1 TO SDTMML. source: :403
            SendTrnrptScreen(ctx); return;                            // source: :404 (FB-1)
        }

        // CSUTLDTC end-date validation. source: :408-426
        _csutldtcResult = DateValidationUtility(WsEndDate, WS_DATE_FORMAT);         // CALL 'CSUTLDTC'. source: :412-414
        if (SevCd(_csutldtcResult) == "0000")
        {
            // CONTINUE. source: :416-417
        }
        else if (MsgNum(_csutldtcResult) != "2513")
        {
            _wsMessage = "End Date - Not a valid date...";             // source: :420-421
            _errFlgOn = true;                                          // source: :422
            _map.Field("EDTMM").CursorLength = -1;                     // MOVE -1 TO EDTMML. source: :423
            SendTrnrptScreen(ctx); return;                            // source: :424 (FB-1)
        }

        // Move dates to the PARM placeholders; WS-REPORT-NAME='Custom'; submit if no error. source: :429-436
        SetParmStartDate(WsStartDate);                                // source: :429-430
        SetParmEndDate(WsEndDate);                                    // source: :431-432
        _wsReportName = "Custom";                                     // MOVE 'Custom' TO WS-REPORT-NAME. source: :433
        if (!ErrFlgOn)                                                // IF NOT ERR-FLG-ON. source: :434
        {
            SubmitJobToIntrdr(ctx);                                   // PERFORM SUBMIT-JOB-TO-INTRDR. source: :435
            if (ctx.Outcome is not null) return;
        }
    }

    // =============================================================================================
    //  SUBMIT-JOB-TO-INTRDR — source: CORPT00C.cbl:462-510
    // =============================================================================================
    private void SubmitJobToIntrdr(CicsContext ctx)
    {
        string confirm = _map.Field("CONFIRM").Value;

        // Confirmation guard. source: :464-474
        if (IsSpacesOrLowValues(confirm))
        {
            // STRING 'Please confirm to print the ' WS-REPORT-NAME(DELIM SPACE) ' report...'. source: :465-470
            _wsMessage = "Please confirm to print the " + DelimBySpace(_wsReportName) + " report...";
            _errFlgOn = true;                                          // source: :471
            _map.Field("CONFIRM").CursorLength = -1;                   // MOVE -1 TO CONFIRML. source: :472
            SendTrnrptScreen(ctx);                                     // PERFORM SEND-TRNRPT-SCREEN. source: :473
            return;                                                    // (FB-1)
        }

        // IF NOT ERR-FLG-ON. source: :476-510
        if (!ErrFlgOn)
        {
            // EVALUATE TRUE on CONFIRMI. source: :477-494
            string c1 = confirm.Length > 0 ? confirm.Substring(0, 1) : "";
            if (c1 == "Y" || c1 == "y")
            {
                // WHEN 'Y' OR 'y' -> CONTINUE (proceed to submit). source: :478-479
            }
            else if (c1 == "N" || c1 == "n")
            {
                // WHEN 'N' OR 'n' -> cancel: clear form, flag, redisplay with blank message. source: :480-483
                InitializeAllFields();                                // PERFORM INITIALIZE-ALL-FIELDS. source: :481
                _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :482
                SendTrnrptScreen(ctx);                                // PERFORM SEND-TRNRPT-SCREEN. source: :483
                return;                                               // (FB-1)
            }
            else
            {
                // WHEN OTHER -> STRING '"' CONFIRMI(DELIM SPACE) '" is not a valid value to confirm...'. source: :484-493
                _wsMessage = "\"" + DelimBySpace(confirm) + "\" is not a valid value to confirm...";
                _errFlgOn = true;                                     // source: :491
                _map.Field("CONFIRM").CursorLength = -1;              // MOVE -1 TO CONFIRML. source: :492
                SendTrnrptScreen(ctx);                                // PERFORM SEND-TRNRPT-SCREEN. source: :493
                return;                                              // (FB-1)
            }

            _endLoopYes = false;                                      // SET END-LOOP-NO TO TRUE. source: :496

            // PERFORM VARYING WS-IDX FROM 1 BY 1 UNTIL WS-IDX > 1000 OR END-LOOP-YES OR ERR-FLG-ON. source: :498-499
            string[] jobLines = BuildJobLines();
            for (_wsIdx = 1; !(_wsIdx > 1000 || _endLoopYes || _errFlgOn); _wsIdx++)
            {
                // MOVE JOB-LINES(WS-IDX) TO JCL-RECORD. source: :501
                _jclRecord = _wsIdx <= jobLines.Length ? jobLines[_wsIdx - 1] : new string(' ', 80);

                // IF JCL-RECORD = '/*EOF' OR SPACES OR LOW-VALUES -> SET END-LOOP-YES (BEFORE the write, FB-3). source: :502-505
                string trimmed = _jclRecord.TrimEnd(' ');
                if (trimmed == "/*EOF" || IsSpacesOrLowValues(_jclRecord))
                    _endLoopYes = true;

                WirteJobsubTdq(ctx);                                  // PERFORM WIRTE-JOBSUB-TDQ. source: :507
                if (ctx.Outcome is not null) return;                 // a SEND/RETURN inside terminated the task.
            }
        }
    }

    // =============================================================================================
    //  WIRTE-JOBSUB-TDQ (paragraph name misspelled in source — keep). source: CORPT00C.cbl:515-535
    // =============================================================================================
    private void WirteJobsubTdq(CicsContext ctx)
    {
        // EXEC CICS WRITEQ TD QUEUE('JOBS') FROM(JCL-RECORD) LENGTH(LENGTH OF JCL-RECORD=80) RESP/RESP2. source: :517-523
        // The in-process job-submit sink: append the fixed 80-byte line; always NORMAL.
        JobsQueue.Add(PadX(_jclRecord, 80));
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;

        // EVALUATE WS-RESP-CD. source: :525-535
        if (_wsRespCd == (int)Resp.Normal)
        {
            // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :526-527
        }
        else
        {
            // WHEN OTHER. source: :528-534
            // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace, FB-7). source: :529
            _errFlgOn = true;                                          // MOVE 'Y' TO WS-ERR-FLG. source: :530
            _wsMessage = "Unable to Write TDQ (JOBS)...";              // source: :531-532
            _map.Field("MONTHLY").CursorLength = -1;                   // MOVE -1 TO MONTHLYL. source: :533
            SendTrnrptScreen(ctx);                                     // PERFORM SEND-TRNRPT-SCREEN. source: :534
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: CORPT00C.cbl:540-551
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :542-544
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;       // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :545
        _commArea.FromProgram = WS_PGMNAME;     // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :546
        _commArea.SetFirstEntry();              // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :547

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :548-551
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-TRNRPT-SCREEN — source: CORPT00C.cbl:556-580
    //  Ends with GO TO RETURN-TO-CICS, which RETURNs to CICS (FB-1): this paragraph terminates the task.
    // =============================================================================================
    private void SendTrnrptScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                       // PERFORM POPULATE-HEADER-INFO. source: :558

        // MOVE WS-MESSAGE TO ERRMSGO (X80 -> X78: silent 2-char clamp, FB-5). source: :560
        _map.Field("ERRMSG").SetValue(ClampTo(_wsMessage, 78), setMdt: false);

        // IF SEND-ERASE-YES (always true here) -> SEND ... ERASE CURSOR; ELSE (dead) SEND ... CURSOR. source: :562-578
        ctx.SendMap("CORPT0A", "CORPT00", _map, new SendMapOptions
        {
            Erase = SendEraseYes,   // SEND-ERASE-YES is always Y (the no-ERASE arm is dead, FB-6).
            FreeKb = true,          // DFHMSD CTRL=(ALARM,FREEKB).
            Cursor = -1,            // CURSOR — honour the MOVE -1 TO xxxL set on the error/cursor field.
        });

        // GO TO RETURN-TO-CICS -> RETURN-TO-CICS. source: :580,585-591
        ReturnToCics(ctx);
    }

    // =============================================================================================
    //  RETURN-TO-CICS — source: CORPT00C.cbl:585-591
    // =============================================================================================
    private void ReturnToCics(CicsContext ctx)
    {
        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA) — ends the task. source: :587-591
        ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // =============================================================================================
    //  RECEIVE-TRNRPT-SCREEN — source: CORPT00C.cbl:596-604
    // =============================================================================================
    private void ReceiveTrnrptScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('CORPT0A') MAPSET('CORPT00') INTO(CORPT0AI) RESP/RESP2 (RESP never inspected). source: :598-604
        ctx.ReceiveMap("CORPT0A", "CORPT00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: CORPT00C.cbl:609-628
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        DateTime now = ctx.Clock.Now;                                 // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :611

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false);  // MOVE CCDA-TITLE01 TO TITLE01O. source: :613
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false);  // MOVE CCDA-TITLE02 TO TITLE02O. source: :614
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);     // MOVE WS-TRANID  TO TRNNAMEO. source: :615
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);    // MOVE WS-PGMNAME TO PGMNAMEO. source: :616

        // CURDATEO = mm/dd/yy (year = last two digits, WS-CURDATE-YEAR(3:2)). source: :618-622
        _map.Field("CURDATE").SetValue(
            $"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);
        // CURTIMEO = hh:mm:ss. source: :624-628
        _map.Field("CURTIME").SetValue(
            $"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: CORPT00C.cbl:633-646
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("MONTHLY").CursorLength = -1;             // MOVE -1 TO MONTHLYL. source: :635
        // INITIALIZE the ten input fields (alphanumeric -> spaces) + WS-MESSAGE. source: :636-646
        _map.Field("MONTHLY").SetValue("", setMdt: false);
        _map.Field("YEARLY").SetValue("", setMdt: false);
        _map.Field("CUSTOM").SetValue("", setMdt: false);
        _map.Field("SDTMM").SetValue("", setMdt: false);
        _map.Field("SDTDD").SetValue("", setMdt: false);
        _map.Field("SDTYYYY").SetValue("", setMdt: false);
        _map.Field("EDTMM").SetValue("", setMdt: false);
        _map.Field("EDTDD").SetValue("", setMdt: false);
        _map.Field("EDTYYYY").SetValue("", setMdt: false);
        _map.Field("CONFIRM").SetValue("", setMdt: false);
        _wsMessage = "";
    }

    // =============================================================================================
    //  JCL job-stream template (JOB-DATA / JOB-LINES). source: CORPT00C.cbl:81-127
    //  JOB-DATA-1 is a flat 80xN byte block of literal JCL lines with the 4 date placeholders embedded;
    //  JOB-DATA-2 REDEFINES it as JOB-LINES OCCURS 1000 PIC X(80). The placeholder MOVEs (PARM-START-DATE-1/2,
    //  PARM-END-DATE-1/2) inject the SAME 10-char date into the SYMNAMES C'...' form and the DATEPARM data line.
    // =============================================================================================
    private string _parmStartDate1 = new string(' ', 10);  // 10 PARM-START-DATE-1 PIC X(10). source: :106
    private string _parmEndDate1 = new string(' ', 10);    // 10 PARM-END-DATE-1   PIC X(10). source: :111
    private string _parmStartDate2 = new string(' ', 10);  // 10 PARM-START-DATE-2 PIC X(10). source: :118
    private string _parmEndDate2 = new string(' ', 10);    // 10 PARM-END-DATE-2   PIC X(10). source: :120

    // MOVE WS-START-DATE TO PARM-START-DATE-1, PARM-START-DATE-2 (X(10) each). source: :220-221,247-248,429-430
    private void SetParmStartDate(string d) { _parmStartDate1 = PadX(d, 10); _parmStartDate2 = PadX(d, 10); }
    // MOVE WS-END-DATE TO PARM-END-DATE-1, PARM-END-DATE-2 (X(10) each). source: :235-236,252-253,431-432
    private void SetParmEndDate(string d) { _parmEndDate1 = PadX(d, 10); _parmEndDate2 = PadX(d, 10); }

    /// <summary>
    /// Builds the JOB-LINES image: the 17 literal JCL lines (each PIC X(80)), with the start/end dates injected
    /// into the FILLER-1/FILLER-2 SYMNAMES lines and the FILLER-3 DATEPARM data line, exactly as the COBOL
    /// JOB-DATA-1 group lays them out. source: CORPT00C.cbl:83-125
    /// </summary>
    private string[] BuildJobLines()
    {
        // FILLER-1 (line 11): X18 "PARM-START-DATE,C'" + PARM-START-DATE-1 X(10) + X52 "'". source: :103-107
        string symStart = PadX("PARM-START-DATE,C'", 18) + PadX(_parmStartDate1, 10) + PadX("'", 52);
        // FILLER-2 (line 12): X16 "PARM-END-DATE,C'"   + PARM-END-DATE-1   X(10) + X54 "'". source: :108-112
        string symEnd = PadX("PARM-END-DATE,C'", 16) + PadX(_parmEndDate1, 10) + PadX("'", 54);
        // FILLER-3 (line 15): PARM-START-DATE-2 X(10) + X1 space + PARM-END-DATE-2 X(10) + X59 spaces. source: :117-121
        string dateParm = PadX(_parmStartDate2, 10) + " " + PadX(_parmEndDate2, 10) + new string(' ', 59);

        var lines = new[]
        {
            "//TRNRPT00 JOB 'TRAN REPORT',CLASS=A,MSGCLASS=0,",      // source: :83-84
            "// NOTIFY=&SYSUID",                                      // source: :85-86
            "//*",                                                   // source: :87-88
            "//JOBLIB JCLLIB ORDER=('AWS.M2.CARDDEMO.PROC')",        // source: :89-90
            "//*",                                                   // source: :91-92
            "//STEP10 EXEC PROC=TRANREPT",                           // source: :93-94
            "//*",                                                   // source: :95-96
            "//STEP05R.SYMNAMES DD *",                               // source: :97-98
            "TRAN-CARD-NUM,263,16,ZD",                               // source: :99-100
            "TRAN-PROC-DT,305,10,CH",                                // source: :101-102
            symStart,                                                // FILLER-1. source: :103-107
            symEnd,                                                  // FILLER-2. source: :108-112
            "/*",                                                    // source: :113-114
            "//STEP10R.DATEPARM DD *",                               // source: :115-116
            dateParm,                                                // FILLER-3. source: :117-121
            "/*",                                                    // source: :122-123
            "/*EOF",                                                 // source: :124-125
        };

        // Each JOB-LINES entry is PIC X(80): right-pad to 80. The OCCURS array has 1000 slots; the loop stops
        // at the first '/*EOF'/blank/low-value line (writing it first, FB-3), so slots past line 17 (all spaces)
        // are never reached — but if the EOF sentinel were absent the loop would read trailing blank slots.
        return lines.Select(l => PadX(l, 80)).ToArray();
    }

    // =============================================================================================
    //  CSUTLDTC LE date-validator stand-in. source: CORPT00C.cbl:392-394,412-414; CSUTLDTC.cbl:128-149
    // =============================================================================================
    /// <summary>
    /// <c>CALL 'CSUTLDTC' USING date, 'YYYY-MM-DD', RESULT</c>. Returns the 80-byte CSUTLDTC-RESULT image whose
    /// only fields the caller reads are CSUTLDTC-RESULT-SEV-CD (bytes 1-4) and CSUTLDTC-RESULT-MSG-NUM
    /// (bytes 16-19). A valid CCYY-MM-DD yields severity <c>'0000'</c> + message <c>'0000'</c>. CEEDAYS reports
    /// each invalid date by its specific LE condition (severity 3): a bad day-of-month/value -> FC-BAD-DATE-VALUE
    /// (msg 2508), an out-of-range month -> FC-INVALID-MONTH (2517), a year-in-era of zero -> FC-YEAR-IN-ERA-ZERO
    /// (2521), non-numeric -> FC-NON-NUMERIC-DATA (2520). CardDemo tolerates ONLY message 2513 (FC-UNSUPP-RANGE),
    /// so each of these makes the caller's <c>IF MSG-NUM NOT = 2513</c> reject the date — e.g. 2023-02-30 /
    /// 2023-04-31 / 2023-00-15 / 2023-01-00 are rejected, faithful to CEEDAYS. // source: CORPT00C.cbl:392-426; CSUTLDTC.cbl:62-70,128-149
    /// </summary>
    private static string DateValidationUtility(string date, string mask)
    {
        string sev = "0003", msgNo;
        string d = (date ?? "").Trim();
        int y = 0, mo = 0, day = 0;
        bool parsed = mask.TrimEnd() == "YYYY-MM-DD" && d.Length == 10 && d[4] == '-' && d[7] == '-'
            && int.TryParse(d[..4], out y) & int.TryParse(d.Substring(5, 2), out mo) & int.TryParse(d.Substring(8, 2), out day);
        if (parsed)
        {
            bool real = y is >= 1 and <= 9999 && mo is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(y, mo);
            if (real) { sev = "0000"; msgNo = "0000"; }
            else if (mo < 1 || mo > 12) msgNo = "2517"; // FC-INVALID-MONTH
            else if (y < 1)             msgNo = "2521"; // FC-YEAR-IN-ERA-ZERO
            else                        msgNo = "2508"; // FC-BAD-DATE-VALUE ("Datevalue error")
        }
        else
        {
            msgNo = "2520"; // FC-NON-NUMERIC-DATA / unparseable CCYY-MM-DD
        }
        // sev(4) + 'Mesg Code:' filler(11) + msg-no(4) + ... — only sev (1-4) and msg-no (16-19) are read.
        string image = sev + "Mesg Code: " + msgNo;
        return image.PadRight(80, ' ');
    }

    /// <summary>CSUTLDTC-RESULT-SEV-CD — bytes 1-4 of the 80-byte result. source: :133,396</summary>
    private static string SevCd(string result) => SafeSlice(result, 0, 4);

    /// <summary>CSUTLDTC-RESULT-MSG-NUM — bytes 16-19 of the 80-byte result. source: :135,399</summary>
    private static string MsgNum(string result) => SafeSlice(result, 15, 4);

    // =============================================================================================
    //  MOVE LOW-VALUES TO CORPT0AO — blank every named output field + clear per-turn overrides. source: :179
    // =============================================================================================
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
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
    /// COBOL <c>STRING ... DELIMITED BY SPACE</c>: copy a field up to (not including) its first space.
    /// E.g. 'Monthly   ' -> 'Monthly'; a 1-char confirm value has no embedded space so it is emitted whole.
    /// </summary>
    private static string DelimBySpace(string? value)
    {
        string v = value ?? "";
        int i = v.IndexOf(' ');
        return i < 0 ? v : v.Substring(0, i);
    }

    /// <summary>
    /// FUNCTION NUMVAL-C over a 2-char field MOVEd to PIC 99: parse the leading/embedded numeric value
    /// (ignoring non-digit "noise" per NUMVAL-C), reduce to 2 digits (silent overflow). Empty/junk -> 0.
    /// </summary>
    private static int NumValC2(string? s) => NumValC(s) % 100;

    /// <summary>
    /// FUNCTION NUMVAL-C over a 4-char field MOVEd to PIC 9999: parse the numeric value, reduce to 4 digits.
    /// </summary>
    private static int NumValC4(string? s) => NumValC(s) % 10000;

    /// <summary>
    /// FUNCTION NUMVAL-C core: extract the integer numeric value from a (possibly punctuated) display string.
    /// Keeps only the digit characters; null/empty/non-numeric -> 0. (No decimal/sign handling is needed for
    /// the MM/DD/YYYY date parts.)
    /// </summary>
    private static int NumValC(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    /// <summary>MOVE PIC 99 -> X(2): zero-padded 2-digit numeric text.</summary>
    private static string Pic2(int value) => (value % 100).ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>MOVE PIC 9999 -> X(4): zero-padded 4-digit numeric text.</summary>
    private static string Pic4(int value) => (value % 10000).ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>
    /// COBOL <c>IS NUMERIC</c> on a 2-char field that now holds zero-padded numeric text (after the FB-2
    /// re-echo): true when both chars are digits.
    /// </summary>
    private static bool IsNumeric2(string s) => s.Length == 2 && s.All(char.IsDigit);

    /// <summary>COBOL <c>IS NUMERIC</c> on a 4-char field of zero-padded numeric text: true when all four are digits.</summary>
    private static bool IsNumeric4(string s) => s.Length == 4 && s.All(char.IsDigit);

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>MOVE a wider source into a narrower X(n) receiver: left-justify, silently truncate to width (FB-5).</summary>
    private static string ClampTo(string? value, int width)
    {
        value ??= "";
        return value.Length > width ? value[..width] : value;
    }

    /// <summary>Bounds-safe substring used to read CSUTLDTC-RESULT slices (missing bytes read as spaces).</summary>
    private static string SafeSlice(string s, int start, int len)
    {
        s ??= "";
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            int idx = start + i;
            sb.Append(idx >= 0 && idx < s.Length ? s[idx] : ' ');
        }
        return sb.ToString();
    }

    private static string Two(int value) => value.ToString("D2", CultureInfo.InvariantCulture);
    private static string Four(int value) => value.ToString("D4", CultureInfo.InvariantCulture);

    // RESP codes the WRITEQ EVALUATE branches on (DFHRESP). source: :525-535
    private enum Resp { Normal = 0 }

    // =============================================================================================
    //  BMS map builder — CORPT0A in mapset CORPT00 (24x80).
    //  source: app/bms/CORPT00.bms:19-228 / SCREEN_CORPT00.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: CORPT00.bms:26.</summary>
    public const string MapName = "CORPT0A";

    /// <summary>The DFHMSD mapset name. source: CORPT00.bms:19.</summary>
    public const string MapsetName = "CORPT00";

    /// <summary>
    /// Constructs the <c>CORPT0A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The <c>IC</c> cursor field is
    /// <c>MONTHLY</c> (7,10). No PICIN/PICOUT clauses appear in this map; the date inputs are NUM.
    /// source: CORPT00.bms:19-228
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

            // ----- 'Transaction Reports' bright heading (bms:75-79) -----
            LitAttr(4, 30, 19, AskipBrt, BmsColor.Neutral, "Transaction Reports"), // bms:75-79

            // ----- Monthly radio + label (bms:80-93) -----
            // MONTHLY: ATTRB=(FSET,IC,NORM,UNPROT) GREEN HILIGHT=UNDERLINE LEN 1 INITIAL ' ' — the IC cursor field.
            InField("MONTHLY", 7, 10, 1, ic: true, initial: " "),              // bms:80-85
            Stopper(7, 12),                                                     // bms:86-88 (LENGTH=0, ASKIP,NORM)
            LitAttr(7, 15, 23, AskipBrt, BmsColor.Turquoise, "Monthly (Current Month)"), // bms:89-93

            // ----- Yearly radio + label (bms:94-107) -----
            InField("YEARLY", 9, 10, 1, ic: false, initial: " "),              // bms:94-99
            Stopper(9, 12),                                                     // bms:100-102
            LitAttr(9, 15, 23, AskipBrt, BmsColor.Turquoise, "Yearly (Current Year)"), // bms:103-107

            // ----- Custom radio + label (bms:108-121) -----
            InField("CUSTOM", 11, 10, 1, ic: false, initial: " "),             // bms:108-113
            Stopper(11, 12),                                                    // bms:114-116
            LitAttr(11, 15, 23, AskipBrt, BmsColor.Turquoise, "Custom (Date Range)"), // bms:117-121

            // ----- Start Date label + SDT inputs (bms:122-160) -----
            Lit(13, 15, 12, BmsColor.Turquoise, "Start Date :"),               // bms:122-126
            NumField("SDTMM", 13, 29, 2, initial: "  "),                       // bms:127-132
            Lit(13, 32, 1, BmsColor.Blue, "/"),                                // bms:133-137
            NumField("SDTDD", 13, 34, 2, initial: "  "),                       // bms:138-143
            Lit(13, 37, 1, BmsColor.Blue, "/"),                                // bms:144-148
            NumField("SDTYYYY", 13, 39, 4, initial: "    "),                   // bms:149-154
            DefStopper(13, 44),                                                 // bms:155-156 (LENGTH=0, no ATTRB=)
            DefField(13, 46, 12, BmsColor.Blue, "(MM/DD/YYYY)"),               // bms:157-160 (no ATTRB=; COLOR=BLUE)

            // ----- End Date label + EDT inputs (bms:161-199) -----
            Lit(14, 15, 12, BmsColor.Turquoise, "  End Date :"),               // bms:161-165 (2 leading spaces)
            NumField("EDTMM", 14, 29, 2, initial: "  "),                       // bms:166-171
            Lit(14, 32, 1, BmsColor.Blue, "/"),                                // bms:172-176
            NumField("EDTDD", 14, 34, 2, initial: "  "),                       // bms:177-182
            Lit(14, 37, 1, BmsColor.Blue, "/"),                                // bms:183-187
            NumField("EDTYYYY", 14, 39, 4, initial: "    "),                   // bms:188-193
            DefStopper(14, 44),                                                 // bms:194-195 (LENGTH=0, no ATTRB=)
            DefField(14, 46, 12, BmsColor.Blue, "(MM/DD/YYYY)"),               // bms:196-199 (no ATTRB=; COLOR=BLUE)

            // ----- confirm prompt + CONFIRM input + (Y/N) (bms:200-217) -----
            Lit(19, 6, 59, BmsColor.Turquoise,
                "The Report will be submitted for printing. Please confirm: "), // bms:200-205 (59 chars, trailing space)
            // CONFIRM: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE LEN 1, no INITIAL.
            InField("CONFIRM", 19, 66, 1, ic: false, initial: null),           // bms:206-210
            DefStopper(19, 68),                                                 // bms:211-212 (LENGTH=0, no ATTRB=)
            Lit(19, 69, 5, BmsColor.Neutral, "(Y/N)"),                         // bms:213-217

            // ----- error line + footer (bms:218-226) -----
            // ERRMSG: ATTRB=(ASKIP,BRT,FSET) RED LEN 78.
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:218-221
            Lit(24, 1, 23, BmsColor.Yellow, "ENTER=Continue  F3=Back"),        // bms:222-226
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) + the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>
    /// Unnamed literal that declares no ATTRB= operand (the '(MM/DD/YYYY)' format hints): 3270 defaults apply
    /// (UNPROT,NORM). Carries its INITIAL text + COLOR=BLUE. source: CORPT00.bms:157-160,196-199
    /// </summary>
    private static ScreenField DefField(int row, int col, int len, BmsColor color, string text) =>
        new()
        {
            Row = row, Col = col, Length = len,
            Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = color, Value = text,
        };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>
    /// Named keyable input field: ATTRB=(FSET,[IC,]NORM,UNPROT) GREEN HILIGHT=UNDERLINE (the radio flags
    /// MONTHLY/YEARLY/CUSTOM and CONFIRM). Optional INITIAL (the radios carry a single-space INITIAL; CONFIRM none).
    /// </summary>
    private static ScreenField InField(string name, int row, int col, int len, bool ic, string? initial)
    {
        BmsAttribute attr = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected;
        if (ic) attr |= BmsAttribute.Ic;
        return new ScreenField
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = attr, Color = BmsColor.Green, Hilight = BmsHilight.Underline,
            Value = initial ?? "",
        };
    }

    /// <summary>
    /// Named numeric keyable input field: ATTRB=(FSET,NORM,NUM,UNPROT) GREEN HILIGHT=UNDERLINE (the six
    /// MM/DD/YYYY date parts), with a spaces INITIAL of the field width.
    /// </summary>
    private static ScreenField NumField(string name, int row, int col, int len, string initial) =>
        new()
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Numeric | BmsAttribute.Unprotected,
            Color = BmsColor.Green, Hilight = BmsHilight.Underline, Value = initial,
        };

    /// <summary>A LENGTH=0 stopper field with ATTRB=(ASKIP,NORM).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };

    /// <summary>
    /// A LENGTH=0 stopper field declaring no ATTRB= operand (the (13,44),(14,44),(19,68) stoppers in CORPT00):
    /// 3270 defaults apply (UNPROT,NORM). source: CORPT00.bms:155-156,194-195,211-212
    /// </summary>
    private static ScreenField DefStopper(int row, int col) =>
        new()
        {
            Row = row, Col = col, Length = 0,
            Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = BmsColor.Default,
        };
}
