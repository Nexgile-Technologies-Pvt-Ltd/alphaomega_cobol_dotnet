using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-exact round-trip for the EXPORT file — the only path that uses COMP-3 (packed) and COMP
/// (binary) fields and a REDEFINES/OCCURS multi-record layout. Each 500-byte record is decoded with
/// the variant selected by its REC-TYPE byte and re-encoded; the bytes must match exactly. This proves
/// the packed/binary codecs and the REDEFINES/OCCURS parser against real mainframe data.
/// </summary>
public class ExportRoundTripTests
{
    private const string ExportFile = "AWS.M2.CARDDEMO.EXPORT.DATA.PS";
    private const int RecLen = 500;

    // REC-TYPE byte (set by CBEXPORT) -> the copybook REDEFINES variant to activate.
    private static readonly Dictionary<string, string> VariantByRecType = new()
    {
        ["C"] = "EXPORT-CUSTOMER-DATA",
        ["A"] = "EXPORT-ACCOUNT-DATA",
        ["X"] = "EXPORT-CARD-XREF-DATA",
        ["T"] = "EXPORT-TRANSACTION-DATA",
        ["D"] = "EXPORT-CARD-DATA",
    };

    [Fact]
    public void Every_variant_layout_is_500_bytes()
    {
        CopybookModel model = CopybookParser.ParseModel(File.ReadAllText(CardDemoPaths.Copybook("CVEXPORT.cpy")));
        foreach (string variant in VariantByRecType.Values)
            Assert.Equal(RecLen, model.Flatten(variant).Length);
    }

    [Fact]
    public void Export_records_round_trip_byte_for_byte()
    {
        CopybookModel model = CopybookParser.ParseModel(File.ReadAllText(CardDemoPaths.Copybook("CVEXPORT.cpy")));
        var layouts = VariantByRecType.ToDictionary(
            kv => kv.Key, kv => model.Flatten(kv.Value), StringComparer.Ordinal);

        byte[] file = File.ReadAllBytes(CardDemoPaths.EbcdicData(ExportFile));
        Assert.True(file.Length % RecLen == 0, $"{ExportFile} length {file.Length} not a multiple of {RecLen}.");
        int recordCount = file.Length / RecLen;

        var typeCounts = new Dictionary<string, int>();
        for (int r = 0; r < recordCount; r++)
        {
            var image = new ReadOnlySpan<byte>(file, r * RecLen, RecLen);
            string recType = HostEncoding.Ebcdic.GetString(image.Slice(0, 1));
            Assert.True(layouts.TryGetValue(recType, out RecordLayout? layout),
                $"Record {r} has unknown REC-TYPE '{recType}'.");
            typeCounts[recType] = typeCounts.GetValueOrDefault(recType) + 1;

            FixedRecord rec = FixedRecord.Parse(layout!, image, HostKind.Ebcdic);
            byte[] reencoded = rec.ToBytes(HostKind.Ebcdic);
            AssertBytesEqual(image, reencoded, layout!, r, recType);
        }

        // Sanity: the shipped distribution is 50 C / 50 A / 50 X / 300 T / 50 D = 500.
        Assert.Equal(50, typeCounts["C"]);
        Assert.Equal(50, typeCounts["A"]);
        Assert.Equal(50, typeCounts["X"]);
        Assert.Equal(300, typeCounts["T"]);
        Assert.Equal(50, typeCounts["D"]);
    }

    [Fact]
    public void First_customer_record_decodes_expected_packed_and_binary_fields()
    {
        CopybookModel model = CopybookParser.ParseModel(File.ReadAllText(CardDemoPaths.Copybook("CVEXPORT.cpy")));
        RecordLayout layout = model.Flatten("EXPORT-CUSTOMER-DATA");
        byte[] file = File.ReadAllBytes(CardDemoPaths.EbcdicData(ExportFile));

        FixedRecord rec = FixedRecord.Parse(layout, new ReadOnlySpan<byte>(file, 0, RecLen), HostKind.Ebcdic);
        Assert.Equal(1m, rec.GetNumber("EXPORT-SEQUENCE-NUM"));     // COMP, 4 bytes 00 00 00 01
        Assert.Equal(1m, rec.GetNumber("EXP-CUST-ID"));            // COMP, 4 bytes
        Assert.Equal(300m, rec.GetNumber("EXP-CUST-FICO-CREDIT-SCORE")); // COMP-3, bytes 30 0F
        Assert.Equal("IMMANUEL", rec.GetText("EXP-CUST-FIRST-NAME").TrimEnd());
    }

    private static void AssertBytesEqual(ReadOnlySpan<byte> expected, byte[] actual, RecordLayout layout, int record, string recType)
    {
        if (expected.SequenceEqual(actual)) return;
        int offset = 0;
        while (offset < expected.Length && expected[offset] == actual[offset]) offset++;
        FieldDef? field = layout.Fields.FirstOrDefault(f => offset >= f.Offset && offset < f.Offset + f.Length);
        Assert.Fail(
            $"EXPORT round-trip mismatch in record {record} (type '{recType}') at byte {offset} " +
            $"(field '{field?.Name ?? "?"}'): expected 0x{expected[offset]:X2}, got 0x{actual[offset]:X2}.");
    }
}
