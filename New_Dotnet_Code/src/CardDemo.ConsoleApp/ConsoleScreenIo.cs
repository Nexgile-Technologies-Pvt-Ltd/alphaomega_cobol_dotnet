using CardDemo.ConsoleApp.Maps;
using CardDemo.Online;

namespace CardDemo.ConsoleApp;

/// <summary>
/// The console implementation of <see cref="IScreenIo"/>: the 3270 terminal emulated on a 24x80 text
/// console. It renders a <see cref="BmsMap"/> through the headless <see cref="TextRenderer"/> into a
/// <see cref="ScreenBuffer"/>, flushes that buffer to the real <see cref="Console"/> via
/// <see cref="ConsoleRenderer"/>, parks the cursor at the IC field, and runs the interactive input loop —
/// reading keystrokes into the map's unprotected fields (with Tab/Shift-Tab navigation) until an AID key
/// ends the RECEIVE (<c>Enter</c> / PF-keys → <see cref="AidKey"/>). See <c>_design/CONSOLE_RUNTIME.md</c>
/// §5 / §6.
/// </summary>
/// <remarks>
/// <para>The "symbolic map" object the CICS shim passes on SEND/RECEIVE is the <see cref="BmsMap"/> field
/// model itself: a handler builds (or asks the catalog for) a map, populates its named output fields, and
/// SENDs it; the host keeps that instance live so a following RECEIVE reads the same fields the operator
/// keyed into. A handler that passes some other symbolic object falls back to a freshly-built map from the
/// <see cref="BmsMapCatalog"/> keyed by the map name.</para>
/// <para>This class also doubles as the <see cref="IAidSource"/> for the dispatcher: between turns the
/// dispatcher asks for "the next operator keystroke", which (in a pure SEND/RECEIVE handler) is supplied by
/// the RECEIVE that already happened — so the source returns a queued AID, or reads one fresh when a turn
/// did no RECEIVE (e.g. the cold-start display).</para>
/// </remarks>
public sealed class ConsoleScreenIo : IScreenIo, IAidSource
{
    private readonly BmsMapCatalog _catalog;
    private readonly ScreenBuffer _buffer;
    private readonly TextRenderer _renderer;
    private readonly ConsoleRenderer _console;

    /// <summary>The map model most recently SENT — the live screen the operator is looking at.</summary>
    private BmsMap? _liveMap;

    /// <summary>An AID captured by a RECEIVE that the dispatcher's AID source should hand back next.</summary>
    private AidKey? _queuedAid;

    public ConsoleScreenIo(BmsMapCatalog? catalog = null, ConsoleRenderer? console = null)
    {
        _catalog = catalog ?? BmsMapCatalog.Default;
        _buffer = new ScreenBuffer();
        _renderer = new TextRenderer(_buffer);
        _console = console ?? new ConsoleRenderer();
    }

    /// <summary>The current logical 24x80 grid (for parity inspection / tests).</summary>
    public ScreenBuffer Buffer => _buffer;

    // === IScreenIo ===========================================================================

    /// <summary>
    /// <c>EXEC CICS SEND MAP</c>. Resolves <paramref name="symbolicMap"/> to a <see cref="BmsMap"/> (the
    /// object itself, or a fresh map from the catalog keyed by <paramref name="map"/>), renders it onto the
    /// buffer honouring <c>ERASE</c> / the cursor request, and flushes the frame to the console.
    /// </summary>
    public void SendMap(string map, string mapset, object symbolicMap, SendMapOptions options)
    {
        BmsMap model = ResolveMap(map, symbolicMap);
        _liveMap = model;

        // ERASE clears the buffer and drops all MDTs; DATAONLY keeps the existing constant template.
        _renderer.Render(model, erase: options.Erase);

        // An explicit CURSOR offset on the SEND overrides the map's IC / ...L = -1 field.
        if (options.Cursor >= 0)
        {
            _buffer.CursorRow = (options.Cursor / _buffer.Cols) + 1;
            _buffer.CursorCol = (options.Cursor % _buffer.Cols) + 1;
        }

        _console.Draw(_buffer);
    }

    /// <summary>
    /// <c>EXEC CICS RECEIVE MAP</c>. Runs the interactive input loop over the live map's keyable fields and
    /// returns the AID that ended it. The keyed values land in the map's symbolic <c>...I</c> cells (the
    /// field <see cref="ScreenField.Value"/>), with the MDT rule applied so unmodified non-FSET fields stay
    /// empty. The captured AID is also queued for the dispatcher's <see cref="IAidSource"/>.
    /// </summary>
    public AidKey ReceiveMap(string map, string mapset, object symbolicMap)
    {
        BmsMap model = ResolveMap(map, symbolicMap, preferLive: true);
        AidKey aid = RunInputLoop(model);
        _queuedAid = aid;
        return aid;
    }

    /// <summary>
    /// <c>EXEC CICS SEND TEXT ... ERASE FREEKB</c>. Clears the screen and prints a single line — the
    /// pseudo-conversational exit path (e.g. the PF3 thank-you message) before a no-TRANSID RETURN.
    /// </summary>
    public void SendText(string text, bool erase = true, bool freeKb = true)
    {
        if (erase) _buffer.Clear();
        // Paint the text on the top line of the logical buffer so parity inspection sees it.
        string line = (text ?? "").Length > _buffer.Cols ? text![.._buffer.Cols] : (text ?? "");
        for (int c = 0; c < line.Length; c++)
            _buffer.Set(1, c + 1, new ScreenCell { Ch = line[c], Color = BmsColor.Neutral });
        _buffer.CursorRow = 1;
        _buffer.CursorCol = 1;
        _console.Draw(_buffer);
    }

    // === IAidSource ==========================================================================

    /// <summary>
    /// Supplies the AID that begins the next pseudo-conversational turn. A turn that issued a RECEIVE has
    /// already captured the operator's keystroke, so that AID is returned here once. A turn that only
    /// displayed (cold start, no RECEIVE) leaves nothing queued, so we read a fresh keystroke from the
    /// keyboard. Returns <c>null</c> only when the input stream is exhausted (operator disconnected).
    /// </summary>
    public AidKey? NextAid(string transId)
    {
        if (_queuedAid is { } queued)
        {
            _queuedAid = null;
            return queued;
        }
        return ReadAidKey();
    }

    // === input loop ==========================================================================

    /// <summary>
    /// The 3270 keyboard loop: positions the cursor at the IC field, then edits the map's keyable fields
    /// until an AID key is pressed. Returns the AID; the keyed text is left in each field's
    /// <see cref="ScreenField.Value"/> with its MDT set, honouring NUM / JUSTIFY and DRK masking.
    /// </summary>
    private AidKey RunInputLoop(BmsMap map)
    {
        List<ScreenField> keyable = map.KeyableFields().ToList();
        if (keyable.Count == 0)
        {
            // No input fields: any AID key just ends the turn (e.g. a display-only panel).
            return ReadAidKey() ?? AidKey.Enter;
        }

        // Working copy of each field's keyed text, sized to the field; clear it for a fresh RECEIVE so the
        // operator types over the (possibly FSET) template.
        var edited = new char[keyable.Count][];
        var len = new int[keyable.Count];
        for (int i = 0; i < keyable.Count; i++)
            edited[i] = new char[keyable[i].Length];

        int active = StartField(map, keyable);
        int caret = 0; // position within the active field

        SyncCursor(keyable[active], caret);

        while (true)
        {
            ConsoleKeyInfo? read = TryReadKey();
            if (read is null)
            {
                // End of input mid-edit: treat as Enter so the keyed fields are committed and the turn ends.
                CommitFields(keyable, edited, len);
                return AidKey.Enter;
            }
            ConsoleKeyInfo key = read.Value;
            AidKey? aid = ConsoleKeyMap.ToAid(key);
            if (aid is { } ended)
            {
                CommitFields(keyable, edited, len);
                return ended;
            }

            switch (key.Key)
            {
                case ConsoleKey.Tab:
                    active = (key.Modifiers & ConsoleModifiers.Shift) != 0
                        ? Prev(active, keyable.Count)
                        : Next(active, keyable.Count);
                    caret = len[active];
                    break;

                case ConsoleKey.Backspace:
                    if (caret > 0)
                    {
                        caret--;
                        RemoveAt(edited[active], ref len[active], caret);
                        EchoField(keyable[active], edited[active], len[active]);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (caret > 0) caret--;
                    break;

                case ConsoleKey.RightArrow:
                    if (caret < len[active]) caret++;
                    break;

                case ConsoleKey.Home:
                    caret = 0;
                    break;

                case ConsoleKey.End:
                    caret = len[active];
                    break;

                default:
                    char ch = key.KeyChar;
                    if (!char.IsControl(ch) && ch != '\0')
                    {
                        ScreenField f = keyable[active];
                        if (f.IsNumeric && !char.IsDigit(ch)) break; // NUM field rejects non-digits
                        if (caret < f.Length)
                        {
                            InsertAt(edited[active], ref len[active], caret, ch, f.Length);
                            caret = Math.Min(caret + 1, f.Length);
                            EchoField(f, edited[active], len[active]);
                            // Auto-skip: filling an ASKIP-adjacent field jumps to the next keyable field.
                            if (caret >= f.Length && f.EffectiveAttribute.IsAutoSkip())
                            {
                                active = Next(active, keyable.Count);
                                caret = len[active];
                            }
                        }
                    }
                    break;
            }

            SyncCursor(keyable[active], caret);
        }
    }

    /// <summary>The keyable index that should hold the initial cursor (the IC / ...L = -1 field).</summary>
    private static int StartField(BmsMap map, List<ScreenField> keyable)
    {
        ScreenField? cursorField = map.CursorField();
        if (cursorField is not null)
        {
            int idx = keyable.IndexOf(cursorField);
            if (idx >= 0) return idx;
        }
        return 0;
    }

    /// <summary>Writes the edited text of each keyable field back into the map (the symbolic in-map),
    /// applying the MDT rule, DRK masking is irrelevant to the value, and NUM justify on commit.</summary>
    private static void CommitFields(List<ScreenField> keyable, char[][] edited, int[] len)
    {
        for (int i = 0; i < keyable.Count; i++)
        {
            ScreenField f = keyable[i];
            if (len[i] > 0)
            {
                // The operator keyed something: store it (TextRenderer applies NUM/JUSTIFY) and set MDT.
                TextRenderer.ApplyInput(f, new string(edited[i], 0, len[i]));
            }
            else if (!f.EffectiveAttribute.IsFset())
            {
                // Unmodified, non-FSET field: comes back empty (LOW-VALUES), MDT off — the "not entered"
                // case the handler distinguishes from a keyed blank.
                f.Value = "";
                f.Mdt = false;
                f.CursorLength = null;
            }
            // FSET + unkeyed: keep its pre-set MDT and current value (returned even if untouched).
        }
    }

    /// <summary>Echoes the field's current edited text to the screen buffer + console (DRK masks).</summary>
    private void EchoField(ScreenField field, char[] data, int count)
    {
        bool hidden = field.EffectiveAttribute.IsHidden();
        for (int i = 0; i < field.Length; i++)
        {
            char ch = i < count ? (hidden ? '*' : data[i]) : ' ';
            _buffer.Set(field.Row, field.Col + i, new ScreenCell
            {
                Ch = ch,
                Color = field.EffectiveColor,
                Hilight = field.Hilight,
                Bright = field.EffectiveAttribute.IsBright(),
                Protected = false,
                Hidden = false, // already masked into ch above
            });
            WriteCellToConsole(field.Row, field.Col + i, ch, field);
        }
    }

    private void WriteCellToConsole(int row, int col, char ch, ScreenField field)
    {
        if (!ConsoleRenderer.HasScreen) return;
        try
        {
            ConsoleColor saved = Console.ForegroundColor;
            Console.SetCursorPosition(col - 1, row - 1);
            Console.ForegroundColor = field.EffectiveColor.ToConsoleColor(field.EffectiveAttribute.IsBright());
            Console.Write(ch);
            Console.ForegroundColor = saved;
        }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private void SyncCursor(ScreenField field, int caret)
    {
        _buffer.CursorRow = field.Row;
        _buffer.CursorCol = field.Col + Math.Min(caret, Math.Max(0, field.Length - 1));
        ConsoleRenderer.PositionCursor(_buffer);
    }

    // === small editing helpers ===============================================================

    private static int Next(int i, int n) => (i + 1) % n;
    private static int Prev(int i, int n) => (i - 1 + n) % n;

    private static void InsertAt(char[] buf, ref int count, int at, char ch, int cap)
    {
        if (count < cap)
        {
            for (int i = count; i > at; i--) buf[i] = buf[i - 1];
            count++;
        }
        else
        {
            // Field full: overwrite at the caret (3270 insert-off behaviour).
            at = Math.Min(at, cap - 1);
        }
        buf[at] = ch;
    }

    private static void RemoveAt(char[] buf, ref int count, int at)
    {
        if (at < 0 || at >= count) return;
        for (int i = at; i < count - 1; i++) buf[i] = buf[i + 1];
        count--;
        buf[count] = '\0';
    }

    // === map / key resolution ================================================================

    private BmsMap ResolveMap(string mapName, object symbolicMap, bool preferLive = false)
    {
        if (symbolicMap is BmsMapBase typed) return typed.Map;
        if (symbolicMap is BmsMap direct) return direct;
        if (preferLive && _liveMap is { } live && string.Equals(live.Name, mapName, StringComparison.OrdinalIgnoreCase))
            return live;
        return _catalog.Find(mapName)
            ?? _liveMap
            ?? throw new KeyNotFoundException($"No BMS map model available for '{mapName}'.");
    }

    /// <summary>Reads keystrokes until an AID key is pressed; returns <c>null</c> at end of input.</summary>
    private AidKey? ReadAidKey()
    {
        while (true)
        {
            ConsoleKeyInfo? k = TryReadKey();
            if (k is null) return null; // end of input (operator disconnected)
            AidKey? aid = ConsoleKeyMap.ToAid(k.Value);
            if (aid is { } a) return a;
            // ignore non-AID keys when nothing is being edited
        }
    }

    /// <summary>
    /// Reads one keystroke as a <see cref="ConsoleKeyInfo"/>, blocking. Returns <c>null</c> at end of
    /// input. On a real console it uses <see cref="Console.ReadKey(bool)"/>; with redirected input it
    /// synthesises a key from the next character (newline → Enter, others → data keys) so the host runs
    /// headlessly from a pipe/file.
    /// </summary>
    private static ConsoleKeyInfo? TryReadKey()
    {
        if (Console.IsInputRedirected)
            return ReadKeyFromStream();

        return Console.ReadKey(intercept: true);
    }

    private static ConsoleKeyInfo? ReadKeyFromStream()
    {
        int c = Console.Read();
        if (c < 0) return null; // EOF

        return c switch
        {
            '\r' => new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
            '\n' => new ConsoleKeyInfo('\n', ConsoleKey.Enter, false, false, false),
            '\t' => new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            '\b' => new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            0x1b => new ConsoleKeyInfo((char)0x1b, ConsoleKey.Escape, false, false, false),
            _ => new ConsoleKeyInfo((char)c, KeyFor((char)c), false, false, false),
        };
    }

    private static ConsoleKey KeyFor(char c)
    {
        if (c >= '0' && c <= '9') return ConsoleKey.D0 + (c - '0');
        char up = char.ToUpperInvariant(c);
        if (up >= 'A' && up <= 'Z') return ConsoleKey.A + (up - 'A');
        return ConsoleKey.Spacebar; // any other printable: a generic data key
    }
}
