namespace CardDemo.Online;

/// <summary>
/// What a handler asked the runtime to do when it finished a turn — the .NET stand-in for the
/// terminating <c>EXEC CICS</c> command (RETURN / XCTL). The <see cref="Dispatcher"/> reads this after
/// <see cref="ITransactionHandler.Handle"/> and acts on it.
/// </summary>
public enum CicsOutcomeKind
{
    /// <summary>
    /// <c>EXEC CICS RETURN TRANSID(t) COMMAREA(c)</c> — end the task pseudo-conversationally; the next
    /// keystroke re-enters transaction <see cref="CicsOutcome.TransId"/> with the saved COMMAREA.
    /// </summary>
    ReturnTransId,

    /// <summary>
    /// <c>EXEC CICS RETURN</c> with no TRANSID — end the conversation entirely (exit path, after a
    /// SEND TEXT). The dispatcher loop terminates.
    /// </summary>
    ReturnTerminal,

    /// <summary>
    /// <c>EXEC CICS XCTL PROGRAM(p) COMMAREA(c)</c> — transfer to another program in the same task with
    /// no return; the dispatcher runs <see cref="CicsOutcome.Program"/> next.
    /// </summary>
    Xctl,
}

/// <summary>
/// Immutable record of a handler's terminating CICS request (see <see cref="CicsOutcomeKind"/>), carrying
/// the target TRANSID / PROGRAM and the COMMAREA to pass on. Produced by the <c>Return*</c>/<c>Xctl</c>
/// helpers on <see cref="CicsContext"/>.
/// </summary>
public sealed class CicsOutcome
{
    public CicsOutcomeKind Kind { get; }

    /// <summary>Target transaction id for <see cref="CicsOutcomeKind.ReturnTransId"/>.</summary>
    public string? TransId { get; }

    /// <summary>Target program name for <see cref="CicsOutcomeKind.Xctl"/>.</summary>
    public string? Program { get; }

    /// <summary>COMMAREA passed forward (null = none / EIBCALEN 0 on the next entry).</summary>
    public CardDemoCommArea? CommArea { get; }

    private CicsOutcome(CicsOutcomeKind kind, string? transId, string? program, CardDemoCommArea? commArea)
    {
        Kind = kind;
        TransId = transId;
        Program = program;
        CommArea = commArea;
    }

    public static CicsOutcome ReturnTransId(string transId, CardDemoCommArea? commArea) =>
        new(CicsOutcomeKind.ReturnTransId, transId, null, commArea);

    public static CicsOutcome ReturnTerminal() =>
        new(CicsOutcomeKind.ReturnTerminal, null, null, null);

    public static CicsOutcome Xctl(string program, CardDemoCommArea? commArea) =>
        new(CicsOutcomeKind.Xctl, null, program, commArea);
}
