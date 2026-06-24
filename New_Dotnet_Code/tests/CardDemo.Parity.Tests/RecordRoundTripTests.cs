using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Byte-exact round-trip proof for the base master records: parse each record image from the
/// authoritative EBCDIC dataset, decode it through the field codecs, re-encode it, and assert the
/// bytes are identical. A single failure pinpoints the first differing byte offset and field — this
/// is the foundational fidelity gate (no GnuCOBOL oracle required).
/// </summary>
public class RecordRoundTripTests
{
    /// <summary>(EBCDIC dataset file, copybook, expected record length) for every flat master file.</summary>
    public static TheoryData<string, string, int> Masters() => new()
    {
        { "AWS.M2.CARDDEMO.ACCTDATA.PS", "CVACT01Y.cpy", 300 },
        { "AWS.M2.CARDDEMO.CARDDATA.PS", "CVACT02Y.cpy", 150 },
        { "AWS.M2.CARDDEMO.CARDXREF.PS", "CVACT03Y.cpy", 50 },
        { "AWS.M2.CARDDEMO.CUSTDATA.PS", "CVCUS01Y.cpy", 500 },
        { "AWS.M2.CARDDEMO.DALYTRAN.PS", "CVTRA06Y.cpy", 350 },
        { "AWS.M2.CARDDEMO.DISCGRP.PS", "CVTRA02Y.cpy", 50 },
        { "AWS.M2.CARDDEMO.TCATBALF.PS", "CVTRA01Y.cpy", 50 },
        { "AWS.M2.CARDDEMO.TRANCATG.PS", "CVTRA04Y.cpy", 60 },
        { "AWS.M2.CARDDEMO.TRANTYPE.PS", "CVTRA03Y.cpy", 60 },
        { "AWS.M2.CARDDEMO.USRSEC.PS", "CSUSR01Y.cpy", 80 },
    };

    [Theory]
    [MemberData(nameof(Masters))]
    public void Layout_length_matches_expected_record_length(string dataFile, string copybook, int expectedReclen)
    {
        _ = dataFile;
        RecordLayout layout = CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(copybook)));
        Assert.Equal(expectedReclen, layout.Length);
    }

    [Theory]
    [MemberData(nameof(Masters))]
    public void Ebcdic_records_round_trip_byte_for_byte(string dataFile, string copybook, int expectedReclen)
    {
        RecordLayout layout = CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(copybook)));
        Assert.Equal(expectedReclen, layout.Length);

        byte[] file = File.ReadAllBytes(CardDemoPaths.EbcdicData(dataFile));
        Assert.True(file.Length % layout.Length == 0,
            $"{dataFile} length {file.Length} is not a multiple of record length {layout.Length}.");

        int recordCount = file.Length / layout.Length;
        for (int r = 0; r < recordCount; r++)
        {
            var image = new ReadOnlySpan<byte>(file, r * layout.Length, layout.Length);
            FixedRecord rec = FixedRecord.Parse(layout, image, HostKind.Ebcdic);
            byte[] reencoded = rec.ToBytes(HostKind.Ebcdic);
            AssertBytesEqual(image, reencoded, layout, dataFile, r);
        }
    }

    private static void AssertBytesEqual(ReadOnlySpan<byte> expected, byte[] actual, RecordLayout layout, string dataFile, int record)
    {
        if (expected.SequenceEqual(actual)) return;

        int offset = 0;
        while (offset < expected.Length && expected[offset] == actual[offset]) offset++;
        FieldDef? field = layout.Fields.FirstOrDefault(f => offset >= f.Offset && offset < f.Offset + f.Length);
        Assert.Fail(
            $"Round-trip mismatch in {dataFile} record {record} at byte {offset} " +
            $"(field '{field?.Name ?? "?"}'): expected 0x{expected[offset]:X2}, got 0x{actual[offset]:X2}.");
    }
}
