namespace CardDemo.Online;

/// <summary>
/// A BMS map (one <c>DFHMDI</c> within a <c>DFHMSD</c>): an ordered list of <see cref="ScreenField"/>,
/// rendered onto a 24x80 grid. See <c>_design/CONSOLE_RUNTIME.md</c> §1 and §5.
/// </summary>
public class BmsMap
{
    /// <summary>The DFHMDI map name (e.g. <c>COSGN0A</c>).</summary>
    public string Name { get; }

    /// <summary>The DFHMSD mapset name (e.g. <c>COSGN00</c>).</summary>
    public string Mapset { get; }

    /// <summary>Map height; 24 for CardDemo.</summary>
    public int Rows { get; }

    /// <summary>Map width; 80 for CardDemo.</summary>
    public int Cols { get; }

    /// <summary>The fields in map (paint) order.</summary>
    public IReadOnlyList<ScreenField> Fields { get; }

    private readonly Dictionary<string, ScreenField> _byName;

    public BmsMap(string name, string mapset, IEnumerable<ScreenField> fields, int rows = 24, int cols = 80)
    {
        Name = name;
        Mapset = mapset;
        Rows = rows;
        Cols = cols;
        Fields = fields.ToArray();
        _byName = new Dictionary<string, ScreenField>(StringComparer.Ordinal);
        foreach (var f in Fields)
            if (f.Name is { } n) _byName[n] = f;
    }

    /// <summary>Returns the named field, or <c>null</c> if no field with that name exists.</summary>
    public ScreenField? Find(string name) => _byName.TryGetValue(name, out var f) ? f : null;

    /// <summary>Returns the named field or throws when it is absent.</summary>
    public ScreenField Field(string name)
        => Find(name) ?? throw new KeyNotFoundException($"Field '{name}' not found on map '{Name}'.");

    /// <summary>Indexer over named fields.</summary>
    public ScreenField this[string name] => Field(name);

    /// <summary>The field that should receive the cursor: the one whose <c>...L</c> is -1, else the IC field, else null.</summary>
    public ScreenField? CursorField()
    {
        ScreenField? ic = null;
        foreach (var f in Fields)
        {
            if (f.CursorLength == -1) return f;            // explicit MOVE -1 TO xxxL overrides IC
            if (ic is null && f.Attribute.IsCursor()) ic = f;
        }
        return ic;
    }

    /// <summary>The keyable (unprotected, data-bearing) fields in map order.</summary>
    public IEnumerable<ScreenField> KeyableFields()
    {
        foreach (var f in Fields)
            if (f.IsKeyable) yield return f;
    }

    /// <summary>
    /// Resets the per-turn state of every field (symbolic overrides + MDT), as a fresh SEND with ERASE
    /// would. FSET fields have their MDT re-set on after this by the renderer.
    /// </summary>
    public void ResetTurn()
    {
        foreach (var f in Fields)
        {
            f.ResetTurnState();
            f.Mdt = false;
        }
    }
}

/// <summary>
/// Base class for generated, strongly-typed map wrappers (one subclass per BMS map). Subclasses expose
/// named accessors over the underlying <see cref="BmsMap"/> while inheriting the symbolic-map plumbing.
/// </summary>
public abstract class BmsMapBase
{
    /// <summary>The underlying field model this typed map wraps.</summary>
    public BmsMap Map { get; }

    protected BmsMapBase(BmsMap map) => Map = map;

    /// <summary>Convenience: resolves a field by name on the wrapped map.</summary>
    protected ScreenField F(string name) => Map.Field(name);

    /// <summary>Sets a named field's output value (symbolic <c>...O</c>), marking MDT on.</summary>
    protected void SetOut(string name, string? value) => Map.Field(name).SetValue(value);

    /// <summary>Reads a named field's trimmed input value (symbolic <c>...I</c>).</summary>
    protected string GetIn(string name) => Map.Field(name).Trimmed();

    /// <summary>Drops the cursor on a named field (COBOL <c>MOVE -1 TO xxxL</c>).</summary>
    protected void PutCursor(string name) => Map.Field(name).CursorLength = -1;
}
