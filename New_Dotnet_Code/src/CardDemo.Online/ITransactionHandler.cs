namespace CardDemo.Online;

/// <summary>
/// One online CICS program (e.g. <c>COSGN00C</c>, <c>COMEN01C</c>, <c>COADM01C</c>) ported to .NET. Each
/// implementation is a near-mechanical port of the program's <c>PROCEDURE DIVISION</c>: it reads
/// <see cref="CicsContext.EibAid"/>/<see cref="CicsContext.EibCalen"/>/<see cref="CicsContext.CommArea"/>,
/// drives the screen via <see cref="CicsContext.SendMap"/>/<see cref="CicsContext.ReceiveMap"/>, and ends
/// by recording a RETURN/XCTL outcome on the context.
/// </summary>
/// <remarks>
/// Handlers are <b>stateless across turns</b>: the dispatcher creates a fresh instance per task so that
/// WORKING-STORAGE starts at its COBOL VALUE/SPACES defaults every turn, exactly as the
/// pseudo-conversational model requires. All cross-turn state lives in the COMMAREA.
/// </remarks>
public interface ITransactionHandler
{
    /// <summary>
    /// The CICS program name this handler implements (e.g. <c>"COSGN00C"</c>). Used by the program
    /// registry for XCTL/LINK target resolution.
    /// </summary>
    string ProgramName { get; }

    /// <summary>The default transaction id that enters this program (e.g. <c>"CC00"</c>), if any.</summary>
    string TransId { get; }

    /// <summary>Runs one pseudo-conversational task and records its terminating outcome on <paramref name="ctx"/>.</summary>
    void Handle(CicsContext ctx);
}

/// <summary>
/// Factory + lookup for online handlers. Maps a CICS <b>program</b> name (XCTL/LINK target) and a
/// <b>transaction</b> id (RETURN TRANSID / initial entry) to a freshly-constructed
/// <see cref="ITransactionHandler"/> — fresh per call so working storage is reinitialised every task.
/// </summary>
public interface IProgramRegistry
{
    /// <summary>New handler instance for a program name. Throws if the program is unknown.</summary>
    ITransactionHandler Resolve(string programName);

    /// <summary>The entry program name for a transaction id, or <c>null</c> if the transaction is unknown.</summary>
    string? ProgramForTransId(string transId);

    /// <summary>True if a handler is registered for the given program name.</summary>
    bool HasProgram(string programName);
}
