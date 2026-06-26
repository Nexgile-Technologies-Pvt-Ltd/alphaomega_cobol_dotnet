using System.Globalization;
using CardDemo.Runtime;
using CardDemo.Domain;
using CardDemo.Import;

namespace CardDemo.Batch;

/// <summary>
/// Relational re-port of the batch program <c>CBIMPORT</c> ("Import Customer Data from Branch Migration
/// Export"). It reads the single multi-record-type flat EXPORT feed (<c>CVEXPORT</c>, 500-byte fixed
/// records carrying COMP/COMP-3 binary numerics), classifies each record by the one-byte
/// <c>EXPORT-REC-TYPE</c> discriminator (C/A/X/T/D), decodes the type-specific overlay into the matching
/// typed <see cref="CardDemo.Domain"/> entity, and writes (appends) one canonical fixed-width record per
/// input record to the matching per-type output flat file — CUSTOUT / ACCTOUT / XREFOUT / TRNXOUT /
/// CARDOUT — via <see cref="BatchSupport"/>. Unknown record types are counted and written to the
/// pipe-delimited ERROUT report. Per-target import counters drive an end-of-run summary.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBIMPORT.cbl</c>; method names mirror the COBOL
/// paragraph names and each carries a <c>// source: CBIMPORT.cbl:NNN</c> citation.</para>
/// <para><b>Data layer.</b> This is the relational re-port that REPLACES the legacy BLOB version. The
/// EXPORT feed is read as a sequential stream of raw 500-byte images through
/// <see cref="BatchSupport.ReadFixedRecords"/> (no VSAM/old BLOB layer). Each image is decoded through
/// the runtime codecs — <see cref="FixedRecord.Parse"/> slices the CVEXPORT REDEFINES variant and decodes
/// PIC X via <see cref="HostEncoding"/>, COMP-3 via <see cref="PackedDecimalCodec"/> and COMP via
/// <see cref="BinaryCodec"/> — which is exactly the EXPORT-record decoding the legacy port used; that
/// decoding is preserved here. The decoded fields are mapped to the typed domain entity, then re-emitted
/// as the canonical zoned-display target record by <see cref="RecordSerializer"/> and appended to the
/// output dataset (OPEN OUTPUT / DISP=NEW truncates the file first, then WRITE appends — see §FB-5).</para>
/// <para><b>FAITHFUL BUGS reproduced</b> (see <c>_design/specs/CBIMPORT.md</c> §6):</para>
/// <list type="bullet">
/// <item><b>FB-1 — CARDOUT has no JCL DD.</b> The program SELECTs/OPENs/WRITEs CARDOUT for type-'D'
/// records, but <c>CBIMPORT.jcl</c> allocates no CARDOUT DD. The JCL-faithful <see cref="Context"/> leaves
/// <see cref="Context.CardOutPath"/> null, so <see cref="OpenFiles1100"/> fails the OPEN OUTPUT of
/// CARD-OUTPUT and abends (CEE3ABD) before processing any record — exactly as the real run would.</item>
/// <item><b>FB-2 — sequence-number truncation in the error report.</b> EXPORT-SEQUENCE-NUM is 9(9) but
/// ERR-SEQUENCE is 9(7); the MOVE truncates the two high-order digits (kept low-order 7 digits).</item>
/// <item><b>FB-3 — "validation" does nothing.</b> 3000-VALIDATE-IMPORT only prints two static lines.</item>
/// <item><b>FB-4 — error timestamp is raw CURRENT-DATE in X(26).</b> ERR-TIMESTAMP receives the 21-char
/// CURRENT-DATE structure left-justified (trailing 5 chars spaces), not the ISO WS-IMPORT-* form.</item>
/// <item><b>FB-5 — no de-duplication / no key checks on output.</b> Outputs are sequential WRITEs, so
/// duplicate feed keys produce duplicate rows; no FILE STATUS '22' is ever seen.</item>
/// </list>
/// </remarks>
public sealed class Cbimport(Cbimport.Context ctx)
{
    private readonly Context _ctx = ctx;
    private readonly List<string> _sysout = [];

    // --- WS-IMPORT-STATISTICS counters (CBIMPORT.cbl:139-147) ------------------------------------
    private long _totalRecordsRead;          // WS-TOTAL-RECORDS-READ
    private long _customerRecordsImported;   // WS-CUSTOMER-RECORDS-IMPORTED
    private long _accountRecordsImported;    // WS-ACCOUNT-RECORDS-IMPORTED
    private long _xrefRecordsImported;       // WS-XREF-RECORDS-IMPORTED
    private long _tranRecordsImported;       // WS-TRAN-RECORDS-IMPORTED
    private long _cardRecordsImported;       // WS-CARD-RECORDS-IMPORTED
    private long _errorRecordsWritten;       // WS-ERROR-RECORDS-WRITTEN
    private long _unknownRecordTypeCount;    // WS-UNKNOWN-RECORD-TYPE-COUNT

    // --- WS-IMPORT-CONTROL (CBIMPORT.cbl:134-136) -----------------------------------------------
    private string _importDate = new(' ', 10);   // WS-IMPORT-DATE  X(10)
    private string _importTime = new(' ', 8);    // WS-IMPORT-TIME  X(08)

    // --- FD buffers (the EXPORT input + the output sinks). ---------------------------------------
    private IReadOnlyList<byte[]> _exportRecords = [];   // EXPORT-INPUT records, sequential read.
    private int _exportCursor;
    private bool _exportEof;

    private FixedFileWriter _customerOut = null!;   // CUSTOMER-OUTPUT (CUSTOUT)
    private FixedFileWriter _accountOut = null!;     // ACCOUNT-OUTPUT  (ACCTOUT)
    private FixedFileWriter _xrefOut = null!;        // XREF-OUTPUT     (XREFOUT)
    private FixedFileWriter _transactionOut = null!; // TRANSACTION-OUTPUT (TRNXOUT)
    private FixedFileWriter? _cardOut;               // CARD-OUTPUT     (CARDOUT) — FB-1: usually no DD.
    private FixedFileWriter _errorOut = null!;       // ERROR-OUTPUT    (ERROUT)

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    // =====================================================================================
    // 0000-MAIN-PROCESSING  // source: CBIMPORT.cbl:165-171
    // =====================================================================================
    /// <summary>
    /// Drives the import: 1000-INITIALIZE -&gt; 2000-PROCESS-EXPORT-FILE -&gt; 3000-VALIDATE-IMPORT -&gt;
    /// 4000-FINALIZE -&gt; GOBACK. // source: CBIMPORT.cbl:167-171
    /// </summary>
    /// <returns>The process RETURN-CODE (CBIMPORT leaves it 0 on a clean run). // source: CBIMPORT.cbl:171</returns>
    public int Run()
    {
        try
        {
            Initialize1000();          // source: CBIMPORT.cbl:167
            ProcessExportFile2000();   // source: CBIMPORT.cbl:168
            ValidateImport3000();      // source: CBIMPORT.cbl:169
            Finalize4000();            // source: CBIMPORT.cbl:170
            return 0;                  // GOBACK  // source: CBIMPORT.cbl:171
        }
        finally
        {
            // CLOSE buffers (FD close also happens in 4000-FINALIZE; this guards the abend paths).
            _customerOut?.Flush(); _customerOut?.Dispose();
            _accountOut?.Flush(); _accountOut?.Dispose();
            _xrefOut?.Flush(); _xrefOut?.Dispose();
            _transactionOut?.Flush(); _transactionOut?.Dispose();
            _cardOut?.Flush(); _cardOut?.Dispose();
            _errorOut?.Flush(); _errorOut?.Dispose();
        }
    }

    // =====================================================================================
    // 1000-INITIALIZE  // source: CBIMPORT.cbl:174-193
    // =====================================================================================
    private void Initialize1000()
    {
        Display("CBIMPORT: Starting Customer Data Import");   // source: CBIMPORT.cbl:176

        // Build WS-IMPORT-DATE X(10) = CCYY-MM-DD from FUNCTION CURRENT-DATE substrings.
        // source: CBIMPORT.cbl:178-182
        string cd = CurrentDate();                            // CCYYMMDDhhmmss... (FUNCTION CURRENT-DATE)
        _importDate = cd.Substring(0, 4) + "-" + cd.Substring(4, 2) + "-" + cd.Substring(6, 2);

        // Build WS-IMPORT-TIME X(8) = HH:MM:SS from CURRENT-DATE (9:2)/(11:2)/(13:2).
        // source: CBIMPORT.cbl:184-188
        _importTime = cd.Substring(8, 2) + ":" + cd.Substring(10, 2) + ":" + cd.Substring(12, 2);

        OpenFiles1100();                                      // source: CBIMPORT.cbl:190

        Display("CBIMPORT: Import Date: " + _importDate);     // source: CBIMPORT.cbl:192
        Display("CBIMPORT: Import Time: " + _importTime);     // source: CBIMPORT.cbl:193
    }

    // =====================================================================================
    // 1100-OPEN-FILES  // source: CBIMPORT.cbl:196-245
    // =====================================================================================
    /// <summary>
    /// OPEN INPUT EXPORT-INPUT, then OPEN OUTPUT the five target sinks + ERROR, in copybook order. Each
    /// failed OPEN displays a file-specific message and abends. The relational read source is the flat
    /// EXPORT feed, slurped as raw 500-byte images; each OPEN OUTPUT truncates the target file (DISP=NEW).
    /// </summary>
    private void OpenFiles1100()
    {
        // OPEN INPUT EXPORT-INPUT  // source: CBIMPORT.cbl:198-203
        try
        {
            _exportRecords = BatchSupport.ReadFixedRecords(_ctx.ExportPath, ExportRecordLength);
            _exportCursor = 0;
            _exportEof = false;
        }
        catch (Exception)
        {
            // NOT WS-EXPORT-OK  // source: CBIMPORT.cbl:199-202
            Display("ERROR: Cannot open EXPORT-INPUT, Status: " + "35");
            AbendProgram9999();
        }

        // OPEN OUTPUT CUSTOMER-OUTPUT  // source: CBIMPORT.cbl:205-210
        _customerOut = OpenOutput(_ctx.CustomerOutPath, "CUSTOMER-OUTPUT");
        // OPEN OUTPUT ACCOUNT-OUTPUT   // source: CBIMPORT.cbl:212-217
        _accountOut = OpenOutput(_ctx.AccountOutPath, "ACCOUNT-OUTPUT");
        // OPEN OUTPUT XREF-OUTPUT      // source: CBIMPORT.cbl:219-224
        _xrefOut = OpenOutput(_ctx.XrefOutPath, "XREF-OUTPUT");
        // OPEN OUTPUT TRANSACTION-OUTPUT  // source: CBIMPORT.cbl:226-231
        _transactionOut = OpenOutput(_ctx.TransactionOutPath, "TRANSACTION-OUTPUT");

        // OPEN OUTPUT CARD-OUTPUT  // source: CBIMPORT.cbl:233-238
        // FB-1: CBIMPORT.jcl allocates no CARDOUT DD. With the JCL-faithful Context (CardOutPath == null)
        // the OPEN fails (status != '00'), so the program DISPLAYs the error and abends here — before any
        // record is read. A caller that supplies a CardOutPath (recommended option-b override) opens the
        // CARD sink normally.
        if (_ctx.CardOutPath is null)
        {
            // NOT WS-CARD-OK  // source: CBIMPORT.cbl:234-237
            Display("ERROR: Cannot open CARD-OUTPUT, Status: " + "35");
            AbendProgram9999();
        }
        else
        {
            _cardOut = OpenOutput(_ctx.CardOutPath, "CARD-OUTPUT");
        }

        // OPEN OUTPUT ERROR-OUTPUT  // source: CBIMPORT.cbl:240-245
        _errorOut = OpenOutput(_ctx.ErrorOutPath, "ERROR-OUTPUT");
    }

    /// <summary>
    /// OPEN OUTPUT a target sink: DISP=NEW truncates the file (delete-then-append, matching the COBOL
    /// OPEN OUTPUT semantics described by <see cref="BatchSupport.OpenWriter"/>), returning the appender.
    /// On any failure: DISPLAY the file-specific error and abend. // source: CBIMPORT.cbl:205-245
    /// </summary>
    private FixedFileWriter OpenOutput(string path, string ddName)
    {
        try
        {
            // DISP=(NEW,CATLG,DELETE): start a fresh load for this run.
            if (File.Exists(path)) File.Delete(path);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return BatchSupport.OpenWriter(path, _ctx.Host);
        }
        catch (Exception)
        {
            Display($"ERROR: Cannot open {ddName}, Status: " + "35");
            AbendProgram9999();
            throw; // unreachable: AbendProgram9999 throws.
        }
    }

    // =====================================================================================
    // 2000-PROCESS-EXPORT-FILE  // source: CBIMPORT.cbl:248-256
    // =====================================================================================
    private void ProcessExportFile2000()
    {
        ReadExportRecord2100();                          // priming read  // source: CBIMPORT.cbl:250

        // PERFORM UNTIL WS-EXPORT-EOF  // source: CBIMPORT.cbl:252-256
        while (!_exportEof)
        {
            _totalRecordsRead++;                         // ADD 1 TO WS-TOTAL-RECORDS-READ  // source: CBIMPORT.cbl:253
            ProcessRecordByType2200();                   // source: CBIMPORT.cbl:254
            ReadExportRecord2100();                      // source: CBIMPORT.cbl:255
        }
    }

    // =====================================================================================
    // 2100-READ-EXPORT-RECORD  // source: CBIMPORT.cbl:259-267
    // =====================================================================================
    /// <summary>
    /// READ EXPORT-INPUT INTO EXPORT-RECORD: advance the sequential cursor over the raw 500-byte images.
    /// End-of-stream sets WS-EXPORT-EOF ('10'); any other read failure abends. // source: CBIMPORT.cbl:261-267
    /// </summary>
    private void ReadExportRecord2100()
    {
        if (_exportCursor >= _exportRecords.Count)
        {
            _exportEof = true;                           // WS-EXPORT-EOF ('10')  // source: CBIMPORT.cbl:118
            _currentImage = null;
            return;
        }
        _currentImage = _exportRecords[_exportCursor];
        _exportCursor++;
        // NOT WS-EXPORT-OK AND NOT WS-EXPORT-EOF would abend (cannot occur for an in-memory image list).
        // source: CBIMPORT.cbl:263-267
    }

    private byte[]? _currentImage; // EXPORT-RECORD image for the record currently being processed.

    // =====================================================================================
    // 2200-PROCESS-RECORD-BY-TYPE  // source: CBIMPORT.cbl:270-285
    // =====================================================================================
    /// <summary>
    /// EVALUATE EXPORT-REC-TYPE -&gt; dispatch to the per-type paragraph. Type 'D' is CARD (not 'C', which
    /// is CUSTOMER). // source: CBIMPORT.cbl:272-285
    /// </summary>
    private void ProcessRecordByType2200()
    {
        byte[] image = _currentImage!;
        // EXPORT-REC-TYPE PIC X(1) at offset 0.  // source: CVEXPORT.cpy:10
        string recType = HostEncoding.For(_ctx.Host).GetString(image.AsSpan(0, 1));

        switch (recType)
        {
            case "C": ProcessCustomerRecord2300(image); break;   // source: CBIMPORT.cbl:273-274
            case "A": ProcessAccountRecord2400(image); break;    // source: CBIMPORT.cbl:275-276
            case "X": ProcessXrefRecord2500(image); break;       // source: CBIMPORT.cbl:277-278
            case "T": ProcessTranRecord2600(image); break;       // source: CBIMPORT.cbl:279-280
            case "D": ProcessCardRecord2650(image); break;       // source: CBIMPORT.cbl:281-282
            default: ProcessUnknownRecord2700(image); break;     // source: CBIMPORT.cbl:283-284
        }
    }

    // =====================================================================================
    // 2300-PROCESS-CUSTOMER-RECORD  // source: CBIMPORT.cbl:288-320
    // =====================================================================================
    /// <summary>
    /// INITIALIZE CUSTOMER-RECORD, MOVE the 17 EXP-CUST-* fields (OCCURS unrolled: ADDR-LINE 1..3,
    /// PHONE-NUM 1..2) into it, WRITE it (append one row), bump the customer counter. Decode preserves the
    /// legacy CVEXPORT path: EXP-CUST-ID 9(9) COMP and EXP-CUST-FICO-CREDIT-SCORE 9(3) COMP-3 go through
    /// the runtime codecs. // source: CBIMPORT.cbl:290-320
    /// </summary>
    private void ProcessCustomerRecord2300(byte[] image)
    {
        FixedRecord exp = FixedRecord.Parse(_ctx.CustomerVariant, image, _ctx.Host);
        var rec = new Customer
        {
            CustId = (long)exp.GetNumber("EXP-CUST-ID"),                  // source: CBIMPORT.cbl:293
            FirstName = exp.GetText("EXP-CUST-FIRST-NAME"),              // source: CBIMPORT.cbl:294
            MiddleName = exp.GetText("EXP-CUST-MIDDLE-NAME"),           // source: CBIMPORT.cbl:295
            LastName = exp.GetText("EXP-CUST-LAST-NAME"),               // source: CBIMPORT.cbl:296
            AddrLine1 = exp.GetText("EXP-CUST-ADDR-LINE_1"),            // source: CBIMPORT.cbl:297
            AddrLine2 = exp.GetText("EXP-CUST-ADDR-LINE_2"),            // source: CBIMPORT.cbl:298
            AddrLine3 = exp.GetText("EXP-CUST-ADDR-LINE_3"),            // source: CBIMPORT.cbl:299
            AddrStateCd = exp.GetText("EXP-CUST-ADDR-STATE-CD"),       // source: CBIMPORT.cbl:300
            AddrCountryCd = exp.GetText("EXP-CUST-ADDR-COUNTRY-CD"),   // source: CBIMPORT.cbl:301
            AddrZip = exp.GetText("EXP-CUST-ADDR-ZIP"),                 // source: CBIMPORT.cbl:302
            PhoneNum1 = exp.GetText("EXP-CUST-PHONE-NUM_1"),           // source: CBIMPORT.cbl:303
            PhoneNum2 = exp.GetText("EXP-CUST-PHONE-NUM_2"),           // source: CBIMPORT.cbl:304
            Ssn = (long)exp.GetNumber("EXP-CUST-SSN"),                   // source: CBIMPORT.cbl:305
            GovtIssuedId = exp.GetText("EXP-CUST-GOVT-ISSUED-ID"),     // source: CBIMPORT.cbl:306
            DobYyyyMmDd = exp.GetText("EXP-CUST-DOB-YYYY-MM-DD"),      // source: CBIMPORT.cbl:307
            EftAccountId = exp.GetText("EXP-CUST-EFT-ACCOUNT-ID"),     // source: CBIMPORT.cbl:308
            PriCardHolderInd = exp.GetText("EXP-CUST-PRI-CARD-HOLDER-IND"), // source: CBIMPORT.cbl:309
            FicoCreditScore = (int)exp.GetNumber("EXP-CUST-FICO-CREDIT-SCORE"), // source: CBIMPORT.cbl:310
        };

        // WRITE CUSTOMER-RECORD  // source: CBIMPORT.cbl:312
        _customerOut.WriteRecord(_ctx.Serializer.Serialize(rec, _ctx.Host), CustomerRecordLength);
        _customerRecordsImported++;                                      // source: CBIMPORT.cbl:320
    }

    // =====================================================================================
    // 2400-PROCESS-ACCOUNT-RECORD  // source: CBIMPORT.cbl:323-349
    // =====================================================================================
    /// <summary>
    /// INITIALIZE ACCOUNT-RECORD, MOVE the 11 EXP-ACCT-* fields (mixed encodings: CURR-BAL/CASH-CREDIT
    /// COMP-3, CURR-CYC-DEBIT COMP, CREDIT-LIMIT/CURR-CYC-CREDIT zoned-display) into it, WRITE, bump the
    /// account counter. // source: CBIMPORT.cbl:325-349
    /// </summary>
    private void ProcessAccountRecord2400(byte[] image)
    {
        FixedRecord exp = FixedRecord.Parse(_ctx.AccountVariant, image, _ctx.Host);
        var rec = new Account
        {
            AcctId = (long)exp.GetNumber("EXP-ACCT-ID"),                 // source: CBIMPORT.cbl:328
            ActiveStatus = exp.GetText("EXP-ACCT-ACTIVE-STATUS"),       // source: CBIMPORT.cbl:329
            CurrBal = exp.GetNumber("EXP-ACCT-CURR-BAL"),               // source: CBIMPORT.cbl:330
            CreditLimit = exp.GetNumber("EXP-ACCT-CREDIT-LIMIT"),       // source: CBIMPORT.cbl:331
            CashCreditLimit = exp.GetNumber("EXP-ACCT-CASH-CREDIT-LIMIT"), // source: CBIMPORT.cbl:332
            OpenDate = exp.GetText("EXP-ACCT-OPEN-DATE"),               // source: CBIMPORT.cbl:333
            ExpirationDate = exp.GetText("EXP-ACCT-EXPIRAION-DATE"),    // source: CBIMPORT.cbl:334
            ReissueDate = exp.GetText("EXP-ACCT-REISSUE-DATE"),         // source: CBIMPORT.cbl:335
            CurrCycCredit = exp.GetNumber("EXP-ACCT-CURR-CYC-CREDIT"),  // source: CBIMPORT.cbl:336
            CurrCycDebit = exp.GetNumber("EXP-ACCT-CURR-CYC-DEBIT"),    // source: CBIMPORT.cbl:337
            AddrZip = exp.GetText("EXP-ACCT-ADDR-ZIP"),                 // source: CBIMPORT.cbl:338
            GroupId = exp.GetText("EXP-ACCT-GROUP-ID"),                 // source: CBIMPORT.cbl:339
        };

        // WRITE ACCOUNT-RECORD  // source: CBIMPORT.cbl:341
        _accountOut.WriteRecord(_ctx.Serializer.Serialize(rec, _ctx.Host), AccountRecordLength);
        _accountRecordsImported++;                                       // source: CBIMPORT.cbl:349
    }

    // =====================================================================================
    // 2500-PROCESS-XREF-RECORD  // source: CBIMPORT.cbl:352-369
    // =====================================================================================
    /// <summary>
    /// INITIALIZE CARD-XREF-RECORD, MOVE EXP-XREF-CARD-NUM/CUST-ID/ACCT-ID (ACCT-ID is 9(11) COMP) into
    /// it, WRITE, bump the xref counter. // source: CBIMPORT.cbl:354-369
    /// </summary>
    private void ProcessXrefRecord2500(byte[] image)
    {
        FixedRecord exp = FixedRecord.Parse(_ctx.XrefVariant, image, _ctx.Host);
        var rec = new CardXref
        {
            XrefCardNum = exp.GetText("EXP-XREF-CARD-NUM"),             // source: CBIMPORT.cbl:357
            CustId = (long)exp.GetNumber("EXP-XREF-CUST-ID"),            // source: CBIMPORT.cbl:358
            AcctId = (long)exp.GetNumber("EXP-XREF-ACCT-ID"),            // source: CBIMPORT.cbl:359
        };

        // WRITE CARD-XREF-RECORD  // source: CBIMPORT.cbl:361
        _xrefOut.WriteRecord(_ctx.Serializer.Serialize(rec, _ctx.Host), XrefRecordLength);
        _xrefRecordsImported++;                                          // source: CBIMPORT.cbl:369
    }

    // =====================================================================================
    // 2600-PROCESS-TRAN-RECORD  // source: CBIMPORT.cbl:372-399
    // =====================================================================================
    /// <summary>
    /// INITIALIZE TRAN-RECORD, MOVE the 13 EXP-TRAN-* fields (AMT S9(9)V99 COMP-3, MERCHANT-ID 9(9) COMP,
    /// CAT-CD 9(4) zoned) into it, WRITE, bump the transaction counter. // source: CBIMPORT.cbl:374-399
    /// </summary>
    private void ProcessTranRecord2600(byte[] image)
    {
        FixedRecord exp = FixedRecord.Parse(_ctx.TransactionVariant, image, _ctx.Host);
        var rec = new Transaction
        {
            TranId = exp.GetText("EXP-TRAN-ID"),                       // source: CBIMPORT.cbl:377
            TypeCd = exp.GetText("EXP-TRAN-TYPE-CD"),                  // source: CBIMPORT.cbl:378
            CatCd = (int)exp.GetNumber("EXP-TRAN-CAT-CD"),             // source: CBIMPORT.cbl:379
            Source = exp.GetText("EXP-TRAN-SOURCE"),                   // source: CBIMPORT.cbl:380
            Desc = exp.GetText("EXP-TRAN-DESC"),                       // source: CBIMPORT.cbl:381
            Amt = exp.GetNumber("EXP-TRAN-AMT"),                       // source: CBIMPORT.cbl:382
            MerchantId = (long)exp.GetNumber("EXP-TRAN-MERCHANT-ID"),  // source: CBIMPORT.cbl:383
            MerchantName = exp.GetText("EXP-TRAN-MERCHANT-NAME"),      // source: CBIMPORT.cbl:384
            MerchantCity = exp.GetText("EXP-TRAN-MERCHANT-CITY"),      // source: CBIMPORT.cbl:385
            MerchantZip = exp.GetText("EXP-TRAN-MERCHANT-ZIP"),        // source: CBIMPORT.cbl:386
            CardNum = exp.GetText("EXP-TRAN-CARD-NUM"),                // source: CBIMPORT.cbl:387
            OrigTs = exp.GetText("EXP-TRAN-ORIG-TS"),                  // source: CBIMPORT.cbl:388
            ProcTs = exp.GetText("EXP-TRAN-PROC-TS"),                  // source: CBIMPORT.cbl:389
        };

        // WRITE TRAN-RECORD  // source: CBIMPORT.cbl:391
        _transactionOut.WriteRecord(_ctx.Serializer.Serialize(rec, _ctx.Host), TransactionRecordLength);
        _tranRecordsImported++;                                          // source: CBIMPORT.cbl:399
    }

    // =====================================================================================
    // 2650-PROCESS-CARD-RECORD  // source: CBIMPORT.cbl:402-422
    // =====================================================================================
    /// <summary>
    /// INITIALIZE CARD-RECORD, MOVE the 6 EXP-CARD-* fields (ACCT-ID 9(11) COMP, CVV-CD 9(3) COMP) into
    /// it, WRITE, bump the card counter. With the JCL-faithful Context this paragraph is never reached:
    /// the program already abended in 1100-OPEN-FILES on the missing CARDOUT DD (FB-1). When a CardOutPath
    /// is supplied (override), the CARD sink is written normally. // source: CBIMPORT.cbl:404-422
    /// </summary>
    private void ProcessCardRecord2650(byte[] image)
    {
        FixedRecord exp = FixedRecord.Parse(_ctx.CardVariant, image, _ctx.Host);
        var rec = new Card
        {
            CardNum = exp.GetText("EXP-CARD-NUM"),                     // source: CBIMPORT.cbl:407
            AcctId = (long)exp.GetNumber("EXP-CARD-ACCT-ID"),          // source: CBIMPORT.cbl:408
            CvvCd = (int)exp.GetNumber("EXP-CARD-CVV-CD"),             // source: CBIMPORT.cbl:409
            EmbossedName = exp.GetText("EXP-CARD-EMBOSSED-NAME"),      // source: CBIMPORT.cbl:410
            ExpirationDate = exp.GetText("EXP-CARD-EXPIRAION-DATE"),   // source: CBIMPORT.cbl:411
            ActiveStatus = exp.GetText("EXP-CARD-ACTIVE-STATUS"),      // source: CBIMPORT.cbl:412
        };

        // WRITE CARD-RECORD  // source: CBIMPORT.cbl:414
        _cardOut!.WriteRecord(_ctx.Serializer.Serialize(rec, _ctx.Host), CardRecordLength);
        _cardRecordsImported++;                                          // source: CBIMPORT.cbl:422
    }

    // =====================================================================================
    // 2700-PROCESS-UNKNOWN-RECORD  // source: CBIMPORT.cbl:425-434
    // =====================================================================================
    /// <summary>
    /// Bump WS-UNKNOWN-RECORD-TYPE-COUNT, build WS-ERROR-RECORD (ts | rec-type | seq | message), then
    /// 2750-WRITE-ERROR. // source: CBIMPORT.cbl:427-434
    /// </summary>
    private void ProcessUnknownRecord2700(byte[] image)
    {
        _unknownRecordTypeCount++;                                       // source: CBIMPORT.cbl:427

        // WS-ERROR-RECORD layout (132 bytes): ERR-TIMESTAMP X(26) | ERR-RECORD-TYPE X(1) |
        // ERR-SEQUENCE 9(7) | ERR-MESSAGE X(50) | FILLER X(43) SPACES. // source: CBIMPORT.cbl:152-160
        var rec = new byte[ErrorRecordLength];
        rec.AsSpan().Fill((byte)' ');

        // ERR-TIMESTAMP <- FUNCTION CURRENT-DATE into X(26). FB-4: CURRENT-DATE is a 21-char structure
        // placed left-justified; the trailing 5 bytes stay spaces (not the ISO WS-IMPORT-* format).
        // source: CBIMPORT.cbl:429
        string ts = CurrentDate();
        ts = ts.Length >= 26 ? ts[..26] : ts.PadRight(26, ' ');
        Put(rec, 0, ts);                                                 // ERR-TIMESTAMP

        rec[26] = (byte)'|';                                             // FILLER '|'  // source: CBIMPORT.cbl:154
        rec[27] = image[0];                                              // ERR-RECORD-TYPE <- EXPORT-REC-TYPE  // source: CBIMPORT.cbl:430
        rec[28] = (byte)'|';                                             // FILLER '|'  // source: CBIMPORT.cbl:156

        // ERR-SEQUENCE 9(7) <- EXPORT-SEQUENCE-NUM 9(9) COMP (offset 27, 4 bytes). FB-2: the MOVE of a
        // 9-digit value into a 7-digit field truncates the two high-order digits (keep low-order 7).
        // source: CVEXPORT.cpy:16; CBIMPORT.cbl:157,431
        long seq = (long)BinaryCodec.Decode(image.AsSpan(SequenceNumOffset, SequenceNumLength), 0, false);
        long seq7 = ((seq % 10_000_000) + 10_000_000) % 10_000_000;     // low-order 7 digits
        Put(rec, 29, seq7.ToString("D7", CultureInfo.InvariantCulture)); // ERR-SEQUENCE 9(7)
        rec[36] = (byte)'|';                                            // FILLER '|'  // source: CBIMPORT.cbl:158

        // ERR-MESSAGE X(50) <- 'Unknown record type encountered' (space-padded to 50).
        // source: CBIMPORT.cbl:159,432
        Put(rec, 37, "Unknown record type encountered".PadRight(50, ' '));
        // bytes 87..131 = FILLER X(43) SPACES (already space-filled).  // source: CBIMPORT.cbl:160

        WriteError2750(rec);                                            // source: CBIMPORT.cbl:434
    }

    // =====================================================================================
    // 2750-WRITE-ERROR  // source: CBIMPORT.cbl:437-446
    // =====================================================================================
    /// <summary>
    /// WRITE ERROR-OUTPUT-RECORD FROM WS-ERROR-RECORD; a write failure on the error file is logged but
    /// TOLERATED (no abend). Bump WS-ERROR-RECORDS-WRITTEN. // source: CBIMPORT.cbl:439-446
    /// </summary>
    private void WriteError2750(byte[] errorRecord)
    {
        try
        {
            _errorOut.WriteRecord(errorRecord, ErrorRecordLength);      // source: CBIMPORT.cbl:439
        }
        catch (Exception)
        {
            // NOT WS-ERROR-OK -> DISPLAY only (no abend).  // source: CBIMPORT.cbl:441-444
            Display("ERROR: Writing error record, Status: " + "30");
        }
        _errorRecordsWritten++;                                         // source: CBIMPORT.cbl:446
    }

    // =====================================================================================
    // 3000-VALIDATE-IMPORT  // source: CBIMPORT.cbl:449-452
    // =====================================================================================
    /// <summary>
    /// FB-3: prints two static lines and performs NO actual validation (despite the documented "validate
    /// data integrity using checksums" purpose). // source: CBIMPORT.cbl:451-452
    /// </summary>
    private void ValidateImport3000()
    {
        Display("CBIMPORT: Import validation completed");                // source: CBIMPORT.cbl:451
        Display("CBIMPORT: No validation errors detected");             // source: CBIMPORT.cbl:452
    }

    // =====================================================================================
    // 4000-FINALIZE  // source: CBIMPORT.cbl:455-478
    // =====================================================================================
    /// <summary>
    /// CLOSE all 7 files, then DISPLAY the end-of-run summary (8 counter lines). Counters are 9(9), so the
    /// summary prints them zero-padded to 9 digits as COBOL DISPLAY of an unedited 9(9) would.
    /// // source: CBIMPORT.cbl:457-478
    /// </summary>
    private void Finalize4000()
    {
        // CLOSE EXPORT-INPUT + the six outputs.  // source: CBIMPORT.cbl:457-463
        _customerOut.Flush();
        _accountOut.Flush();
        _xrefOut.Flush();
        _transactionOut.Flush();
        _cardOut?.Flush();
        _errorOut.Flush();

        Display("CBIMPORT: Import completed");                           // source: CBIMPORT.cbl:465
        Display("CBIMPORT: Total Records Read: " + Counter(_totalRecordsRead));        // source: CBIMPORT.cbl:466-467
        Display("CBIMPORT: Customers Imported: " + Counter(_customerRecordsImported)); // source: CBIMPORT.cbl:468-469
        Display("CBIMPORT: Accounts Imported: " + Counter(_accountRecordsImported));   // source: CBIMPORT.cbl:470-471
        Display("CBIMPORT: XRefs Imported: " + Counter(_xrefRecordsImported));         // source: CBIMPORT.cbl:472
        Display("CBIMPORT: Transactions Imported: " + Counter(_tranRecordsImported));  // source: CBIMPORT.cbl:473-474
        Display("CBIMPORT: Cards Imported: " + Counter(_cardRecordsImported));         // source: CBIMPORT.cbl:475
        Display("CBIMPORT: Errors Written: " + Counter(_errorRecordsWritten));         // source: CBIMPORT.cbl:476
        Display("CBIMPORT: Unknown Record Types: " + Counter(_unknownRecordTypeCount)); // source: CBIMPORT.cbl:477-478
    }

    // =====================================================================================
    // 9999-ABEND-PROGRAM  // source: CBIMPORT.cbl:481-484
    // =====================================================================================
    /// <summary>DISPLAY the abend banner then CALL 'CEE3ABD' (mapped to <see cref="AbendException"/>). // source: CBIMPORT.cbl:483-484</summary>
    private void AbendProgram9999()
    {
        Display("CBIMPORT: ABENDING PROGRAM");                          // source: CBIMPORT.cbl:483
        throw new AbendException("999", "CBIMPORT abend.");             // CALL 'CEE3ABD'  // source: CBIMPORT.cbl:484
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>DISPLAY -&gt; SYSOUT: collect the line in order.</summary>
    private void Display(string line) => _sysout.Add(line);

    /// <summary>
    /// FUNCTION CURRENT-DATE: the 21-char <c>CCYYMMDDhhmmssnn±hhmm</c> structure from the injected clock,
    /// used both for the WS-IMPORT-* fields and (raw) for the ERR-TIMESTAMP (FB-4).
    /// </summary>
    private string CurrentDate()
    {
        DateTime now = _ctx.Clock.Now;
        // CCYYMMDDhhmmssnn = 16 chars, then offset ±hhmm = 5 chars -> 21 chars total.
        string body = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                    + (now.Millisecond / 10).ToString("D2", CultureInfo.InvariantCulture);
        TimeSpan off = TimeZoneInfo.Local.GetUtcOffset(now);
        string sign = off < TimeSpan.Zero ? "-" : "+";
        string offset = sign + Math.Abs(off.Hours).ToString("D2", CultureInfo.InvariantCulture)
                             + Math.Abs(off.Minutes).ToString("D2", CultureInfo.InvariantCulture);
        return body + offset; // 21 chars
    }

    /// <summary>COBOL DISPLAY of an unedited 9(9) counter: all 9 digits, zero-padded (low-order on overflow).</summary>
    private static string Counter(long value)
    {
        string digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        return digits.Length >= 9 ? digits[^9..] : digits.PadLeft(9, '0');
    }

    /// <summary>Host-encodes <paramref name="text"/> and copies it into <paramref name="buffer"/> at <paramref name="offset"/>.</summary>
    private void Put(byte[] buffer, int offset, string text)
        => HostEncoding.For(_ctx.Host).GetBytes(text).CopyTo(buffer, offset);

    // --- Record lengths (CVEXPORT input + the per-type output copybooks). ------------------------
    private const int ExportRecordLength = 500;       // CVEXPORT  // source: CBIMPORT.cbl:78
    private const int CustomerRecordLength = 500;     // CVCUS01Y  // source: CBIMPORT.cbl:83
    private const int AccountRecordLength = 300;      // CVACT01Y  // source: CBIMPORT.cbl:88
    private const int XrefRecordLength = 50;          // CVACT03Y  // source: CBIMPORT.cbl:93
    private const int TransactionRecordLength = 350;  // CVTRA05Y  // source: CBIMPORT.cbl:98
    private const int CardRecordLength = 150;         // CVACT02Y  // source: CBIMPORT.cbl:103
    private const int ErrorRecordLength = 132;        // WS-ERROR-RECORD  // source: CBIMPORT.cbl:108-109

    // EXPORT-SEQUENCE-NUM 9(9) COMP: 4 bytes at offset 27 (header = REC-TYPE 1 + TIMESTAMP 26).
    // source: CVEXPORT.cpy:10-16
    private const int SequenceNumOffset = 27;
    private const int SequenceNumLength = 4;

    /// <summary>
    /// Inputs for <see cref="Cbimport"/> over the relational data layer.
    /// </summary>
    /// <param name="ExportPath">EXPFILE — the flat 500-byte EXPORT feed (read sequentially).</param>
    /// <param name="CustomerOutPath">CUSTOUT — customer staging output (500-byte CVCUS01Y).</param>
    /// <param name="AccountOutPath">ACCTOUT — account staging output (300-byte CVACT01Y).</param>
    /// <param name="XrefOutPath">XREFOUT — card-xref staging output (50-byte CVACT03Y).</param>
    /// <param name="TransactionOutPath">TRNXOUT — transaction staging output (350-byte CVTRA05Y).</param>
    /// <param name="ErrorOutPath">ERROUT — error/reject report (132-byte pipe-delimited lines).</param>
    /// <param name="Serializer">Re-serializes a typed entity to its canonical fixed-width record image.</param>
    /// <param name="CustomerVariant">CVEXPORT layout with EXPORT-CUSTOMER-DATA active (decodes type 'C').</param>
    /// <param name="AccountVariant">CVEXPORT layout with EXPORT-ACCOUNT-DATA active (decodes type 'A').</param>
    /// <param name="XrefVariant">CVEXPORT layout with EXPORT-CARD-XREF-DATA active (decodes type 'X').</param>
    /// <param name="TransactionVariant">CVEXPORT layout with EXPORT-TRANSACTION-DATA active (decodes type 'T').</param>
    /// <param name="CardVariant">CVEXPORT layout with EXPORT-CARD-DATA active (decodes type 'D').</param>
    /// <param name="Clock">Source of FUNCTION CURRENT-DATE.</param>
    /// <param name="Host">Host encoding for the input feed and the output datasets.</param>
    /// <param name="CardOutPath">
    /// CARDOUT — card staging output (150-byte CVACT02Y). FB-1: <c>CBIMPORT.jcl</c> allocates NO CARDOUT
    /// DD, so the JCL-faithful default is <c>null</c>, which makes <see cref="OpenFiles1100"/> abend on
    /// the missing DD. Supply a path to enable the recommended option-b CARD target.
    /// </param>
    public sealed record Context(
        string ExportPath,
        string CustomerOutPath,
        string AccountOutPath,
        string XrefOutPath,
        string TransactionOutPath,
        string ErrorOutPath,
        RecordSerializer Serializer,
        RecordLayout CustomerVariant,
        RecordLayout AccountVariant,
        RecordLayout XrefVariant,
        RecordLayout TransactionVariant,
        RecordLayout CardVariant,
        IClock Clock,
        HostKind Host,
        string? CardOutPath = null);
}
