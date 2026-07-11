namespace CardDemo.Online;

/// <summary>
/// In-process <see cref="IProgramRegistry"/>: a table of program name -> handler factory and
/// transaction id -> entry program name. Factories are invoked fresh on every <see cref="Resolve"/> so
/// each task gets a clean handler (reinitialised WORKING-STORAGE), matching CICS pseudo-conversational
/// semantics.
/// </summary>
public sealed class ProgramRegistry : IProgramRegistry
{
    private readonly Dictionary<string, Func<ITransactionHandler>> _byProgram =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _transToProgram =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a program by its factory. If the factory's handler declares a non-empty
    /// <see cref="ITransactionHandler.TransId"/>, that transaction is wired to this program too.
    /// </summary>
    public ProgramRegistry Register(Func<ITransactionHandler> factory)
    {
        // Probe once to learn the program name / transaction id without retaining the instance.
        ITransactionHandler probe = factory();
        string program = probe.ProgramName;
        _byProgram[program] = factory;
        if (!string.IsNullOrWhiteSpace(probe.TransId))
            _transToProgram[probe.TransId.Trim()] = program;
        return this;
    }

    /// <summary>Registers a program under an explicit program name and (optional) transaction id.</summary>
    public ProgramRegistry Register(string programName, Func<ITransactionHandler> factory, string? transId = null)
    {
        _byProgram[programName] = factory;
        if (!string.IsNullOrWhiteSpace(transId))
            _transToProgram[transId!.Trim()] = programName;
        return this;
    }

    /// <summary>Wires an extra transaction id to an already-registered program.</summary>
    public ProgramRegistry MapTransaction(string transId, string programName)
    {
        _transToProgram[transId.Trim()] = programName;
        return this;
    }

    public bool HasProgram(string programName) => _byProgram.ContainsKey(programName);

    public ITransactionHandler Resolve(string programName)
    {
        if (!_byProgram.TryGetValue(programName, out var factory))
            throw new KeyNotFoundException($"No online program registered for '{programName}'.");
        return factory();
    }

    public string? ProgramForTransId(string transId) =>
        _transToProgram.TryGetValue(transId.Trim(), out var program) ? program : null;
}
