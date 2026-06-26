using System.Globalization;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Ims;

/// <summary>
/// Faithful relational re-port of the IMS BMP batch program <c>CBPAUP0C</c> — "Delete Expired Pending
/// Authoriation Messages" (sic — the source header omits the 'z', bug #5). The original walks the IMS
/// HIDAM Pending-Authorization database <c>DBPAUTP0</c> root-by-root: for every Pending Authorization
/// Summary root (<c>PAUTSUM0</c>) it iterates that root's Pending Authorization Detail children
/// (<c>PAUTDTL1</c>); each detail whose authorization age (today minus the detail's authorization Julian
/// date, decoded from its 9s-complement form) is <b>≥ the expiry threshold</b> is deleted, and the
/// parent summary's running approved/declined counts and amounts are decremented in memory; after
/// processing all of a summary's details, if the summary's approved-auth count is <c>&lt;= 0</c> the
/// summary root itself is deleted. Work is committed periodically via IMS <c>CHKP</c> checkpoints, and a
/// final checkpoint and totals report are emitted at end-of-database. Run parameters (expiry days,
/// checkpoint frequency, checkpoint-display frequency, debug flag) are read once from <c>SYSIN</c>.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from
/// <c>app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl</c> (the authoritative source) per
/// <c>_design/specs/CBPAUP0C.md</c> and <c>_design/specs/optional/IMS_SCHEMA.md</c>. Each
/// PROCEDURE-DIVISION paragraph is a method whose name mirrors the COBOL paragraph and whose body keeps
/// the original statement order and control flow (with <c>// source: CBPAUP0C.cbl:NNN</c> citations).</para>
/// <para>DL/I → SQL mapping (IMS_SCHEMA.md §3): the program uses the EXEC DLI macro interface (status in
/// <c>DIBSTAT</c>), not <c>CALL 'CBLTDLI'</c>. <b>GN</b>(PAUTSUM0) → a forward root cursor over
/// <see cref="PautSummaryRepository.ReadNext"/> (<c>'  '</c>=row / <c>'GB'</c>=end-of-db);
/// <b>GNP</b>(PAUTDTL1) → a per-parent child cursor over
/// <see cref="PautDetailRepository.ReadNextInParent"/> (<c>'  '</c>=row / <c>'GE'/'GB'</c>=no-more);
/// <b>DLET</b>(PAUTDTL1) → <see cref="PautDetailRepository.Delete"/>; <b>DLET</b>(PAUTSUM0) →
/// <see cref="PautSummaryRepository.Delete"/> (FK ON DELETE CASCADE mirrors IMS root-delete cascading);
/// <b>CHKP</b> → a relational <c>COMMIT</c> (modeled here as a no-op commit boundary; the in-memory
/// repositories autocommit). There is <b>no GU/REPL/ISRT</b> in this program — only the two DLETs mutate
/// the database.</para>
/// <para>FAITHFUL BUGS preserved verbatim (see <c>_design/specs/CBPAUP0C.md</c> §7):
/// <list type="number">
/// <item>#1 — the decremented summary totals (4000-CHECK-IF-EXPIRED) are mutated only in the in-memory
/// io-area and are <b>never written back</b> (no REPL); they survive only until the next root GN
/// overwrites the area. The port mutates the in-memory <see cref="PautSummary"/> for the delete gate but
/// issues no UPDATE on PAUT_SUMMARY.</item>
/// <item>#2 — the summary-delete gate tests <c>PA-APPROVED-AUTH-CNT &lt;= 0 AND PA-APPROVED-AUTH-CNT
/// &lt;= 0</c> — both conjuncts test the <b>approved</b> count; the declined count is never tested.
/// Reproduced exactly.</item>
/// <item>#3 — <c>P-CHKP-FREQ</c>/<c>P-CHKP-DIS-FREQ</c> are PIC X(05) alphanumeric, defaulted via
/// <c>MOVE 5</c>/<c>MOVE 10</c> (yielding <c>'5    '</c>/<c>'10   '</c>, left-justified space-filled) and
/// then used as the right operand of numeric comparisons. Reproduced via the IBM numeric-class
/// comparison of those exact bytes (see <see cref="CompareNumericToAlphanumeric"/>).</item>
/// <item>#4 — dead WS status definitions (IMS-RETURN-CODE 88s, WS-IMS-PSB-SCHD-FLG, WS-INFILE-STATUS,
/// WS-CUSTID-STATUS, IDX, WS-TOT-REC-WRITTEN, WS-ERR-FLG) are carried as inert fields; the program tests
/// the literal DIBSTAT directly and never SETs ERR-FLG-ON.</item>
/// <item>#5 — header/comment typo "Authoriation" preserved verbatim.</item>
/// <item>#6 — <c>ACCEPT CURRENT-DATE FROM DATE</c> populates a field that is never used afterward; kept
/// as a side-effect-free accept.</item>
/// <item>#7 — <c>WK-CHKPT-ID-CTR</c> is never incremented, so every checkpoint id stays
/// <c>'RMAD0000'</c>.</item>
/// </list></para>
/// <para>Money rule (ARCHITECTURE.md §money): subtracts truncate toward zero with silent high-order
/// overflow (S9(10)V99 detail amount minus into an S9(09)V99 summary amount can overflow) — modeled via
/// <see cref="Decimals.Store"/>. The YYDDD subtraction in 4000 is a raw integer subtraction of two
/// Julian YYDDD values (NOT a calendar day-difference across year boundaries) — reproduced as-is.</para>
/// </remarks>
public sealed class Cbpaup0c
{
    // ---- PIC widths/scales for the summary amount fields touched by the in-memory decrements -----------
    // PA-APPROVED-AUTH-AMT / PA-DECLINED-AUTH-AMT are S9(09)V99 COMP-3 (9 integer digits, 2 fraction).
    private const int SummaryAmtIntDigits = 9;
    private const int SummaryAmtScale = 2;
    // PA-APPROVED-AUTH-CNT / PA-DECLINED-AUTH-CNT are S9(04) COMP (signed halfword, may go negative).
    private const int CountIntDigits = 4;

    private readonly PautSummaryRepository _summary;
    private readonly PautDetailRepository _detail;
    private readonly IClock _clock;
    private readonly List<string> _sysout = [];

    // ---- WORKING-STORAGE: WS-VARIABLES (CBPAUP0C.cbl:41-77) -------------------------------------------
    // private const string WsPgmName = "CBPAUP0C";                 // WS-PGMNAME (X08) — never tested
    private int _currentDate;        // CURRENT-DATE   9(06) — ACCEPT FROM DATE (bug #6: unused after accept)
    private int _currentYyddd;       // CURRENT-YYDDD  9(05) — ACCEPT FROM DAY (today, drives expiry math)
    private int _wsAuthDate;         // WS-AUTH-DATE   9(05) — decoded real Julian of current detail
    private int _wsExpiryDays;       // WS-EXPIRY-DAYS S9(4) COMP — active expiry threshold
    private int _wsDayDiff;          // WS-DAY-DIFF    S9(4) COMP — CURRENT-YYDDD − WS-AUTH-DATE
    // IDX S9(4) COMP — declared, unused (bug #4).
    private long _wsCurrAppId;       // WS-CURR-APP-ID 9(11) — last summary's account id (chkp display)

    private int _wsNoChkp;           // WS-NO-CHKP            9(8)  VALUE 0
    private int _wsAuthSmryProcCnt;  // WS-AUTH-SMRY-PROC-CNT 9(8)  VALUE 0
    // WS-TOT-REC-WRITTEN S9(8) COMP VALUE 0 — declared, unused (bug #4).
    private int _wsNoSumryRead;      // WS-NO-SUMRY-READ      S9(8) COMP VALUE 0
    private int _wsNoSumryDeleted;   // WS-NO-SUMRY-DELETED   S9(8) COMP VALUE 0
    private int _wsNoDtlRead;        // WS-NO-DTL-READ        S9(8) COMP VALUE 0
    private int _wsNoDtlDeleted;     // WS-NO-DTL-DELETED     S9(8) COMP VALUE 0

    // WS-ERR-FLG (88 ERR-FLG-ON/'Y') — never SET to 'Y'; dead in the outer loop condition (bug #4).
    private bool _errFlgOn;          // WS-ERR-FLG = 'Y'?  (always false)
    private bool _endOfAuthDb;       // WS-END-OF-AUTHDB-FLAG: END-OF-AUTHDB = 'Y'?
    private bool _moreAuths;         // WS-MORE-AUTHS-FLAG:   MORE-AUTHS = 'Y'?  (NO-MORE-AUTHS = !this)
    private bool _qualifiedForDelete;// WS-QUALIFY-DELETE-FLAG: QUALIFIED-FOR-DELETE = 'Y'?
    // WS-INFILE-STATUS / WS-CUSTID-STATUS (88 END-OF-FILE '10') — unused (bug #4).

    // WK-CHKPT-ID = 'RMAD' + WK-CHKPT-ID-CTR(9999, VALUE ZEROES, never incremented) — constant (bug #7).
    private const string WkChkptId = "RMAD0000";

    // ---- PRM-INFO (read from SYSIN, CBPAUP0C.cbl:98-108) ----------------------------------------------
    private string _pExpiryDays = "  ";  // P-EXPIRY-DAYS   9(02)
    private string _pChkpFreq = "     ";  // P-CHKP-FREQ     X(05)
    private string _pChkpDisFreq = "     ";  // P-CHKP-DIS-FREQ X(05)
    private string _pDebugFlag = " ";    // P-DEBUG-FLAG    X(01) (88 DEBUG-ON 'Y')
    private string _prmInfoRaw = "";     // the verbatim SYSIN card, for the 'PARM RECEIVED' echo

    // ---- DIBSTAT — the EXEC DLI interface-block status the program branches on -------------------------
    // The current DL/I status of the last EXEC DLI verb. Values: "  " ok, "GB" end-of-db, "GE" no children.
    private string _dibstat = "  ";

    // ---- The io-areas (PCB INTO targets) --------------------------------------------------------------
    // PENDING-AUTH-SUMMARY (root io-area). Decrements in 4000 mutate THIS object only (bug #1).
    private PautSummary? _summaryRec;
    // PENDING-AUTH-DETAILS (child io-area).
    private PautDetail? _detailRec;

    private bool DebugOn => _pDebugFlag == "Y";   // 88 DEBUG-ON VALUE 'Y'

    private Cbpaup0c(PautSummaryRepository summary, PautDetailRepository detail, IClock clock)
    {
        _summary = summary;
        _detail = detail;
        _clock = clock;
    }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>The batch return code: 0 on a clean run, 16 after <c>9999-ABEND</c> (MOVE 16 TO RETURN-CODE).</summary>
    public int ReturnCode { get; private set; }

    /// <summary>
    /// Runs CBPAUP0C over the relational PAUT_SUMMARY / PAUT_DETAIL tables, given the SYSIN control card
    /// (positional, e.g. <c>"00,00001,00001,Y"</c>). Returns the program result (SYSOUT lines + return
    /// code). A <c>9999-ABEND</c> sets <see cref="Cbpaup0cResult.ReturnCode"/> to 16 and stops the run
    /// (faithful GOBACK to the IMS region controller, not a system abend dump).
    /// </summary>
    /// <param name="summary">PAUT_SUMMARY root repository (forward GN scan + root DLET).</param>
    /// <param name="detail">PAUT_DETAIL child repository (per-parent GNP scan + child DLET).</param>
    /// <param name="sysin">The SYSIN control card read by <c>ACCEPT PRM-INFO FROM SYSIN</c>.</param>
    /// <param name="clock">Clock supplying today's date for <c>ACCEPT … FROM DATE/DAY</c> (defaults to system).</param>
    public static Cbpaup0cResult Run(
        PautSummaryRepository summary,
        PautDetailRepository detail,
        string sysin,
        IClock? clock = null)
    {
        var program = new Cbpaup0c(summary, detail, clock ?? SystemClock.Instance);
        program.MainPara(sysin);
        return new Cbpaup0cResult(program.Sysout, program.ReturnCode);
    }

    // =================================================================================================
    // MAIN-PARA // source: CBPAUP0C.cbl:136-180
    // =================================================================================================
    private void MainPara(string sysin)
    {
        try
        {
            Initialize1000(sysin);                                   // source: CBPAUP0C.cbl:138
            FindNextAuthSummary2000();                               // source: CBPAUP0C.cbl:140

            // PERFORM UNTIL ERR-FLG-ON OR END-OF-AUTHDB. // source: CBPAUP0C.cbl:142
            while (!_errFlgOn && !_endOfAuthDb)
            {
                FindNextAuthDtl3000();                               // source: CBPAUP0C.cbl:144

                // PERFORM UNTIL NO-MORE-AUTHS. // source: CBPAUP0C.cbl:146
                while (_moreAuths)
                {
                    CheckIfExpired4000();                            // source: CBPAUP0C.cbl:147

                    if (_qualifiedForDelete)                         // source: CBPAUP0C.cbl:149
                    {
                        DeleteAuthDtl5000();                         // source: CBPAUP0C.cbl:150
                    }

                    FindNextAuthDtl3000();                           // source: CBPAUP0C.cbl:153
                }

                // IF PA-APPROVED-AUTH-CNT <= 0 AND PA-APPROVED-AUTH-CNT <= 0 (bug #2: duplicated conjunct;
                // PA-DECLINED-AUTH-CNT is NOT tested). // source: CBPAUP0C.cbl:156
                if (_summaryRec!.ApprovedAuthCnt <= 0 && _summaryRec!.ApprovedAuthCnt <= 0)
                {
                    DeleteAuthSummary6000();                         // source: CBPAUP0C.cbl:157
                }

                // IF WS-AUTH-SMRY-PROC-CNT > P-CHKP-FREQ (numeric > X(05); see bug #3). // source: CBPAUP0C.cbl:160
                if (CompareNumericToAlphanumeric(_wsAuthSmryProcCnt, _pChkpFreq) > 0)
                {
                    TakeCheckpoint9000();                            // source: CBPAUP0C.cbl:161
                    _wsAuthSmryProcCnt = 0;                          // MOVE 0 TO WS-AUTH-SMRY-PROC-CNT // source: CBPAUP0C.cbl:163
                }

                FindNextAuthSummary2000();                          // source: CBPAUP0C.cbl:165
            }

            TakeCheckpoint9000();                                    // final checkpoint // source: CBPAUP0C.cbl:169

            _sysout.Add(" ");                                        // source: CBPAUP0C.cbl:171
            _sysout.Add("*-------------------------------------*");  // source: CBPAUP0C.cbl:172
            _sysout.Add("# TOTAL SUMMARY READ  :" + DisplayS9_8(_wsNoSumryRead));    // source: CBPAUP0C.cbl:173
            _sysout.Add("# SUMMARY REC DELETED :" + DisplayS9_8(_wsNoSumryDeleted)); // source: CBPAUP0C.cbl:174
            _sysout.Add("# TOTAL DETAILS READ  :" + DisplayS9_8(_wsNoDtlRead));      // source: CBPAUP0C.cbl:175
            _sysout.Add("# DETAILS REC DELETED :" + DisplayS9_8(_wsNoDtlDeleted));   // source: CBPAUP0C.cbl:176
            _sysout.Add("*-------------------------------------*");  // source: CBPAUP0C.cbl:177
            _sysout.Add(" ");                                        // source: CBPAUP0C.cbl:178

            // GOBACK. // source: CBPAUP0C.cbl:180
        }
        catch (AbendException)
        {
            // 9999-ABEND has already DISPLAYed 'CBPAUP0C ABENDING ...' and set RETURN-CODE = 16; the
            // GOBACK in 9999-ABEND returns to the IMS region controller and does NOT fall through to the
            // totals report. The AbendException unwinds the whole program exactly like that GOBACK.
        }
    }

    // =================================================================================================
    // 1000-INITIALIZE // source: CBPAUP0C.cbl:183-213
    // =================================================================================================
    private void Initialize1000(string sysin)
    {
        // ACCEPT CURRENT-DATE FROM DATE (YYMMDD); ACCEPT CURRENT-YYDDD FROM DAY (Julian YYDDD).
        DateTime now = _clock.Now;                                  // source: CBPAUP0C.cbl:186-187
        _currentDate = AcceptFromDate(now);                        // 9(06) YYMMDD (bug #6: unused after this)
        _currentYyddd = AcceptFromDay(now);                        // 9(05) YYDDD (today)

        // ACCEPT PRM-INFO FROM SYSIN — read the control card positionally. // source: CBPAUP0C.cbl:189
        ParsePrmInfo(sysin);

        _sysout.Add("STARTING PROGRAM CBPAUP0C::");                 // source: CBPAUP0C.cbl:190
        _sysout.Add("*-------------------------------------*");     // source: CBPAUP0C.cbl:191
        _sysout.Add("CBPAUP0C PARM RECEIVED :" + _prmInfoRaw);      // source: CBPAUP0C.cbl:192
        _sysout.Add("TODAYS DATE            :" + Display9_5(_currentYyddd)); // source: CBPAUP0C.cbl:193
        _sysout.Add(" ");                                           // source: CBPAUP0C.cbl:194

        // IF P-EXPIRY-DAYS IS NUMERIC -> use it, ELSE default 5. // source: CBPAUP0C.cbl:196-200
        if (IsNumeric(_pExpiryDays))
        {
            _wsExpiryDays = int.Parse(_pExpiryDays, CultureInfo.InvariantCulture);
        }
        else
        {
            _wsExpiryDays = 5;
        }

        // IF P-CHKP-FREQ = SPACES OR 0 OR LOW-VALUES -> MOVE 5 (yields '5    ', bug #3). // source: CBPAUP0C.cbl:201-203
        if (IsSpacesZeroOrLowValues(_pChkpFreq))
        {
            _pChkpFreq = MoveNumericLiteralToAlpha(5, 5);
        }

        // IF P-CHKP-DIS-FREQ = SPACES OR 0 OR LOW-VALUES -> MOVE 10 (yields '10   ', bug #3). // source: CBPAUP0C.cbl:204-206
        if (IsSpacesZeroOrLowValues(_pChkpDisFreq))
        {
            _pChkpDisFreq = MoveNumericLiteralToAlpha(10, 5);
        }

        // IF P-DEBUG-FLAG NOT = 'Y' -> MOVE 'N'. // source: CBPAUP0C.cbl:207-209
        if (_pDebugFlag != "Y")
        {
            _pDebugFlag = "N";
        }
    }

    // =================================================================================================
    // 2000-FIND-NEXT-AUTH-SUMMARY // source: CBPAUP0C.cbl:216-244
    // =================================================================================================
    private void FindNextAuthSummary2000()
    {
        if (DebugOn)                                                // source: CBPAUP0C.cbl:219-221
        {
            _sysout.Add("DEBUG: AUTH SMRY READ : " + DisplayS9_8(_wsNoSumryRead));
        }

        // EXEC DLI GN SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY). // source: CBPAUP0C.cbl:223-226
        // Forward root cursor MoveNext: '  ' = row returned, 'GB' = end of database.
        string status = _summary.ReadNext(out PautSummary? next);
        _dibstat = (status == FileStatus.Ok) ? "  " : "GB";        // repo '00'->'  ', '10'->'GB'

        // EVALUATE DIBSTAT. // source: CBPAUP0C.cbl:228-241
        switch (_dibstat)
        {
            case "  ":
                _endOfAuthDb = false;                              // SET NOT-END-OF-AUTHDB // source: CBPAUP0C.cbl:230
                _summaryRec = next;
                _wsNoSumryRead++;                                  // ADD 1 TO WS-NO-SUMRY-READ // source: CBPAUP0C.cbl:231
                _wsAuthSmryProcCnt++;                              // ADD 1 TO WS-AUTH-SMRY-PROC-CNT // source: CBPAUP0C.cbl:232
                _wsCurrAppId = _summaryRec!.AcctId;               // MOVE PA-ACCT-ID TO WS-CURR-APP-ID // source: CBPAUP0C.cbl:233

                // Establish parentage for the following GNPs (resolved ACCT_ID = :current_parent_acct_id).
                _detail.StartParentScan(_summaryRec.AcctId);
                break;

            case "GB":
                _endOfAuthDb = true;                              // SET END-OF-AUTHDB // source: CBPAUP0C.cbl:235
                break;

            default:                                              // WHEN OTHER // source: CBPAUP0C.cbl:236-240
                _sysout.Add("AUTH SUMMARY READ FAILED  :" + _dibstat);
                _sysout.Add("SUMMARY READ BEFORE ABEND :" + DisplayS9_8(_wsNoSumryRead));
                Abend9999();
                break;
        }
    }

    // =================================================================================================
    // 3000-FIND-NEXT-AUTH-DTL // source: CBPAUP0C.cbl:248-274
    // =================================================================================================
    private void FindNextAuthDtl3000()
    {
        if (DebugOn)                                                // source: CBPAUP0C.cbl:251-253
        {
            _sysout.Add("DEBUG: AUTH DTL READ : " + DisplayS9_8(_wsNoDtlRead));
        }

        // EXEC DLI GNP SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS). // source: CBPAUP0C.cbl:255-258
        // Per-parent child cursor MoveNext: '  ' = row, exhausted = 'GE' (no more children).
        string status = _detail.ReadNextInParent(out PautDetail? next);
        _dibstat = (status == FileStatus.Ok) ? "  " : "GE";        // repo '00'->'  ', '10'->'GE'

        // EVALUATE DIBSTAT. // source: CBPAUP0C.cbl:259-271
        switch (_dibstat)
        {
            case "  ":
                _moreAuths = true;                                // SET MORE-AUTHS // source: CBPAUP0C.cbl:261
                _detailRec = next;
                _wsNoDtlRead++;                                   // ADD 1 TO WS-NO-DTL-READ // source: CBPAUP0C.cbl:262
                break;

            case "GE":                                            // WHEN 'GE' / WHEN 'GB' -> same action // source: CBPAUP0C.cbl:263-265
            case "GB":
                _moreAuths = false;                               // SET NO-MORE-AUTHS
                break;

            default:                                              // WHEN OTHER // source: CBPAUP0C.cbl:266-270
                _sysout.Add("AUTH DETAIL READ FAILED  :" + _dibstat);
                _sysout.Add("SUMMARY AUTH APP ID      :" + DisplayPaAcctId(_summaryRec));
                _sysout.Add("DETAIL READ BEFORE ABEND :" + DisplayS9_8(_wsNoDtlRead));
                Abend9999();
                break;
        }
    }

    // =================================================================================================
    // 4000-CHECK-IF-EXPIRED // source: CBPAUP0C.cbl:277-300
    // =================================================================================================
    private void CheckIfExpired4000()
    {
        PautDetail d = _detailRec!;

        // COMPUTE WS-AUTH-DATE = 99999 - PA-AUTH-DATE-9C  (decode 9s-complement -> real Julian YYDDD).
        _wsAuthDate = 99999 - d.AuthDate9c;                        // source: CBPAUP0C.cbl:280

        // COMPUTE WS-DAY-DIFF = CURRENT-YYDDD - WS-AUTH-DATE  (S9(4) COMP; raw YYDDD subtraction —
        // NOT a calendar day-diff across year boundaries; may be negative). // source: CBPAUP0C.cbl:282
        _wsDayDiff = StoreS9_4(_currentYyddd - _wsAuthDate);

        if (_wsDayDiff >= _wsExpiryDays)                           // source: CBPAUP0C.cbl:284
        {
            _qualifiedForDelete = true;                           // SET QUALIFIED-FOR-DELETE // source: CBPAUP0C.cbl:285

            PautSummary s = _summaryRec!;
            if (d.AuthRespCode == "00")                            // IF PA-AUTH-RESP-CODE = '00' (approved) // source: CBPAUP0C.cbl:287
            {
                // SUBTRACT 1 FROM PA-APPROVED-AUTH-CNT (S9(04) COMP; may go negative). // source: CBPAUP0C.cbl:288
                s.ApprovedAuthCnt = StoreS9_4(s.ApprovedAuthCnt - 1);
                // SUBTRACT PA-APPROVED-AMT FROM PA-APPROVED-AUTH-AMT
                // (S9(10)V99 detail amt into S9(09)V99 summary amt -> truncate + silent overflow). // source: CBPAUP0C.cbl:289
                s.ApprovedAuthAmt = StoreSummaryAmt(s.ApprovedAuthAmt - d.ApprovedAmt);
            }
            else                                                  // ELSE (declined) // source: CBPAUP0C.cbl:290
            {
                // SUBTRACT 1 FROM PA-DECLINED-AUTH-CNT. // source: CBPAUP0C.cbl:291
                s.DeclinedAuthCnt = StoreS9_4(s.DeclinedAuthCnt - 1);
                // SUBTRACT PA-TRANSACTION-AMT FROM PA-DECLINED-AUTH-AMT (same width mismatch). // source: CBPAUP0C.cbl:292
                s.DeclinedAuthAmt = StoreSummaryAmt(s.DeclinedAuthAmt - d.TransactionAmt);
            }
            // NOTE (bug #1): the mutated summary fields above live ONLY in the in-memory io-area; the
            // program issues no REPL on PAUTSUM0, so PAUT_SUMMARY is never UPDATEd here.
        }
        else
        {
            _qualifiedForDelete = false;                          // SET NOT-QUALIFIED-FOR-DELETE // source: CBPAUP0C.cbl:295
        }
    }

    // =================================================================================================
    // 5000-DELETE-AUTH-DTL // source: CBPAUP0C.cbl:303-325
    // =================================================================================================
    private void DeleteAuthDtl5000()
    {
        PautDetail d = _detailRec!;

        if (DebugOn)                                               // source: CBPAUP0C.cbl:306-308
        {
            _sysout.Add("DEBUG: AUTH DTL DLET : " + DisplayPaAcctId(_summaryRec));
        }

        // EXEC DLI DLET SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS)
        //   -> DELETE FROM PAUT_DETAIL WHERE ACCT_ID=:a AND AUTH_KEY=:k. // source: CBPAUP0C.cbl:310-313
        string status = _detail.Delete(d.AcctId, d.AuthKey);
        _dibstat = (status == FileStatus.Ok) ? "  " : status;     // '00' -> SPACES; anything else -> non-spaces

        if (_dibstat == "  ")                                     // IF DIBSTAT = SPACES // source: CBPAUP0C.cbl:315
        {
            _wsNoDtlDeleted++;                                    // ADD 1 TO WS-NO-DTL-DELETED // source: CBPAUP0C.cbl:316
        }
        else                                                      // source: CBPAUP0C.cbl:317-321
        {
            _sysout.Add("AUTH DETAIL DELETE FAILED :" + _dibstat);
            _sysout.Add("AUTH APP ID               :" + DisplayPaAcctId(_summaryRec));
            Abend9999();
        }
    }

    // =================================================================================================
    // 6000-DELETE-AUTH-SUMMARY // source: CBPAUP0C.cbl:328-349
    // =================================================================================================
    private void DeleteAuthSummary6000()
    {
        PautSummary s = _summaryRec!;

        if (DebugOn)                                               // source: CBPAUP0C.cbl:331-333
        {
            _sysout.Add("DEBUG: AUTH SMRY DLET : " + DisplayPaAcctId(_summaryRec));
        }

        // EXEC DLI DLET SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY)
        //   -> DELETE FROM PAUT_SUMMARY WHERE ACCT_ID=:a (FK ON DELETE CASCADE removes leftover children).
        // source: CBPAUP0C.cbl:335-338
        string status = _summary.Delete(s.AcctId);
        _dibstat = (status == FileStatus.Ok) ? "  " : status;

        if (_dibstat == "  ")                                     // IF DIBSTAT = SPACES // source: CBPAUP0C.cbl:340
        {
            _wsNoSumryDeleted++;                                  // ADD 1 TO WS-NO-SUMRY-DELETED // source: CBPAUP0C.cbl:341
        }
        else                                                      // source: CBPAUP0C.cbl:342-346
        {
            _sysout.Add("AUTH SUMMARY DELETE FAILED :" + _dibstat);
            _sysout.Add("AUTH APP ID                :" + DisplayPaAcctId(_summaryRec));
            Abend9999();
        }
    }

    // =================================================================================================
    // 9000-TAKE-CHECKPOINT // source: CBPAUP0C.cbl:352-374
    // =================================================================================================
    private void TakeCheckpoint9000()
    {
        // EXEC DLI CHKP ID(WK-CHKPT-ID)  -> COMMIT + persist restart token (constant 'RMAD0000', bug #7).
        // The in-memory SQLite repositories autocommit each statement, so the checkpoint is a logical
        // commit boundary; modeled as an always-succeeds CHKP (DIBSTAT = SPACES). // source: CBPAUP0C.cbl:355-356
        _dibstat = "  ";

        if (_dibstat == "  ")                                     // IF DIBSTAT = SPACES // source: CBPAUP0C.cbl:358
        {
            _wsNoChkp++;                                          // ADD 1 TO WS-NO-CHKP // source: CBPAUP0C.cbl:359

            // IF WS-NO-CHKP >= P-CHKP-DIS-FREQ (numeric >= X(05); see bug #3). // source: CBPAUP0C.cbl:360
            if (CompareNumericToAlphanumeric(_wsNoChkp, _pChkpDisFreq) >= 0)
            {
                _wsNoChkp = 0;                                    // MOVE 0 TO WS-NO-CHKP // source: CBPAUP0C.cbl:361
                _sysout.Add("CHKP SUCCESS: AUTH COUNT - " + DisplayS9_8(_wsNoSumryRead) +
                            ", APP ID - " + Display9_11(_wsCurrAppId)); // source: CBPAUP0C.cbl:362-363
            }
        }
        else                                                      // source: CBPAUP0C.cbl:365-369
        {
            _sysout.Add("CHKP FAILED: DIBSTAT - " + _dibstat +
                        ", REC COUNT - " + DisplayS9_8(_wsNoSumryRead) +
                        ", APP ID - " + Display9_11(_wsCurrAppId));
            Abend9999();
        }

        _ = WkChkptId; // WK-CHKPT-ID is the (constant) checkpoint id; referenced for parity, never changes.
    }

    // =================================================================================================
    // 9999-ABEND // source: CBPAUP0C.cbl:377-383
    // =================================================================================================
    private void Abend9999()
    {
        _sysout.Add("CBPAUP0C ABENDING ...");                     // source: CBPAUP0C.cbl:380
        ReturnCode = 16;                                          // MOVE 16 TO RETURN-CODE // source: CBPAUP0C.cbl:382
        // GOBACK — returns to the IMS region controller; does NOT unwind via ERR-FLG. // source: CBPAUP0C.cbl:383
        throw new AbendException("16", "CBPAUP0C abend (RETURN-CODE 16).");
    }

    // =================================================================================================
    // SYSIN parsing (ACCEPT PRM-INFO FROM SYSIN — positional card with literal-comma FILLERs)
    // PRM-INFO: P-EXPIRY-DAYS 9(02) | FILLER X(01) | P-CHKP-FREQ X(05) | FILLER X(01) |
    //           P-CHKP-DIS-FREQ X(05) | FILLER X(01) | P-DEBUG-FLAG X(01) | FILLER X(01).
    // source: CBPAUP0C.cbl:98-108
    // =================================================================================================
    private void ParsePrmInfo(string sysin)
    {
        // ACCEPT moves the SYSIN card byte-for-byte into the 16-byte PRM-INFO group (space-padded to its
        // length). Slice each subfield by its fixed offset, exactly like the COBOL group overlay.
        string card = sysin ?? string.Empty;
        const int prmLen = 2 + 1 + 5 + 1 + 5 + 1 + 1 + 1; // = 16
        string padded = card.Length >= prmLen ? card[..prmLen] : card.PadRight(prmLen, ' ');
        _prmInfoRaw = padded;

        _pExpiryDays = padded.Substring(0, 2);   // 9(02)
        // padded[2]                              // FILLER X(01) (the literal comma)
        _pChkpFreq = padded.Substring(3, 5);     // X(05)
        // padded[8]                              // FILLER X(01)
        _pChkpDisFreq = padded.Substring(9, 5);  // X(05)
        // padded[14]                             // FILLER X(01)
        _pDebugFlag = padded.Substring(15, 1);   // X(01)
        // (trailing FILLER X(01) lies beyond the 16-byte group for an exactly-16 card.)
    }

    // =================================================================================================
    // COBOL numeric/arithmetic helpers
    // =================================================================================================

    /// <summary>ACCEPT … FROM DATE — the system date as YYMMDD packed into 9(06).</summary>
    private static int AcceptFromDate(DateTime now)
        => (now.Year % 100) * 10000 + now.Month * 100 + now.Day;

    /// <summary>ACCEPT … FROM DAY — the system Julian date as YYDDD packed into 9(05).</summary>
    private static int AcceptFromDay(DateTime now)
        => (now.Year % 100) * 1000 + now.DayOfYear;

    /// <summary>
    /// Stores a value into an S9(4) COMP (signed binary halfword) field, reproducing COBOL's
    /// truncate-toward-zero and silent high-order overflow at 4 integer digits (modulo 10^4, signed).
    /// </summary>
    private static int StoreS9_4(int value) => (int)Decimals.Store(value, CountIntDigits, 0, signed: true);

    /// <summary>
    /// Stores an amount into an S9(09)V99 COMP-3 summary field (9 integer digits, 2 fraction), reproducing
    /// the truncate-toward-zero + silent high-order overflow that occurs when an S9(10)V99 detail amount is
    /// subtracted into the narrower summary field (no ON SIZE ERROR). // source: CBPAUP0C.cbl:289,292
    /// </summary>
    private static decimal StoreSummaryAmt(decimal value)
        => Decimals.Store(value, SummaryAmtIntDigits, SummaryAmtScale, signed: true);

    /// <summary>COBOL <c>IS NUMERIC</c> class test for a PIC 9(02) display field (all chars are digits).</summary>
    private static bool IsNumeric(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s)
            if (ch < '0' || ch > '9') return false;
        return true;
    }

    /// <summary>
    /// COBOL <c>field = SPACES OR 0 OR LOW-VALUES</c> for an X(05) alphanumeric. True when the bytes are
    /// all spaces, all NUL (LOW-VALUES), or compare as the numeric literal 0 (all-digit content == 0).
    /// </summary>
    private static bool IsSpacesZeroOrLowValues(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        bool allSpaces = true, allNul = true, allDigits = true;
        foreach (char ch in s)
        {
            if (ch != ' ') allSpaces = false;
            if (ch != '\0') allNul = false;
            if (ch < '0' || ch > '9') allDigits = false;
        }
        if (allSpaces || allNul) return true;
        // "= 0": the numeric comparison of an all-digit alphanumeric to the literal 0.
        return allDigits && long.Parse(s, CultureInfo.InvariantCulture) == 0;
    }

    /// <summary>
    /// Reproduces <c>MOVE &lt;numeric-literal&gt; TO &lt;X(width)&gt;</c>: a numeric literal moved into an
    /// alphanumeric receiving field is stored <b>left-justified, space-filled</b> (so <c>MOVE 5</c> →
    /// <c>'5    '</c>, <c>MOVE 10</c> → <c>'10   '</c> for X(05)). // source: CBPAUP0C.cbl:202,205 (bug #3)
    /// </summary>
    private static string MoveNumericLiteralToAlpha(int literal, int width)
    {
        string digits = literal.ToString(CultureInfo.InvariantCulture);
        return digits.Length >= width ? digits[..width] : digits.PadRight(width, ' ');
    }

    /// <summary>
    /// Compares a numeric counter (left operand) against an X(05) alphanumeric (right operand), as IBM
    /// Enterprise COBOL does for a numeric-vs-alphanumeric relation (bug #3). The alphanumeric operand is
    /// interpreted numerically: leading/embedded characters are read as their digit value; under the
    /// CardDemo run card the values are <c>'00001'</c> (== 1) or the defaulted left-justified space-filled
    /// <c>'5    '</c>/<c>'10   '</c>. We parse the leading run of digits (ignoring trailing spaces), which
    /// yields 1 for <c>'00001'</c>, 5 for <c>'5    '</c>, and 10 for <c>'10   '</c> — matching the observed
    /// comparison value. A non-digit lead yields 0.
    /// </summary>
    private static int CompareNumericToAlphanumeric(int left, string alpha)
    {
        long right = AlphanumericAsNumber(alpha);
        return left.CompareTo((int)right);
    }

    /// <summary>
    /// Numeric value of an alphanumeric checkpoint-frequency field: the leading run of digits, trimmed of
    /// surrounding spaces (so <c>'00001'</c>→1, <c>'5    '</c>→5, <c>'10   '</c>→10, all-blank→0).
    /// </summary>
    private static long AlphanumericAsNumber(string alpha)
    {
        string t = (alpha ?? string.Empty).Trim();
        if (t.Length == 0) return 0;
        long v = 0;
        bool any = false;
        foreach (char ch in t)
        {
            if (ch < '0' || ch > '9') break;
            v = v * 10 + (ch - '0');
            any = true;
        }
        return any ? v : 0;
    }

    // =================================================================================================
    // COBOL DISPLAY formatting (SYSOUT characterization)
    // =================================================================================================

    /// <summary>
    /// Renders an <c>S9(8) COMP</c> counter as IBM COBOL <c>DISPLAY</c> would: a leading sign position
    /// (space for non-negative, '-' for negative) followed by 8 unsigned digits, zero-padded.
    /// </summary>
    private static string DisplayS9_8(int value)
    {
        int magnitude = Math.Abs(value);
        string digits = (magnitude % 100000000).ToString("D8", CultureInfo.InvariantCulture);
        return (value < 0 ? "-" : " ") + digits;
    }

    /// <summary>Renders a <c>9(05)</c> unsigned display field as 5 zero-padded digits.</summary>
    private static string Display9_5(int value)
        => (Math.Abs(value) % 100000).ToString("D5", CultureInfo.InvariantCulture);

    /// <summary>Renders a <c>9(11)</c> unsigned display field as 11 zero-padded digits.</summary>
    private static string Display9_11(long value)
        => (Math.Abs(value) % 100000000000L).ToString("D11", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders <c>PA-ACCT-ID</c> (S9(11) COMP-3) as DISPLAY would for a signed packed field: a leading
    /// sign position (space / '-') and 11 unsigned digits. Used in the DEBUG and abend lines that DISPLAY
    /// PA-ACCT-ID directly. A null io-area renders as the all-zero default.
    /// </summary>
    private static string DisplayPaAcctId(PautSummary? summary)
    {
        long value = summary?.AcctId ?? 0L;
        long magnitude = Math.Abs(value);
        string digits = (magnitude % 100000000000L).ToString("D11", CultureInfo.InvariantCulture);
        return (value < 0 ? "-" : " ") + digits;
    }
}

/// <summary>
/// The result of a <see cref="Cbpaup0c"/> run: the SYSOUT (DISPLAY) lines in order and the batch
/// <see cref="ReturnCode"/> (0 on a clean run, 16 after <c>9999-ABEND</c>).
/// </summary>
/// <param name="Sysout">The DISPLAY lines emitted to SYSOUT, in order.</param>
/// <param name="ReturnCode">The COBOL RETURN-CODE: 0 on success, 16 on abend.</param>
public sealed record Cbpaup0cResult(IReadOnlyList<string> Sysout, int ReturnCode);
