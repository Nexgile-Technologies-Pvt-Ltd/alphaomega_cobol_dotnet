using CardDemo.Batch;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Import;
using Microsoft.Data.Sqlite;

namespace CardDemo.JobControl;

/// <summary>
/// The z/OS utility-program primitives the CardDemo file-setup / posting-prep jobs use, re-expressed over
/// the relational data layer. Each method returns the RETURN-CODE the utility step would set, so the
/// <see cref="JobRunner"/> can gate later steps on it:
/// <list type="bullet">
///   <item><b>IDCAMS DELETE</b> a VSAM cluster -&gt; truncate the matching relational table; tolerant of
///         "not found" (mirrors <c>IF MAXCC LE 08 THEN SET MAXCC = 0</c>).</item>
///   <item><b>IDCAMS DEFINE</b> a cluster -&gt; ensure the (empty) table exists (the schema is created with
///         the database, so DEFINE is a verify + clear).</item>
///   <item><b>IDCAMS REPRO</b> from a <c>.PS</c> seed -&gt; bulk-load the table from its EBCDIC seed dataset
///         via <see cref="MasterImporter"/>; REPRO from a flat (combined) file -&gt; load rows from that file.</item>
///   <item><b>SORT</b> -&gt; order (and optionally filter / merge) a flat dataset on its control fields using
///         ordinal (EBCDIC/ASCII coincident) collation.</item>
///   <item><b>IEFBR14</b> -&gt; a no-op whose only effect is dataset disposition (delete-if-exists of the
///         flat outputs a step is about to recreate).</item>
/// </list>
/// </summary>
public static class UtilitySteps
{
    /// <summary>The maximum MAXCC an IDCAMS DELETE tolerates (entry-not-found is RC=8): <c>IF MAXCC LE 08</c>.</summary>
    public const int DeleteToleranceMaxCc = 8;

    // =================================================================================================
    // IDCAMS DELETE / DEFINE  (relational cluster lifecycle)
    // =================================================================================================

    /// <summary>
    /// IDCAMS <c>DELETE ... CLUSTER</c> for a base table: removes every row from <paramref name="table"/>.
    /// Always succeeds (a missing/empty table is the tolerated "entry not found" case), returning RC 0 to
    /// mirror the <c>IF MAXCC LE 08 THEN SET MAXCC = 0</c> normalization the JCL applies after a DELETE.
    /// </summary>
    public static int IdcamsDeleteCluster(JobContext ctx, string table)
    {
        DeleteAllRows(ctx.Db, table);
        return 0;
    }

    /// <summary>
    /// IDCAMS <c>DEFINE CLUSTER</c> for a base table: the relational schema is created with the database,
    /// so DEFINE verifies the table exists and (re)starts it empty, returning RC 0. A define against a
    /// table the schema does not know about is RC 12 (a genuine "cannot define" error).
    /// </summary>
    public static int IdcamsDefineCluster(JobContext ctx, string table)
    {
        if (!TableExists(ctx.Db, table))
            return 12;
        DeleteAllRows(ctx.Db, table); // a freshly DEFINEd cluster is empty.
        return 0;
    }

    /// <summary>
    /// IDCAMS <c>DELETE ... CLUSTER</c> for the GDG-backed transaction master (TRANBKP STEP05): clears the
    /// <c>TRANSACTION</c> table and drops the alternate-index concept (no separate store in the relational
    /// model). RC 0 (not-found tolerated).
    /// </summary>
    public static int IdcamsDeleteTransactionMaster(JobContext ctx)
    {
        DeleteAllRows(ctx.Db, "\"TRANSACTION\"");
        return 0;
    }

    // =================================================================================================
    // IDCAMS REPRO  (seed -> table, and flat file -> table)
    // =================================================================================================

    /// <summary>
    /// IDCAMS <c>REPRO INFILE(seed.PS) OUTFILE(cluster)</c> that loads a base table from its EBCDIC
    /// <c>.PS</c> seed dataset via <see cref="MasterImporter"/> (the file-setup jobs' load step). The
    /// caller passes the per-file importer action so each table loads from its own copybook + dataset.
    /// Returns RC 0 on a clean load, RC 12 if the seed directory / copybooks are not configured.
    /// </summary>
    public static int IdcamsReproSeed(JobContext ctx, Func<MasterImporter, RelationalDb, int> loadOne)
    {
        if (ctx.SeedDataDir is null || ctx.CopybookDir is null)
            return 12; // no seed configured -> the REPRO INFILE cannot be opened.

        var importer = new MasterImporter(ctx.SeedDataDir, ctx.CopybookDir);
        ctx.Db.InTransaction(_ => loadOne(importer, ctx.Db));
        return 0;
    }

    /// <summary>
    /// IDCAMS <c>REPRO</c> that bulk-loads the <c>TRANSACTION</c> master from a flat (combined/sorted)
    /// 350-byte dataset of <c>CVTRA05Y</c> images (COMBTRAN STEP10, TRANFILE STEP15-equivalent). Each
    /// record is decoded with the copybook layout and inserted keyed on <c>TRAN-ID</c>; a duplicate key is
    /// tolerated by overwriting (REPRO REPLACE semantics) so a rebuild does not fail. Returns RC 0, or RC
    /// 12 if the copybooks are not configured or the file length is not a record multiple.
    /// </summary>
    public static int IdcamsReproTransactionsFromFile(JobContext ctx, string flatPath, HostKind host = HostKind.Ebcdic)
    {
        if (ctx.CopybookDir is null) return 12;

        var layouts = new RecordLayouts(ctx.CopybookDir);
        RecordLayout layout = layouts.For(CardDemoFiles.Transaction.Copybook);
        byte[] data = File.ReadAllBytes(flatPath);
        if (data.Length % layout.Length != 0) return 12;

        var repo = new TransactionRepository(ctx.Db);
        int count = data.Length / layout.Length;
        ctx.Db.InTransaction(_ =>
        {
            for (int i = 0; i < count; i++)
            {
                FixedRecord r = FixedRecord.Parse(layout, new ReadOnlySpan<byte>(data, i * layout.Length, layout.Length), host);
                var tran = ToTransaction(r);
                if (repo.Insert(tran) == FileStatus.DuplicateKeyError)
                    repo.Update(tran); // REPRO REPLACE: load over an existing key.
            }
        });
        return 0;
    }

    /// <summary>
    /// IDCAMS <c>REPRO INFILE(cluster) OUTFILE(seq)</c> that unloads a base table to a flat fixed-width
    /// dataset of canonical copybook images, in ascending primary-key order (the backup/unload step used by
    /// COMBTRAN/TRANREPT/TRANBKP/PRTCATBL). <paramref name="serializeRows"/> writes each row's image to the
    /// given writer. Returns RC 0.
    /// </summary>
    public static int IdcamsReproUnload(JobContext ctx, string outPath, Action<FixedFileWriter> serializeRows, HostKind host = HostKind.Ebcdic)
    {
        if (File.Exists(outPath)) File.Delete(outPath);
        string? dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using FixedFileWriter writer = BatchSupport.OpenWriter(outPath, host);
        serializeRows(writer);
        writer.Flush();
        return 0;
    }

    // =================================================================================================
    // SORT  (EBCDIC/ordinal collation on the control fields)
    // =================================================================================================

    /// <summary>
    /// DFSORT/SORT over a flat fixed-length dataset: reads every <paramref name="recordLength"/>-byte record
    /// from the concatenated <paramref name="inPaths"/> (a merge when more than one), optionally keeps only
    /// those satisfying <paramref name="include"/>, orders them by <paramref name="fields"/> using ordinal
    /// byte collation (EBCDIC and ASCII coincide for the digit/space/punct subset CardDemo keys use), and
    /// writes the result to <paramref name="outPath"/>. Returns RC 0, or RC 16 if an input length is not a
    /// record multiple (a SORT data error).
    /// </summary>
    /// <param name="fields">The control fields (offset/length), compared in order, each ascending.</param>
    /// <param name="include">Optional INCLUDE COND predicate over the raw record bytes (keep when true).</param>
    public static int Sort(
        IReadOnlyList<string> inPaths,
        string outPath,
        int recordLength,
        IReadOnlyList<SortField> fields,
        Func<byte[], bool>? include = null)
    {
        var records = new List<byte[]>();
        foreach (string p in inPaths)
        {
            byte[] data = File.ReadAllBytes(p);
            if (data.Length % recordLength != 0) return 16;
            for (int i = 0; i < data.Length / recordLength; i++)
            {
                var rec = new byte[recordLength];
                Array.Copy(data, i * recordLength, rec, 0, recordLength);
                if (include is null || include(rec))
                    records.Add(rec);
            }
        }

        records.Sort((a, b) => CompareByFields(a, b, fields));

        string? dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using FileStream fs = File.Create(outPath);
        foreach (byte[] rec in records)
            fs.Write(rec);
        return 0;
    }

    /// <summary>Compares two records over the ordered control <paramref name="fields"/> (ordinal bytes, ascending).</summary>
    private static int CompareByFields(byte[] a, byte[] b, IReadOnlyList<SortField> fields)
    {
        foreach (SortField f in fields)
        {
            int end = f.Offset + f.Length;
            for (int i = f.Offset; i < end; i++)
            {
                int av = i < a.Length ? a[i] : 0;
                int bv = i < b.Length ? b[i] : 0;
                int c = (f.Ascending ? 1 : -1) * av.CompareTo(bv);
                if (c != 0) return c;
            }
        }
        return 0;
    }

    // =================================================================================================
    // IEFBR14  (no-op: dataset disposition only)
    // =================================================================================================

    /// <summary>
    /// IEFBR14: a do-nothing program whose only effect on z/OS is dataset disposition processing. Here it
    /// deletes any flat output datasets a later step will recreate (the <c>DISP=(MOD,DELETE,DELETE)</c>
    /// pre-delete idiom), so the job is re-runnable. Always RC 0.
    /// </summary>
    public static int Iefbr14(params string[] deletePaths)
    {
        foreach (string p in deletePaths)
            if (File.Exists(p)) File.Delete(p);
        return 0;
    }

    // =================================================================================================
    // Helpers
    // =================================================================================================

    /// <summary>Deletes every row from a (already-quoted-if-needed) table name. Tolerant of an empty table.</summary>
    private static void DeleteAllRows(RelationalDb db, string table)
    {
        using SqliteCommand cmd = db.Connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {table};";
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(RelationalDb db, string table)
    {
        string name = table.Trim('"');
        using SqliteCommand cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
        cmd.Parameters.AddWithValue("@n", name);
        using SqliteDataReader rd = cmd.ExecuteReader();
        return rd.Read();
    }

    private static Domain.Transaction ToTransaction(FixedRecord r) => new()
    {
        TranId = r.GetText("TRAN-ID"),
        TypeCd = r.GetText("TRAN-TYPE-CD"),
        CatCd = (int)r.GetNumber("TRAN-CAT-CD"),
        Source = r.GetText("TRAN-SOURCE"),
        Desc = r.GetText("TRAN-DESC"),
        Amt = r.GetNumber("TRAN-AMT"),
        MerchantId = (long)r.GetNumber("TRAN-MERCHANT-ID"),
        MerchantName = r.GetText("TRAN-MERCHANT-NAME"),
        MerchantCity = r.GetText("TRAN-MERCHANT-CITY"),
        MerchantZip = r.GetText("TRAN-MERCHANT-ZIP"),
        CardNum = r.GetText("TRAN-CARD-NUM"),
        OrigTs = r.GetText("TRAN-ORIG-TS"),
        ProcTs = r.GetText("TRAN-PROC-TS"),
    };
}

/// <summary>
/// One SORT control field: a byte range in the fixed-length record and its direction. Offsets are 0-based
/// (the JCL <c>SYMNAMES</c> 1-based position minus one). Collation is ordinal over the raw host bytes.
/// </summary>
/// <param name="Offset">0-based byte offset of the field within the record.</param>
/// <param name="Length">Field length in bytes.</param>
/// <param name="Ascending">Sort direction (true = A, false = D). CardDemo's sorts are all ascending.</param>
public sealed record SortField(int Offset, int Length, bool Ascending = true);
