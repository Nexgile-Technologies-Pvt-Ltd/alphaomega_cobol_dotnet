using System.Collections.Concurrent;
using CardDemo.Cobol.Runtime;
using CardDemo.Tooling;

namespace CardDemo.Import;

/// <summary>
/// A small cache of parsed copybook <see cref="RecordLayout"/>s keyed by copybook file name (e.g.
/// "CVACT01Y.cpy"). Layouts are derived from the <c>.cpy</c> source by <see cref="CopybookParser"/>, so
/// offsets/lengths are never hand-transcribed. Shared by <see cref="MasterImporter"/> (decode) and
/// <see cref="RecordSerializer"/> (encode) so both sides use one authoritative layout per record.
/// </summary>
public sealed class RecordLayouts
{
    private readonly string _copybookDir;
    private readonly ConcurrentDictionary<string, RecordLayout> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a cache that loads copybooks from <paramref name="copybookDirectory"/>.</summary>
    public RecordLayouts(string copybookDirectory) => _copybookDir = copybookDirectory;

    /// <summary>The directory copybooks are loaded from.</summary>
    public string CopybookDirectory => _copybookDir;

    /// <summary>Returns the (cached) flattened layout for the named copybook, parsing it on first use.</summary>
    public RecordLayout For(string copybookFileName) =>
        _cache.GetOrAdd(copybookFileName, name =>
        {
            string path = Path.Combine(_copybookDir, name);
            return CopybookParser.Parse(File.ReadAllText(path));
        });
}
