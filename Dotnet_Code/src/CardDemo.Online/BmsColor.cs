namespace CardDemo.Online;

/// <summary>
/// The BMS <c>COLOR=</c> value of a field (3270 extended-colour set). See
/// <c>_design/CONSOLE_RUNTIME.md</c> §1.2. <see cref="Default"/> means the field carried no
/// <c>COLOR=</c> operand (the device default green/neutral).
/// </summary>
public enum BmsColor
{
    /// <summary>No COLOR= operand — device default.</summary>
    Default = 0,
    Blue,
    Green,
    Red,
    Pink,
    Turquoise,
    Yellow,
    /// <summary>NEUTRAL — white on a colour terminal.</summary>
    Neutral,
}

/// <summary>
/// The BMS <c>HILIGHT=</c> value of a field. See <c>_design/CONSOLE_RUNTIME.md</c> §1.3.
/// </summary>
public enum BmsHilight
{
    /// <summary>OFF — no extended highlight (default).</summary>
    Off = 0,
    Blink,
    Reverse,
    Underline,
}

/// <summary>Maps a <see cref="BmsColor"/> to a <see cref="System.ConsoleColor"/> for the renderer.</summary>
public static class BmsColorExtensions
{
    /// <summary>
    /// Resolves the console colour for a BMS colour, lifting the dark variant to the bright variant
    /// when the field is BRT (high intensity), per <c>CONSOLE_RUNTIME.md</c> §1.2.
    /// </summary>
    public static ConsoleColor ToConsoleColor(this BmsColor color, bool bright = false) => color switch
    {
        BmsColor.Blue => bright ? ConsoleColor.Blue : ConsoleColor.DarkBlue,
        BmsColor.Green => bright ? ConsoleColor.Green : ConsoleColor.DarkGreen,
        BmsColor.Red => bright ? ConsoleColor.Red : ConsoleColor.DarkRed,
        BmsColor.Pink => ConsoleColor.Magenta,
        BmsColor.Turquoise => bright ? ConsoleColor.Cyan : ConsoleColor.DarkCyan,
        BmsColor.Yellow => ConsoleColor.Yellow,
        BmsColor.Neutral => bright ? ConsoleColor.White : ConsoleColor.Gray,
        _ => bright ? ConsoleColor.Green : ConsoleColor.Gray,
    };
}
