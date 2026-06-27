using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Ims;

/// <summary>
/// Faithful relational re-port of the IMS batch utility <c>DBUNLDGS</c> ("IMSUNLOD" — unload the
/// pending-authorization IMS DB into two GSAM files). It is the GSAM-output twin of <see cref="PendingAuthDbUnloadUtility"/>:
/// it walks the HIDAM Pending-Auth database root-by-root with <c>CALL 'CBLTDLI' FUNC-GN</c> over
/// <c>PAUTSUM0</c> and, for each summary root, <c>FUNC-GNP</c> over its <c>PAUTDTL1</c> children — but
/// instead of <c>WRITE</c>-ing QSAM records it <c>ISRT</c>s each summary into the GSAM summary PCB
/// (<c>PASFLPCB</c>) and each detail into the GSAM detail PCB (<c>PADFLPCB</c>).
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from
/// <c>app/app-authorization-ims-db2-mq/cbl/DBUNLDGS.CBL</c> (read in full). Each PROCEDURE-DIVISION
/// paragraph is a method whose name mirrors the COBOL paragraph and whose body keeps the original
/// statement order and control flow (with <c>// source: DBUNLDGS.CBL:NNN</c> citations).</para>
/// <para>DL/I → repository/file mapping: <b>GN</b>(PAUTSUM0) → <see cref="PautSummaryRepository.ReadNext"/>;
/// <b>GNP</b>(PAUTDTL1) → <see cref="PautDetailRepository.StartParentScan"/> +
/// <see cref="PautDetailRepository.ReadNextInParent"/>; <b>ISRT</b> into the two GSAM PCBs → an append to
/// the corresponding sequential fixed-width output file. Per the unit brief the two GSAM output PCBs are
/// treated as two sequential output files using the SAME segment images as <see cref="PendingAuthDbUnloadUtility"/>: the
/// summary GSAM file holds 100-byte CIPAUSMY images, the detail GSAM file holds 200-byte CIPAUDTY images.
/// A GSAM ISRT always succeeds here, so PASFL-PCB-STATUS / PADFL-PCB-STATUS are SPACES (the abend branch is
/// reproduced but never taken in normal operation).</para>
/// <para><b>Note vs. PAUDBUNL</b>: DBUNLDGS ISRTs <c>PENDING-AUTH-DETAILS</c> (200 bytes) into the detail
/// GSAM with no 6-byte ROOT-SEG-KEY prefix — the <c>WRITE OPFIL2-REC</c> is commented out in the source and
/// replaced by <c>3200-INSERT-CHILD-SEG-GSAM</c>. (The ROOT-SEG-KEY MOVEs still run against the now
/// WORKING-STORAGE OPFIL2-REC but are never written.) This differs from PAUDBUNL's 206-byte OUTFIL2 record;
/// it is the faithful behaviour of this program.</para>
/// <para>FAITHFUL BUGS preserved verbatim (identical to PAUDBUNL):
/// <list type="number">
/// <item>#1 — <c>3000-FIND-NEXT-AUTH-DTL</c> increments the <i>summary</i> counters (WS-NO-SUMRY-READ,
/// WS-AUTH-SMRY-PROC-CNT) once per <i>detail</i> read.</item>
/// <item>#2 — the GNP loop is driven by <c>WS-END-OF-CHILD-SEG</c>; the MORE-AUTHS/NO-MORE-AUTHS 88s are
/// SET but never tested.</item>
/// <item>#3 — the "GSAM PARENT FAIL" DISPLAY in <c>3200-INSERT-CHILD-SEG-GSAM</c> is mislabelled (it is the
/// child-segment insert, not the parent). Reproduced verbatim.</item>
/// </list></para>
/// </remarks>
public sealed class PendingAuthGsamUnloadUtility
{
    private readonly PautSummaryRepository _summary;
    private readonly PautDetailRepository _detail;
    private readonly IClock _clock;
    private readonly HostKind _host;
    private readonly List<string> _sysout = [];

    // ---- WORKING-STORAGE: WS-VARIABLES (DBUNLDGS.CBL:57-94) -------------------------------------------
    private int _currentDate;        // CURRENT-DATE   9(06) — ACCEPT FROM DATE
    private int _currentYyddd;       // CURRENT-YYDDD  9(05) — ACCEPT FROM DAY
    // WS-AUTH-DATE/WS-EXPIRY-DAYS/WS-DAY-DIFF/IDX/WS-CURR-APP-ID — declared, unused.

    private int _wsNoChkp;           // WS-NO-CHKP            9(8)  VALUE 0 (unused)
    private int _wsAuthSmryProcCnt;  // WS-AUTH-SMRY-PROC-CNT 9(8)  VALUE 0
    private int _wsNoSumryRead;      // WS-NO-SUMRY-READ      S9(8) COMP VALUE 0
    private int _wsNoSumryDeleted;   // WS-NO-SUMRY-DELETED   S9(8) COMP VALUE 0 (unused)
    private int _wsNoDtlRead;        // WS-NO-DTL-READ        S9(8) COMP VALUE 0 (unused — bug #1)
    private int _wsNoDtlDeleted;     // WS-NO-DTL-DELETED     S9(8) COMP VALUE 0 (unused)

    private bool _endOfAuthDb;       // WS-END-OF-AUTHDB-FLAG (inert)
    private bool _moreAuths;         // WS-MORE-AUTHS-FLAG    (inert, bug #2)
    private string _wsEndOfRootSeg = " ";   // WS-END-OF-ROOT-SEG  X(01) — root loop sentinel
    private string _wsEndOfChildSeg = " ";  // WS-END-OF-CHILD-SEG X(01) — child loop sentinel

    // ---- PCB statuses the program branches on --------------------------------------------------------
    private string _pautPcbStatus = "  ";   // PAUT-PCB-STATUS: '  ' ok / 'GB' end-of-db / 'GE' no-more
    private string _pautKeyfb = "";         // PAUT-KEYFB
    private string _pasflPcbStatus = "  ";  // PASFL-PCB-STATUS (GSAM summary): SPACES = ok
    private string _pasflKeyfb = "";        // PASFL-KEYFB
    private string _padflPcbStatus = "  ";  // PADFL-PCB-STATUS (GSAM detail): SPACES = ok
    private string _padflKeyfb = "";        // PADFL-KEYFB

    // ---- The io-areas (PCB INTO targets) -------------------------------------------------------------
    private PautSummary? _summaryRec;       // PENDING-AUTH-SUMMARY (root io-area)
    private PautDetail? _detailRec;         // PENDING-AUTH-DETAILS (child io-area)

    // ---- GSAM output datasets (the two ISRT targets) -------------------------------------------------
    private FileStream? _pasflGsam;         // PASFLPCB — GSAM summary file (100-byte images)
    private FileStream? _padflGsam;         // PADFLPCB — GSAM detail file (200-byte images)

    /// <summary>The batch return code: 0 on a clean run, 16 after <c>9999-ABEND</c>.</summary>
    public int ReturnCode { get; private set; }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    private PendingAuthGsamUnloadUtility(PautSummaryRepository summary, PautDetailRepository detail, IClock clock, HostKind host)
    {
        _summary = summary;
        _detail = detail;
        _clock = clock;
        _host = host;
    }

    /// <summary>
    /// Runs DBUNLDGS, unloading the relational PAUT_SUMMARY / PAUT_DETAIL tables into the two GSAM output
    /// files. <paramref name="summaryGsamPath"/> receives the 100-byte summary images (PASFLPCB);
    /// <paramref name="detailGsamPath"/> receives the 200-byte detail images (PADFLPCB).
    /// </summary>
    /// <param name="summary">PAUT_SUMMARY root repository (forward GN scan).</param>
    /// <param name="detail">PAUT_DETAIL child repository (per-parent GNP scan).</param>
    /// <param name="summaryGsamPath">GSAM summary dataset path (PASFLPCB output, LRECL 100).</param>
    /// <param name="detailGsamPath">GSAM detail dataset path (PADFLPCB output, LRECL 200).</param>
    /// <param name="clock">Clock for <c>ACCEPT … FROM DATE/DAY</c> (defaults to system).</param>
    /// <param name="host">Host encoding for the output datasets (defaults to EBCDIC).</param>
    public static PendingAuthGsamUnloadUtilityResult Run(
        PautSummaryRepository summary,
        PautDetailRepository detail,
        string summaryGsamPath,
        string detailGsamPath,
        IClock? clock = null,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new PendingAuthGsamUnloadUtility(summary, detail, clock ?? SystemClock.Instance, host);
        program.MainPara(summaryGsamPath, detailGsamPath);
        return new PendingAuthGsamUnloadUtilityResult(program.Sysout, program.ReturnCode);
    }

    // =================================================================================================
    // MAIN-PARA // source: DBUNLDGS.CBL:164-179
    // =================================================================================================
    private void MainPara(string summaryGsamPath, string detailGsamPath)
    {
        try
        {
            // ENTRY 'DLITCBL' USING PAUTBPCB PASFLPCB PADFLPCB. // source: DBUNLDGS.CBL:165-167

            Initialize1000(summaryGsamPath, detailGsamPath);          // source: DBUNLDGS.CBL:170

            // PERFORM 2000-FIND-NEXT-AUTH-SUMMARY UNTIL WS-END-OF-ROOT-SEG = 'Y'. // source: DBUNLDGS.CBL:172-173
            _summary.StartBrowse();
            while (_wsEndOfRootSeg != "Y")
            {
                FindNextAuthSummary2000();
            }

            FileClose4000();                                          // source: DBUNLDGS.CBL:175

            // GOBACK. // source: DBUNLDGS.CBL:179
        }
        catch (AbendException)
        {
            // 9999-ABEND DISPLAYed 'DBUNLDGS ABENDING ...' and set RETURN-CODE = 16; the GOBACK returns to
            // the IMS region controller. The AbendException unwinds exactly like that GOBACK.
        }
        finally
        {
            _pasflGsam?.Dispose();
            _padflGsam?.Dispose();
        }
    }

    // =================================================================================================
    // 1000-INITIALIZE // source: DBUNLDGS.CBL:182-213
    // (The OPEN OUTPUT statements for the QSAM OPFILE1/OPFILE2 are commented out in the source; the GSAM
    //  datasets are opened by the IMS region. Here we create the two GSAM output files.)
    // =================================================================================================
    private void Initialize1000(string summaryGsamPath, string detailGsamPath)
    {
        // ACCEPT CURRENT-DATE FROM DATE; ACCEPT CURRENT-YYDDD FROM DAY. // source: DBUNLDGS.CBL:185-186
        DateTime now = _clock.Now;
        _currentDate = AcceptFromDate(now);
        _currentYyddd = AcceptFromDay(now);

        // ACCEPT PRM-INFO FROM SYSIN is commented out in the source. // source: DBUNLDGS.CBL:188

        _sysout.Add("STARTING PROGRAM DBUNLDGS::");                   // source: DBUNLDGS.CBL:189
        _sysout.Add("*-------------------------------------*");       // source: DBUNLDGS.CBL:190
        _sysout.Add("TODAYS DATE            :" + Display9_6(_currentDate)); // source: DBUNLDGS.CBL:191
        _sysout.Add(" ");                                             // source: DBUNLDGS.CBL:192

        // The GSAM PCBs are output datasets the region opens; create them fresh.
        _pasflGsam = OpenOutput(summaryGsamPath);
        _padflGsam = OpenOutput(detailGsamPath);
    }

    // =================================================================================================
    // 2000-FIND-NEXT-AUTH-SUMMARY // source: DBUNLDGS.CBL:216-258
    // =================================================================================================
    private void FindNextAuthSummary2000()
    {
        // INITIALIZE PAUT-PCB-STATUS. // source: DBUNLDGS.CBL:221
        _pautPcbStatus = "  ";

        // CALL 'CBLTDLI' USING FUNC-GN PAUTBPCB PENDING-AUTH-SUMMARY ROOT-UNQUAL-SSA. // source: DBUNLDGS.CBL:222-225
        string status = _summary.ReadNext(out PautSummary? next);
        _pautPcbStatus = (status == FileStatus.Ok) ? "  " : "GB";

        // IF PAUT-PCB-STATUS = SPACES ... // source: DBUNLDGS.CBL:232
        if (_pautPcbStatus == "  ")
        {
            _summaryRec = next;
            _wsNoSumryRead++;                                         // ADD 1 TO WS-NO-SUMRY-READ // source: DBUNLDGS.CBL:234
            _wsAuthSmryProcCnt++;                                     // ADD 1 TO WS-AUTH-SMRY-PROC-CNT // source: DBUNLDGS.CBL:235

            // MOVE PENDING-AUTH-SUMMARY TO OPFIL1-REC. // source: DBUNLDGS.CBL:236
            // INITIALIZE ROOT-SEG-KEY; INITIALIZE CHILD-SEG-REC; MOVE PA-ACCT-ID TO ROOT-SEG-KEY.
            // (OPFIL1-REC / ROOT-SEG-KEY are now WORKING-STORAGE; the GSAM ISRT uses PENDING-AUTH-SUMMARY.)
            // source: DBUNLDGS.CBL:237-239
            byte[] rootSegKey = PautSegmentImages.EncodeRootSegKey(_summaryRec!.AcctId);

            // IF PA-ACCT-ID IS NUMERIC ... // source: DBUNLDGS.CBL:241
            if (PautSegmentImages.IsNumericComp3(rootSegKey))
            {
                // WRITE OPFIL1-REC is commented out; replaced by 3100-INSERT-PARENT-SEG-GSAM. // source: DBUNLDGS.CBL:242-243
                InsertParentSegGsam3100();

                // INITIALIZE WS-END-OF-CHILD-SEG. // source: DBUNLDGS.CBL:244
                _wsEndOfChildSeg = " ";

                // Establish parentage for the following GNPs (this root's children).
                _detail.StartParentScan(_summaryRec.AcctId);

                // PERFORM 3000-FIND-NEXT-AUTH-DTL UNTIL WS-END-OF-CHILD-SEG = 'Y'. // source: DBUNLDGS.CBL:245-246
                while (_wsEndOfChildSeg != "Y")
                {
                    FindNextAuthDtl3000();
                }
            }
        }

        // IF PAUT-PCB-STATUS = 'GB' ... // source: DBUNLDGS.CBL:249
        if (_pautPcbStatus == "GB")
        {
            _endOfAuthDb = true;                                      // SET END-OF-AUTHDB TO TRUE // source: DBUNLDGS.CBL:250
            _wsEndOfRootSeg = "Y";                                    // MOVE 'Y' TO WS-END-OF-ROOT-SEG // source: DBUNLDGS.CBL:251
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'GB' ... // source: DBUNLDGS.CBL:253
        if (_pautPcbStatus != "  " && _pautPcbStatus != "GB")
        {
            _sysout.Add("AUTH SUM  GN FAILED  :" + _pautPcbStatus);   // source: DBUNLDGS.CBL:254
            _sysout.Add("KEY FEEDBACK AREA    :" + _pautKeyfb);       // source: DBUNLDGS.CBL:255
            Abend9999();                                              // source: DBUNLDGS.CBL:256
        }
    }

    // =================================================================================================
    // 3000-FIND-NEXT-AUTH-DTL // source: DBUNLDGS.CBL:263-297
    // =================================================================================================
    private void FindNextAuthDtl3000()
    {
        // CALL 'CBLTDLI' USING FUNC-GNP PAUTBPCB PENDING-AUTH-DETAILS CHILD-UNQUAL-SSA. // source: DBUNLDGS.CBL:267-270
        string status = _detail.ReadNextInParent(out PautDetail? next);
        _pautPcbStatus = (status == FileStatus.Ok) ? "  " : "GE";

        // IF PAUT-PCB-STATUS = SPACES ... // source: DBUNLDGS.CBL:276
        if (_pautPcbStatus == "  ")
        {
            _moreAuths = true;                                       // SET MORE-AUTHS TO TRUE (inert, bug #2) // source: DBUNLDGS.CBL:277
            _detailRec = next;

            // FAITHFUL BUG #1: summary counters incremented per detail read. // source: DBUNLDGS.CBL:278-279
            _wsNoSumryRead++;                                        // ADD 1 TO WS-NO-SUMRY-READ
            _wsAuthSmryProcCnt++;                                    // ADD 1 TO WS-AUTH-SMRY-PROC-CNT

            // MOVE PENDING-AUTH-DETAILS TO CHILD-SEG-REC (WRITE OPFIL2-REC is commented out). // source: DBUNLDGS.CBL:280-281
            // PERFORM 3200-INSERT-CHILD-SEG-GSAM. // source: DBUNLDGS.CBL:282
            InsertChildSegGsam3200();
        }

        // IF PAUT-PCB-STATUS = 'GE' ... // source: DBUNLDGS.CBL:284
        if (_pautPcbStatus == "GE")
        {
            _wsEndOfChildSeg = "Y";                                  // MOVE 'Y' TO WS-END-OF-CHILD-SEG // source: DBUNLDGS.CBL:286
            _sysout.Add("CHILD SEG FLAG GE : " + _wsEndOfChildSeg);  // source: DBUNLDGS.CBL:287-288
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'GE' ... // source: DBUNLDGS.CBL:290
        if (_pautPcbStatus != "  " && _pautPcbStatus != "GE")
        {
            _sysout.Add("GNP CALL FAILED  :" + _pautPcbStatus);      // source: DBUNLDGS.CBL:291
            _sysout.Add("KFB AREA IN CHILD:" + _pautKeyfb);          // source: DBUNLDGS.CBL:292
            Abend9999();                                             // source: DBUNLDGS.CBL:293
        }

        // INITIALIZE PAUT-PCB-STATUS. // source: DBUNLDGS.CBL:295
        _pautPcbStatus = "  ";
    }

    // =================================================================================================
    // 3100-INSERT-PARENT-SEG-GSAM // source: DBUNLDGS.CBL:300-317
    // =================================================================================================
    private void InsertParentSegGsam3100()
    {
        // CALL 'CBLTDLI' USING FUNC-ISRT PASFLPCB PENDING-AUTH-SUMMARY. // source: DBUNLDGS.CBL:302-304
        byte[] summaryImage = PautSegmentImages.EncodeSummary(_summaryRec!, _host);
        _pasflGsam!.Write(summaryImage, 0, summaryImage.Length);
        _pasflPcbStatus = "  ";                                       // GSAM ISRT succeeds -> SPACES

        // IF PASFL-PCB-STATUS NOT EQUAL TO SPACES ... // source: DBUNLDGS.CBL:311
        if (_pasflPcbStatus != "  ")
        {
            _sysout.Add("GSAM PARENT FAIL :" + _pasflPcbStatus);     // source: DBUNLDGS.CBL:312
            _sysout.Add("KFB AREA IN GSAM:" + _pasflKeyfb);          // source: DBUNLDGS.CBL:313
            Abend9999();                                             // source: DBUNLDGS.CBL:314
        }
    }

    // =================================================================================================
    // 3200-INSERT-CHILD-SEG-GSAM // source: DBUNLDGS.CBL:319-336
    // =================================================================================================
    private void InsertChildSegGsam3200()
    {
        // CALL 'CBLTDLI' USING FUNC-ISRT PADFLPCB PENDING-AUTH-DETAILS. // source: DBUNLDGS.CBL:321-323
        byte[] detailImage = PautSegmentImages.EncodeDetail(_detailRec!, _host);
        _padflGsam!.Write(detailImage, 0, detailImage.Length);
        _padflPcbStatus = "  ";                                       // GSAM ISRT succeeds -> SPACES

        // IF PADFL-PCB-STATUS NOT EQUAL TO SPACES ... // source: DBUNLDGS.CBL:330
        if (_padflPcbStatus != "  ")
        {
            // FAITHFUL BUG #3: mislabelled 'GSAM PARENT FAIL' for the child-segment insert. // source: DBUNLDGS.CBL:331
            _sysout.Add("GSAM PARENT FAIL :" + _padflPcbStatus);     // source: DBUNLDGS.CBL:331
            _sysout.Add("KFB AREA IN GSAM:" + _padflKeyfb);          // source: DBUNLDGS.CBL:332
            Abend9999();                                             // source: DBUNLDGS.CBL:333
        }
    }

    // =================================================================================================
    // 4000-FILE-CLOSE // source: DBUNLDGS.CBL:338-355
    // (The QSAM CLOSE statements are commented out; only the DISPLAY remains. The GSAM datasets are closed
    //  by the region — flushed/disposed here.)
    // =================================================================================================
    private void FileClose4000()
    {
        _sysout.Add("CLOSING THE FILE");                             // source: DBUNLDGS.CBL:339
        _pasflGsam?.Flush();
        _padflGsam?.Flush();
    }

    // =================================================================================================
    // 9999-ABEND // source: DBUNLDGS.CBL:357-363
    // =================================================================================================
    private void Abend9999()
    {
        _sysout.Add("DBUNLDGS ABENDING ...");                        // source: DBUNLDGS.CBL:360
        ReturnCode = 16;                                             // MOVE 16 TO RETURN-CODE // source: DBUNLDGS.CBL:362
        // GOBACK. // source: DBUNLDGS.CBL:363
        throw new AbendException("16", "DBUNLDGS abend (RETURN-CODE 16).");
    }

    // =================================================================================================
    // COBOL ACCEPT / DISPLAY helpers
    // =================================================================================================

    private static int AcceptFromDate(DateTime now)
        => (now.Year % 100) * 10000 + now.Month * 100 + now.Day;

    private static int AcceptFromDay(DateTime now)
        => (now.Year % 100) * 1000 + now.DayOfYear;

    private static string Display9_6(int value)
        => (Math.Abs(value) % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    private static FileStream OpenOutput(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }
}

/// <summary>
/// The result of a <see cref="PendingAuthGsamUnloadUtility"/> run: the SYSOUT (DISPLAY) lines in order and the batch
/// <see cref="ReturnCode"/> (0 on a clean run, 16 after <c>9999-ABEND</c>).
/// </summary>
/// <param name="Sysout">The DISPLAY lines emitted to SYSOUT, in order.</param>
/// <param name="ReturnCode">The COBOL RETURN-CODE: 0 on success, 16 on abend.</param>
public sealed record PendingAuthGsamUnloadUtilityResult(IReadOnlyList<string> Sysout, int ReturnCode);
