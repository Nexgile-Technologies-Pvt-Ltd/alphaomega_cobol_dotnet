namespace CardDemo.Online;

/// <summary>
/// A single rendered cell of the screen buffer: the character plus its logical attributes. The
/// screen-parity tests assert on these logical attributes rather than pixels
/// (see <c>ARCHITECTURE.md</c> §Verification.4).
/// </summary>
public struct ScreenCell
{
    public char Ch;
    public BmsColor Color;
    public BmsHilight Hilight;
    public bool Bright;
    public bool Protected;
    public bool Hidden;

    public static ScreenCell Blank => new()
    {
        Ch = ' ',
        Color = BmsColor.Default,
        Hilight = BmsHilight.Off,
        Bright = false,
        Protected = true,
        Hidden = false,
    };
}

/// <summary>
/// The two parallel 24x80 grids of the emulated 3270: characters and their logical attributes.
/// Origin (1,1) (top-left) maps to index [0,0]. See <c>_design/CONSOLE_RUNTIME.md</c> §5.
/// </summary>
public sealed class ScreenBuffer
{
    public int Rows { get; }
    public int Cols { get; }

    private readonly ScreenCell[,] _cells;

    /// <summary>The cursor position (1-based row/col); (1,1) by default.</summary>
    public int CursorRow { get; set; } = 1;

    /// <summary>The cursor column (1-based).</summary>
    public int CursorCol { get; set; } = 1;

    public ScreenBuffer(int rows = 24, int cols = 80)
    {
        Rows = rows;
        Cols = cols;
        _cells = new ScreenCell[rows, cols];
        Clear();
    }

    /// <summary>Clears both grids to blank (the ERASE effect), resetting the cursor to home.</summary>
    public void Clear()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c] = ScreenCell.Blank;
        CursorRow = 1;
        CursorCol = 1;
    }

    /// <summary>Bounds check for a 1-based (row,col).</summary>
    public bool InBounds(int row, int col) => row >= 1 && row <= Rows && col >= 1 && col <= Cols;

    /// <summary>Reads the cell at a 1-based (row,col).</summary>
    public ScreenCell Get(int row, int col) => _cells[row - 1, col - 1];

    /// <summary>Writes a cell at a 1-based (row,col); ignores out-of-bounds writes (BMS clips at the edge).</summary>
    public void Set(int row, int col, ScreenCell cell)
    {
        if (InBounds(row, col)) _cells[row - 1, col - 1] = cell;
    }

    /// <summary>Reads the character at a 1-based (row,col).</summary>
    public char CharAt(int row, int col) => _cells[row - 1, col - 1].Ch;

    /// <summary>Returns row <paramref name="row"/> (1-based) as a string of <see cref="Cols"/> chars.</summary>
    public string Line(int row)
    {
        Span<char> buf = stackalloc char[Cols];
        for (int c = 0; c < Cols; c++) buf[c] = _cells[row - 1, c].Ch;
        return new string(buf);
    }

    /// <summary>The whole screen as <see cref="Rows"/> lines of text (the parity-test rendering).</summary>
    public string[] ToText()
    {
        var lines = new string[Rows];
        for (int r = 1; r <= Rows; r++) lines[r - 1] = Line(r);
        return lines;
    }

    /// <summary>The whole screen as one newline-joined string.</summary>
    public override string ToString() => string.Join('\n', ToText());
}
