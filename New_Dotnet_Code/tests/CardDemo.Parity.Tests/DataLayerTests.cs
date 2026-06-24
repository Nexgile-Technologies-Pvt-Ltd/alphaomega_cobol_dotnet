using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Exercises the SQLite-backed VSAM layer against real EBCDIC master data: byte-exact storage and
/// retrieval, key-ordered browse, exact FILE STATUS codes, alternate-index reads, and write/rewrite/
/// delete. Keys are derived from the copybook (never hand-coded offsets).
/// </summary>
public class DataLayerTests
{
    private sealed record Loaded(CardDemoDatabase Db, VsamFile File, byte[] Bytes, RecordLayout Layout, VsamFileDefinition Def)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static Loaded Load(string dataFile, string copybook, string[] pkFields, string[]? akFields = null)
    {
        RecordLayout layout = CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(copybook)));
        (int po, int pl) = layout.KeyRange(pkFields);
        KeyDef? alt = akFields is null ? null : ToKeyDef(layout.KeyRange(akFields));
        var def = new VsamFileDefinition(Path.GetFileNameWithoutExtension(dataFile).Replace('.', '_'),
            layout.Length, new KeyDef(po, pl), alt);

        var db = new CardDemoDatabase();
        VsamFile file = db.DefineFile(def);

        byte[] bytes = File.ReadAllBytes(CardDemoPaths.EbcdicData(dataFile));
        Assert.True(bytes.Length % layout.Length == 0);
        for (int r = 0; r < bytes.Length / layout.Length; r++)
        {
            var image = bytes[(r * layout.Length)..((r + 1) * layout.Length)];
            Assert.Equal(FileStatus.Ok, file.Write(image));
        }
        return new Loaded(db, file, bytes, layout, def);
    }

    private static KeyDef ToKeyDef((int Offset, int Length) r) => new(r.Offset, r.Length);

    [Fact]
    public void Account_file_stores_and_reads_back_byte_exact()
    {
        using Loaded a = Load("AWS.M2.CARDDEMO.ACCTDATA.PS", "CVACT01Y.cpy", ["ACCT-ID"]);
        Assert.Equal(50, a.File.Count());

        // Read the first record's key back; the image must be identical to the file bytes.
        byte[] firstImage = a.Bytes[..a.Layout.Length];
        byte[] key = new KeyDef(0, 11).Extract(firstImage); // ACCT-ID
        Assert.Equal(FileStatus.Ok, a.File.Read(key, out byte[]? got));
        Assert.Equal(firstImage, got);

        // Missing key -> "23".
        byte[] missing = Enumerable.Repeat((byte)0x40, 11).ToArray(); // EBCDIC spaces
        Assert.Equal(FileStatus.RecordNotFound, a.File.Read(missing, out byte[]? none));
        Assert.Null(none);
    }

    [Fact]
    public void Account_browse_returns_all_records_in_ascending_key_order()
    {
        using Loaded a = Load("AWS.M2.CARDDEMO.ACCTDATA.PS", "CVACT01Y.cpy", ["ACCT-ID"]);

        var images = new List<byte[]>();
        byte[]? prevKey = null;
        a.File.StartBrowse();
        while (a.File.ReadNext(out byte[]? img) == FileStatus.Ok)
        {
            images.Add(img!);
            byte[] key = img!.AsSpan(0, 11).ToArray();
            if (prevKey is not null)
                Assert.True(Memcmp(prevKey, key) < 0, "browse keys must be strictly ascending");
            prevKey = key;
        }
        a.File.EndBrowse();

        Assert.Equal(50, images.Count);
        // The browsed set equals the original file's record set, byte for byte.
        var original = new HashSet<string>(SplitRecords(a.Bytes, a.Layout.Length).Select(Convert.ToHexString));
        var browsed = new HashSet<string>(images.Select(Convert.ToHexString));
        Assert.Equal(original, browsed);
    }

    [Fact]
    public void Xref_alternate_index_reads_first_matching_record()
    {
        using Loaded x = Load("AWS.M2.CARDDEMO.CARDXREF.PS", "CVACT03Y.cpy",
            pkFields: ["XREF-CARD-NUM"], akFields: ["XREF-ACCT-ID"]);

        // Take an account id present in the file (offset 25, length 11) and read by alternate key.
        byte[] firstImage = x.Bytes[..x.Layout.Length];
        byte[] acctKey = firstImage.AsSpan(25, 11).ToArray();
        Assert.Equal(FileStatus.Ok, x.File.ReadByAlternateKey(acctKey, out byte[]? got));
        Assert.Equal(acctKey, got!.AsSpan(25, 11).ToArray()); // returned record has the requested account id

        byte[] missing = Enumerable.Repeat((byte)0x40, 11).ToArray();
        Assert.Equal(FileStatus.RecordNotFound, x.File.ReadByAlternateKey(missing, out _));
    }

    [Fact]
    public void Write_rewrite_delete_status_codes_are_exact()
    {
        var def = new VsamFileDefinition("TESTACCT", 300, new KeyDef(0, 11));
        using var db = new CardDemoDatabase();
        VsamFile f = db.DefineFile(def);

        byte[] rec = MakeAccountImage(acctId: "00000000042", status: (byte)0xC1); // 'A'
        Assert.Equal(FileStatus.Ok, f.Write(rec));
        Assert.Equal(FileStatus.DuplicateKeyError, f.Write(rec)); // duplicate PK -> "22"

        Assert.Equal(FileStatus.Ok, f.Read(rec.AsSpan(0, 11).ToArray(), out byte[]? back));
        Assert.Equal(rec, back);

        byte[] rec2 = (byte[])rec.Clone();
        rec2[11] = 0xD5; // change ACCT-ACTIVE-STATUS byte ('N')
        Assert.Equal(FileStatus.Ok, f.Rewrite(rec2));
        Assert.Equal(FileStatus.Ok, f.Read(rec.AsSpan(0, 11).ToArray(), out byte[]? back2));
        Assert.Equal(rec2, back2);

        Assert.Equal(FileStatus.Ok, f.Delete(rec.AsSpan(0, 11).ToArray()));
        Assert.Equal(FileStatus.RecordNotFound, f.Read(rec.AsSpan(0, 11).ToArray(), out _));
        Assert.Equal(FileStatus.RecordNotFound, f.Rewrite(rec2)); // rewrite of absent record -> "23"
    }

    private static byte[] MakeAccountImage(string acctId, byte status)
    {
        var rec = new byte[300];
        rec.AsSpan().Fill(0x40); // EBCDIC spaces
        byte[] id = HostEncoding.Ebcdic.GetBytes(acctId); // 11 digits
        id.CopyTo(rec, 0);
        rec[11] = status;
        return rec;
    }

    private static IEnumerable<byte[]> SplitRecords(byte[] file, int reclen)
    {
        for (int i = 0; i < file.Length; i += reclen)
            yield return file[i..(i + reclen)];
    }

    private static int Memcmp(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i] - b[i];
        return a.Length - b.Length;
    }
}
