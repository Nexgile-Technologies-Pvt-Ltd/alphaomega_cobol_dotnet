using CardDemo.Cobol.Runtime;

namespace CardDemo.Online;

/// <summary>
/// The per-task CICS execution context handed to a handler — the .NET stand-in for the EIB plus the
/// COMMAREA and the screen surface. It is the single object a ported <c>PROCEDURE DIVISION</c> reads
/// (<see cref="EibAid"/>, <see cref="EibCalen"/>, <see cref="CommArea"/>) and writes its terminating
/// request through (<see cref="ReturnTransId"/> / <see cref="Xctl"/> / <see cref="Link"/>). One context is
/// built by the <see cref="Dispatcher"/> per pseudo-conversational turn.
/// </summary>
public sealed class CicsContext
{
    private readonly IProgramRegistry? _programs;

    /// <param name="screen">Screen-I/O surface for SEND/RECEIVE (use <see cref="NullScreenIo.Instance"/> headless).</param>
    /// <param name="aid">The AID that ended the previous RECEIVE (cold start: <see cref="AidKey.None"/>).</param>
    /// <param name="commArea">Restored COMMAREA, or <c>null</c> on cold start (EIBCALEN = 0).</param>
    /// <param name="transId">The transaction id under which this task is running (EIBTRNID).</param>
    /// <param name="clock">Clock for the header date/time; defaults to the system clock.</param>
    /// <param name="programs">Program registry used by <see cref="Link"/> (optional; required only if LINK is called).</param>
    public CicsContext(
        IScreenIo screen,
        AidKey aid,
        CardDemoCommArea? commArea,
        string transId = "",
        IClock? clock = null,
        IProgramRegistry? programs = null)
    {
        Screen = screen;
        EibAid = aid;
        CommArea = commArea;
        TransId = transId;
        Clock = clock ?? SystemClock.Instance;
        _programs = programs;
    }

    /// <summary>The screen-I/O surface for <c>SEND MAP</c> / <c>RECEIVE MAP</c> / <c>SEND TEXT</c>.</summary>
    public IScreenIo Screen { get; }

    /// <summary>Clock feeding the shared header date/time (<c>FUNCTION CURRENT-DATE</c> / ASKTIME).</summary>
    public IClock Clock { get; }

    /// <summary>
    /// <c>EIBAID</c> — the AID key that ended the last RECEIVE for this task. On cold start (first display)
    /// this is <see cref="AidKey.None"/>.
    /// </summary>
    public AidKey EibAid { get; set; }

    /// <summary>
    /// <c>EIBCALEN</c> — length of the passed COMMAREA in bytes; <c>0</c> on a cold start. Derived from
    /// <see cref="CommArea"/> (null COMMAREA => 0). Handlers branch on <c>EIBCALEN = 0</c> for first display.
    /// </summary>
    public int EibCalen => CommArea is null ? 0 : CardDemoCommArea.Length;

    /// <summary><c>EIBTRNID</c> — the transaction id this task is running under.</summary>
    public string TransId { get; set; }

    /// <summary>
    /// <c>EIBCPOSN</c> — cursor position from the last RECEIVE, as a 0-based 24x80 buffer offset
    /// (row*80 + col). -1 = unknown / not set.
    /// </summary>
    public int CursorPos { get; set; } = -1;

    /// <summary>
    /// The restored COMMAREA (typed view). <c>null</c> models CICS <c>EIBCALEN = 0</c> (cold start). The
    /// handler mutates this and passes it to <see cref="ReturnTransId"/> / <see cref="Xctl"/>.
    /// </summary>
    public CardDemoCommArea? CommArea { get; set; }

    /// <summary>The raw fixed-width COMMAREA image, or <c>null</c> when <see cref="CommArea"/> is null.</summary>
    public byte[]? CommAreaBytes => CommArea?.ToBytes();

    /// <summary>Name of the map this task last drove (mirrors <c>CDEMO-LAST-MAP</c>).</summary>
    public string CurrentMap { get; set; } = "";

    /// <summary>Name of the mapset this task last drove (mirrors <c>CDEMO-LAST-MAPSET</c>).</summary>
    public string CurrentMapSet { get; set; } = "";

    /// <summary>
    /// The terminating request the handler recorded (RETURN / XCTL). <c>null</c> until one of the
    /// <c>Return*</c>/<c>Xctl</c> helpers is called; the dispatcher acts on it after <c>Handle</c>.
    /// </summary>
    public CicsOutcome? Outcome { get; private set; }

    // === Screen hooks (thin wrappers so handlers read like the COBOL PERFORM SEND/RECEIVE) ===

    /// <summary><c>EXEC CICS SEND MAP</c>. Remembers the map/mapset as the current ones.</summary>
    public void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options)
    {
        CurrentMap = map;
        CurrentMapSet = mapset;
        Screen.SendMap(map, mapset, symbolicMap, options);
    }

    /// <summary><c>EXEC CICS RECEIVE MAP</c>; records the returned AID into <see cref="EibAid"/>.</summary>
    public AidKey ReceiveMap(string map, string mapset, object symbolicMap)
    {
        CurrentMap = map;
        CurrentMapSet = mapset;
        EibAid = Screen.ReceiveMap(map, mapset, symbolicMap);
        return EibAid;
    }

    /// <summary><c>EXEC CICS SEND TEXT ... ERASE FREEKB</c>.</summary>
    public void SendText(string text, bool erase = true, bool freeKb = true) =>
        Screen.SendText(text, erase, freeKb);

    /// <summary>
    /// Maps <see cref="EibAid"/> into the COMMAREA AID code (the <c>YYYY-STORE-PFKEY</c> idiom). Folds
    /// PF13..PF24 onto PFK01..PFK12 per <c>CSSTRPFY</c>.
    /// </summary>
    public CcardAid StorePfKey() => CssTrpfy.StorePfKey(EibAid);

    // === Terminating CICS commands recorded as outcomes for the dispatcher ===

    /// <summary>
    /// <c>EXEC CICS RETURN TRANSID(transId) COMMAREA(commArea)</c>. Ends the task; next keystroke
    /// re-enters <paramref name="transId"/> with the saved COMMAREA. Defaults to <see cref="CommArea"/>.
    /// </summary>
    public void ReturnTransId(string transId, CardDemoCommArea? commArea = null)
    {
        var ca = commArea ?? CommArea;
        Outcome = CicsOutcome.ReturnTransId(transId, ca?.Clone());
    }

    /// <summary><c>EXEC CICS RETURN</c> (no TRANSID) — terminate the conversation (exit path).</summary>
    public void ReturnTerminal() => Outcome = CicsOutcome.ReturnTerminal();

    /// <summary>
    /// <c>EXEC CICS XCTL PROGRAM(program) COMMAREA(commArea)</c>. Transfers to <paramref name="program"/>
    /// in the same task with no return; defaults the COMMAREA to <see cref="CommArea"/>.
    /// </summary>
    public void Xctl(string program, CardDemoCommArea? commArea = null)
    {
        var ca = commArea ?? CommArea;
        Outcome = CicsOutcome.Xctl(program, ca?.Clone());
    }

    /// <summary>
    /// <c>EXEC CICS LINK PROGRAM(program) COMMAREA(commArea)</c>. Synchronous nested call: the linked
    /// handler runs to its RETURN and control comes back here, with the (possibly updated) COMMAREA copied
    /// back into <paramref name="commArea"/> (and into <see cref="CommArea"/> if it is the same instance).
    /// Requires a program registry to have been supplied.
    /// </summary>
    public void Link(string program, CardDemoCommArea commArea)
    {
        if (_programs is null)
            throw new InvalidOperationException(
                $"LINK PROGRAM('{program}') requires a program registry; none was supplied to this context.");

        ITransactionHandler handler = _programs.Resolve(program);

        // Nested task: same AID/screen/clock, fresh context, COMMAREA passed by value.
        var nested = new CicsContext(Screen, EibAid, commArea.Clone(), TransId, Clock, _programs)
        {
            CursorPos = CursorPos,
        };
        handler.Handle(nested);

        // Copy back the linked program's view of the COMMAREA (LINK shares the storage with the caller).
        if (nested.Outcome?.CommArea is { } updated)
            CopyInto(commArea, updated);
        else if (nested.CommArea is { } end)
            CopyInto(commArea, end);
    }

    private static void CopyInto(CardDemoCommArea dest, CardDemoCommArea src)
    {
        var bytes = src.ToBytes();
        var parsed = CardDemoCommArea.Parse(bytes);
        dest.FromTranId = parsed.FromTranId;
        dest.FromProgram = parsed.FromProgram;
        dest.ToTranId = parsed.ToTranId;
        dest.ToProgram = parsed.ToProgram;
        dest.UserId = parsed.UserId;
        dest.UserType = parsed.UserType;
        dest.PgmContext = parsed.PgmContext;
        dest.CustId = parsed.CustId;
        dest.CustFName = parsed.CustFName;
        dest.CustMName = parsed.CustMName;
        dest.CustLName = parsed.CustLName;
        dest.AcctId = parsed.AcctId;
        dest.AcctStatus = parsed.AcctStatus;
        dest.CardNum = parsed.CardNum;
        dest.LastMap = parsed.LastMap;
        dest.LastMapSet = parsed.LastMapSet;
    }
}
