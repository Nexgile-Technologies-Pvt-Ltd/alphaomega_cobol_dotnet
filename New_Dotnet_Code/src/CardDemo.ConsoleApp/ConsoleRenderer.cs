using CardDemo.Online;

namespace CardDemo.ConsoleApp;

/// <summary>
/// Flushes a logical 24x80 <see cref="ScreenBuffer"/> to the real <see cref="Console"/>: it paints every
/// cell with its BMS colour / intensity / highlight and then parks the hardware cursor where the buffer
/// asks. This is the only component that performs actual <see cref="Console"/> output; the rest of the
/// host works on the pure logical grid (see <c>_design/CONSOLE_RUNTIME.md</c> §5.1).
/// </summary>
/// <remarks>
/// When no full-screen console is attached (output redirected to a file/pipe, or no console buffer), the
/// renderer degrades to a plain line-by-line text dump of the frame — so the host still runs headlessly
/// for smoke tests and parity capture without throwing on cursor/clear operations.
/// </remarks>
public sealed class ConsoleRenderer
{
    /// <summary>When false, suppresses colour control (e.g. redirected output / dumb terminal).</summary>
    public bool UseColor { get; init; } = true;

    /// <summary>True when a real, addressable console screen is available for full-screen rendering.</summary>
    public static bool HasScreen => !Console.IsOutputRedirected && ConsoleBufferAvailable();

    /// <summary>
    /// Repaints the whole frame from <paramref name="buffer"/>. On an addressable console it clears the
    /// screen and paints each cell with colour; otherwise it dumps the 24 rows as plain text lines. At
    /// 24x80 there is no need to diff — the frame is cheap to redraw on each SEND.
    /// </summary>
    public void Draw(ScreenBuffer buffer)
    {
        if (!HasScreen)
        {
            DrawPlain(buffer);
            return;
        }

        bool color = UseColor;
        ConsoleColor savedFg = Console.ForegroundColor;
        ConsoleColor savedBg = Console.BackgroundColor;

        try
        {
            SafeSetCursorVisible(false);
            SafeClear();

            for (int row = 1; row <= buffer.Rows; row++)
            {
                SafeSetCursorPosition(0, row - 1);
                for (int col = 1; col <= buffer.Cols; col++)
                {
                    ScreenCell cell = buffer.Get(row, col);
                    if (color) ApplyCellColor(cell);
                    // The last column of the last row is left unwritten: writing it would scroll the
                    // console on most terminals. BMS leaves (24,80) blank in these maps anyway.
                    if (row == buffer.Rows && col == buffer.Cols) break;
                    Console.Write(cell.Hidden ? ' ' : cell.Ch);
                }
            }
        }
        finally
        {
            if (color)
            {
                Console.ForegroundColor = savedFg;
                Console.BackgroundColor = savedBg;
            }
        }

        // Park the hardware cursor at the buffer's requested position (the IC / -1 field).
        SafeSetCursorPosition(buffer.CursorCol - 1, buffer.CursorRow - 1);
        SafeSetCursorVisible(true);
    }

    /// <summary>Plain, no-positioning dump of the frame (headless / redirected output).</summary>
    private static void DrawPlain(ScreenBuffer buffer)
    {
        foreach (string line in buffer.ToText())
            Console.WriteLine(line.TrimEnd());
    }

    /// <summary>Moves the hardware cursor to the buffer's current 1-based (row, col), when addressable.</summary>
    public static void PositionCursor(ScreenBuffer buffer)
    {
        if (HasScreen) SafeSetCursorPosition(buffer.CursorCol - 1, buffer.CursorRow - 1);
    }

    private static void ApplyCellColor(ScreenCell cell)
    {
        ConsoleColor fg = cell.Color.ToConsoleColor(cell.Bright);
        if (cell.Hilight == BmsHilight.Reverse)
        {
            Console.BackgroundColor = fg;
            Console.ForegroundColor = ConsoleColor.Black;
        }
        else
        {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = ConsoleColor.Black;
        }
    }

    private static bool ConsoleBufferAvailable()
    {
        try
        {
            _ = Console.BufferWidth;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SafeClear()
    {
        try { Console.Clear(); }
        catch (IOException) { }
    }

    private static void SafeSetCursorPosition(int left, int top)
    {
        try
        {
            left = Math.Clamp(left, 0, Math.Max(0, Console.BufferWidth - 1));
            top = Math.Clamp(top, 0, Math.Max(0, Console.BufferHeight - 1));
            Console.SetCursorPosition(left, top);
        }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void SafeSetCursorVisible(bool visible)
    {
        try { Console.CursorVisible = visible; }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }
    }
}
