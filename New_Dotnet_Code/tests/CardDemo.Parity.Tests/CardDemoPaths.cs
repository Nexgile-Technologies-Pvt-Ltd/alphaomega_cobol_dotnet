namespace CardDemo.Parity.Tests;

/// <summary>
/// Resolves paths into the original COBOL repository (the conversion source of truth), located by
/// walking up from the test assembly directory until the <c>Old_Cobol_Code</c> folder is found.
/// </summary>
public static class CardDemoPaths
{
    /// <summary>Root of the legacy app: <c>Old_Cobol_Code/aws-mainframe-modernization-carddemo</c>.</summary>
    public static string LegacyAppRoot { get; } = Locate();

    public static string CopybookDir => Path.Combine(LegacyAppRoot, "app", "cpy");
    public static string EbcdicDataDir => Path.Combine(LegacyAppRoot, "app", "data", "EBCDIC");
    public static string AsciiDataDir => Path.Combine(LegacyAppRoot, "app", "data", "ASCII");

    public static string Copybook(string name) => Path.Combine(CopybookDir, name);
    public static string EbcdicData(string name) => Path.Combine(EbcdicDataDir, name);
    public static string AsciiData(string name) => Path.Combine(AsciiDataDir, name);

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Old_Cobol_Code", "aws-mainframe-modernization-carddemo");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate 'Old_Cobol_Code/aws-mainframe-modernization-carddemo' above " + AppContext.BaseDirectory);
    }
}
