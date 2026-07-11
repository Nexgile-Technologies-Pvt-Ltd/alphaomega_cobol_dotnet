using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Ims;

/// <summary>
/// Faithful relational re-port of the IMS batch utility <c>PAUDBUNL</c> ("IMSUNLOD" — unload the
/// pending-authorization IMS DB to two sequential files). The original walks the HIDAM Pending-Auth
/// database root-by-root with <c>CALL 'CBLTDLI' FUNC-GN</c> over <c>PAUTSUM0</c>; for every summary root
/// it writes the summary image to <c>OUTFIL1</c> (OPFILE1) and then iterates that root's
/// <c>PAUTDTL1</c> children with <c>FUNC-GNP</c>, writing each detail image to <c>OUTFIL2</c> (OPFILE2).
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from
/// <c>app/app-authorization-ims-db2-mq/cbl/PAUDBUNL.CBL</c> (read in full). Each PROCEDURE-DIVISION
/// paragraph is a method whose name mirrors the COBOL paragraph and whose body keeps the original
/// statement order and control flow (with <c>// source: PAUDBUNL.CBL:NNN</c> citations).</para>
/// <para>DL/I → repository mapping (matching <c>CBPAUP0C.cs</c>): <b>GN</b>(PAUTSUM0) → a forward root
/// cursor over <see cref="PautSummaryRepository.ReadNext"/> (<c>'  '</c>=row / <c>'GB'</c>=end-of-db);
/// <b>GNP</b>(PAUTDTL1) → a per-parent child cursor over
/// <see cref="PautDetailRepository.StartParentScan"/> + <see cref="PautDetailRepository.ReadNextInParent"/>
/// (<c>'  '</c>=row / <c>'GE'</c>=no-more-children). The PCB status byte is mapped <c>'00'→'  '</c>,
/// <c>'10'→'GB'/'GE'</c>.</para>
/// <para><b>Output record formats</b>: <c>OUTFIL1</c> records are the 100-byte CIPAUSMY summary image
/// (OPFIL1-REC PIC X(100)). <c>OUTFIL2</c> records are 206 bytes: a 6-byte ROOT-SEG-KEY (PA-ACCT-ID
/// S9(11) COMP-3) followed by the 200-byte CIPAUDTY detail image (CHILD-SEG-REC X(200)) — exactly the
/// layout <see cref="PendingAuthDbLoadUtility"/> reads back, so an unload then load round-trips.</para>
/// <para>FAITHFUL BUGS preserved verbatim:
/// <list type="number">
/// <item>#1 — <c>3000-FIND-NEXT-AUTH-DTL</c> increments <b>WS-NO-SUMRY-READ</b> and
/// <b>WS-AUTH-SMRY-PROC-CNT</b> (the <i>summary</i> counters) once per <i>detail</i> read — the detail
/// counters WS-NO-DTL-READ are never touched here. Reproduced exactly.</item>
/// <item>#2 — the GNP loop is driven by <c>WS-END-OF-CHILD-SEG</c> (set only on <c>'GE'</c>); the
/// <c>MORE-AUTHS</c>/<c>NO-MORE-AUTHS</c> 88s are SET but never tested, so they are inert.</item>
/// <item>#3 — many WS counters/flags (WS-EXPIRY-DAYS, WS-DAY-DIFF, IDX, WS-NO-CHKP, WS-NO-DTL-DELETED,
/// the IMS-RETURN-CODE 88s, etc.) are declared and never used; carried as inert fields.</item>
/// <item>#4 — <c>ACCEPT CURRENT-DATE FROM DATE</c>/<c>FROM DAY</c> populate fields only used in a startup
/// DISPLAY; kept as side-effect-free accepts. <c>ACCEPT PRM-INFO FROM SYSIN</c> is commented out in the
/// source, so PRM-INFO is never read.</item>
/// </list></para>
/// </remarks>
public sealed class PendingAuthDbUnloadUtility
{
    private readonly PautSummaryRepository _summary;
    private readonly PautDetailRepository _detail;
    private readonly IClock _clock;
    private readonly HostKind _host;
    private readonly List<string> _sysout = [];

    // ---- WORKING-STORAGE: WS-VARIABLES (PAUDBUNL.CBL:53-90) -------------------------------------------
    // private const string WsPgmName = "IMSUNLOD";       // WS-PGMNAME (X08)
    private int _currentDate;        // CURRENT-DATE   9(06) — ACCEPT FROM DATE
    private int _currentJulianDate;  // WS-CURRENT-YYDDD  9(05) — ACCEPT FROM DAY
    // WS-AUTH-DATE 9(05), WS-EXPIRY-DAYS/WS-DAY-DIFF/IDX S9(4) COMP, WS-CURR-APP-ID 9(11) — unused (bug #3).

    private int _checkpointCount;        // WS-NO-CHKP            9(8)  VALUE 0 (unused, bug #3)
    private int _authSummaryProcessedCount; // WS-AUTH-SMRY-PROC-CNT 9(8)  VALUE 0
    private int _summaryReadCount;       // WS-NO-SUMRY-READ      S9(8) COMP VALUE 0
    private int _summaryDeletedCount;    // WS-NO-SUMRY-DELETED   S9(8) COMP VALUE 0 (unused)
    private int _detailReadCount;        // WS-NO-DTL-READ        S9(8) COMP VALUE 0 (unused — bug #1)
    private int _detailDeletedCount;     // WS-NO-DTL-DELETED     S9(8) COMP VALUE 0 (unused)

    // Flags. END-OF-AUTHDB / MORE-AUTHS 88s are SET but the loops test WS-END-OF-ROOT-SEG /
    // WS-END-OF-CHILD-SEG instead (bug #2).
    private bool _endOfAuthDb;       // WS-END-OF-AUTHDB-FLAG: END-OF-AUTHDB = 'Y'? (inert)
    private bool _moreAuths;         // WS-MORE-AUTHS-FLAG:    MORE-AUTHS = 'Y'?    (inert)
    private string _endOfRootSeg = " ";     // WS-END-OF-ROOT-SEG  X(01) VALUE SPACES — root loop sentinel
    private string _endOfChildSeg = " ";    // WS-END-OF-CHILD-SEG X(01) VALUE SPACES — child loop sentinel

    private string _outfile1Status = "  ";  // WS-OUTFL1-STATUS X(02) VALUE SPACES
    private string _outfile2Status = "  ";  // WS-OUTFL2-STATUS X(02) VALUE SPACES

    // ---- PCB status (PAUT-PCB-STATUS in the PAUTBPCB mask) — the byte the program branches on ---------
    private string _pautPcbStatus = "  ";   // '  ' ok, 'GB' end-of-db, 'GE' no-more-children
    private string _pautKeyfb = "";         // PAUT-KEYFB — key feedback area (echoed on failure)

    // ---- The io-areas (PCB INTO targets) -------------------------------------------------------------
    private PautSummary? _summaryRec;       // PENDING-AUTH-SUMMARY (root io-area)
    private PautDetail? _detailRec;         // PENDING-AUTH-DETAILS (child io-area)

    // ---- Output datasets -----------------------------------------------------------------------------
    private FileStream? _opfile1;           // OPFILE1 / OUTFIL1
    private FileStream? _opfile2;           // OPFILE2 / OUTFIL2

    /// <summary>The batch return code: 0 on a clean run, 16 after <c>9999-ABEND</c> (MOVE 16 TO RETURN-CODE).</summary>
    public int ReturnCode { get; private set; }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    private PendingAuthDbUnloadUtility(PautSummaryRepository summary, PautDetailRepository detail, IClock clock, HostKind host)
    {
        _summary = summary;
        _detail = detail;
        _clock = clock;
        _host = host;
    }

    /// <summary>
    /// Runs PAUDBUNL, unloading the relational PAUT_SUMMARY / PAUT_DETAIL tables to the two sequential
    /// output files. <paramref name="outfil1Path"/> receives the 100-byte summary images;
    /// <paramref name="outfil2Path"/> receives the 206-byte (key + detail) records.
    /// </summary>
    /// <param name="summary">PAUT_SUMMARY root repository (forward GN scan).</param>
    /// <param name="detail">PAUT_DETAIL child repository (per-parent GNP scan).</param>
    /// <param name="outfil1Path">OUTFIL1 dataset path (summary images, LRECL 100).</param>
    /// <param name="outfil2Path">OUTFIL2 dataset path (root-key + detail images, LRECL 206).</param>
    /// <param name="clock">Clock for <c>ACCEPT … FROM DATE/DAY</c> (defaults to system).</param>
    /// <param name="host">Host encoding for the output datasets (defaults to EBCDIC).</param>
    public static PendingAuthDbUnloadUtilityResult Run(
        PautSummaryRepository summary,
        PautDetailRepository detail,
        string outfil1Path,
        string outfil2Path,
        IClock? clock = null,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new PendingAuthDbUnloadUtility(summary, detail, clock ?? SystemClock.Instance, host);
        program.MainPara(outfil1Path, outfil2Path);
        return new PendingAuthDbUnloadUtilityResult(program.Sysout, program.ReturnCode);
    }

    // =================================================================================================
    // MAIN-PARA // source: PAUDBUNL.CBL:157-170
    // =================================================================================================
    private void MainPara(string outfil1Path, string outfil2Path)  // COBOL paragraph: MAIN-PARA
    {
        try
        {
            // ENTRY 'DLITCBL' USING PAUTBPCB. // source: PAUDBUNL.CBL:158

            Initialize(outfil1Path, outfil2Path);                 // source: PAUDBUNL.CBL:161

            // PERFORM 2000-FIND-NEXT-AUTH-SUMMARY UNTIL WS-END-OF-ROOT-SEG = 'Y'. // source: PAUDBUNL.CBL:163-164
            // Establish the forward root GN cursor before the first GN.
            _summary.StartBrowse();
            while (_endOfRootSeg != "Y")
            {
                FindNextAuthSummary();
            }

            FileClose();                                          // source: PAUDBUNL.CBL:166

            // GOBACK. // source: PAUDBUNL.CBL:170
        }
        catch (AbendException)
        {
            // 9999-ABEND has already DISPLAYed 'IMSUNLOD ABENDING ...' and set RETURN-CODE = 16; the
            // GOBACK in 9999-ABEND returns to the IMS region controller. The AbendException unwinds the
            // whole program exactly like that GOBACK (no 4000-FILE-CLOSE is reached).
        }
        finally
        {
            // On the mainframe the runtime closes any still-open datasets at GOBACK.
            _opfile1?.Dispose();
            _opfile2?.Dispose();
        }
    }

    // =================================================================================================
    // 1000-INITIALIZE // source: PAUDBUNL.CBL:173-204
    // =================================================================================================
    private void Initialize(string outfil1Path, string outfil2Path)  // COBOL paragraph: 1000-INITIALIZE
    {
        // ACCEPT CURRENT-DATE FROM DATE; ACCEPT CURRENT-YYDDD FROM DAY. // source: PAUDBUNL.CBL:176-177
        DateTime now = _clock.Now;
        _currentDate = AcceptFromDate(now);
        _currentJulianDate = AcceptFromDay(now);

        // ACCEPT PRM-INFO FROM SYSIN is commented out in the source. // source: PAUDBUNL.CBL:179

        _sysout.Add("STARTING PROGRAM PAUDBUNL::");                   // source: PAUDBUNL.CBL:180
        _sysout.Add("*-------------------------------------*");       // source: PAUDBUNL.CBL:181
        _sysout.Add("TODAYS DATE            :" + Display9_6(_currentDate)); // source: PAUDBUNL.CBL:182
        _sysout.Add(" ");                                             // source: PAUDBUNL.CBL:183

        // OPEN OUTPUT OPFILE1. // source: PAUDBUNL.CBL:186
        _opfile1 = OpenOutput(outfil1Path);
        _outfile1Status = "00";
        if (_outfile1Status == "  " || _outfile1Status == "00")       // source: PAUDBUNL.CBL:187-188
        {
            // CONTINUE
        }
        else                                                          // source: PAUDBUNL.CBL:189-192
        {
            _sysout.Add("ERROR IN OPENING OPFILE1:" + _outfile1Status);
            Abend();
        }

        // OPEN OUTPUT OPFILE2. // source: PAUDBUNL.CBL:194
        _opfile2 = OpenOutput(outfil2Path);
        _outfile2Status = "00";
        if (_outfile2Status == "  " || _outfile2Status == "00")       // source: PAUDBUNL.CBL:195-196
        {
            // CONTINUE
        }
        else                                                          // source: PAUDBUNL.CBL:197-200
        {
            _sysout.Add("ERROR IN OPENING OPFILE2:" + _outfile2Status);
            Abend();
        }
    }

    // =================================================================================================
    // 2000-FIND-NEXT-AUTH-SUMMARY // source: PAUDBUNL.CBL:207-249
    // =================================================================================================
    private void FindNextAuthSummary()  // COBOL paragraph: 2000-FIND-NEXT-AUTH-SUMMARY
    {
        // INITIALIZE PAUT-PCB-STATUS. // source: PAUDBUNL.CBL:212
        _pautPcbStatus = "  ";

        // CALL 'CBLTDLI' USING FUNC-GN PAUTBPCB PENDING-AUTH-SUMMARY ROOT-UNQUAL-SSA. // source: PAUDBUNL.CBL:213-216
        string status = _summary.ReadNext(out PautSummary? next);
        _pautPcbStatus = (status == FileStatus.Ok) ? "  " : "GB";     // '00'->'  ', '10'->'GB'

        // IF PAUT-PCB-STATUS = SPACES ... // source: PAUDBUNL.CBL:223
        if (_pautPcbStatus == "  ")
        {
            _summaryRec = next;
            _summaryReadCount++;                                         // ADD 1 TO WS-NO-SUMRY-READ // source: PAUDBUNL.CBL:225
            _authSummaryProcessedCount++;                                     // ADD 1 TO WS-AUTH-SMRY-PROC-CNT // source: PAUDBUNL.CBL:226

            // MOVE PENDING-AUTH-SUMMARY TO OPFIL1-REC. // source: PAUDBUNL.CBL:227
            byte[] opfil1Rec = PautSegmentImages.EncodeSummary(_summaryRec!, _host);

            // INITIALIZE ROOT-SEG-KEY; INITIALIZE CHILD-SEG-REC; MOVE PA-ACCT-ID TO ROOT-SEG-KEY.
            // ROOT-SEG-KEY belongs to OPFIL2-REC and is set once here (per root). // source: PAUDBUNL.CBL:228-230
            byte[] rootSegKey = PautSegmentImages.EncodeRootSegKey(_summaryRec!.AcctId);

            // IF PA-ACCT-ID IS NUMERIC ... // source: PAUDBUNL.CBL:232
            if (PautSegmentImages.IsNumericComp3(rootSegKey))
            {
                // WRITE OPFIL1-REC. // source: PAUDBUNL.CBL:233
                WriteOpfil1(opfil1Rec);

                // INITIALIZE WS-END-OF-CHILD-SEG. // source: PAUDBUNL.CBL:234
                _endOfChildSeg = " ";

                // Establish parentage for the following GNPs (this root's children, twin-chain order).
                _detail.StartParentScan(_summaryRec.AcctId);

                // PERFORM 3000-FIND-NEXT-AUTH-DTL UNTIL WS-END-OF-CHILD-SEG = 'Y'. // source: PAUDBUNL.CBL:235-236
                while (_endOfChildSeg != "Y")
                {
                    FindNextAuthDetail(rootSegKey);
                }
            }
        }

        // IF PAUT-PCB-STATUS = 'GB' ... // source: PAUDBUNL.CBL:239
        if (_pautPcbStatus == "GB")
        {
            _endOfAuthDb = true;                                      // SET END-OF-AUTHDB TO TRUE // source: PAUDBUNL.CBL:240
            _endOfRootSeg = "Y";                                    // MOVE 'Y' TO WS-END-OF-ROOT-SEG // source: PAUDBUNL.CBL:241
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'GB' ... // source: PAUDBUNL.CBL:243
        if (_pautPcbStatus != "  " && _pautPcbStatus != "GB")
        {
            _sysout.Add("AUTH SUM  GN FAILED  :" + _pautPcbStatus);   // source: PAUDBUNL.CBL:244
            _sysout.Add("KEY FEEDBACK AREA    :" + _pautKeyfb);       // source: PAUDBUNL.CBL:245
            Abend();                                              // source: PAUDBUNL.CBL:246
        }
    }

    // =================================================================================================
    // 3000-FIND-NEXT-AUTH-DTL // source: PAUDBUNL.CBL:253-286
    // =================================================================================================
    private void FindNextAuthDetail(byte[] rootSegKey)  // COBOL paragraph: 3000-FIND-NEXT-AUTH-DTL
    {
        // CALL 'CBLTDLI' USING FUNC-GNP PAUTBPCB PENDING-AUTH-DETAILS CHILD-UNQUAL-SSA. // source: PAUDBUNL.CBL:257-260
        string status = _detail.ReadNextInParent(out PautDetail? next);
        _pautPcbStatus = (status == FileStatus.Ok) ? "  " : "GE";     // '00'->'  ', '10'->'GE'

        // IF PAUT-PCB-STATUS = SPACES ... // source: PAUDBUNL.CBL:266
        if (_pautPcbStatus == "  ")
        {
            _moreAuths = true;                                       // SET MORE-AUTHS TO TRUE (inert, bug #2) // source: PAUDBUNL.CBL:267
            _detailRec = next;

            // FAITHFUL BUG #1: the summary counters are incremented per DETAIL read. // source: PAUDBUNL.CBL:268-269
            _summaryReadCount++;                                        // ADD 1 TO WS-NO-SUMRY-READ
            _authSummaryProcessedCount++;                                    // ADD 1 TO WS-AUTH-SMRY-PROC-CNT

            // MOVE PENDING-AUTH-DETAILS TO CHILD-SEG-REC; WRITE OPFIL2-REC. // source: PAUDBUNL.CBL:270-271
            // OPFIL2-REC = ROOT-SEG-KEY (6) + CHILD-SEG-REC (200).
            byte[] childSegRec = PautSegmentImages.EncodeDetail(_detailRec!, _host);
            WriteOpfil2(rootSegKey, childSegRec);
        }

        // IF PAUT-PCB-STATUS = 'GE' ... // source: PAUDBUNL.CBL:273
        if (_pautPcbStatus == "GE")
        {
            _endOfChildSeg = "Y";                                  // MOVE 'Y' TO WS-END-OF-CHILD-SEG // source: PAUDBUNL.CBL:275
            _sysout.Add("CHILD SEG FLAG GE : " + _endOfChildSeg);  // source: PAUDBUNL.CBL:276-277
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'GE' ... // source: PAUDBUNL.CBL:279
        if (_pautPcbStatus != "  " && _pautPcbStatus != "GE")
        {
            _sysout.Add("GNP CALL FAILED  :" + _pautPcbStatus);      // source: PAUDBUNL.CBL:280
            _sysout.Add("KFB AREA IN CHILD:" + _pautKeyfb);          // source: PAUDBUNL.CBL:281
            Abend();                                             // source: PAUDBUNL.CBL:282
        }

        // INITIALIZE PAUT-PCB-STATUS. // source: PAUDBUNL.CBL:284
        _pautPcbStatus = "  ";
    }

    // =================================================================================================
    // 4000-FILE-CLOSE // source: PAUDBUNL.CBL:289-306
    // =================================================================================================
    private void FileClose()  // COBOL paragraph: 4000-FILE-CLOSE
    {
        _sysout.Add("CLOSING THE FILE");                             // source: PAUDBUNL.CBL:290

        // CLOSE OPFILE1. // source: PAUDBUNL.CBL:291
        _opfile1?.Dispose();
        _opfile1 = null;
        _outfile1Status = "00";
        if (_outfile1Status == "  " || _outfile1Status == "00")      // source: PAUDBUNL.CBL:293-294
        {
            // CONTINUE
        }
        else                                                         // source: PAUDBUNL.CBL:295-297
        {
            _sysout.Add("ERROR IN CLOSING 1ST FILE:" + _outfile1Status);
        }

        // CLOSE OPFILE2. // source: PAUDBUNL.CBL:298
        _opfile2?.Dispose();
        _opfile2 = null;
        _outfile2Status = "00";
        if (_outfile2Status == "  " || _outfile2Status == "00")      // source: PAUDBUNL.CBL:300-301
        {
            // CONTINUE
        }
        else                                                         // source: PAUDBUNL.CBL:302-304
        {
            _sysout.Add("ERROR IN CLOSING 2ND FILE:" + _outfile2Status);
        }
    }

    // =================================================================================================
    // 9999-ABEND // source: PAUDBUNL.CBL:308-314
    // =================================================================================================
    private void Abend()  // COBOL paragraph: 9999-ABEND
    {
        _sysout.Add("IMSUNLOD ABENDING ...");                        // source: PAUDBUNL.CBL:311
        ReturnCode = 16;                                             // MOVE 16 TO RETURN-CODE // source: PAUDBUNL.CBL:313
        // GOBACK — returns to the IMS region controller. // source: PAUDBUNL.CBL:314
        throw new AbendException("16", "PAUDBUNL abend (RETURN-CODE 16).");
    }

    // =================================================================================================
    // File-write helpers (sequential WRITE of OPFIL1-REC / OPFIL2-REC)
    // =================================================================================================

    // WRITE OPFIL1-REC (PIC X(100)).
    private void WriteOpfil1(byte[] opfil1Rec)
    {
        _opfile1!.Write(opfil1Rec, 0, opfil1Rec.Length);
    }

    // WRITE OPFIL2-REC (ROOT-SEG-KEY S9(11) COMP-3 (6) + CHILD-SEG-REC X(200) = 206 bytes).
    private void WriteOpfil2(byte[] rootSegKey, byte[] childSegRec)
    {
        _opfile2!.Write(rootSegKey, 0, rootSegKey.Length);
        _opfile2!.Write(childSegRec, 0, childSegRec.Length);
    }

    // =================================================================================================
    // COBOL ACCEPT / DISPLAY helpers
    // =================================================================================================

    /// <summary>ACCEPT … FROM DATE — the system date as YYMMDD packed into 9(06).</summary>
    private static int AcceptFromDate(DateTime now)
        => (now.Year % 100) * 10000 + now.Month * 100 + now.Day;

    /// <summary>ACCEPT … FROM DAY — the system Julian date as YYDDD packed into 9(05).</summary>
    private static int AcceptFromDay(DateTime now)
        => (now.Year % 100) * 1000 + now.DayOfYear;

    /// <summary>Renders a <c>9(06)</c> unsigned display field as 6 zero-padded digits.</summary>
    private static string Display9_6(int value)
        => (Math.Abs(value) % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    // OPEN OUTPUT (DISP=NEW): create fresh, truncating any existing file.
    private static FileStream OpenOutput(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }
}

/// <summary>
/// The result of a <see cref="PendingAuthDbUnloadUtility"/> run: the SYSOUT (DISPLAY) lines in order and the batch
/// <see cref="ReturnCode"/> (0 on a clean run, 16 after <c>9999-ABEND</c>).
/// </summary>
/// <param name="Sysout">The DISPLAY lines emitted to SYSOUT, in order.</param>
/// <param name="ReturnCode">The COBOL RETURN-CODE: 0 on success, 16 on abend.</param>
public sealed record PendingAuthDbUnloadUtilityResult(IReadOnlyList<string> Sysout, int ReturnCode);
