using CardDemo.Cobol.Runtime;

namespace CardDemo.Online;

/// <summary>
/// One BMS field (one <c>DFHMDF</c>): a single 3270 attribute byte at <see cref="Row"/>/<see cref="Col"/>
/// followed by <see cref="Length"/> data bytes. Constant (unnamed) literal fields carry their text in
/// <see cref="Value"/>; named fields carry their runtime value there via the symbolic map.
/// See <c>_design/CONSOLE_RUNTIME.md</c> §1.
/// </summary>
public sealed class ScreenField
{
    /// <summary>The DFHMDF label, or <c>null</c> for an unnamed literal/constant field.</summary>
    public string? Name { get; init; }

    /// <summary>1-based line (POS line), 1..24.</summary>
    public int Row { get; init; }

    /// <summary>1-based column (POS col) of the field's <em>attribute</em> byte; data starts at Col+1 logically,
    /// but BMS POS is the attribute cell and the renderer writes data beginning at the attribute cell's
    /// column for parity with the CardDemo maps. 1..80.</summary>
    public int Col { get; init; }

    /// <summary>Data length (LENGTH=). 0 = stopper field (attribute cell only, no data). See §1.4.</summary>
    public int Length { get; init; }

    /// <summary>The static BMS attribute (ATTRB=).</summary>
    public BmsAttribute Attribute { get; init; } = BmsAttribute.Unprotected;

    /// <summary>The BMS colour (COLOR=).</summary>
    public BmsColor Color { get; init; } = BmsColor.Default;

    /// <summary>The BMS highlight (HILIGHT=).</summary>
    public BmsHilight Hilight { get; init; } = BmsHilight.Off;

    /// <summary>True when JUSTIFY=(RIGHT,...) — used by NUM fields to right-justify / zero-fill (e.g. OPTION).</summary>
    public bool RightJustify { get; init; }

    /// <summary>True when JUSTIFY=(...,ZERO) — zero-fill the justified field rather than blank-fill.</summary>
    public bool ZeroFill { get; init; }

    /// <summary>The PICIN edit pattern (input picture), if any. Currently informational (handlers de-edit raw text).</summary>
    public string? PicIn { get; init; }

    /// <summary>The PICOUT edit pattern (output picture), if any — delegated to <see cref="CobolEditedNumeric"/> on render.</summary>
    public string? PicOut { get; init; }

    // ---- mutable runtime state (the symbolic-map cell) -------------------------------------------

    /// <summary>
    /// The field value. For literals this is the INITIAL text (set once); for named fields it is the
    /// current symbolic <c>...O</c> (out) / <c>...I</c> (in) value. Never longer than <see cref="Length"/>
    /// after <see cref="SetValue"/>.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// Per-turn attribute override (symbolic <c>...A</c>), e.g. CSSETATY turning a field bright on error.
    /// When non-null it replaces the static <see cref="Attribute"/> for this SEND.
    /// </summary>
    public BmsAttribute? AttributeOverride { get; set; }

    /// <summary>Per-turn colour override (symbolic <c>...C</c>), e.g. CSSETATY -> DFHRED. Null = use <see cref="Color"/>.</summary>
    public BmsColor? ColorOverride { get; set; }

    /// <summary>
    /// The symbolic length / cursor cell (<c>...L</c>): when set to <c>-1</c> the cursor is placed on this
    /// field on SEND (COBOL <c>MOVE -1 TO xxxL</c>), overriding the map's IC field. On RECEIVE it carries
    /// the keyed input length. <c>null</c> = unset.
    /// </summary>
    public int? CursorLength { get; set; }

    /// <summary>The Modified Data Tag. True when this field will be transmitted on the next RECEIVE (§4.1).</summary>
    public bool Mdt { get; set; }

    // ---- derived attribute helpers --------------------------------------------------------------

    /// <summary>The attribute in effect for this turn (override if present, else the static BMS attribute).</summary>
    public BmsAttribute EffectiveAttribute => AttributeOverride ?? Attribute;

    /// <summary>The colour in effect for this turn (override if present, else the static BMS colour).</summary>
    public BmsColor EffectiveColor => ColorOverride ?? Color;

    /// <summary>True for a named (symbolic) field; false for an unnamed literal/constant.</summary>
    public bool IsNamed => Name is not null;

    /// <summary>True for a LENGTH=0 stopper field (attribute cell only, no data).</summary>
    public bool IsStopper => Length == 0;

    /// <summary>True when the operator may key into this field.</summary>
    public bool IsKeyable => EffectiveAttribute.IsKeyable() && Length > 0;

    /// <summary>True for a non-display (DRK) field.</summary>
    public bool IsHidden => EffectiveAttribute.IsHidden();

    /// <summary>True for a numeric-only (NUM) field.</summary>
    public bool IsNumeric => EffectiveAttribute.IsNumeric();

    // ---- value helpers --------------------------------------------------------------------------

    /// <summary>The value with trailing spaces removed (COBOL "trimmed" read of a fixed field).</summary>
    public string Trimmed() => Value.TrimEnd(' ');

    /// <summary>
    /// The value rendered to exactly <see cref="Length"/> chars: padded with spaces, honouring
    /// <see cref="RightJustify"/> / <see cref="ZeroFill"/> for justified NUM fields, and truncated
    /// (silently) if longer than the field — matching BMS placement into a fixed cell.
    /// </summary>
    public string Padded()
    {
        if (Length <= 0) return "";
        string v = Value ?? "";
        if (v.Length > Length) return v[..Length];
        char fill = ZeroFill && RightJustify ? '0' : ' ';
        return RightJustify ? v.PadLeft(Length, fill) : v.PadRight(Length, ' ');
    }

    /// <summary>
    /// The text painted on SEND for this field's data cells: the PICOUT-edited numeric when
    /// <see cref="PicOut"/> is present and <see cref="Value"/> parses as a number, otherwise <see cref="Padded"/>.
    /// </summary>
    public string RenderedText()
    {
        if (PicOut is { Length: > 0 } pic && decimal.TryParse(Value, out decimal n))
        {
            string edited = CobolEditedNumeric.Format(n, pic);
            return edited.Length >= Length ? edited[..Length] : edited.PadRight(Length, ' ');
        }
        return Padded();
    }

    /// <summary>
    /// Stores a value into this field, truncating to <see cref="Length"/> and marking the MDT on
    /// (an operator/handler write). Use when applying input or a handler MOVE.
    /// </summary>
    public void SetValue(string? value, bool setMdt = true)
    {
        string v = value ?? "";
        Value = v.Length > Length && Length > 0 ? v[..Length] : v;
        if (setMdt) Mdt = true;
    }

    /// <summary>Resets the per-turn symbolic overrides (used by ERASE / fresh SEND).</summary>
    public void ResetTurnState()
    {
        AttributeOverride = null;
        ColorOverride = null;
        CursorLength = null;
    }
}
