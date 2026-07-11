namespace CardDemo.Online;

/// <summary>
/// Renders a <see cref="BmsMap"/> onto a 24x80 <see cref="ScreenBuffer"/> (the SEND side) and applies
/// keyed input back onto the map's unprotected fields (the RECEIVE side), tracking the Modified Data
/// Tag. See <c>_design/CONSOLE_RUNTIME.md</c> §5.
/// </summary>
/// <remarks>
/// This is a pure, headless renderer: it produces the logical 24x80 grid (characters + logical
/// attributes) that the screen-parity tests assert on, and that a console front-end can flush with
/// real cursor/colour control. No <see cref="Console"/> I/O happens here.
/// </remarks>
public sealed class TextRenderer
{
    private readonly ScreenBuffer _buffer;

    /// <summary>The grid this renderer draws into.</summary>
    public ScreenBuffer Buffer => _buffer;

    public TextRenderer(ScreenBuffer? buffer = null) => _buffer = buffer ?? new ScreenBuffer();

    /// <summary>
    /// Paints <paramref name="map"/>'s fields onto the buffer (a CICS <c>SEND MAP</c>). When
    /// <paramref name="erase"/> is true the buffer is cleared first (<c>ERASE</c>) and all MDTs go off;
    /// FSET fields then get their MDT set on. The cursor is placed on the IC / <c>...L = -1</c> field.
    /// </summary>
    public void Render(BmsMap map, bool erase = true)
    {
        if (erase) _buffer.Clear();

        foreach (var field in map.Fields)
        {
            BmsAttribute attr = field.EffectiveAttribute;
            BmsColor color = field.EffectiveColor;
            bool bright = attr.IsBright();
            bool hidden = attr.IsHidden();
            bool prot = attr.IsProtected();

            // The attribute byte occupies one cell at POS; for parity with the CardDemo maps the data
            // is painted starting at the same column (BMS POS already points at the field origin used by
            // these maps' INITIAL placement). A LENGTH=0 stopper writes only its attribute cell.
            string text = field.IsStopper ? "" : field.RenderedText();

            for (int i = 0; i < Math.Max(field.Length, 1); i++)
            {
                int col = field.Col + i;
                if (!_buffer.InBounds(field.Row, col)) break;

                char ch;
                if (field.IsStopper)
                {
                    ch = ' ';
                }
                else if (hidden)
                {
                    ch = ' '; // DRK: never displayed regardless of value
                }
                else
                {
                    ch = i < text.Length ? text[i] : ' ';
                }

                _buffer.Set(field.Row, col, new ScreenCell
                {
                    Ch = ch,
                    Color = color,
                    Hilight = field.Hilight,
                    Bright = bright,
                    Protected = prot,
                    Hidden = hidden,
                });

                if (field.IsStopper) break; // stopper is exactly one cell
            }

            // FSET fields are returned on the next RECEIVE even if unkeyed: pre-set their MDT.
            if (attr.IsFset()) field.Mdt = true;
        }

        PositionCursor(map);
    }

    /// <summary>Places the buffer's cursor on the map's IC / <c>...L = -1</c> field; defaults to home.</summary>
    public void PositionCursor(BmsMap map)
    {
        var cursorField = map.CursorField();
        if (cursorField is not null && _buffer.InBounds(cursorField.Row, cursorField.Col))
        {
            _buffer.CursorRow = cursorField.Row;
            _buffer.CursorCol = cursorField.Col;
        }
        else
        {
            // No IC field: drop on the top-most keyable field, else home.
            var firstKeyable = map.KeyableFields().FirstOrDefault();
            if (firstKeyable is not null)
            {
                _buffer.CursorRow = firstKeyable.Row;
                _buffer.CursorCol = firstKeyable.Col;
            }
            else
            {
                _buffer.CursorRow = 1;
                _buffer.CursorCol = 1;
            }
        }
    }

    /// <summary>The 24 rendered text lines of the buffer.</summary>
    public string[] ToText() => _buffer.ToText();

    /// <summary>The current cursor position as a 1-based (row, col) tuple.</summary>
    public (int Row, int Col) Cursor => (_buffer.CursorRow, _buffer.CursorCol);

    /// <summary>The zero-based linear cursor address (<c>row*Cols + col</c>), as EIBCPOSN would carry it.</summary>
    public int CursorAddress => (_buffer.CursorRow - 1) * _buffer.Cols + (_buffer.CursorCol - 1);

    /// <summary>
    /// Applies a keyed string into a single named field (an operator typing then transmitting): stores
    /// the value (truncated to the field length), sets the field's MDT on, and records the keyed length
    /// in the symbolic <c>...L</c> cell. NUM + JUSTIFY=(RIGHT,ZERO) fields are right-justified / zero-filled.
    /// Protected fields are ignored. Returns true when the field accepted the input.
    /// </summary>
    public static bool ApplyInput(ScreenField field, string keyed)
    {
        if (!field.IsKeyable) return false;

        string v = keyed ?? "";
        if (field.IsNumeric)
        {
            // NUM fields accept only digits; non-digits are dropped (the 3270 keyboard would reject them).
            v = new string(v.Where(char.IsDigit).ToArray());
            if (field.RightJustify)
                v = (field.ZeroFill ? v.PadLeft(field.Length, '0') : v.PadLeft(field.Length, ' '));
        }

        if (v.Length > field.Length && field.Length > 0) v = v[..field.Length];

        field.Value = v;
        field.CursorLength = keyed?.Length ?? 0;
        field.Mdt = true;
        return true;
    }

    /// <summary>
    /// Applies a set of keyed values (field name -> text) onto a map's unprotected fields and returns the
    /// map. Honours the MDT rule: only the named fields present in <paramref name="input"/> are marked
    /// modified; FSET fields keep their pre-set MDT. This is the symbolic in-map after a RECEIVE.
    /// </summary>
    public static void ApplyInput(BmsMap map, IReadOnlyDictionary<string, string> input)
    {
        foreach (var (name, keyed) in input)
        {
            var field = map.Find(name);
            if (field is not null) ApplyInput(field, keyed);
        }
    }
}
