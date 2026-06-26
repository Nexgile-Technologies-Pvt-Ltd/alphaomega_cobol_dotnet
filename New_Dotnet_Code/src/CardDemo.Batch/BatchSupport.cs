using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Import;

namespace CardDemo.Batch;

/// <summary>
/// Shared scaffolding for the relational re-port of the CardDemo batch jobs (the <c>CB*</c> programs).
/// A <see cref="BatchSupport"/> owns one open <see cref="RelationalDb"/> (in-memory or on-disk),
/// optionally seeds it from the EBCDIC masters via <see cref="MasterImporter"/>, and hands each program
/// its repositories without every job re-wiring the data layer. Flat (non-table) sequential I/O — the
/// QSAM datasets a job reads or writes that are not VSAM/relational masters — is handled by the
/// <see cref="ReadFixedRecords"/> reader and the <see cref="FixedFileWriter"/> appender, both of which
/// route field formatting through the <c>CardDemo.Cobol.Runtime</c> codecs so the bytes stay faithful.
/// </summary>
/// <remarks>
/// The repositories are created lazily on first access and cached, so a job touches only the tables it
/// needs. <see cref="Db"/> stays open for the lifetime of this instance; dispose it (or use a
/// <c>using</c>) to close the underlying connection — for an in-memory database that also discards the
/// data, so keep the instance alive for as long as the rows must live.
/// </remarks>
public sealed class BatchSupport : IDisposable
{
    private readonly bool _ownsDb;

    private AccountRepository? _account;
    private CardRepository? _card;
    private CardXrefRepository? _cardXref;
    private CustomerRepository? _customer;
    private TransactionRepository? _transaction;
    private DailyTransactionRepository? _dailyTransaction;
    private TranCatBalanceRepository? _tranCatBalance;
    private DisclosureGroupRepository? _disclosureGroup;
    private TranTypeRepository? _tranType;
    private TranCategoryRepository? _tranCategory;
    private UserSecurityRepository? _userSecurity;

    /// <summary>The open relational database backing every repository this instance exposes.</summary>
    public RelationalDb Db { get; }

    /// <summary>Wraps an existing, caller-owned <see cref="RelationalDb"/> (not disposed by this instance).</summary>
    public BatchSupport(RelationalDb db)
    {
        Db = db;
        _ownsDb = false;
    }

    private BatchSupport(RelationalDb db, bool ownsDb)
    {
        Db = db;
        _ownsDb = ownsDb;
    }

    /// <summary>
    /// Opens a fresh, empty database (private in-memory by default; pass a file connection string such as
    /// <c>"Data Source=carddemo.db"</c> for a durable one) with the full schema created. The returned
    /// instance owns the database and closes it on <see cref="Dispose"/>.
    /// </summary>
    public static BatchSupport Open(string connectionString = RelationalDb.InMemory)
        => new(new RelationalDb(connectionString), ownsDb: true);

    /// <summary>
    /// Opens a fresh database (see <see cref="Open"/>) and seeds every base master from the EBCDIC
    /// datasets in <paramref name="ebcdicDataDirectory"/> (decoded with the copybooks in
    /// <paramref name="copybookDirectory"/>) via <see cref="MasterImporter.ImportAll"/>. The per-table
    /// row counts are returned for logging/asserting; the returned instance owns the database.
    /// </summary>
    public static BatchSupport OpenSeeded(
        string ebcdicDataDirectory,
        string copybookDirectory,
        out ImportResult imported,
        string connectionString = RelationalDb.InMemory)
    {
        var support = Open(connectionString);
        try
        {
            imported = support.Seed(ebcdicDataDirectory, copybookDirectory);
            return support;
        }
        catch
        {
            support.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Seeds every base master into <see cref="Db"/> from the EBCDIC datasets in
    /// <paramref name="ebcdicDataDirectory"/>, decoded with the copybooks in
    /// <paramref name="copybookDirectory"/>. Equivalent to running the initial-load jobs; runs in a
    /// single transaction (see <see cref="MasterImporter.ImportAll"/>).
    /// </summary>
    public ImportResult Seed(string ebcdicDataDirectory, string copybookDirectory)
        => new MasterImporter(ebcdicDataDirectory, copybookDirectory).ImportAll(Db);

    // ---- Repositories (lazily created, cached) --------------------------------------------------------

    /// <summary>ACCOUNT master (copybook CVACT01Y).</summary>
    public AccountRepository Account => _account ??= new AccountRepository(Db);

    /// <summary>CARD master (copybook CVACT02Y).</summary>
    public CardRepository Card => _card ??= new CardRepository(Db);

    /// <summary>CARD_XREF cross-reference (copybook CVACT03Y).</summary>
    public CardXrefRepository CardXref => _cardXref ??= new CardXrefRepository(Db);

    /// <summary>CUSTOMER master (copybook CVCUS01Y).</summary>
    public CustomerRepository Customer => _customer ??= new CustomerRepository(Db);

    /// <summary>TRANSACTION master (copybook CVTRA05Y); built by the posting job.</summary>
    public TransactionRepository Transaction => _transaction ??= new TransactionRepository(Db);

    /// <summary>DAILY_TRANSACTION input (copybook CVTRA06Y).</summary>
    public DailyTransactionRepository DailyTransaction => _dailyTransaction ??= new DailyTransactionRepository(Db);

    /// <summary>TRAN_CAT_BAL category balances (copybook CVTRA01Y).</summary>
    public TranCatBalanceRepository TranCatBalance => _tranCatBalance ??= new TranCatBalanceRepository(Db);

    /// <summary>DISCLOSURE_GROUP interest-rate disclosure (copybook CVTRA02Y).</summary>
    public DisclosureGroupRepository DisclosureGroup => _disclosureGroup ??= new DisclosureGroupRepository(Db);

    /// <summary>TRAN_TYPE reference (copybook CVTRA03Y).</summary>
    public TranTypeRepository TranType => _tranType ??= new TranTypeRepository(Db);

    /// <summary>TRAN_CATEGORY reference (copybook CVTRA04Y).</summary>
    public TranCategoryRepository TranCategory => _tranCategory ??= new TranCategoryRepository(Db);

    /// <summary>USER_SECURITY sign-on master (copybook CSUSR01Y).</summary>
    public UserSecurityRepository UserSecurity => _userSecurity ??= new UserSecurityRepository(Db);

    // ---- Flat sequential-file helpers (non-table QSAM batch I/O) --------------------------------------

    /// <summary>
    /// Reads a fixed-length-record sequential file from disk and returns each record as its own byte
    /// image (length <paramref name="recordLength"/>), in file order. This is the raw-bytes counterpart
    /// to <see cref="FixedRecord.Parse"/>: decode a returned image with a <see cref="RecordLayout"/> when
    /// the job needs typed fields, or compare it byte-for-byte for characterization.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file length is not an exact multiple of <paramref name="recordLength"/>.
    /// </exception>
    public static IReadOnlyList<byte[]> ReadFixedRecords(string path, int recordLength)
    {
        if (recordLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(recordLength), recordLength, "Record length must be positive.");

        byte[] data = File.ReadAllBytes(path);
        if (data.Length % recordLength != 0)
            throw new InvalidDataException(
                $"{path}: file length {data.Length} is not a multiple of record length {recordLength}.");

        int count = data.Length / recordLength;
        var records = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            var image = new byte[recordLength];
            Array.Copy(data, i * recordLength, image, 0, recordLength);
            records[i] = image;
        }
        return records;
    }

    /// <summary>
    /// Opens a <see cref="FixedFileWriter"/> that appends to (or creates) <paramref name="path"/> in the
    /// given <paramref name="host"/> encoding. Use it for any job output dataset that is a flat file
    /// rather than a relational master (e.g. a reject file, a print/report line file, an export image).
    /// </summary>
    public static FixedFileWriter OpenWriter(string path, HostKind host = HostKind.Ebcdic)
        => new(path, host);

    public void Dispose()
    {
        if (_ownsDb) Db.Dispose();
    }
}

/// <summary>
/// Appends fixed-width record images and host-encoded text lines to a flat output dataset — the writer
/// side of a job's non-table QSAM I/O. Field formatting goes through the <c>CardDemo.Cobol.Runtime</c>
/// codecs so numbers land in the dataset exactly as the original program would have written them.
/// </summary>
/// <remarks>
/// The writer opens the file in append mode (matching COBOL <c>OPEN EXTEND</c>; an
/// <c>OPEN OUTPUT</c>/DISP=NEW caller should delete the file first). It is line-oriented only when you
/// call <see cref="WriteLine"/> — fixed-width images are written with no separator, contiguous, which is
/// how mainframe RECFM=F datasets are laid out.
/// </remarks>
public sealed class FixedFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly HostKind _host;

    internal FixedFileWriter(string path, HostKind host)
    {
        _host = host;
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    /// <summary>The host encoding fields/text are written in.</summary>
    public HostKind Host => _host;

    /// <summary>
    /// Appends a complete fixed-width record image of length <paramref name="recordLength"/> (when given)
    /// to the file with no separator. Pass <paramref name="recordLength"/> to assert the image is exactly
    /// the expected RECFM=F record size.
    /// </summary>
    public void WriteRecord(ReadOnlySpan<byte> image, int recordLength = -1)
    {
        if (recordLength >= 0 && image.Length != recordLength)
            throw new ArgumentException(
                $"Record image length {image.Length} != expected record length {recordLength}.", nameof(image));
        _stream.Write(image);
    }

    /// <summary>Appends a built <see cref="FixedRecord"/> re-serialized into this writer's host encoding.</summary>
    public void WriteRecord(FixedRecord record) => _stream.Write(record.ToBytes(_host));

    /// <summary>
    /// Encodes <paramref name="text"/> in the writer's host code page (no newline) and appends it. Use it
    /// for the body of a fixed-width text line; follow with <see cref="WriteNewLine"/> if the dataset is
    /// line-delimited rather than RECFM=F.
    /// </summary>
    public void WriteText(string text) => _stream.Write(HostEncoding.For(_host).GetBytes(text));

    /// <summary>Appends <paramref name="text"/> in the host encoding followed by a host newline (0x25 EBCDIC LF / 0x0A ASCII).</summary>
    public void WriteLine(string text)
    {
        WriteText(text);
        WriteNewLine();
    }

    /// <summary>Appends a single host-appropriate newline byte (EBCDIC LF 0x25, otherwise ASCII LF 0x0A).</summary>
    public void WriteNewLine() => _stream.WriteByte(_host == HostKind.Ebcdic ? (byte)0x25 : (byte)0x0A);

    /// <summary>
    /// Formats <paramref name="value"/> as a fixed-width host-encoded zoned-decimal (USAGE DISPLAY) field
    /// of <paramref name="totalDigits"/> digits and <paramref name="scale"/> fractional digits — the same
    /// codec the record serializer uses — and appends those bytes (no separator). Reproduces COBOL
    /// truncate-toward-zero and silent high-order overflow.
    /// </summary>
    public void WriteNumeric(decimal value, int totalDigits, int scale, bool signed)
    {
        var field = new byte[totalDigits];
        ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
        _stream.Write(field);
    }

    /// <summary>
    /// Formats <paramref name="text"/> as a fixed-width PIC X(<paramref name="width"/>) field
    /// (left-justified, space-padded, right-truncated), host-encodes it, and appends it (no separator).
    /// </summary>
    public void WriteAlpha(string text, int width)
    {
        string padded = text.Length >= width ? text[..width] : text.PadRight(width, ' ');
        WriteText(padded);
    }

    /// <summary>Flushes buffered bytes to disk.</summary>
    public void Flush() => _stream.Flush();

    public void Dispose() => _stream.Dispose();
}
