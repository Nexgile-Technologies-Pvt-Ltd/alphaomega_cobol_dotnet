using CardDemo.Cobol.Runtime;
using CardDemo.ConsoleApp.Maps;
using CardDemo.Data;
using CardDemo.Import;
using CardDemo.Online;

namespace CardDemo.ConsoleApp;

/// <summary>
/// Top-level console host for the CardDemo pseudo-conversational runtime. It owns the shared
/// <see cref="RelationalDb"/> (one open SQLite connection, seeded from the master datasets), the program
/// registry of the 17 ported online handlers, the console screen surface, and the <see cref="Dispatcher"/>,
/// and starts the conversation at a chosen transaction (the sign-on transaction <c>CC00</c> by default).
/// See <c>_design/CONSOLE_RUNTIME.md</c> §6 and <c>_design/specs/optional/CSD_TRANSACTIONS.md</c>.
/// </summary>
/// <remarks>
/// The host is the .NET stand-in for "CICS starts transaction CC00 at the terminal": it primes the
/// dispatcher with the start TRANSID and a <c>null</c> COMMAREA (cold start, EIBCALEN 0), then lets the
/// pseudo-conversational loop run until a handler issues a terminal RETURN (logoff) or the operator
/// disconnects. The same <see cref="ConsoleScreenIo"/> instance serves as both the SEND/RECEIVE surface
/// and the dispatcher's AID source, so the keystroke an in-turn RECEIVE captured drives the next turn.
/// </remarks>
public sealed class CardDemoConsoleHost : IDisposable
{
    private readonly RelationalDb _db;
    private readonly bool _ownsDb;
    private readonly IProgramRegistry _programs;
    private readonly ConsoleScreenIo _screen;
    private readonly IClock _clock;

    /// <summary>
    /// Creates a host. When <paramref name="db"/> is supplied it is used as-is (caller owns/seeds it);
    /// otherwise a fresh in-memory <see cref="RelationalDb"/> is created and seeded from the master
    /// datasets, and disposed with the host. When <paramref name="programs"/> is supplied it is used
    /// verbatim; otherwise the 17 online handlers are registered over the shared DB. The screen's map
    /// catalog defaults to the full online catalog so by-name map lookups resolve every handler's map.
    /// </summary>
    public CardDemoConsoleHost(
        RelationalDb? db = null,
        IProgramRegistry? programs = null,
        ConsoleScreenIo? screen = null,
        IClock? clock = null)
    {
        _ownsDb = db is null;
        _db = db ?? CreateSeededDb();
        _programs = programs ?? OnlinePrograms.BuildRegistry(_db);
        _screen = screen ?? new ConsoleScreenIo(OnlinePrograms.BuildMapCatalog());
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>The shared relational database backing every transaction (exposed for inspection / tests).</summary>
    public RelationalDb Db => _db;

    /// <summary>The screen surface this host drives (exposed for headless inspection / tests).</summary>
    public ConsoleScreenIo Screen => _screen;

    /// <summary>The program registry of the 17 online handlers (exposed for inspection / tests).</summary>
    public IProgramRegistry Programs => _programs;

    /// <summary>
    /// Runs the conversation starting at <paramref name="startTransId"/> (default <c>CC00</c>, sign-on)
    /// until a terminal RETURN or end-of-input. Cold start: the very first turn is the EIBCALEN-0 display.
    /// </summary>
    public void Run(string startTransId = "CC00")
    {
        var dispatcher = new Dispatcher(_programs, _screen, _clock);
        dispatcher.Run(startTransId, _screen, initialCommArea: null);
    }

    /// <summary>
    /// Creates a fresh in-memory <see cref="RelationalDb"/> and seeds it from the EBCDIC master datasets via
    /// <see cref="MasterImporter"/> when those datasets are reachable; if they cannot be located (e.g. the
    /// legacy tree is absent) the empty schema is returned so the host still renders the sign-on screen.
    /// </summary>
    public static RelationalDb CreateSeededDb()
    {
        var db = new RelationalDb();
        if (SeedDataPaths.TryLocate(out string ebcdicDir, out string copybookDir))
        {
            var importer = new MasterImporter(ebcdicDir, copybookDir);
            importer.ImportAll(db);
        }
        return db;
    }

    /// <summary>The default program registry over a freshly seeded shared DB (all 17 online handlers).</summary>
    public static IProgramRegistry DefaultRegistry() => OnlinePrograms.BuildRegistry(CreateSeededDb());

    public void Dispose()
    {
        if (_ownsDb) _db.Dispose();
    }
}

/// <summary>
/// Locates the legacy COBOL repository's EBCDIC master datasets and copybooks (the conversion source of
/// truth) by walking up from the running assembly directory until the
/// <c>Old_Cobol_Code/aws-mainframe-modernization-carddemo</c> tree is found.
/// </summary>
internal static class SeedDataPaths
{
    /// <summary>
    /// Tries to resolve the EBCDIC data and copybook directories. Returns <c>false</c> (with empty paths)
    /// when the legacy tree is not present above the assembly directory.
    /// </summary>
    public static bool TryLocate(out string ebcdicDir, out string copybookDir)
    {
        ebcdicDir = "";
        copybookDir = "";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string root = Path.Combine(dir.FullName, "Old_Cobol_Code", "aws-mainframe-modernization-carddemo");
            string ebcdic = Path.Combine(root, "app", "data", "EBCDIC");
            string cpy = Path.Combine(root, "app", "cpy");
            if (Directory.Exists(ebcdic) && Directory.Exists(cpy))
            {
                ebcdicDir = ebcdic;
                copybookDir = cpy;
                return true;
            }
            dir = dir.Parent;
        }
        return false;
    }
}
