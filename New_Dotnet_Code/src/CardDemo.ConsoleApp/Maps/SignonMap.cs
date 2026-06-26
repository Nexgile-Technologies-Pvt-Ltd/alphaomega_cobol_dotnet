using CardDemo.Online;

namespace CardDemo.ConsoleApp.Maps;

/// <summary>
/// Builds the BMS field model for the CardDemo sign-on screen (map <c>COSGN0A</c> in mapset
/// <c>COSGN00</c>). The layout mirrors <c>app/bms/COSGN00.bms</c> as captured in
/// <c>_design/specs/COSGN00C.md</c> §5.5 / §5.6 — the shared 3-line header, the User-ID / Password
/// input pair, the static prompts / footer, and the bright-red error line.
/// </summary>
/// <remarks>
/// Until the screen-model generator emits these maps, the console host hand-builds the one map it needs
/// to render an empty signon-style screen. Field names match the BMS labels so the ported
/// <c>COSGN00C</c> handler can drive them by name (<c>USERID</c>, <c>PASSWD</c>, <c>ERRMSG</c>, …).
/// </remarks>
public static class SignonMap
{
    /// <summary>The DFHMDI map name.</summary>
    public const string MapName = "COSGN0A";

    /// <summary>The DFHMSD mapset name.</summary>
    public const string MapsetName = "COSGN00";

    /// <summary>Builds a fresh <see cref="BmsMap"/> for the sign-on screen.</summary>
    public static BmsMap Build()
    {
        var fields = new List<ScreenField>
        {
            // --- shared 3-line header (Tran/Prog/Date/Time + titles) ---
            Literal(1, 1, 5, "Tran:", BmsColor.Blue),
            Named("TRNNAME", 1, 7, 4, Protected(BmsAttribute.Fset), BmsColor.Blue),
            Named("TITLE01", 1, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow),
            Literal(1, 65, 5, "Date:", BmsColor.Blue),
            Named("CURDATE", 1, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue),

            Literal(2, 1, 5, "Prog:", BmsColor.Blue),
            Named("PGMNAME", 2, 7, 8, Protected(BmsAttribute.Fset), BmsColor.Blue),
            Named("TITLE02", 2, 21, 40, Protected(BmsAttribute.Fset), BmsColor.Yellow),
            Literal(2, 65, 5, "Time:", BmsColor.Blue),
            Named("CURTIME", 2, 71, 8, Protected(BmsAttribute.Fset), BmsColor.Blue),

            // --- application / sign-on info lines (APPLID / SYSID on signon only) ---
            Literal(4, 36, 8, "Sign-on", BmsColor.Neutral, bright: true),

            // --- prompts + the two input fields ---
            Literal(8, 28, 24, "Type your User ID ...", BmsColor.Neutral),
            Literal(10, 28, 9, "User ID:", BmsColor.Turquoise),
            new ScreenField
            {
                Name = "USERID",
                Row = 10,
                Col = 42,
                Length = 8,
                // ATTRB=(FSET,IC,NORM,UNPROT) — keyable, MDT preset, cursor home.
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal | BmsAttribute.Fset | BmsAttribute.Ic,
                Color = BmsColor.Green,
            },
            Stopper(10, 51, BmsColor.Blue),

            Literal(11, 28, 9, "Password:", BmsColor.Turquoise),
            new ScreenField
            {
                Name = "PASSWD",
                Row = 11,
                Col = 42,
                Length = 8,
                // ATTRB=(DRK,FSET,UNPROT) — keyable but non-display (masked), MDT preset.
                Attribute = BmsAttribute.Unprotected | BmsAttribute.Dark | BmsAttribute.Fset,
                Color = BmsColor.Green,
            },
            Stopper(11, 51, BmsColor.Blue),

            // --- bright-red error line (ERRMSG) + footer legend ---
            Named("ERRMSG", 23, 1, 78, Protected(BmsAttribute.Bright | BmsAttribute.Fset), BmsColor.Red),
            Literal(24, 1, 22, "ENTER=Sign-on  F3=Exit", BmsColor.Yellow),
        };

        return new BmsMap(MapName, MapsetName, fields);
    }

    private static BmsAttribute Protected(BmsAttribute extra) =>
        BmsAttribute.AutoSkip | BmsAttribute.Normal | extra;

    private static ScreenField Literal(int row, int col, int len, string text, BmsColor color, bool bright = false) =>
        new()
        {
            Row = row,
            Col = col,
            Length = len,
            Attribute = BmsAttribute.AutoSkip | (bright ? BmsAttribute.Bright : BmsAttribute.Normal),
            Color = color,
            Value = text,
        };

    private static ScreenField Named(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    private static ScreenField Stopper(int row, int col, BmsColor color) =>
        new()
        {
            Row = row,
            Col = col,
            Length = 0,
            Attribute = BmsAttribute.AutoSkip | BmsAttribute.Normal,
            Color = color,
        };
}
