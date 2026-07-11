namespace CardDemo.Online;

/// <summary>
/// Options for an <c>EXEC CICS SEND MAP</c>, mirroring the BMS SEND keywords the CardDemo handlers use.
/// </summary>
public readonly record struct SendMapOptions
{
    /// <summary><c>ERASE</c> — clear the screen before painting (first SEND of a fresh map).</summary>
    public bool Erase { get; init; }

    /// <summary><c>DATAONLY</c> — repaint only field data, keep the existing constant/template text.</summary>
    public bool DataOnly { get; init; }

    /// <summary><c>FREEKB</c> — unlock the keyboard after the SEND.</summary>
    public bool FreeKb { get; init; }

    /// <summary><c>CURSOR</c> / IC — request the hardware cursor at this 0-based buffer offset (-1 = none).</summary>
    public int Cursor { get; init; }

    /// <summary>Convenience for the common <c>SEND ... ERASE</c> first-display case.</summary>
    public static SendMapOptions FirstDisplay => new() { Erase = true, FreeKb = true, Cursor = -1 };
}

/// <summary>
/// The screen-I/O surface the CICS shim hands to handlers. It abstracts the BMS renderer (supplied by
/// <c>CardDemo.ConsoleApp</c>) behind the two operations handlers actually perform — <c>SEND MAP</c> and
/// <c>RECEIVE MAP</c> — plus the plain-text <c>SEND TEXT</c> used by exit paths (e.g. COSGN00C's
/// "Thank you for using CardDemo"). <c>map</c>/<c>mapset</c> name the BMS map being driven; the symbolic
/// map object (<paramref name="symbolicMap"/>) is the generated I/O DSECT the handler populates/reads.
/// </summary>
/// <remarks>
/// Keeping this as an interface lets the shim, the handlers, and the screen-parity tests share one
/// contract: tests inject a recording <see cref="IScreenIo"/> to assert the field values and attributes a
/// handler SENDs without a real console, exactly as the design's screen-parity harness requires.
/// </remarks>
public interface IScreenIo
{
    /// <summary>
    /// <c>EXEC CICS SEND MAP(map) MAPSET(mapset) FROM(symbolicMap) ...</c>. Paints the symbolic out-map
    /// to the screen buffer with the requested options.
    /// </summary>
    void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options);

    /// <summary>
    /// <c>EXEC CICS RECEIVE MAP(map) MAPSET(mapset) INTO(symbolicMap)</c>. Copies the keyed (MDT-on)
    /// fields from the screen buffer into the symbolic in-map and returns the AID that ended the RECEIVE.
    /// </summary>
    AidKey ReceiveMap(string map, string mapset, object symbolicMap);

    /// <summary>
    /// <c>EXEC CICS SEND TEXT FROM(text) ERASE FREEKB</c>. Clears the screen and prints a single line —
    /// the pseudo-conversational exit path before a no-TRANSID RETURN.
    /// </summary>
    void SendText(string text, bool erase = true, bool freeKb = true);
}

/// <summary>
/// A no-op <see cref="IScreenIo"/> for unit tests or headless dispatch where no screen is attached.
/// <see cref="ReceiveMap"/> returns <see cref="AidKey.Enter"/>.
/// </summary>
public sealed class NullScreenIo : IScreenIo
{
    public static readonly NullScreenIo Instance = new();
    public void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options) { }
    public AidKey ReceiveMap(string map, string mapset, object symbolicMap) => AidKey.Enter;
    public void SendText(string text, bool erase = true, bool freeKb = true) { }
}
