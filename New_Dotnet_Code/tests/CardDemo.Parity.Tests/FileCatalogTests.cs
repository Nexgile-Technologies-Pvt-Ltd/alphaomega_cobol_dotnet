using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Validates the <see cref="CardDemoFiles"/> catalog against the copybooks (key offsets cannot drift)
/// and exercises the <see cref="DataBootstrapper"/> loading the whole catalog from the EBCDIC source.
/// </summary>
public class FileCatalogTests
{
    public static TheoryData<string> VsamNames()
    {
        var data = new TheoryData<string>();
        foreach (VsamFileSpec s in CardDemoFiles.Vsam) data.Add(s.Definition.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(VsamNames))]
    public void Catalog_keys_and_length_match_copybook(string fileName)
    {
        VsamFileSpec spec = CardDemoFiles.Vsam.Single(s => s.Definition.Name == fileName);
        RecordLayout layout = CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(spec.Copybook)));

        Assert.Equal(spec.Definition.RecordLength, layout.Length);

        (int po, int pl) = layout.KeyRange(spec.KeyFields);
        Assert.Equal(new KeyDef(po, pl), spec.Definition.PrimaryKey);

        if (spec.AlternateKeyFields is not null)
        {
            (int ao, int al) = layout.KeyRange(spec.AlternateKeyFields);
            Assert.Equal(new KeyDef(ao, al), spec.Definition.AlternateKey);
        }
        else
        {
            Assert.Null(spec.Definition.AlternateKey);
        }
    }

    [Fact]
    public void Bootstrap_loads_expected_record_counts()
    {
        using var db = new CardDemoDatabase();
        var boot = new DataBootstrapper(CardDemoPaths.EbcdicDataDir);
        BootstrapResult r = boot.BootstrapAll(db);

        Assert.Equal(50, r.File("ACCTFILE").Count());
        Assert.Equal(50, r.File("CARDDAT").Count());
        Assert.Equal(50, r.File("CARDXREF").Count());
        Assert.Equal(50, r.File("CUSTDAT").Count());
        Assert.Equal(50, r.File("TCATBALF").Count());
        Assert.Equal(51, r.File("DISCGRP").Count());   // 2550 / 50
        Assert.Equal(7, r.File("TRANTYPE").Count());    // 420 / 60
        Assert.Equal(18, r.File("TRANCATG").Count());   // 1080 / 60
        Assert.Equal(10, r.File("USRSEC").Count());     // 800 / 80
        Assert.Equal(0, r.File("TRANSACT").Count());    // produced by the posting job

        Assert.Equal(300, r.Seq("DALYTRAN").Count());   // 105000 / 350
    }

    [Fact]
    public void Daily_transactions_sequential_file_round_trips_to_source_image()
    {
        using var db = new CardDemoDatabase();
        var boot = new DataBootstrapper(CardDemoPaths.EbcdicDataDir);
        BootstrapResult r = boot.BootstrapAll(db);

        byte[] expected = File.ReadAllBytes(CardDemoPaths.EbcdicData("AWS.M2.CARDDEMO.DALYTRAN.PS"));
        Assert.Equal(expected, r.Seq("DALYTRAN").ToImage());

        // Sequential read returns all records then end-of-file.
        SequentialFile daly = r.Seq("DALYTRAN");
        daly.OpenInput();
        int count = 0;
        while (daly.Read(out byte[]? rec) == FileStatus.Ok)
        {
            Assert.Equal(350, rec!.Length);
            count++;
        }
        Assert.Equal(300, count);
    }

    [Fact]
    public void Bootstrapped_account_is_readable_by_key()
    {
        using var db = new CardDemoDatabase();
        var boot = new DataBootstrapper(CardDemoPaths.EbcdicDataDir);
        BootstrapResult r = boot.BootstrapAll(db);

        // Account id "00000000001" exists in the sample data (first ACCTDATA record).
        byte[] key = HostEncoding.Ebcdic.GetBytes("00000000001");
        Assert.Equal(FileStatus.Ok, r.File("ACCTFILE").Read(key, out byte[]? image));
        Assert.Equal(300, image!.Length);
    }
}
