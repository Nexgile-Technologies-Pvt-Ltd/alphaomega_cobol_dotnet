namespace CardDemo.Online;

/// <summary>
/// The logical Attention IDentifier (AID) reported by CICS in <c>EIBAID</c> — the key the operator
/// pressed to end a RECEIVE. Values mirror the standard DFHAID set (<c>DFHENTER</c>, <c>DFHCLEAR</c>,
/// <c>DFHPA1/2</c>, <c>DFHPF1..PF24</c>). The console runtime captures a physical keystroke, maps it to
/// one of these, and exposes it as <see cref="CicsContext.EibAid"/>.
/// </summary>
/// <remarks>
/// The byte value attached to each member (<see cref="AidKeyExtensions.ToByte"/>) is the standard 3270
/// AID byte, kept for fixture/parity fidelity only — the dispatcher and handlers branch on the enum, not
/// the byte. PF13..PF24 are distinct AIDs here; the CardDemo COBOL folds them onto PFK01..PFK12 in the
/// COMMAREA via <see cref="CssTrpfy"/> (see <c>CSSTRPFY.cpy</c>).
/// </remarks>
public enum AidKey
{
    /// <summary>No AID recorded yet (cold start / before first RECEIVE). Not a real 3270 AID.</summary>
    None = 0,

    /// <summary><c>DFHENTER</c> — Enter key, x'7D'.</summary>
    Enter,

    /// <summary><c>DFHCLEAR</c> — Clear key, x'6D' (console: Esc / Ctrl+Home).</summary>
    Clear,

    /// <summary><c>DFHPA1</c> — Program Attention 1, x'6C'.</summary>
    Pa1,

    /// <summary><c>DFHPA2</c> — Program Attention 2, x'6E'.</summary>
    Pa2,

    /// <summary><c>DFHPF1</c> — F1.</summary>
    Pf1,
    Pf2,
    Pf3,
    Pf4,
    Pf5,
    Pf6,
    Pf7,
    Pf8,
    Pf9,
    Pf10,
    Pf11,
    Pf12,

    /// <summary><c>DFHPF13</c> — Shift+F1; folds to PFK01 in the COMMAREA.</summary>
    Pf13,
    Pf14,
    Pf15,
    Pf16,
    Pf17,
    Pf18,
    Pf19,
    Pf20,
    Pf21,
    Pf22,
    Pf23,
    Pf24,
}

/// <summary>Standard 3270 AID byte values and helpers for <see cref="AidKey"/>.</summary>
public static class AidKeyExtensions
{
    /// <summary>True for any of <see cref="AidKey.Pf1"/>..<see cref="AidKey.Pf24"/>.</summary>
    public static bool IsPfKey(this AidKey aid) => aid >= AidKey.Pf1 && aid <= AidKey.Pf24;

    /// <summary>
    /// The 1-based PF number (1..24) for a PF AID, or 0 if <paramref name="aid"/> is not a PF key.
    /// </summary>
    public static int PfNumber(this AidKey aid) => aid.IsPfKey() ? (aid - AidKey.Pf1) + 1 : 0;

    /// <summary>
    /// The standard 3270 AID byte for this key, as it would appear in <c>EIBAID</c>. Kept for fixture
    /// fidelity; the runtime branches on the enum, not this value.
    /// </summary>
    public static byte ToByte(this AidKey aid) => aid switch
    {
        AidKey.Enter => 0x7D,
        AidKey.Clear => 0x6D,
        AidKey.Pa1 => 0x6C,
        AidKey.Pa2 => 0x6E,
        AidKey.Pf1 => 0xF1,
        AidKey.Pf2 => 0xF2,
        AidKey.Pf3 => 0xF3,
        AidKey.Pf4 => 0xF4,
        AidKey.Pf5 => 0xF5,
        AidKey.Pf6 => 0xF6,
        AidKey.Pf7 => 0xF7,
        AidKey.Pf8 => 0xF8,
        AidKey.Pf9 => 0xF9,
        AidKey.Pf10 => 0x7A,
        AidKey.Pf11 => 0x7B,
        AidKey.Pf12 => 0x7C,
        AidKey.Pf13 => 0xC1,
        AidKey.Pf14 => 0xC2,
        AidKey.Pf15 => 0xC3,
        AidKey.Pf16 => 0xC4,
        AidKey.Pf17 => 0xC5,
        AidKey.Pf18 => 0xC6,
        AidKey.Pf19 => 0xC7,
        AidKey.Pf20 => 0xC8,
        AidKey.Pf21 => 0xC9,
        AidKey.Pf22 => 0x4A,
        AidKey.Pf23 => 0x4B,
        AidKey.Pf24 => 0x4C,
        _ => 0x00, // None
    };
}
