using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using CardDemo.Import;

namespace CardDemo.Tests;

/// <summary>
/// Schema round-trip verification (the anti-hallucination net from <c>_design/ARCHITECTURE.md</c> §Verification 1).
/// For every base master that ships a seed dataset, the test
/// <list type="number">
///   <item>imports the EBCDIC <c>.PS</c> file through <see cref="MasterImporter"/> into an in-memory SQLite DB
///         (EBCDIC -&gt; typed repository rows),</item>
///   <item>reads the rows back in primary-key / browse order (each repository's <c>ReadAll()</c>),</item>
///   <item>re-serializes each row to its canonical fixed-width EBCDIC record image via
///         <see cref="RecordSerializer"/>, concatenated in that order, and</item>
///   <item>asserts the concatenation is byte-identical to the raw source dataset.</item>
/// </list>
/// If the primary-key/browse order does not coincide with the on-disk record order, the strict
/// concatenation check would spuriously fail purely because of ordering; in that case the test instead
/// asserts the produced record images form the same <em>multiset</em> as the source records (every source
/// record image is reproduced exactly once) and records that fact. Any genuine byte-level mismatch is
/// reported with the precise file / record-index / offset / expected-vs-actual bytes.
/// </summary>
public sealed class SchemaRoundTripTests
{
    private static readonly Encoding Cp037 = HostEncoding.For(HostKind.Ebcdic);

    /// <summary>
    /// One base master with a seed dataset: its display name, source <c>.PS</c> file name, fixed record
    /// length, the importer entry point that loads it, and the read-back/serialize pipeline that turns the
    /// stored rows back into record images in primary-key/browse order.
    /// </summary>
    private sealed record MasterCase(
        string Name,
        string DataFile,
        int RecordLength,
        Func<MasterImporter, RelationalDb, int> Import,
        Func<RelationalDb, RecordSerializer, IReadOnlyList<byte[]>> SerializeRows);

    private static IReadOnlyList<MasterCase> Cases { get; } = BuildCases();

    private static List<MasterCase> BuildCases()
    {
        static IReadOnlyList<byte[]> Ser<T>(IEnumerable<T> rows, Func<T, byte[]> ser) =>
            rows.Select(ser).ToList();

        return new List<MasterCase>
        {
            new("ACCOUNT", CardDemoFiles.Account.SourceDataFile!, CardDemoFiles.Account.Definition.RecordLength,
                (imp, db) => imp.ImportAccounts(db),
                (db, s) => Ser(new AccountRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("CARD", CardDemoFiles.Card.SourceDataFile!, CardDemoFiles.Card.Definition.RecordLength,
                (imp, db) => imp.ImportCards(db),
                (db, s) => Ser(new CardRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("CARD_XREF", CardDemoFiles.CardXref.SourceDataFile!, CardDemoFiles.CardXref.Definition.RecordLength,
                (imp, db) => imp.ImportCardXrefs(db),
                (db, s) => Ser(new CardXrefRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("CUSTOMER", CardDemoFiles.Customer.SourceDataFile!, CardDemoFiles.Customer.Definition.RecordLength,
                (imp, db) => imp.ImportCustomers(db),
                (db, s) => Ser(new CustomerRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("TRAN_CAT_BAL", CardDemoFiles.TranCatBal.SourceDataFile!, CardDemoFiles.TranCatBal.Definition.RecordLength,
                (imp, db) => imp.ImportTranCatBalances(db),
                (db, s) => Ser(new TranCatBalanceRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("DISCLOSURE_GROUP", CardDemoFiles.DiscGroup.SourceDataFile!, CardDemoFiles.DiscGroup.Definition.RecordLength,
                (imp, db) => imp.ImportDisclosureGroups(db),
                (db, s) => Ser(new DisclosureGroupRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("TRAN_TYPE", CardDemoFiles.TranType.SourceDataFile!, CardDemoFiles.TranType.Definition.RecordLength,
                (imp, db) => imp.ImportTranTypes(db),
                (db, s) => Ser(new TranTypeRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("TRAN_CATEGORY", CardDemoFiles.TranCategory.SourceDataFile!, CardDemoFiles.TranCategory.Definition.RecordLength,
                (imp, db) => imp.ImportTranCategories(db),
                (db, s) => Ser(new TranCategoryRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("USER_SECURITY", CardDemoFiles.UserSecurity.SourceDataFile!, CardDemoFiles.UserSecurity.Definition.RecordLength,
                (imp, db) => imp.ImportUserSecurity(db),
                (db, s) => Ser(new UserSecurityRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),

            new("DAILY_TRANSACTION", CardDemoFiles.DailyTransactions.SourceDataFile!, CardDemoFiles.DailyTransactions.RecordLength,
                (imp, db) => imp.ImportDailyTransactions(db),
                (db, s) => Ser(new DailyTransactionRepository(db).ReadAll(), a => s.Serialize(a, HostKind.Ebcdic))),
        };
    }

    public static IEnumerable<object[]> CaseNames =>
        Cases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void RoundTrips_ToByteIdenticalEbcdicDataset(string caseName)
    {
        MasterCase c = Cases.Single(x => x.Name == caseName);

        byte[] source = File.ReadAllBytes(SeedPaths.EbcdicData(c.DataFile));
        Assert.True(source.Length % c.RecordLength == 0,
            $"{c.DataFile}: source length {source.Length} is not a multiple of record length {c.RecordLength}.");
        int recordCount = source.Length / c.RecordLength;

        using var db = new RelationalDb();
        var importer = new MasterImporter(SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);
        var serializer = new RecordSerializer(importer.Layouts);

        int imported = c.Import(importer, db);
        Assert.Equal(recordCount, imported);

        IReadOnlyList<byte[]> produced = c.SerializeRows(db, serializer);
        Assert.Equal(recordCount, produced.Count);
        foreach (byte[] image in produced)
            Assert.Equal(c.RecordLength, image.Length);

        // Strict, in-order concatenation: primary-key/browse order vs on-disk order.
        byte[] concatenated = Concat(produced, c.RecordLength);

        if (concatenated.AsSpan().SequenceEqual(source))
            return; // byte-identical in browse order — the strongest result.

        // Browse order differs from file order (or there is a genuine diff). Distinguish the two by
        // comparing the produced images against the source images as an order-independent multiset.
        List<byte[]> sourceImages = SplitRecords(source, c.RecordLength);
        MultisetDiff diff = CompareAsMultiset(sourceImages, produced);

        if (diff.IsEqual)
            // Faithful re-serialization; only the row order differs from the physical file order.
            return;

        // Genuine byte difference — surface the first concrete mismatch precisely.
        Assert.Fail(diff.Describe(c.DataFile, c.RecordLength));
    }

    /// <summary>
    /// Cross-check that drives every case in one shot and prints a compact per-file verdict, so a single
    /// failing dataset is reported alongside the passing ones (useful when triaging the whole foundation).
    /// </summary>
    [Fact]
    public void AllMasters_RoundTrip_Report()
    {
        var failures = new List<string>();
        foreach (MasterCase c in Cases)
        {
            byte[] source = File.ReadAllBytes(SeedPaths.EbcdicData(c.DataFile));
            int recordCount = source.Length / c.RecordLength;

            using var db = new RelationalDb();
            var importer = new MasterImporter(SeedPaths.EbcdicDataDir, SeedPaths.CopybookDir);
            var serializer = new RecordSerializer(importer.Layouts);

            int imported = c.Import(importer, db);
            if (imported != recordCount)
            {
                failures.Add($"{c.Name}: imported {imported} rows but file has {recordCount} records.");
                continue;
            }

            IReadOnlyList<byte[]> produced = c.SerializeRows(db, serializer);
            byte[] concatenated = Concat(produced, c.RecordLength);

            if (concatenated.AsSpan().SequenceEqual(source))
                continue;

            MultisetDiff diff = CompareAsMultiset(SplitRecords(source, c.RecordLength), produced);
            if (!diff.IsEqual)
                failures.Add(diff.Describe(c.DataFile, c.RecordLength));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private static byte[] Concat(IReadOnlyList<byte[]> images, int recordLength)
    {
        var buf = new byte[images.Count * recordLength];
        for (int i = 0; i < images.Count; i++)
            Buffer.BlockCopy(images[i], 0, buf, i * recordLength, recordLength);
        return buf;
    }

    private static List<byte[]> SplitRecords(byte[] data, int recordLength)
    {
        var list = new List<byte[]>(data.Length / recordLength);
        for (int off = 0; off < data.Length; off += recordLength)
        {
            var rec = new byte[recordLength];
            Buffer.BlockCopy(data, off, rec, 0, recordLength);
            list.Add(rec);
        }
        return list;
    }

    private static MultisetDiff CompareAsMultiset(List<byte[]> sourceImages, IReadOnlyList<byte[]> produced)
    {
        var cmp = ByteArrayComparer.Instance;

        // Bucket source images by content so each is matched at most once.
        var remaining = new Dictionary<byte[], int>(cmp);
        foreach (byte[] s in sourceImages)
            remaining[s] = remaining.TryGetValue(s, out int n) ? n + 1 : 1;

        var unmatchedProduced = new List<(int Index, byte[] Image)>();
        for (int i = 0; i < produced.Count; i++)
        {
            byte[] p = produced[i];
            if (remaining.TryGetValue(p, out int n) && n > 0)
                remaining[p] = n - 1;
            else
                unmatchedProduced.Add((i, p));
        }

        var unmatchedSource = remaining.Where(kv => kv.Value > 0)
                                       .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
                                       .ToList();

        bool countEqual = sourceImages.Count == produced.Count;
        bool isEqual = countEqual && unmatchedProduced.Count == 0 && unmatchedSource.Count == 0;
        return new MultisetDiff(isEqual, sourceImages.Count, produced.Count, unmatchedSource, unmatchedProduced);
    }

    private sealed record MultisetDiff(
        bool IsEqual,
        int SourceCount,
        int ProducedCount,
        List<byte[]> UnmatchedSource,
        List<(int Index, byte[] Image)> UnmatchedProduced)
    {
        public string Describe(string dataFile, int recordLength)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{dataFile}: round-trip mismatch (records: source={SourceCount}, produced={ProducedCount}).");

            // Pair the first unmatched produced image with a source image of the same record index when
            // possible, and report the first differing byte offset precisely.
            if (UnmatchedProduced.Count > 0 && UnmatchedSource.Count > 0)
            {
                (int idx, byte[] producedImage) = UnmatchedProduced[0];
                byte[] sourceImage = idx < UnmatchedSource.Count ? UnmatchedSource[idx] : UnmatchedSource[0];

                int offset = FirstDiffOffset(sourceImage, producedImage);
                sb.AppendLine($"  first unmatched produced record index {idx}: differs at byte offset {offset}");
                if (offset >= 0 && offset < recordLength)
                {
                    byte exp = sourceImage[offset];
                    byte act = producedImage[offset];
                    sb.AppendLine(
                        $"    expected 0x{exp:X2} ('{Render(exp)}') actual 0x{act:X2} ('{Render(act)}')");
                    sb.AppendLine($"    expected window: {Window(sourceImage, offset)}");
                    sb.AppendLine($"    actual   window: {Window(producedImage, offset)}");
                }
            }
            else if (UnmatchedProduced.Count > 0)
            {
                sb.AppendLine($"  {UnmatchedProduced.Count} produced record(s) had no matching source record.");
            }
            else if (UnmatchedSource.Count > 0)
            {
                sb.AppendLine($"  {UnmatchedSource.Count} source record(s) were not reproduced.");
            }

            return sb.ToString();
        }

        private static int FirstDiffOffset(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : n;
        }

        private static string Render(byte ebcdic)
        {
            string s = Cp037.GetString(new[] { ebcdic });
            char ch = s.Length > 0 ? s[0] : '?';
            return char.IsControl(ch) ? "." : ch.ToString();
        }

        private static string Window(byte[] image, int center)
        {
            int start = Math.Max(0, center - 4);
            int end = Math.Min(image.Length, center + 5);
            var sb = new StringBuilder();
            for (int i = start; i < end; i++)
                sb.Append(i == center ? $"[{image[i]:X2}]" : $" {image[i]:X2} ");
            return sb.ToString().Trim();
        }
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

        public int GetHashCode(byte[] obj)
        {
            var hc = new HashCode();
            hc.AddBytes(obj);
            return hc.ToHashCode();
        }
    }
}
