using CardDemo.Online;

namespace CardDemo.ConsoleApp;

/// <summary>
/// Translates a physical console keystroke into the logical 3270 <see cref="AidKey"/> reported in
/// <c>EIBAID</c>, per the AID table in <c>_design/CONSOLE_RUNTIME.md</c> §2:
/// Enter→Enter, Esc→Clear, PageUp/PageDown→PA1/PA2, F1..F12→PF1..PF12, Shift+F1..F12→PF13..PF24.
/// </summary>
/// <remarks>
/// Only AID-bearing keys end a RECEIVE; every other key is a data/edit keystroke handled by the input
/// loop. <see cref="ToAid"/> returns <c>null</c> for a non-AID key so the caller keeps editing.
/// </remarks>
public static class ConsoleKeyMap
{
    /// <summary>
    /// The AID for an AID-bearing keystroke, or <c>null</c> when the key is an ordinary data/edit key
    /// (printable, arrows, Backspace, Tab, …) that does not terminate the RECEIVE.
    /// </summary>
    public static AidKey? ToAid(ConsoleKeyInfo key)
    {
        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return AidKey.Enter;

            case ConsoleKey.Escape:
                return AidKey.Clear;

            case ConsoleKey.PageUp:
                return AidKey.Pa1;

            case ConsoleKey.PageDown:
                return AidKey.Pa2;

            case ConsoleKey.F1: return Pf(1, shift);
            case ConsoleKey.F2: return Pf(2, shift);
            case ConsoleKey.F3: return Pf(3, shift);
            case ConsoleKey.F4: return Pf(4, shift);
            case ConsoleKey.F5: return Pf(5, shift);
            case ConsoleKey.F6: return Pf(6, shift);
            case ConsoleKey.F7: return Pf(7, shift);
            case ConsoleKey.F8: return Pf(8, shift);
            case ConsoleKey.F9: return Pf(9, shift);
            case ConsoleKey.F10: return Pf(10, shift);
            case ConsoleKey.F11: return Pf(11, shift);
            case ConsoleKey.F12: return Pf(12, shift);

            default:
                return null; // data / editing key — keep typing
        }
    }

    /// <summary>
    /// The PF AID for function-key <paramref name="n"/> (1..12); when <paramref name="shift"/> is held it
    /// becomes PF13..PF24, which the COMMAREA folds back onto PFK01..PFK12 (see <see cref="CssTrpfy"/>).
    /// </summary>
    private static AidKey Pf(int n, bool shift)
    {
        int pf = shift ? n + 12 : n;
        return AidKey.Pf1 + (pf - 1);
    }
}
