namespace CardDemo.Tests;

/// <summary>
/// Resolves paths to the original COBOL repository's seed data and copybooks (the conversion source of
/// truth), located by walking up from the test assembly directory until <c>Old_Cobol_Code</c> is found.
/// The schema round-trip suite reads the raw EBCDIC <c>.PS</c> datasets and the <c>.cpy</c> copybooks
/// from here.
/// </summary>
internal static class SeedPaths
{
    /// <summary>Root of the legacy app: <c>Old_Cobol_Code/aws-mainframe-modernization-carddemo</c>.</summary>
    public static string LegacyAppRoot { get; } = Locate();

    public static string CopybookDir => Path.Combine(LegacyAppRoot, "app", "cpy");
    public static string EbcdicDataDir => Path.Combine(LegacyAppRoot, "app", "data", "EBCDIC");

    public static string EbcdicData(string name) => Path.Combine(EbcdicDataDir, name);

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
