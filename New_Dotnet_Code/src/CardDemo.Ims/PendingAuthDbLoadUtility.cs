using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Ims;

/// <summary>
/// Faithful relational re-port of the IMS batch utility <c>PAUDBLOD</c> ("IMS LOAD" — load the
/// pending-authorization IMS DB from two sequential files). It reads <c>INFILE1</c> (summary records) and
/// <c>ISRT</c>s each as a <c>PAUTSUM0</c> root, then reads <c>INFILE2</c> (root-key + detail records) and,
/// for each, performs a <c>FUNC-GU</c> existence check on the parent root before <c>ISRT</c>-ing the
/// <c>PAUTDTL1</c> child. The input record images are byte-identical to <see cref="PendingAuthDbUnloadUtility"/>'s output, so
/// an unload then load round-trips.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from
/// <c>app/app-authorization-ims-db2-mq/cbl/PAUDBLOD.CBL</c> (read in full). Each PROCEDURE-DIVISION
/// paragraph is a method whose name mirrors the COBOL paragraph and whose body keeps the original
/// statement order and control flow (with <c>// source: PAUDBLOD.CBL:NNN</c> citations).</para>
/// <para>DL/I → repository mapping (matching <c>CBPAUP0C.cs</c>): <b>ISRT</b>(PAUTSUM0) →
/// <see cref="PautSummaryRepository.Insert"/> ('00'→SPACES, '22'→'II' duplicate); <b>GU</b>(PAUTSUM0) →
/// <see cref="PautSummaryRepository.ReadByKey"/> ('00'→SPACES, '23'→'GE' not-found); <b>ISRT</b>(PAUTDTL1)
/// → <see cref="PautDetailRepository.Insert"/> ('00'→SPACES, '22'→'II'). The PCB status byte the program
/// branches on is <c>PAUT-PCB-STATUS</c>.</para>
/// <para><b>Input record formats</b>: <c>INFILE1</c> records are the 100-byte CIPAUSMY summary image
/// (INFIL1-REC PIC X(100)). <c>INFILE2</c> records are 206 bytes: a 6-byte ROOT-SEG-KEY (PA-ACCT-ID
/// S9(11) COMP-3) followed by the 200-byte CIPAUDTY detail image (CHILD-SEG-REC X(200)).</para>
/// <para>FAITHFUL BUGS / quirks preserved verbatim:
/// <list type="number">
/// <item>#1 — in <c>3100-INSERT-CHILD-SEG</c> the GU existence check on the parent root has <b>no else
/// branch</b>: if the GU returns anything other than SPACES the child is <b>silently skipped</b> (no
/// abend, no DISPLAY). The "ROOT GU CALL FAIL" abend is reachable only <i>after</i> a successful GU + the
/// child ISRT (it actually re-tests the post-ISRT status). Both reproduced exactly.</item>
/// <item>#2 — <c>2000-READ-ROOT-SEG-FILE</c>/<c>3000-READ-CHILD-SEG-FILE</c> treat any non-'00'/'10' read
/// status as a DISPLAY-only "ERROR READING ..." that does <b>not</b> set the end flag, so the read loop
/// would spin on a hard read error. Reproduced (a hard error is not modelled in normal operation).</item>
/// <item>#3 — the root and child loads are two <b>separate full passes</b> (all roots first, then all
/// children); a child whose root was never loaded is simply skipped by the GU check (#1).</item>
/// </list></para>
/// </remarks>
public sealed class PendingAuthDbLoadUtility
{
    private readonly PautSummaryRepository _summary;
    private readonly PautDetailRepository _detail;
    private readonly IClock _clock;
    private readonly HostKind _host;
    private readonly List<string> _sysout = [];

    // ---- WORKING-STORAGE: WS-VARIABLES (PAUDBLOD.CBL:53-92) -------------------------------------------
    private int _currentDate;        // CURRENT-DATE   9(06) — ACCEPT FROM DATE
    private int _currentYyddd;       // CURRENT-YYDDD  9(05) — ACCEPT FROM DAY

    private int _wsNoSumryRead;      // WS-NO-SUMRY-READ    (unused here)
    private int _wsNoDtlRead;        // WS-NO-DTL-READ      (unused here)

    private string _wsInfil1Status = "  ";  // WS-INFIL1-STATUS X(02) VALUE SPACES
    private string _wsInfil2Status = "  ";  // WS-INFIL2-STATUS X(02) VALUE SPACES
    private string _endRootSegFile = " ";   // END-ROOT-SEG-FILE  X(01) — root file sentinel
    private string _endChildSegFile = " ";  // END-CHILD-SEG-FILE X(01) — child file sentinel

    // ---- PCB status the program branches on ----------------------------------------------------------
    private string _pautPcbStatus = "  ";   // PAUT-PCB-STATUS: '  ' ok / 'II' dup / 'GE' not-found
    private string _pautKeyfb = "";         // PAUT-KEYFB

    // ROOT-QUAL-SSA key value (MOVE ROOT-SEG-KEY TO QUAL-SSA-KEY-VALUE) — the GU parent key.
    private long _qualSsaKeyValue;

    // ---- The io-areas --------------------------------------------------------------------------------
    private PautSummary? _summaryRec;       // PENDING-AUTH-SUMMARY (root io-area)
    private PautDetail? _detailRec;         // PENDING-AUTH-DETAILS (child io-area)

    // ---- Input record cursors (sequential READ of INFILE1 / INFILE2) ---------------------------------
    private byte[][] _infile1 = [];
    private int _infile1Pos;
    private byte[][] _infile2 = [];
    private int _infile2Pos;

    private string _infile1Path = "";
    private string _infile2Path = "";

    /// <summary>The batch return code: 0 on a clean run, 16 after <c>9999-ABEND</c>.</summary>
    public int ReturnCode { get; private set; }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    private PendingAuthDbLoadUtility(PautSummaryRepository summary, PautDetailRepository detail, IClock clock, HostKind host)
    {
        _summary = summary;
        _detail = detail;
        _clock = clock;
        _host = host;
    }

    /// <summary>
    /// Runs PAUDBLOD, loading the PAUT_SUMMARY / PAUT_DETAIL tables from the two sequential input files.
    /// <paramref name="infile1Path"/> holds the 100-byte summary images; <paramref name="infile2Path"/>
    /// holds the 206-byte (root-key + detail) records — exactly what <see cref="PendingAuthDbUnloadUtility"/> writes.
    /// </summary>
    /// <param name="summary">PAUT_SUMMARY root repository (ISRT roots, GU existence check).</param>
    /// <param name="detail">PAUT_DETAIL child repository (ISRT children).</param>
    /// <param name="infile1Path">INFILE1 dataset path (summary images, LRECL 100).</param>
    /// <param name="infile2Path">INFILE2 dataset path (root-key + detail images, LRECL 206).</param>
    /// <param name="clock">Clock for <c>ACCEPT … FROM DATE/DAY</c> (defaults to system).</param>
    /// <param name="host">Host encoding of the input datasets (defaults to EBCDIC).</param>
    public static PendingAuthDbLoadUtilityResult Run(
        PautSummaryRepository summary,
        PautDetailRepository detail,
        string infile1Path,
        string infile2Path,
        IClock? clock = null,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new PendingAuthDbLoadUtility(summary, detail, clock ?? SystemClock.Instance, host);
        program.MainPara(infile1Path, infile2Path);
        return new PendingAuthDbLoadUtilityResult(program.Sysout, program.ReturnCode);
    }

    // =================================================================================================
    // MAIN-PARA // source: PAUDBLOD.CBL:169-187
    // =================================================================================================
    private void MainPara(string infile1Path, string infile2Path)
    {
        _infile1Path = infile1Path;
        _infile2Path = infile2Path;
        try
        {
            // ENTRY 'DLITCBL' USING PAUTBPCB. // source: PAUDBLOD.CBL:171
            _sysout.Add("STARTING PAUDBLOD");                         // source: PAUDBLOD.CBL:173

            Initialize1000();                                         // source: PAUDBLOD.CBL:175

            // PERFORM 2000-READ-ROOT-SEG-FILE UNTIL END-ROOT-SEG-FILE = 'Y'. // source: PAUDBLOD.CBL:177-178
            while (_endRootSegFile != "Y")
            {
                ReadRootSegFile2000();
            }

            // PERFORM 3000-READ-CHILD-SEG-FILE UNTIL END-CHILD-SEG-FILE = 'Y'. // source: PAUDBLOD.CBL:180-181
            while (_endChildSegFile != "Y")
            {
                ReadChildSegFile3000();
            }

            FileClose4000();                                          // source: PAUDBLOD.CBL:183

            // GOBACK. // source: PAUDBLOD.CBL:187
        }
        catch (AbendException)
        {
            // 9999-ABEND DISPLAYed 'IMS LOAD ABENDING ...' and set RETURN-CODE = 16; the GOBACK returns to
            // the IMS region controller. The AbendException unwinds exactly like that GOBACK.
        }
    }

    // =================================================================================================
    // 1000-INITIALIZE // source: PAUDBLOD.CBL:190-219
    // =================================================================================================
    private void Initialize1000()
    {
        // ACCEPT CURRENT-DATE FROM DATE; ACCEPT CURRENT-YYDDD FROM DAY. // source: PAUDBLOD.CBL:193-194
        DateTime now = _clock.Now;
        _currentDate = AcceptFromDate(now);
        _currentYyddd = AcceptFromDay(now);

        _sysout.Add("*-------------------------------------*");       // source: PAUDBLOD.CBL:196
        _sysout.Add("TODAYS DATE            :" + Display9_6(_currentDate)); // source: PAUDBLOD.CBL:197
        _sysout.Add(" ");                                             // source: PAUDBLOD.CBL:198

        // OPEN INPUT INFILE1. // source: PAUDBLOD.CBL:201
        _infile1 = ReadAllRecords(_infile1Path, PautSegmentImages.SummaryImageLength, out string s1);
        _infile1Pos = 0;
        _wsInfil1Status = s1;
        if (_wsInfil1Status == "  " || _wsInfil1Status == "00")       // source: PAUDBLOD.CBL:202-203
        {
            // CONTINUE
        }
        else                                                          // source: PAUDBLOD.CBL:204-207
        {
            _sysout.Add("ERROR IN OPENING INFILE1:" + _wsInfil1Status);
            Abend9999();
        }

        // OPEN INPUT INFILE2. // source: PAUDBLOD.CBL:209
        _infile2 = ReadAllRecords(_infile2Path,
            PautSegmentImages.RootSegKeyLength + PautSegmentImages.DetailImageLength, out string s2);
        _infile2Pos = 0;
        _wsInfil2Status = s2;
        if (_wsInfil2Status == "  " || _wsInfil2Status == "00")       // source: PAUDBLOD.CBL:210-211
        {
            // CONTINUE
        }
        else                                                          // source: PAUDBLOD.CBL:212-215
        {
            _sysout.Add("ERROR IN OPENING INFILE2:" + _wsInfil2Status);
            Abend9999();
        }
    }

    // =================================================================================================
    // 2000-READ-ROOT-SEG-FILE // source: PAUDBLOD.CBL:222-240
    // =================================================================================================
    private void ReadRootSegFile2000()
    {
        // READ INFILE1. // source: PAUDBLOD.CBL:226
        _wsInfil1Status = ReadNext(_infile1, ref _infile1Pos, out byte[]? rec);

        // IF WS-INFIL1-STATUS = SPACES OR '00' ... // source: PAUDBLOD.CBL:228
        if (_wsInfil1Status == "  " || _wsInfil1Status == "00")
        {
            // MOVE INFIL1-REC TO PENDING-AUTH-SUMMARY. // source: PAUDBLOD.CBL:229
            _summaryRec = PautSegmentImages.DecodeSummary(rec!, _host);
            // PERFORM 2100-INSERT-ROOT-SEG. // source: PAUDBLOD.CBL:230
            InsertRootSeg2100();
        }
        else                                                          // source: PAUDBLOD.CBL:231
        {
            // IF WS-INFIL1-STATUS = '10' MOVE 'Y' TO END-ROOT-SEG-FILE. // source: PAUDBLOD.CBL:232-233
            if (_wsInfil1Status == "10")
            {
                _endRootSegFile = "Y";
            }
            else                                                      // source: PAUDBLOD.CBL:234-235
            {
                _sysout.Add("ERROR READING ROOT SEG INFILE");
            }
        }
    }

    // =================================================================================================
    // 2100-INSERT-ROOT-SEG // source: PAUDBLOD.CBL:242-265
    // =================================================================================================
    private void InsertRootSeg2100()
    {
        // CALL 'CBLTDLI' USING FUNC-ISRT PAUTBPCB PENDING-AUTH-SUMMARY ROOT-UNQUAL-SSA. // source: PAUDBLOD.CBL:244-247
        string status = _summary.Insert(_summaryRec!);
        _pautPcbStatus = MapInsertStatus(status);                     // '00'->'  ', '22'->'II'

        _sysout.Add(" *******************************");              // source: PAUDBLOD.CBL:248
        _sysout.Add(" *******************************");              // source: PAUDBLOD.CBL:252

        // IF PAUT-PCB-STATUS = SPACES DISPLAY 'ROOT INSERT SUCCESS'. // source: PAUDBLOD.CBL:253-255
        if (_pautPcbStatus == "  ")
        {
            _sysout.Add("ROOT INSERT SUCCESS    ");
        }

        // IF PAUT-PCB-STATUS = 'II' DISPLAY 'ROOT SEGMENT ALREADY IN DB'. // source: PAUDBLOD.CBL:256-258
        if (_pautPcbStatus == "II")
        {
            _sysout.Add("ROOT SEGMENT ALREADY IN DB");
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'II' ... // source: PAUDBLOD.CBL:259-262
        if (_pautPcbStatus != "  " && _pautPcbStatus != "II")
        {
            _sysout.Add("ROOT INSERT FAILED  :" + _pautPcbStatus);
            Abend9999();
        }
    }

    // =================================================================================================
    // 3000-READ-CHILD-SEG-FILE // source: PAUDBLOD.CBL:269-291
    // =================================================================================================
    private void ReadChildSegFile3000()
    {
        // READ INFILE2. // source: PAUDBLOD.CBL:272
        _wsInfil2Status = ReadNext(_infile2, ref _infile2Pos, out byte[]? rec);

        // IF WS-INFIL2-STATUS = SPACES OR '00' ... // source: PAUDBLOD.CBL:274
        if (_wsInfil2Status == "  " || _wsInfil2Status == "00")
        {
            // INFIL2-REC = ROOT-SEG-KEY (6) + CHILD-SEG-REC (200).
            ReadOnlySpan<byte> rootSegKey = rec.AsSpan(0, PautSegmentImages.RootSegKeyLength);
            ReadOnlySpan<byte> childSegRec = rec.AsSpan(
                PautSegmentImages.RootSegKeyLength, PautSegmentImages.DetailImageLength);

            // IF ROOT-SEG-KEY IS NUMERIC ... // source: PAUDBLOD.CBL:275
            if (PautSegmentImages.IsNumericComp3(rootSegKey))
            {
                // MOVE ROOT-SEG-KEY TO QUAL-SSA-KEY-VALUE. // source: PAUDBLOD.CBL:277
                _qualSsaKeyValue = PautSegmentImages.DecodeRootSegKey(rootSegKey);

                // MOVE CHILD-SEG-REC TO PENDING-AUTH-DETAILS. // source: PAUDBLOD.CBL:280
                // The child's parentage (ACCT_ID) is the GU'd root key (QUAL-SSA-KEY-VALUE).
                _detailRec = PautSegmentImages.DecodeDetail(childSegRec, _qualSsaKeyValue, _host);

                // PERFORM 3100-INSERT-CHILD-SEG. // source: PAUDBLOD.CBL:281
                InsertChildSeg3100();
            }
        }
        else                                                          // source: PAUDBLOD.CBL:283
        {
            // IF WS-INFIL2-STATUS = '10' MOVE 'Y' TO END-CHILD-SEG-FILE. // source: PAUDBLOD.CBL:284-285
            if (_wsInfil2Status == "10")
            {
                _endChildSegFile = "Y";
            }
            else                                                      // source: PAUDBLOD.CBL:286-287
            {
                _sysout.Add("ERROR READING CHILD SEG INFILE");
            }
        }
    }

    // =================================================================================================
    // 3100-INSERT-CHILD-SEG // source: PAUDBLOD.CBL:292-315
    // =================================================================================================
    private void InsertChildSeg3100()
    {
        // INITIALIZE PAUT-PCB-STATUS. // source: PAUDBLOD.CBL:295
        _pautPcbStatus = "  ";

        // CALL 'CBLTDLI' USING FUNC-GU PAUTBPCB PENDING-AUTH-SUMMARY ROOT-QUAL-SSA. // source: PAUDBLOD.CBL:296-299
        // GU the parent root by QUAL-SSA-KEY-VALUE (the existence check).
        string guStatus = _summary.ReadByKey(_qualSsaKeyValue, out PautSummary? root);
        _pautPcbStatus = (guStatus == FileStatus.Ok) ? "  " : "GE";   // '00'->'  ', '23'->'GE'
        if (_pautPcbStatus == "  ") _summaryRec = root;               // GU ... INTO PENDING-AUTH-SUMMARY

        _sysout.Add("***************************");                   // source: PAUDBLOD.CBL:300
        _sysout.Add("***************************");                   // source: PAUDBLOD.CBL:304

        // IF PAUT-PCB-STATUS = SPACES ... (no ELSE — a failed GU silently skips the child, bug #1)
        // source: PAUDBLOD.CBL:305
        if (_pautPcbStatus == "  ")
        {
            _sysout.Add("GU CALL TO ROOT SEG SUCCESS");               // source: PAUDBLOD.CBL:306

            // PERFORM 3200-INSERT-IMS-CALL. // source: PAUDBLOD.CBL:309
            InsertImsCall3200();

            // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'II' ... (re-tests the post-ISRT status, bug #1)
            // source: PAUDBLOD.CBL:310-313
            if (_pautPcbStatus != "  " && _pautPcbStatus != "II")
            {
                _sysout.Add("ROOT GU CALL FAIL:" + _pautPcbStatus);
                _sysout.Add("KFB AREA IN CHILD:" + _pautKeyfb);
                Abend9999();
            }
        }
    }

    // =================================================================================================
    // 3200-INSERT-IMS-CALL // source: PAUDBLOD.CBL:318-339
    // =================================================================================================
    private void InsertImsCall3200()
    {
        // CALL 'CBLTDLI' USING FUNC-ISRT PAUTBPCB PENDING-AUTH-DETAILS CHILD-UNQUAL-SSA. // source: PAUDBLOD.CBL:321-324
        string status = _detail.Insert(_detailRec!);
        _pautPcbStatus = MapInsertStatus(status);                     // '00'->'  ', '22'->'II'

        // IF PAUT-PCB-STATUS = SPACES DISPLAY 'CHILD SEGMENT INSERTED SUCCESS'. // source: PAUDBLOD.CBL:326-328
        if (_pautPcbStatus == "  ")
        {
            _sysout.Add("CHILD SEGMENT INSERTED SUCCESS");
        }

        // IF PAUT-PCB-STATUS = 'II' DISPLAY 'CHILD SEGMENT ALREADY IN DB'. // source: PAUDBLOD.CBL:329-331
        if (_pautPcbStatus == "II")
        {
            _sysout.Add("CHILD SEGMENT ALREADY IN DB");
        }

        // IF PAUT-PCB-STATUS NOT EQUAL TO SPACES AND 'II' ... // source: PAUDBLOD.CBL:332-336
        if (_pautPcbStatus != "  " && _pautPcbStatus != "II")
        {
            _sysout.Add("INSERT CALL FAIL FOR CHILD:" + _pautPcbStatus);
            _sysout.Add("KFB AREA IN CHILD:" + _pautKeyfb);
            Abend9999();
        }
    }

    // =================================================================================================
    // 4000-FILE-CLOSE // source: PAUDBLOD.CBL:341-358
    // =================================================================================================
    private void FileClose4000()
    {
        _sysout.Add("CLOSING THE FILE");                             // source: PAUDBLOD.CBL:342

        // CLOSE INFILE1. // source: PAUDBLOD.CBL:343
        _wsInfil1Status = "00";
        if (_wsInfil1Status == "  " || _wsInfil1Status == "00")      // source: PAUDBLOD.CBL:345-346
        {
            // CONTINUE
        }
        else                                                         // source: PAUDBLOD.CBL:347-349
        {
            _sysout.Add("ERROR IN CLOSING 1ST FILE:" + _wsInfil1Status);
        }

        // CLOSE INFILE2. // source: PAUDBLOD.CBL:350
        _wsInfil2Status = "00";
        if (_wsInfil2Status == "  " || _wsInfil2Status == "00")      // source: PAUDBLOD.CBL:352-353
        {
            // CONTINUE
        }
        else                                                         // source: PAUDBLOD.CBL:354-356
        {
            _sysout.Add("ERROR IN CLOSING 2ND FILE:" + _wsInfil2Status);
        }
    }

    // =================================================================================================
    // 9999-ABEND // source: PAUDBLOD.CBL:360-366
    // =================================================================================================
    private void Abend9999()
    {
        _sysout.Add("IMS LOAD ABENDING ...");                        // source: PAUDBLOD.CBL:363
        ReturnCode = 16;                                             // MOVE 16 TO RETURN-CODE // source: PAUDBLOD.CBL:365
        // GOBACK. // source: PAUDBLOD.CBL:366
        throw new AbendException("16", "PAUDBLOD abend (RETURN-CODE 16).");
    }

    // =================================================================================================
    // Sequential-file READ helpers (fixed-length records)
    // =================================================================================================

    /// <summary>
    /// OPEN INPUT of a fixed-length-record sequential file: slurps every record image (length
    /// <paramref name="recordLength"/>) and returns the open status ('00' on success, '35' FILE-NOT-FOUND
    /// when the dataset is missing). The records are then handed out one-at-a-time by <see cref="ReadNext"/>.
    /// </summary>
    private static byte[][] ReadAllRecords(string path, int recordLength, out string openStatus)
    {
        if (!File.Exists(path))
        {
            openStatus = FileStatus.FileNotFound; // '35'
            return [];
        }

        byte[] data = File.ReadAllBytes(path);
        int count = data.Length / recordLength;       // trailing partial bytes (if any) are ignored
        var records = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            var image = new byte[recordLength];
            Array.Copy(data, i * recordLength, image, 0, recordLength);
            records[i] = image;
        }
        openStatus = FileStatus.Ok; // '00'
        return records;
    }

    /// <summary>
    /// Sequential READ: returns the next record image and status '00', or '10' (END-OF-FILE) when the file
    /// is exhausted (leaving <paramref name="record"/> null — COBOL READ ... AT END leaves the record area
    /// unchanged on '10').
    /// </summary>
    private static string ReadNext(byte[][] records, ref int pos, out byte[]? record)
    {
        if (pos < records.Length)
        {
            record = records[pos];
            pos++;
            return FileStatus.Ok; // '00'
        }
        record = null;
        return FileStatus.EndOfFile; // '10'
    }

    // Maps a repository Insert FileStatus to the PAUT-PCB-STATUS the program tests ('00'->SPACES,
    // '22' duplicate -> 'II'; any other status surfaces as itself, driving the abend branch).
    private static string MapInsertStatus(string status) => status switch
    {
        FileStatus.Ok => "  ",
        FileStatus.DuplicateKeyError => "II",
        _ => status,
    };

    // =================================================================================================
    // COBOL ACCEPT / DISPLAY helpers
    // =================================================================================================

    private static int AcceptFromDate(DateTime now)
        => (now.Year % 100) * 10000 + now.Month * 100 + now.Day;

    private static int AcceptFromDay(DateTime now)
        => (now.Year % 100) * 1000 + now.DayOfYear;

    private static string Display9_6(int value)
        => (Math.Abs(value) % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The result of a <see cref="PendingAuthDbLoadUtility"/> run: the SYSOUT (DISPLAY) lines in order and the batch
/// <see cref="ReturnCode"/> (0 on a clean run, 16 after <c>9999-ABEND</c>).
/// </summary>
/// <param name="Sysout">The DISPLAY lines emitted to SYSOUT, in order.</param>
/// <param name="ReturnCode">The COBOL RETURN-CODE: 0 on success, 16 on abend.</param>
public sealed record PendingAuthDbLoadUtilityResult(IReadOnlyList<string> Sysout, int ReturnCode);
