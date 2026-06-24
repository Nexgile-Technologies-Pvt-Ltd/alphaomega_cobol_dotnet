using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Verifies that a record carries the same typed values whether it is represented in EBCDIC or ASCII:
/// each EBCDIC master record is decoded, re-encoded to ASCII, and decoded again, and the values must
/// match. This underpins the GnuCOBOL oracle strategy — GnuCOBOL runs on the ASCII form generated from
/// the EBCDIC source, so the two encodings must be value-equivalent.
/// </summary>
/// <remarks>
/// Note: the shipped <c>app/data/ASCII</c> twins are NOT byte- or content-faithful copies of the
/// EBCDIC datasets (e.g. <c>acctdata.txt</c> diverges from <c>ACCTDATA.PS</c> at record 48), so this
/// test derives the ASCII form from the EBCDIC source of truth rather than reading the twin files.
/// </remarks>
public class CrossEncodingTests
{
    public static TheoryData<string, string> Masters() => new()
    {
        { "AWS.M2.CARDDEMO.ACCTDATA.PS", "CVACT01Y.cpy" },
        { "AWS.M2.CARDDEMO.CARDDATA.PS", "CVACT02Y.cpy" },
        { "AWS.M2.CARDDEMO.CARDXREF.PS", "CVACT03Y.cpy" },
        { "AWS.M2.CARDDEMO.CUSTDATA.PS", "CVCUS01Y.cpy" },
        { "AWS.M2.CARDDEMO.DALYTRAN.PS", "CVTRA06Y.cpy" },
        { "AWS.M2.CARDDEMO.TCATBALF.PS", "CVTRA01Y.cpy" },
        { "AWS.M2.CARDDEMO.USRSEC.PS", "CSUSR01Y.cpy" },
    };

    [Theory]
    [MemberData(nameof(Masters))]
    public void Same_record_decodes_identically_in_ebcdic_and_ascii(string ebcdicFile, string copybook)
    {
        RecordLayout layout = CopybookParser.Parse(File.ReadAllText(CardDemoPaths.Copybook(copybook)));
        byte[] file = File.ReadAllBytes(CardDemoPaths.EbcdicData(ebcdicFile));
        int count = file.Length / layout.Length;

        for (int r = 0; r < count; r++)
        {
            var image = new ReadOnlySpan<byte>(file, r * layout.Length, layout.Length);

            FixedRecord fromEbcdic = FixedRecord.Parse(layout, image, HostKind.Ebcdic);
            byte[] asciiImage = fromEbcdic.ToBytes(HostKind.Ascii);
            FixedRecord fromAscii = FixedRecord.Parse(layout, asciiImage, HostKind.Ascii);

            foreach (FieldDef f in layout.Fields)
            {
                object? e = fromEbcdic.GetValue(f.Name);
                object? a = fromAscii.GetValue(f.Name);
                Assert.True(Equals(e, a),
                    $"{ebcdicFile} record {r} field '{f.Name}': EBCDIC '{e}' != ASCII '{a}'.");
            }

            // Re-encoding the ASCII values back to EBCDIC must reproduce the original bytes.
            Assert.Equal(image.ToArray(), fromAscii.ToBytes(HostKind.Ebcdic));
        }
    }
}
