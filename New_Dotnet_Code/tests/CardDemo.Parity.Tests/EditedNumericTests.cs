using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;
using Xunit;

namespace CardDemo.Parity.Tests;

/// <summary>
/// Validates <see cref="CobolEditedNumeric"/> byte-for-byte against GnuCOBOL: the same values are moved
/// into real <c>-ZZZ,ZZZ,ZZZ.ZZ</c> and <c>+ZZZ,ZZZ,ZZZ.ZZ</c> edited fields and the formatted output
/// is compared. Skipped when GnuCOBOL is not installed.
/// </summary>
public class EditedNumericTests
{
    private const string MinusPic = "-ZZZ,ZZZ,ZZZ.ZZ";
    private const string PlusPic = "+ZZZ,ZZZ,ZZZ.ZZ";

    private static readonly decimal[] Values =
    {
        0m, 1234.56m, -1234.56m, 12m, -12m, 0.05m, -0.05m,
        999999999.99m, -999999999.99m, 1000000.00m, 7.89m, -7.89m,
    };

    private const string EdChkCob = @"       IDENTIFICATION DIVISION.
       PROGRAM-ID. EDCHK.
       DATA DIVISION.
       WORKING-STORAGE SECTION.
       01 VALS.
          05 FILLER PIC S9(9)V99 VALUE 0.
          05 FILLER PIC S9(9)V99 VALUE 1234.56.
          05 FILLER PIC S9(9)V99 VALUE -1234.56.
          05 FILLER PIC S9(9)V99 VALUE 12.
          05 FILLER PIC S9(9)V99 VALUE -12.
          05 FILLER PIC S9(9)V99 VALUE 0.05.
          05 FILLER PIC S9(9)V99 VALUE -0.05.
          05 FILLER PIC S9(9)V99 VALUE 999999999.99.
          05 FILLER PIC S9(9)V99 VALUE -999999999.99.
          05 FILLER PIC S9(9)V99 VALUE 1000000.00.
          05 FILLER PIC S9(9)V99 VALUE 7.89.
          05 FILLER PIC S9(9)V99 VALUE -7.89.
       01 VR REDEFINES VALS.
          05 V PIC S9(9)V99 OCCURS 12.
       01 EM PIC -ZZZ,ZZZ,ZZZ.ZZ.
       01 EP PIC +ZZZ,ZZZ,ZZZ.ZZ.
       01 I PIC 9(2).
       PROCEDURE DIVISION.
           PERFORM VARYING I FROM 1 BY 1 UNTIL I > 12
              MOVE V(I) TO EM
              MOVE V(I) TO EP
              DISPLAY '[' EM '][' EP ']'
           END-PERFORM
           STOP RUN.
";

    [Fact]
    public void Edited_numeric_matches_gnucobol()
    {
        GnuCobolInstall? install = GnuCobolInstall.TryLocate();
        if (install is null) return;

        string work = Path.Combine(Path.GetTempPath(), "carddemo_edchk");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        var h = new GnuCobolHarness(install);
        string src = Path.Combine(work, "EDCHK.cob");
        File.WriteAllText(src, EdChkCob);
        Assert.True(h.CompileExecutable(src, [], "EDCHK.exe", work).Success, "compile EDCHK");
        ProcessResult run = h.Run(Path.Combine(work, "EDCHK.exe"), work);
        Assert.True(run.Success, run.StdErr);

        string[] lines = run.StdOut.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal(Values.Length, lines.Length);

        for (int i = 0; i < Values.Length; i++)
        {
            // Line format: [EM][EP]  with EM and EP each 15 chars.
            string em = lines[i].Substring(1, 15);
            string ep = lines[i].Substring(18, 15);
            Assert.True(em == CobolEditedNumeric.Format(Values[i], MinusPic),
                $"value {Values[i]} minus-pic: GnuCOBOL [{em}] vs .NET [{CobolEditedNumeric.Format(Values[i], MinusPic)}]");
            Assert.True(ep == CobolEditedNumeric.Format(Values[i], PlusPic),
                $"value {Values[i]} plus-pic: GnuCOBOL [{ep}] vs .NET [{CobolEditedNumeric.Format(Values[i], PlusPic)}]");
        }
    }
}
