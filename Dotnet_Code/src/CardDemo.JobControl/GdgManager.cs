namespace CardDemo.JobControl;

/// <summary>
/// Models a z/OS Generation Data Group (GDG) for the flat (sequential) datasets CardDemo jobs create with
/// relative generation references. Each GDG base (e.g. <c>AWS.M2.CARDDEMO.DALYREJS</c>) keeps an ordered
/// list of catalogued generations; a job references <c>(0)</c> = the latest catalogued generation and
/// <c>(+1)</c> = a new generation it is creating this run. The manager maps each generation to a concrete
/// file path under its root directory and enforces the <c>LIMIT(n) SCRATCH</c> retention CardDemo's
/// <c>DEFGDG*</c> jobs define (keep the last <c>n</c> generations, delete older ones).
/// </summary>
/// <remarks>
/// <para>Within a single job run a relative <c>(+1)</c> resolves to <b>the same</b> generation for every
/// step that references it (z/OS allocates the new generation once at job initiation); call
/// <see cref="AllocateNext"/> once per base per job and reuse the returned path for later steps. On
/// <see cref="Catalog"/> the new generation becomes the latest (so a subsequent job's <c>(0)</c> sees it)
/// and over-limit generations are scratched.</para>
/// </remarks>
public sealed class GdgManager
{
    private readonly string _root;
    private readonly Dictionary<string, GdgBase> _bases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a manager whose generations live under <paramref name="rootDirectory"/>.</summary>
    public GdgManager(string rootDirectory)
    {
        _root = rootDirectory;
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Defines a GDG base (the <c>DEFINE GENERATIONDATAGROUP</c> / <c>DEFGDG*</c> step). Idempotent — a
    /// re-define keeps the existing generations but resets the retention limit.
    /// </summary>
    public void Define(string baseName, int limit = 5, bool scratch = true)
    {
        if (_bases.TryGetValue(baseName, out GdgBase? existing))
        {
            existing.Limit = limit;
            existing.Scratch = scratch;
        }
        else
        {
            _bases[baseName] = new GdgBase(baseName, limit, scratch);
        }
    }

    private GdgBase Ensure(string baseName) =>
        _bases.TryGetValue(baseName, out GdgBase? b) ? b : (_bases[baseName] = new GdgBase(baseName, 5, true));

    /// <summary>
    /// Allocates the next (<c>+1</c>) generation for <paramref name="baseName"/> and returns its (empty)
    /// file path. The generation is <b>pending</b> until <see cref="Catalog"/> is called (DISP=(NEW,CATLG,
    /// DELETE)); a pending generation is resolvable by <c>(+1)</c> for the rest of the job but is not yet
    /// the latest <c>(0)</c>.
    /// </summary>
    public string AllocateNext(string baseName)
    {
        GdgBase b = Ensure(baseName);
        if (b.PendingPath is not null)
            return b.PendingPath; // a (+1) resolves to the same generation for every step in the job.

        int nextNumber = b.LatestNumber + 1;
        string path = GenerationPath(baseName, nextNumber);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);
        File.Create(path).Dispose(); // empty dataset, ready to be written/REPRO'd.

        b.PendingNumber = nextNumber;
        b.PendingPath = path;
        return path;
    }

    /// <summary>
    /// The path of the pending <c>(+1)</c> generation for <paramref name="baseName"/> (allocating it if a
    /// step hasn't already), so a later step in the same job reads exactly what the earlier step wrote.
    /// </summary>
    public string Plus1(string baseName) => AllocateNext(baseName);

    /// <summary>
    /// The path of the current latest catalogued generation <c>(0)</c> for <paramref name="baseName"/>, or
    /// <c>null</c> if the base has no catalogued generation yet (a job reading <c>(0)</c> on an empty base
    /// would get a JCL allocation failure on z/OS).
    /// </summary>
    public string? Current(string baseName)
    {
        GdgBase b = Ensure(baseName);
        return b.LatestNumber == 0 ? null : GenerationPath(baseName, b.LatestNumber);
    }

    /// <summary>
    /// Catalogs the pending <c>(+1)</c> generation for <paramref name="baseName"/> (DISP=CATLG on a clean
    /// step end): it becomes the latest <c>(0)</c>, and any generation beyond the base's <c>LIMIT</c> is
    /// scratched. No-op if there is no pending generation.
    /// </summary>
    public void Catalog(string baseName)
    {
        GdgBase b = Ensure(baseName);
        if (b.PendingPath is null) return;

        b.Generations.Add(b.PendingNumber);
        b.LatestNumber = b.PendingNumber;
        b.PendingPath = null;
        b.PendingNumber = 0;

        // LIMIT(n) SCRATCH: keep the most recent n generations, scratch (delete) the rest.
        if (b.Scratch)
        {
            b.Generations.Sort();
            while (b.Generations.Count > b.Limit)
            {
                int oldest = b.Generations[0];
                b.Generations.RemoveAt(0);
                string p = GenerationPath(baseName, oldest);
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }

    /// <summary>
    /// Discards the pending <c>(+1)</c> generation (DISP=DELETE on an abend): the just-created file is
    /// removed and the base's latest generation is unchanged. No-op if there is no pending generation.
    /// </summary>
    public void Discard(string baseName)
    {
        GdgBase b = Ensure(baseName);
        if (b.PendingPath is null) return;
        if (File.Exists(b.PendingPath)) File.Delete(b.PendingPath);
        b.PendingPath = null;
        b.PendingNumber = 0;
    }

    /// <summary>The number of catalogued generations currently retained for the base.</summary>
    public int GenerationCount(string baseName) => Ensure(baseName).Generations.Count;

    private string GenerationPath(string baseName, int generation)
    {
        // Mirror the z/OS GnnnnV00 generation suffix as a filesystem-safe name.
        string safeBase = baseName.Replace('.', '_');
        return Path.Combine(_root, $"{safeBase}.G{generation:D4}V00.dat");
    }

    private sealed class GdgBase(string name, int limit, bool scratch)
    {
        public string Name { get; } = name;
        public int Limit { get; set; } = limit;
        public bool Scratch { get; set; } = scratch;
        public List<int> Generations { get; } = [];
        public int LatestNumber { get; set; }
        public int PendingNumber { get; set; }
        public string? PendingPath { get; set; }
    }
}
