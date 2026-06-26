using CardDemo.Cobol.Runtime;

namespace CardDemo.Online;

/// <summary>
/// Supplies the AID for each pseudo-conversational turn — the console/test stand-in for "the operator
/// pressed a key and the terminal sent a RECEIVE". Returning <c>null</c> ends the conversation (the
/// operator disconnected), so the dispatcher stops.
/// </summary>
public interface IAidSource
{
    /// <summary>
    /// The AID that begins the next turn of transaction <paramref name="transId"/>, or <c>null</c> to end
    /// the session. Called once per loop iteration before the handler runs.
    /// </summary>
    AidKey? NextAid(string transId);
}

/// <summary>
/// Drives the CICS pseudo-conversational loop over an <see cref="IProgramRegistry"/>: restore COMMAREA,
/// instantiate the handler (fresh working storage), <c>Handle</c>, then act on the recorded outcome —
/// re-loop on <c>RETURN TRANSID</c> or run the <c>XCTL</c> target in the same turn.
/// </summary>
/// <remarks>
/// Per the design note §4 / §6:
/// <list type="number">
/// <item>RESTORE state — load the COMMAREA saved by the previous turn (null on cold start => EIBCALEN 0).</item>
/// <item>REINIT WS — resolve a fresh handler instance from the registry.</item>
/// <item>Build a <see cref="CicsContext"/> with the turn's AID + COMMAREA and run <c>Handle</c>.</item>
/// <item>Act on <see cref="CicsContext.Outcome"/>: <c>RETURN TRANSID</c> re-loops (next AID re-enters that
///   transaction); <c>XCTL</c> runs the target program immediately in the same turn (program switch, no
///   new keystroke); <c>RETURN</c> (no transid) ends the conversation.</item>
/// </list>
/// </remarks>
public sealed class Dispatcher
{
    private readonly IProgramRegistry _programs;
    private readonly IScreenIo _screen;
    private readonly IClock _clock;

    /// <summary>Safety cap on XCTL chaining within a single turn to catch accidental program-switch loops.</summary>
    public int MaxXctlChain { get; init; } = 64;

    public Dispatcher(IProgramRegistry programs, IScreenIo screen, IClock? clock = null)
    {
        _programs = programs;
        _screen = screen;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>
    /// Runs the conversation starting at transaction <paramref name="startTransId"/> until a terminal
    /// RETURN or until <paramref name="aidSource"/> stops supplying AIDs. <paramref name="initialCommArea"/>
    /// is the COMMAREA for the very first turn (normally <c>null</c> = cold start, EIBCALEN 0).
    /// </summary>
    public void Run(string startTransId, IAidSource aidSource, CardDemoCommArea? initialCommArea = null)
    {
        string? transId = startTransId;
        CardDemoCommArea? commArea = initialCommArea;
        // First turn of a brand-new transaction is the cold-start display: no AID yet.
        AidKey? pendingAid = AidKey.None;

        while (transId is not null)
        {
            AidKey aid;
            if (pendingAid is { } forced)
            {
                aid = forced;
                pendingAid = null;
            }
            else
            {
                AidKey? next = aidSource.NextAid(transId);
                if (next is null) return; // operator disconnected
                aid = next.Value;
            }

            CicsOutcome outcome = RunTurn(transId, aid, commArea);

            switch (outcome.Kind)
            {
                case CicsOutcomeKind.ReturnTransId:
                    transId = outcome.TransId;
                    commArea = outcome.CommArea;
                    // Next loop iteration asks the AID source for the operator's next keystroke.
                    break;

                case CicsOutcomeKind.Xctl:
                    // Program switch handled inside RunTurn; outcome already reduced to RETURN/terminal.
                    // (RunTurn never returns Xctl — see below.)
                    throw new InvalidOperationException("XCTL outcome should have been resolved by RunTurn.");

                case CicsOutcomeKind.ReturnTerminal:
                    return;
            }
        }
    }

    /// <summary>
    /// Runs a single transaction turn: resolves the entry program for <paramref name="transId"/>, executes
    /// it, and follows any <c>XCTL</c> chain within the same turn until the running program ends with a
    /// <c>RETURN</c>. Returns that terminating outcome (never an <see cref="CicsOutcomeKind.Xctl"/>).
    /// </summary>
    public CicsOutcome RunTurn(string transId, AidKey aid, CardDemoCommArea? commArea)
    {
        string? program = _programs.ProgramForTransId(transId)
            ?? (_programs.HasProgram(transId) ? transId : null)
            ?? throw new KeyNotFoundException($"No online program/transaction registered for TRANSID '{transId}'.");

        CardDemoCommArea? currentCa = commArea;
        AidKey currentAid = aid;

        for (int hop = 0; ; hop++)
        {
            if (hop > MaxXctlChain)
                throw new InvalidOperationException(
                    $"XCTL chain exceeded {MaxXctlChain} hops starting from TRANSID '{transId}' — likely a loop.");

            // REINIT WS: a fresh handler instance for this program (clean working storage).
            ITransactionHandler handler = _programs.Resolve(program);

            // RESTORE state into a fresh context. COMMAREA is passed by value (clone) so a handler's
            // mutations never leak unless it RETURNs/XCTLs them forward.
            var ctx = new CicsContext(
                _screen, currentAid, currentCa?.Clone(), transId, _clock, _programs);

            handler.Handle(ctx);

            CicsOutcome outcome = ctx.Outcome ??
                // A handler that forgot to RETURN behaves like RETURN TRANSID(self) with the current CA
                // (defensive; real ported handlers always issue an explicit RETURN/XCTL).
                CicsOutcome.ReturnTransId(transId, ctx.CommArea);

            if (outcome.Kind != CicsOutcomeKind.Xctl)
                return outcome;

            // XCTL: switch program in the same turn, carry the COMMAREA, no new keystroke. The transferred
            // program sees EIBAID = the same AID (CICS does not change EIBAID on XCTL).
            program = outcome.Program!;
            currentCa = outcome.CommArea;
        }
    }
}
