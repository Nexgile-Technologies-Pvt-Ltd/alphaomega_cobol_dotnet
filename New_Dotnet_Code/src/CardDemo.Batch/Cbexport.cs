using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Tooling;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch program <c>CBEXPORT</c> (Customer Data Export for Branch
/// Migration). It reads the five CardDemo masters — CUSTOMER, ACCOUNT, CARD_XREF, TRANSACTION and CARD —
/// in strict <c>C -&gt; A -&gt; X -&gt; T -&gt; D</c> phase order, each table browsed ascending by its
/// primary key (the VSAM KSDS sequential read), and for every source row emits one fixed-length
/// <b>500-byte</b> export record (copybook <c>CVEXPORT</c>) into a single multi-record-type export
/// dataset. Each record carries a 1-char REC-TYPE discriminator, a shared 26-char run timestamp, an
/// ascending global sequence number, the hard-coded branch id <c>'0001'</c> / region code <c>'NORTH'</c>,
/// and a 460-byte type-specific payload mapped field-by-field from the source row.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBEXPORT.cbl</c>; each PROCEDURE-DIVISION paragraph
/// is a method whose body keeps the original statement order and control flow (with
/// <c>// source: CBEXPORT.cbl:NNN</c> citations). Per <c>_design/ARCHITECTURE.md</c> only the five INPUT
/// files are base tables (read via the repositories); <c>EXPFILE</c> is the program's derived product — a
/// byte-faithful fixed-width 500-byte record stream written through <see cref="FixedFileWriter"/>
/// (BatchSupport). The data SOURCE changed (TABLES, not VSAM/BLOB images); the EXPORT-record layout and
/// its COMP / COMP-3 / zoned encodings are preserved byte-for-byte by building each record through the
/// parsed <c>CVEXPORT</c> variant <see cref="RecordLayout"/> and the runtime codecs
/// (<see cref="BinaryCodec"/> COMP, <see cref="PackedDecimalCodec"/> COMP-3, <see cref="ZonedDecimalCodec"/>
/// zoned DISPLAY).</para>
/// <para>FAITHFUL BUGS preserved verbatim (see <c>_design/faithful-bugs.md</c> / spec §7):
/// <list type="number">
/// <item>Timestamp hundredths are the hard-coded literal <c>.00</c>; the real ACCEPT-FROM-TIME hundredths
/// are ignored (the 26-char stamp is always <c>YYYY-MM-DD HH:MM:SS.00</c>).</item>
/// <item>Every record carries branch id <c>'0001'</c> and region code <c>'NORTH'</c> regardless of the
/// source data — never derived.</item>
/// <item><c>9999-ABEND-PROGRAM</c> is <c>CALL 'CEE3ABD'</c> with no arguments — modeled as an immediate
/// abnormal termination (<see cref="AbendException"/>) after printing the ABEND banner.</item>
/// <item>No status check on any CLOSE — a close failure is silently ignored.</item>
/// <item>Per-type FILLER (and any payload byte the active type does not touch) is left at INITIALIZE's
/// space fill over the X(460) base view; the active type's COMP/COMP-3/zoned numeric subfields are then
/// overlaid. Reproduced by space-filling the 460-byte payload then overlaying the mapped fields.</item>
/// <item>The misspelled name <c>EXPIRAION-DATE</c> is carried through (column + export field name).</item>
/// <item>EOF ends a phase only on cursor exhaustion (file status <c>'10'</c>).</item>
/// <item>The sequence counter is incremented <em>before</em> each WRITE, so the first record's key is 1;
/// no record ever has sequence number 0.</item>
/// </list></para>
/// </remarks>
public sealed class Cbexport
{
    private readonly CustomerRepository _customer;
    private readonly AccountRepository _account;
    private readonly CardXrefRepository _xref;
    private readonly TransactionRepository _transaction;
    private readonly CardRepository _card;

    private readonly FixedFileWriter _export;
    private readonly RecordLayout _customerVariant;
    private readonly RecordLayout _accountVariant;
    private readonly RecordLayout _xrefVariant;
    private readonly RecordLayout _transactionVariant;
    private readonly RecordLayout _cardVariant;
    private readonly HostKind _host;

    private readonly List<string> _sysout = [];

    // WS-EXPORT-CONTROL (CBEXPORT.cbl:119-123).
    private readonly string _exportDate;          // WS-EXPORT-DATE  X(10)  YYYY-MM-DD
    private readonly string _exportTime;          // WS-EXPORT-TIME  X(08)  HH:MM:SS
    private readonly string _formattedTimestamp;  // WS-FORMATTED-TIMESTAMP X(26)
    private long _sequenceCounter;                // WS-SEQUENCE-COUNTER 9(09) VALUE 0

    // WS-EXPORT-STATISTICS (CBEXPORT.cbl:138-144) — six 9(09) counters.
    private long _customerRecordsExported;
    private long _accountRecordsExported;
    private long _xrefRecordsExported;
    private long _tranRecordsExported;
    private long _cardRecordsExported;
    private long _totalRecordsExported;

    private const int RecordLength = 500;

    private Cbexport(
        CustomerRepository customer, AccountRepository account, CardXrefRepository xref,
        TransactionRepository transaction, CardRepository card,
        FixedFileWriter export,
        RecordLayout customerVariant, RecordLayout accountVariant, RecordLayout xrefVariant,
        RecordLayout transactionVariant, RecordLayout cardVariant,
        IClock clock, HostKind host)
    {
        _customer = customer;
        _account = account;
        _xref = xref;
        _transaction = transaction;
        _card = card;
        _export = export;
        _customerVariant = customerVariant;
        _accountVariant = accountVariant;
        _xrefVariant = xrefVariant;
        _transactionVariant = transactionVariant;
        _cardVariant = cardVariant;
        _host = host;

        // 1050-GENERATE-TIMESTAMP is performed once at init; capture the run timestamp here so every
        // record in the run reuses the same value. (Hundredths are the hard-coded literal '.00' — bug #1.)
        DateTime now = clock.Now;
        _exportDate = $"{now.Year:D4}-{now.Month:D2}-{now.Day:D2}";        // YYYY-MM-DD
        _exportTime = $"{now.Hour:D2}:{now.Minute:D2}:{now.Second:D2}";    // HH:MM:SS
        _formattedTimestamp = $"{_exportDate} {_exportTime}.00";           // 26 chars
    }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>Customers exported (WS-CUSTOMER-RECORDS-EXPORTED).</summary>
    public long CustomerRecordsExported => _customerRecordsExported;

    /// <summary>Accounts exported (WS-ACCOUNT-RECORDS-EXPORTED).</summary>
    public long AccountRecordsExported => _accountRecordsExported;

    /// <summary>Cross-references exported (WS-XREF-RECORDS-EXPORTED).</summary>
    public long XrefRecordsExported => _xrefRecordsExported;

    /// <summary>Transactions exported (WS-TRAN-RECORDS-EXPORTED).</summary>
    public long TranRecordsExported => _tranRecordsExported;

    /// <summary>Cards exported (WS-CARD-RECORDS-EXPORTED).</summary>
    public long CardRecordsExported => _cardRecordsExported;

    /// <summary>Grand total exported across all five types (WS-TOTAL-RECORDS-EXPORTED).</summary>
    public long TotalRecordsExported => _totalRecordsExported;

    // =================================================================================================
    // Run factories
    // =================================================================================================

    /// <summary>
    /// Runs CBEXPORT over the five relational masters and writes the multi-record EXPORT dataset to
    /// <paramref name="exportPath"/> (OPEN OUTPUT semantics: any existing file is deleted first). The five
    /// CVEXPORT record-type variant layouts are parsed once from the copybook in
    /// <paramref name="copybookDirectory"/>. Returns the SYSOUT (DISPLAY) lines in order.
    /// </summary>
    /// <param name="support">Owns the open database; supplies the five source repositories.</param>
    /// <param name="exportPath">EXPFILE dataset path (RECFM=F, LRECL 500).</param>
    /// <param name="copybookDirectory">Directory holding <c>CVEXPORT.cpy</c>.</param>
    /// <param name="clock">Run clock for the shared 26-char export timestamp.</param>
    /// <param name="host">Host encoding for the export record image (defaults to EBCDIC, the mainframe form).</param>
    public static IReadOnlyList<string> Run(
        BatchSupport support,
        string exportPath,
        string copybookDirectory,
        IClock clock,
        HostKind host = HostKind.Ebcdic)
    {
        ArgumentNullException.ThrowIfNull(support);
        return Run(
            support.Customer, support.Account, support.CardXref, support.Transaction, support.Card,
            exportPath, copybookDirectory, clock, host);
    }

    /// <summary>
    /// Runs CBEXPORT over the five supplied repositories and writes the EXPORT dataset to
    /// <paramref name="exportPath"/>. The CVEXPORT variant layouts are parsed from
    /// <paramref name="copybookDirectory"/>; the export writer is opened with OPEN OUTPUT semantics
    /// (existing file deleted first). Returns the SYSOUT lines in order.
    /// </summary>
    public static IReadOnlyList<string> Run(
        CustomerRepository customer, AccountRepository account, CardXrefRepository xref,
        TransactionRepository transaction, CardRepository card,
        string exportPath,
        string copybookDirectory,
        IClock clock,
        HostKind host = HostKind.Ebcdic)
    {
        ExportVariants variants = ExportVariants.FromCopybookDirectory(copybookDirectory);

        // OPEN OUTPUT EXPORT-OUTPUT — DISP=NEW / re-DEFINEd cluster: start from an empty dataset. The
        // FixedFileWriter opens in append mode, so an OPEN OUTPUT caller deletes the file first. A failure
        // to (re)create the dataset maps to 1100-OPEN-FILES' "Cannot open EXPORT-OUTPUT" -> ABEND.
        FixedFileWriter writer;
        try
        {
            string? dir = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(exportPath)) File.Delete(exportPath);
            writer = BatchSupport.OpenWriter(exportPath, host);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 1100-OPEN-FILES: IF NOT WS-EXPORT-OK -> DISPLAY error + 9999-ABEND-PROGRAM.
            // source: CBEXPORT.cbl:235-240
            throw new AbendException("999", $"CBEXPORT: Cannot open EXPORT-OUTPUT ({e.Message}).");
        }

        using (writer)
        {
            var program = new Cbexport(
                customer, account, xref, transaction, card, writer,
                variants.Customer, variants.Account, variants.Xref, variants.Transaction, variants.Card,
                clock, host);
            program.MainProcessing0000();
            return program.Sysout;
        }
    }

    // =================================================================================================
    // 0000-MAIN-PROCESSING // source: CBEXPORT.cbl:149-158
    // =================================================================================================
    private void MainProcessing0000()
    {
        Initialize1000();          // source: CBEXPORT.cbl:151
        ExportCustomers2000();     // source: CBEXPORT.cbl:152
        ExportAccounts3000();      // source: CBEXPORT.cbl:153
        ExportXrefs4000();         // source: CBEXPORT.cbl:154
        ExportTransactions5000();  // source: CBEXPORT.cbl:155
        ExportCards5500();         // source: CBEXPORT.cbl:156
        Finalize6000();            // source: CBEXPORT.cbl:157
        // GOBACK // source: CBEXPORT.cbl:158
    }

    // =================================================================================================
    // 1000-INITIALIZE // source: CBEXPORT.cbl:161-169
    // =================================================================================================
    private void Initialize1000()
    {
        _sysout.Add("CBEXPORT: Starting Customer Data Export");          // source: CBEXPORT.cbl:163

        // 1050-GENERATE-TIMESTAMP already ran in the constructor (one capture per run). // source: CBEXPORT.cbl:165
        OpenFiles1100();                                                 // source: CBEXPORT.cbl:166

        _sysout.Add("CBEXPORT: Export Date: " + _exportDate);           // source: CBEXPORT.cbl:168
        _sysout.Add("CBEXPORT: Export Time: " + _exportTime);          // source: CBEXPORT.cbl:169
    }

    // =================================================================================================
    // 1100-OPEN-FILES // source: CBEXPORT.cbl:198-240
    // =================================================================================================
    // The five inputs are relational tables opened as forward cursors (StartBrowse) at the head of each
    // phase; the export output writer is already open (created in Run with OPEN OUTPUT semantics). No input
    // open can fail here, so no per-file open ABEND fires (the abend wiring lives at the cursor/write
    // sites). // source: CBEXPORT.cbl:200-240
    private static void OpenFiles1100()
    {
    }

    // =================================================================================================
    // 2000-EXPORT-CUSTOMERS // source: CBEXPORT.cbl:243-255
    // =================================================================================================
    private void ExportCustomers2000()
    {
        _sysout.Add("CBEXPORT: Processing customer records");           // source: CBEXPORT.cbl:245

        _customer.StartBrowse();
        string status = ReadCustomerRecord2100(out Customer? cust);     // priming read // source: CBEXPORT.cbl:247

        while (status == FileStatus.Ok)                                 // PERFORM UNTIL WS-CUSTOMER-EOF // source: CBEXPORT.cbl:249
        {
            CreateCustomerExpRec2200(cust!);                            // source: CBEXPORT.cbl:250
            status = ReadCustomerRecord2100(out cust);                  // source: CBEXPORT.cbl:251
        }
        _customer.EndBrowse();

        // source: CBEXPORT.cbl:254-255
        _sysout.Add("CBEXPORT: Customers exported: " + Count9(_customerRecordsExported));
    }

    // 2100-READ-CUSTOMER-RECORD // source: CBEXPORT.cbl:258-266
    private string ReadCustomerRecord2100(out Customer? cust)
    {
        string status = _customer.ReadNext(out cust);
        // IF NOT WS-CUSTOMER-OK AND NOT WS-CUSTOMER-EOF -> error + ABEND. Cursor returns only '00'/'10'.
        if (status != FileStatus.Ok && status != FileStatus.EndOfFile) // source: CBEXPORT.cbl:262
        {
            _sysout.Add("ERROR: Reading CUSTOMER-INPUT, Status: " + status); // source: CBEXPORT.cbl:263-264
            AbendProgram9999();                                        // source: CBEXPORT.cbl:265
        }
        return status;
    }

    // 2200-CREATE-CUSTOMER-EXP-REC // source: CBEXPORT.cbl:269-310
    private void CreateCustomerExpRec2200(Customer c)
    {
        FixedRecord exp = NewExportRecord(_customerVariant, "C");        // INITIALIZE + header // source: CBEXPORT.cbl:271-279

        // Map customer fields to export record. // source: CBEXPORT.cbl:282-299
        exp.SetNumber("EXP-CUST-ID", c.CustId);
        exp.SetText("EXP-CUST-FIRST-NAME", c.FirstName);
        exp.SetText("EXP-CUST-MIDDLE-NAME", c.MiddleName);
        exp.SetText("EXP-CUST-LAST-NAME", c.LastName);
        exp.SetText("EXP-CUST-ADDR-LINE_1", c.AddrLine1);               // OCCURS(1) // source: CBEXPORT.cbl:286
        exp.SetText("EXP-CUST-ADDR-LINE_2", c.AddrLine2);               // OCCURS(2) // source: CBEXPORT.cbl:287
        exp.SetText("EXP-CUST-ADDR-LINE_3", c.AddrLine3);               // OCCURS(3) // source: CBEXPORT.cbl:288
        exp.SetText("EXP-CUST-ADDR-STATE-CD", c.AddrStateCd);
        exp.SetText("EXP-CUST-ADDR-COUNTRY-CD", c.AddrCountryCd);
        exp.SetText("EXP-CUST-ADDR-ZIP", c.AddrZip);
        exp.SetText("EXP-CUST-PHONE-NUM_1", c.PhoneNum1);               // OCCURS(1) // source: CBEXPORT.cbl:292
        exp.SetText("EXP-CUST-PHONE-NUM_2", c.PhoneNum2);               // OCCURS(2) // source: CBEXPORT.cbl:293
        exp.SetNumber("EXP-CUST-SSN", c.Ssn);
        exp.SetText("EXP-CUST-GOVT-ISSUED-ID", c.GovtIssuedId);
        exp.SetText("EXP-CUST-DOB-YYYY-MM-DD", c.DobYyyyMmDd);
        exp.SetText("EXP-CUST-EFT-ACCOUNT-ID", c.EftAccountId);
        exp.SetText("EXP-CUST-PRI-CARD-HOLDER-IND", c.PriCardHolderInd);
        exp.SetNumber("EXP-CUST-FICO-CREDIT-SCORE", c.FicoCreditScore);

        WriteExportRecord(exp);                                         // source: CBEXPORT.cbl:301-307

        _customerRecordsExported++;                                     // source: CBEXPORT.cbl:309
        _totalRecordsExported++;                                        // source: CBEXPORT.cbl:310
    }

    // =================================================================================================
    // 3000-EXPORT-ACCOUNTS // source: CBEXPORT.cbl:312-324
    // =================================================================================================
    private void ExportAccounts3000()
    {
        _sysout.Add("CBEXPORT: Processing account records");            // source: CBEXPORT.cbl:314

        _account.StartBrowse();
        string status = ReadAccountRecord3100(out Account? acct);       // source: CBEXPORT.cbl:316

        while (status == FileStatus.Ok)                                 // source: CBEXPORT.cbl:318
        {
            CreateAccountExpRec3200(acct!);                            // source: CBEXPORT.cbl:319
            status = ReadAccountRecord3100(out acct);                  // source: CBEXPORT.cbl:320
        }
        _account.EndBrowse();

        // source: CBEXPORT.cbl:323-324
        _sysout.Add("CBEXPORT: Accounts exported: " + Count9(_accountRecordsExported));
    }

    // 3100-READ-ACCOUNT-RECORD // source: CBEXPORT.cbl:327-335
    private string ReadAccountRecord3100(out Account? acct)
    {
        string status = _account.ReadNext(out acct);
        if (status != FileStatus.Ok && status != FileStatus.EndOfFile) // source: CBEXPORT.cbl:331
        {
            _sysout.Add("ERROR: Reading ACCOUNT-INPUT, Status: " + status); // source: CBEXPORT.cbl:332-333
            AbendProgram9999();                                        // source: CBEXPORT.cbl:334
        }
        return status;
    }

    // 3200-CREATE-ACCOUNT-EXP-REC // source: CBEXPORT.cbl:338-373
    private void CreateAccountExpRec3200(Account a)
    {
        FixedRecord exp = NewExportRecord(_accountVariant, "A");        // INITIALIZE + header // source: CBEXPORT.cbl:340-348

        // Map account fields to export record. // source: CBEXPORT.cbl:351-362
        exp.SetNumber("EXP-ACCT-ID", a.AcctId);
        exp.SetText("EXP-ACCT-ACTIVE-STATUS", a.ActiveStatus);
        exp.SetNumber("EXP-ACCT-CURR-BAL", a.CurrBal);
        exp.SetNumber("EXP-ACCT-CREDIT-LIMIT", a.CreditLimit);
        exp.SetNumber("EXP-ACCT-CASH-CREDIT-LIMIT", a.CashCreditLimit);
        exp.SetText("EXP-ACCT-OPEN-DATE", a.OpenDate);
        exp.SetText("EXP-ACCT-EXPIRAION-DATE", a.ExpirationDate);       // misspelled name carried through (bug #6)
        exp.SetText("EXP-ACCT-REISSUE-DATE", a.ReissueDate);
        exp.SetNumber("EXP-ACCT-CURR-CYC-CREDIT", a.CurrCycCredit);
        exp.SetNumber("EXP-ACCT-CURR-CYC-DEBIT", a.CurrCycDebit);
        exp.SetText("EXP-ACCT-ADDR-ZIP", a.AddrZip);
        exp.SetText("EXP-ACCT-GROUP-ID", a.GroupId);

        WriteExportRecord(exp);                                         // source: CBEXPORT.cbl:364-370

        _accountRecordsExported++;                                      // source: CBEXPORT.cbl:372
        _totalRecordsExported++;                                        // source: CBEXPORT.cbl:373
    }

    // =================================================================================================
    // 4000-EXPORT-XREFS // source: CBEXPORT.cbl:376-388
    // =================================================================================================
    private void ExportXrefs4000()
    {
        _sysout.Add("CBEXPORT: Processing cross-reference records");    // source: CBEXPORT.cbl:378

        _xref.StartBrowse();
        string status = ReadXrefRecord4100(out CardXref? xref);         // source: CBEXPORT.cbl:380

        while (status == FileStatus.Ok)                                 // source: CBEXPORT.cbl:382
        {
            CreateXrefExportRecord4200(xref!);                         // source: CBEXPORT.cbl:383
            status = ReadXrefRecord4100(out xref);                     // source: CBEXPORT.cbl:384
        }
        _xref.EndBrowse();

        // source: CBEXPORT.cbl:387-388
        _sysout.Add("CBEXPORT: Cross-references exported: " + Count9(_xrefRecordsExported));
    }

    // 4100-READ-XREF-RECORD // source: CBEXPORT.cbl:391-399
    private string ReadXrefRecord4100(out CardXref? xref)
    {
        string status = _xref.ReadNext(out xref);
        if (status != FileStatus.Ok && status != FileStatus.EndOfFile) // source: CBEXPORT.cbl:395
        {
            _sysout.Add("ERROR: Reading XREF-INPUT, Status: " + status); // source: CBEXPORT.cbl:396-397
            AbendProgram9999();                                        // source: CBEXPORT.cbl:398
        }
        return status;
    }

    // 4200-CREATE-XREF-EXPORT-RECORD // source: CBEXPORT.cbl:402-428
    private void CreateXrefExportRecord4200(CardXref x)
    {
        FixedRecord exp = NewExportRecord(_xrefVariant, "X");           // INITIALIZE + header // source: CBEXPORT.cbl:404-412

        // Map xref fields to export record. // source: CBEXPORT.cbl:415-417
        exp.SetText("EXP-XREF-CARD-NUM", x.XrefCardNum);
        exp.SetNumber("EXP-XREF-CUST-ID", x.CustId);
        exp.SetNumber("EXP-XREF-ACCT-ID", x.AcctId);

        WriteExportRecord(exp);                                         // source: CBEXPORT.cbl:419-425

        _xrefRecordsExported++;                                         // source: CBEXPORT.cbl:427
        _totalRecordsExported++;                                        // source: CBEXPORT.cbl:428
    }

    // =================================================================================================
    // 5000-EXPORT-TRANSACTIONS // source: CBEXPORT.cbl:431-443
    // =================================================================================================
    private void ExportTransactions5000()
    {
        _sysout.Add("CBEXPORT: Processing transaction records");        // source: CBEXPORT.cbl:433

        _transaction.StartBrowse();
        string status = ReadTransactionRecord5100(out Transaction? tran); // source: CBEXPORT.cbl:435

        while (status == FileStatus.Ok)                                 // source: CBEXPORT.cbl:437
        {
            CreateTranExpRec5200(tran!);                               // source: CBEXPORT.cbl:438
            status = ReadTransactionRecord5100(out tran);             // source: CBEXPORT.cbl:439
        }
        _transaction.EndBrowse();

        // source: CBEXPORT.cbl:442-443
        _sysout.Add("CBEXPORT: Transactions exported: " + Count9(_tranRecordsExported));
    }

    // 5100-READ-TRANSACTION-RECORD // source: CBEXPORT.cbl:446-454
    private string ReadTransactionRecord5100(out Transaction? tran)
    {
        string status = _transaction.ReadNext(out tran);
        if (status != FileStatus.Ok && status != FileStatus.EndOfFile) // source: CBEXPORT.cbl:450
        {
            _sysout.Add("ERROR: Reading TRANSACTION-INPUT, Status: " + status); // source: CBEXPORT.cbl:451-452
            AbendProgram9999();                                        // source: CBEXPORT.cbl:453
        }
        return status;
    }

    // 5200-CREATE-TRAN-EXP-REC // source: CBEXPORT.cbl:457-493
    private void CreateTranExpRec5200(Transaction t)
    {
        FixedRecord exp = NewExportRecord(_transactionVariant, "T");    // INITIALIZE + header // source: CBEXPORT.cbl:459-467

        // Map transaction fields to export record. // source: CBEXPORT.cbl:470-482
        exp.SetText("EXP-TRAN-ID", t.TranId);
        exp.SetText("EXP-TRAN-TYPE-CD", t.TypeCd);
        exp.SetNumber("EXP-TRAN-CAT-CD", t.CatCd);
        exp.SetText("EXP-TRAN-SOURCE", t.Source);
        exp.SetText("EXP-TRAN-DESC", t.Desc);
        exp.SetNumber("EXP-TRAN-AMT", t.Amt);
        exp.SetNumber("EXP-TRAN-MERCHANT-ID", t.MerchantId);
        exp.SetText("EXP-TRAN-MERCHANT-NAME", t.MerchantName);
        exp.SetText("EXP-TRAN-MERCHANT-CITY", t.MerchantCity);
        exp.SetText("EXP-TRAN-MERCHANT-ZIP", t.MerchantZip);
        exp.SetText("EXP-TRAN-CARD-NUM", t.CardNum);
        exp.SetText("EXP-TRAN-ORIG-TS", t.OrigTs);
        exp.SetText("EXP-TRAN-PROC-TS", t.ProcTs);

        WriteExportRecord(exp);                                         // source: CBEXPORT.cbl:484-490

        _tranRecordsExported++;                                         // source: CBEXPORT.cbl:492
        _totalRecordsExported++;                                        // source: CBEXPORT.cbl:493
    }

    // =================================================================================================
    // 5500-EXPORT-CARDS // source: CBEXPORT.cbl:496-508
    // =================================================================================================
    private void ExportCards5500()
    {
        _sysout.Add("CBEXPORT: Processing card records");               // source: CBEXPORT.cbl:498

        _card.StartBrowse();
        string status = ReadCardRecord5600(out Card? card);             // source: CBEXPORT.cbl:500

        while (status == FileStatus.Ok)                                 // source: CBEXPORT.cbl:502
        {
            CreateCardExportRecord5700(card!);                        // source: CBEXPORT.cbl:503
            status = ReadCardRecord5600(out card);                    // source: CBEXPORT.cbl:504
        }
        _card.EndBrowse();

        // source: CBEXPORT.cbl:507-508
        _sysout.Add("CBEXPORT: Cards exported: " + Count9(_cardRecordsExported));
    }

    // 5600-READ-CARD-RECORD // source: CBEXPORT.cbl:511-519
    private string ReadCardRecord5600(out Card? card)
    {
        string status = _card.ReadNext(out card);
        if (status != FileStatus.Ok && status != FileStatus.EndOfFile) // source: CBEXPORT.cbl:515
        {
            _sysout.Add("ERROR: Reading CARD-INPUT, Status: " + status); // source: CBEXPORT.cbl:516-517
            AbendProgram9999();                                        // source: CBEXPORT.cbl:518
        }
        return status;
    }

    // 5700-CREATE-CARD-EXPORT-RECORD // source: CBEXPORT.cbl:522-551
    private void CreateCardExportRecord5700(Card d)
    {
        FixedRecord exp = NewExportRecord(_cardVariant, "D");           // INITIALIZE + header // source: CBEXPORT.cbl:524-532

        // Map card fields to export record. // source: CBEXPORT.cbl:535-540
        exp.SetText("EXP-CARD-NUM", d.CardNum);
        exp.SetNumber("EXP-CARD-ACCT-ID", d.AcctId);
        exp.SetNumber("EXP-CARD-CVV-CD", d.CvvCd);
        exp.SetText("EXP-CARD-EMBOSSED-NAME", d.EmbossedName);
        exp.SetText("EXP-CARD-EXPIRAION-DATE", d.ExpirationDate);       // misspelled name carried through (bug #6)
        exp.SetText("EXP-CARD-ACTIVE-STATUS", d.ActiveStatus);

        WriteExportRecord(exp);                                         // source: CBEXPORT.cbl:542-548

        _cardRecordsExported++;                                         // source: CBEXPORT.cbl:550
        _totalRecordsExported++;                                        // source: CBEXPORT.cbl:551
    }

    // =================================================================================================
    // 6000-FINALIZE // source: CBEXPORT.cbl:554-573
    // =================================================================================================
    private void Finalize6000()
    {
        // CLOSE all six files (CUSTOMER, ACCOUNT, XREF, TRANSACTION, CARD, EXPORT) — no status checks on
        // CLOSE (bug #4). The input cursors are already ended; the export writer is closed by the caller's
        // using-block at the end of Run. // source: CBEXPORT.cbl:556-561

        _sysout.Add("CBEXPORT: Export completed");                                   // source: CBEXPORT.cbl:563
        _sysout.Add("CBEXPORT: Customers Exported: " + Count9(_customerRecordsExported)); // source: CBEXPORT.cbl:564-565
        _sysout.Add("CBEXPORT: Accounts Exported: " + Count9(_accountRecordsExported));   // source: CBEXPORT.cbl:566-567
        _sysout.Add("CBEXPORT: XRefs Exported: " + Count9(_xrefRecordsExported));         // source: CBEXPORT.cbl:568
        _sysout.Add("CBEXPORT: Transactions Exported: " + Count9(_tranRecordsExported));  // source: CBEXPORT.cbl:569-570
        _sysout.Add("CBEXPORT: Cards Exported: " + Count9(_cardRecordsExported));         // source: CBEXPORT.cbl:571
        _sysout.Add("CBEXPORT: Total Records Exported: " + Count9(_totalRecordsExported)); // source: CBEXPORT.cbl:572-573
    }

    // =================================================================================================
    // 9999-ABEND-PROGRAM // source: CBEXPORT.cbl:576-579
    // =================================================================================================
    private void AbendProgram9999()
    {
        _sysout.Add("CBEXPORT: ABENDING PROGRAM");                      // source: CBEXPORT.cbl:578
        // CALL 'CEE3ABD' (no arguments) -> immediate abnormal termination (bug #3). // source: CBEXPORT.cbl:579
        throw new AbendException("999", "CBEXPORT abend (CEE3ABD).");
    }

    // =================================================================================================
    // Shared helpers
    // =================================================================================================

    /// <summary>
    /// INITIALIZE EXPORT-RECORD + populate the common 40-byte header (REC-TYPE, the shared 26-char
    /// timestamp, the pre-incremented global sequence number, branch '0001' and region 'NORTH'). The
    /// variant layout space-fills the X(460) base view (so unused payload bytes and FILLER stay spaces —
    /// bug #5); the caller then overlays the active type's payload fields.
    /// </summary>
    private FixedRecord NewExportRecord(RecordLayout variant, string recType)
    {
        // INITIALIZE EXPORT-RECORD — alphanumeric (and the X(460) base view) to spaces, numeric to zero.
        FixedRecord exp = FixedRecord.CreateInitialized(variant, _host); // source: CBEXPORT.cbl:271,340,404,459,524

        exp.SetText("EXPORT-REC-TYPE", recType);                        // source: CBEXPORT.cbl:274,343,407,462,527
        exp.SetText("EXPORT-TIMESTAMP", _formattedTimestamp);           // source: CBEXPORT.cbl:275,344,408,463,528
        _sequenceCounter++;                                             // ADD 1 TO WS-SEQUENCE-COUNTER (before WRITE — bug #8) // source: CBEXPORT.cbl:276,345,409,464,529
        exp.SetNumber("EXPORT-SEQUENCE-NUM", _sequenceCounter);        // 9(9) COMP fullword // source: CBEXPORT.cbl:277,346,410,465,530
        exp.SetText("EXPORT-BRANCH-ID", "0001");                       // hard-coded (bug #2) // source: CBEXPORT.cbl:278,347,411,466,531
        exp.SetText("EXPORT-REGION-CODE", "NORTH");                    // hard-coded (bug #2) // source: CBEXPORT.cbl:279,348,412,467,532
        return exp;
    }

    /// <summary>
    /// WRITE EXPORT-OUTPUT-RECORD FROM EXPORT-RECORD: serialize the 500-byte image and append it to the
    /// export dataset. An I/O failure maps to <c>IF NOT WS-EXPORT-OK -&gt; error + 9999-ABEND-PROGRAM</c>.
    /// </summary>
    private void WriteExportRecord(FixedRecord exp)
    {
        try
        {
            _export.WriteRecord(exp.ToBytes(_host), RecordLength);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            // source: CBEXPORT.cbl:303-307, 366-370, 421-425, 486-490, 544-548
            _sysout.Add("ERROR: Writing export record, Status: " + FileStatus.PermanentError);
            AbendProgram9999();
        }
    }

    /// <summary>
    /// Formats a 9(09) DISPLAY counter the way COBOL DISPLAYs it: nine zero-padded digits (the on-screen
    /// image of a <c>PIC 9(09)</c> unsigned zoned field).
    /// </summary>
    private static string Count9(long value) => value.ToString("D9");

    /// <summary>
    /// The five CVEXPORT record-type variant layouts (each a full 500-byte <see cref="RecordLayout"/> with
    /// the common header + one active REDEFINES payload), parsed once from <c>CVEXPORT.cpy</c>.
    /// </summary>
    private sealed record ExportVariants(
        RecordLayout Customer, RecordLayout Account, RecordLayout Xref,
        RecordLayout Transaction, RecordLayout Card)
    {
        public static ExportVariants FromCopybookDirectory(string copybookDirectory)
        {
            string text = File.ReadAllText(Path.Combine(copybookDirectory, "CVEXPORT.cpy"));
            return new ExportVariants(
                CopybookParser.ParseVariant(text, "EXPORT-CUSTOMER-DATA"),
                CopybookParser.ParseVariant(text, "EXPORT-ACCOUNT-DATA"),
                CopybookParser.ParseVariant(text, "EXPORT-CARD-XREF-DATA"),
                CopybookParser.ParseVariant(text, "EXPORT-TRANSACTION-DATA"),
                CopybookParser.ParseVariant(text, "EXPORT-CARD-DATA"));
        }
    }
}
