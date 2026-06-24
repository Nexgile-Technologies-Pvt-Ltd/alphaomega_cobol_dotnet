using System.Text;
using CardDemo.Cobol.Runtime;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Proves the host encodings are complete single-byte code pages: every one of the 256 byte values
/// survives a byte -> char -> byte round-trip. This is the guarantee that lets PIC X fields be stored
/// as decoded strings yet re-serialize to identical bytes.
/// </summary>
public class EncodingTests
{
    public static TheoryData<HostKind> Hosts() => new() { HostKind.Ebcdic, HostKind.Ascii };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void All_256_bytes_round_trip(HostKind host)
    {
        Encoding enc = HostEncoding.For(host);
        var all = new byte[256];
        for (int i = 0; i < 256; i++) all[i] = (byte)i;

        string decoded = enc.GetString(all);
        Assert.Equal(256, decoded.Length); // single-byte: one char per byte

        byte[] reencoded = enc.GetBytes(decoded);
        Assert.Equal(all, reencoded);
    }

    [Fact]
    public void Ebcdic_known_characters_match_observed_data_bytes()
    {
        Encoding e = HostEncoding.Ebcdic;
        Assert.Equal(new byte[] { 0xE8 }, e.GetBytes("Y"));   // ACCT-ACTIVE-STATUS 'Y'
        Assert.Equal(new byte[] { 0xC1 }, e.GetBytes("A"));   // group-id 'A'
        Assert.Equal(new byte[] { 0x40 }, e.GetBytes(" "));   // FILLER space
        Assert.Equal(new byte[] { 0x60 }, e.GetBytes("-"));   // date hyphen
        Assert.Equal(new byte[] { 0xF0 }, e.GetBytes("0"));   // digit zero zone F
    }
}
