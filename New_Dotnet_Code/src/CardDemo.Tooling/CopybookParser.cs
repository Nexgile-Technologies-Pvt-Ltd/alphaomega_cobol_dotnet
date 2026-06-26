using System.Text;
using CardDemo.Runtime;

namespace CardDemo.Tooling;

/// <summary>
/// Parses a COBOL copybook into a byte-exact layout. Offsets and lengths are derived directly from the
/// <c>.cpy</c> source (PIC, USAGE, levels, OCCURS, REDEFINES) so they are never hand-transcribed.
/// </summary>
/// <remarks>
/// Supports the constructs CardDemo's record copybooks use: nested group/elementary levels,
/// PIC X / 9 / S9 / V, USAGE DISPLAY / COMP-3 / COMP, FILLER, fixed OCCURS, and REDEFINES (including a
/// group redefining an elementary, as in the EXPORT file). 88-level condition names and 66-level
/// RENAMES carry no storage and are skipped.
/// </remarks>
public static class CopybookParser
{
    /// <summary>Parses the first 01-level record, using each region's original (non-redefining) definition.</summary>
    public static RecordLayout Parse(string copybookText) => ParseModel(copybookText).Flatten();

    /// <summary>
    /// Parses the first 01-level record, activating the named REDEFINES alternates (e.g. an EXPORT
    /// record-type variant) in place of the regions they redefine.
    /// </summary>
    public static RecordLayout ParseVariant(string copybookText, params string[] selectedRedefines) =>
        ParseModel(copybookText).Flatten(selectedRedefines);

    /// <summary>Parses the first 01-level record into a reusable model that retains REDEFINES alternates.</summary>
    public static CopybookModel ParseModel(string copybookText)
    {
        List<Entry> entries = Tokenize(copybookText);
        int start = entries.FindIndex(e => e.Level == 1);
        if (start < 0) throw new FormatException("No 01-level record found in copybook.");

        CopybookNode root = BuildTree(entries, start);
        return new CopybookModel(root);
    }

    private static CopybookNode BuildTree(List<Entry> entries, int start)
    {
        CopybookNode root = NodeFor(entries[start]);
        var stack = new Stack<CopybookNode>();
        stack.Push(root);

        for (int i = start + 1; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.Level == 1) break;            // next record begins
            if (e.Level is 88 or 66) continue;  // condition name / renames: no storage

            CopybookNode node = NodeFor(e);
            while (stack.Count > 1 && stack.Peek().Level >= e.Level) stack.Pop();
            stack.Peek().Children.Add(node);
            stack.Push(node);
        }

        return root;
    }

    private static CopybookNode NodeFor(Entry e) => new()
    {
        Level = e.Level,
        Name = e.Name,
        IsFiller = e.IsFiller,
        Pic = e.Picture is null ? null : PicInfo.Parse(e.Picture),
        Usage = e.Usage,
        Occurs = e.Occurs,
        Redefines = e.Redefines,
    };

    private sealed record Entry(
        int Level,
        string Name,
        bool IsFiller,
        string? Picture,
        PicUsage Usage,
        string? Redefines,
        int Occurs);

    private static List<Entry> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] == '*') continue;                    // full comment line
            if (line.Length >= 7 && line[6] == '*') continue;   // column-7 comment indicator
            if (line.Length > 72) line = line[..72];            // ignore identification area
            sb.Append(' ').Append(line);
        }

        var entries = new List<Entry>();
        foreach (string clause in sb.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] tok = clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0 || !int.TryParse(tok[0], out int level)) continue;
            Entry? entry = ParseEntry(level, tok);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private static Entry? ParseEntry(int level, string[] tok)
    {
        if (tok.Length < 2) return null;
        string name = tok[1];
        bool isFiller = name.Equals("FILLER", StringComparison.OrdinalIgnoreCase);

        string? picture = null;
        string? redefines = null;
        int occurs = 1;
        PicUsage usage = PicUsage.Display;

        for (int i = 2; i < tok.Length; i++)
        {
            switch (tok[i].ToUpperInvariant())
            {
                case "PIC":
                case "PICTURE":
                    int j = i + 1;
                    if (j < tok.Length && tok[j].Equals("IS", StringComparison.OrdinalIgnoreCase)) j++;
                    if (j < tok.Length) { picture = tok[j]; i = j; }
                    break;
                case "REDEFINES":
                    if (i + 1 < tok.Length) { redefines = tok[i + 1]; i++; }
                    break;
                case "OCCURS":
                    if (i + 1 < tok.Length && int.TryParse(tok[i + 1], out int n)) { occurs = n; i++; }
                    break;
                case "COMP-3":
                case "COMPUTATIONAL-3":
                case "PACKED-DECIMAL":
                    usage = PicUsage.Comp3;
                    break;
                case "COMP":
                case "COMP-4":
                case "COMPUTATIONAL":
                case "COMPUTATIONAL-4":
                case "BINARY":
                    usage = PicUsage.Comp;
                    break;
                case "DISPLAY":
                    usage = PicUsage.Display;
                    break;
            }
        }

        return new Entry(level, name, isFiller, picture, usage, redefines, occurs);
    }
}
