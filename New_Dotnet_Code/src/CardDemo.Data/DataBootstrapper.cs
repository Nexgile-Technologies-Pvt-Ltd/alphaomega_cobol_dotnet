using CardDemo.Runtime;

namespace CardDemo.Data;

/// <summary>
/// Loads the CardDemo catalog into a <see cref="CardDemoDatabase"/> from the authoritative EBCDIC
/// dataset images. Records are stored byte-exact; no decoding happens at load time, so the database is
/// a faithful image of the source data.
/// </summary>
public sealed class DataBootstrapper(string ebcdicDataDirectory)
{
    private readonly string _dir = ebcdicDataDirectory;

    /// <summary>Defines and loads every catalog file that has a source dataset, returning the open accessors.</summary>
    public BootstrapResult BootstrapAll(CardDemoDatabase db)
    {
        var vsam = new Dictionary<string, VsamFile>(StringComparer.Ordinal);
        foreach (VsamFileSpec spec in CardDemoFiles.Vsam)
        {
            VsamFile file = db.DefineFile(spec.Definition);
            if (spec.SourceDataFile is not null)
                LoadVsam(file, spec);
            vsam[spec.Definition.Name] = file;
        }

        var sequential = new Dictionary<string, SequentialFile>(StringComparer.Ordinal);
        foreach (SequentialFileSpec spec in CardDemoFiles.Sequential)
        {
            SequentialFile file = db.DefineSequentialFile(spec.Name, spec.RecordLength);
            if (spec.SourceDataFile is not null)
                file.LoadImage(File.ReadAllBytes(Path.Combine(_dir, spec.SourceDataFile)));
            sequential[spec.Name] = file;
        }

        return new BootstrapResult(vsam, sequential);
    }

    private void LoadVsam(VsamFile file, VsamFileSpec spec)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(_dir, spec.SourceDataFile!));
        int reclen = spec.Definition.RecordLength;
        if (data.Length % reclen != 0)
            throw new InvalidDataException(
                $"{spec.Definition.Name}: dataset length {data.Length} is not a multiple of record length {reclen}.");

        for (int i = 0; i < data.Length; i += reclen)
        {
            string status = file.Write(data[i..(i + reclen)]);
            if (status != FileStatus.Ok)
                throw new InvalidDataException(
                    $"{spec.Definition.Name}: load failed at record {i / reclen} with FILE STATUS '{status}'.");
        }
    }
}

/// <summary>The opened file accessors produced by <see cref="DataBootstrapper.BootstrapAll"/>.</summary>
public sealed record BootstrapResult(
    IReadOnlyDictionary<string, VsamFile> Vsam,
    IReadOnlyDictionary<string, SequentialFile> Sequential)
{
    public VsamFile File(string name) => Vsam[name];
    public SequentialFile Seq(string name) => Sequential[name];
}
