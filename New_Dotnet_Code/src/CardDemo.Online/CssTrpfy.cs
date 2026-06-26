namespace CardDemo.Online;

/// <summary>
/// COMMAREA-side AID code, the 5-char value <c>CCARD-AID</c> takes in <c>CVCRD01Y</c>:
/// <c>ENTER</c>, <c>CLEAR</c>, <c>PA1  </c>, <c>PA2  </c>, <c>PFK01</c>..<c>PFK12</c>.
/// </summary>
/// <remarks>
/// There are only 12 PFK codes: the <c>YYYY-STORE-PFKEY</c> idiom (copybook <c>CSSTRPFY.cpy</c>) folds
/// PF13..PF24 onto PFK01..PFK12, so Shift+F3 stores exactly the same <c>PFK03</c> as F3.
/// </remarks>
public enum CcardAid
{
    /// <summary>No AID stored (the <c>CCARD-AID</c> field's initial / unset state).</summary>
    None = 0,
    Enter,
    Clear,
    Pa1,
    Pa2,
    Pfk01,
    Pfk02,
    Pfk03,
    Pfk04,
    Pfk05,
    Pfk06,
    Pfk07,
    Pfk08,
    Pfk09,
    Pfk10,
    Pfk11,
    Pfk12,
}

/// <summary>
/// .NET equivalent of the <c>YYYY-STORE-PFKEY</c> paragraph in <c>CSSTRPFY.cpy</c>: maps the AID in
/// <c>EIBAID</c> to the COMMAREA <c>CCARD-AID-*</c> flag. Faithful to the copybook, PF13..PF24 fold onto
/// PFK01..PFK12 (the copybook sets <c>CCARD-AID-PFK01</c> for both <c>DFHPF1</c> and <c>DFHPF13</c>).
/// </summary>
public static class CssTrpfy
{
    /// <summary>
    /// Maps an <see cref="AidKey"/> to its <see cref="CcardAid"/> COMMAREA code, replicating the
    /// PF13..PF24 -> PFK01..PFK12 folding in <c>CSSTRPFY</c>.
    /// </summary>
    public static CcardAid StorePfKey(AidKey aid) => aid switch
    {
        AidKey.Enter => CcardAid.Enter,
        AidKey.Clear => CcardAid.Clear,
        AidKey.Pa1 => CcardAid.Pa1,
        AidKey.Pa2 => CcardAid.Pa2,
        AidKey.Pf1 or AidKey.Pf13 => CcardAid.Pfk01,
        AidKey.Pf2 or AidKey.Pf14 => CcardAid.Pfk02,
        AidKey.Pf3 or AidKey.Pf15 => CcardAid.Pfk03,
        AidKey.Pf4 or AidKey.Pf16 => CcardAid.Pfk04,
        AidKey.Pf5 or AidKey.Pf17 => CcardAid.Pfk05,
        AidKey.Pf6 or AidKey.Pf18 => CcardAid.Pfk06,
        AidKey.Pf7 or AidKey.Pf19 => CcardAid.Pfk07,
        AidKey.Pf8 or AidKey.Pf20 => CcardAid.Pfk08,
        AidKey.Pf9 or AidKey.Pf21 => CcardAid.Pfk09,
        AidKey.Pf10 or AidKey.Pf22 => CcardAid.Pfk10,
        AidKey.Pf11 or AidKey.Pf23 => CcardAid.Pfk11,
        AidKey.Pf12 or AidKey.Pf24 => CcardAid.Pfk12,
        _ => CcardAid.None, // None / unmapped -> "Invalid key pressed" path
    };

    /// <summary>
    /// The fixed 5-character <c>CCARD-AID</c> literal a code serializes to (e.g. <c>PA1  </c> with two
    /// trailing spaces, <c>PFK03</c>). Empty (5 spaces) for <see cref="CcardAid.None"/>.
    /// </summary>
    public static string ToCode(CcardAid aid) => aid switch
    {
        CcardAid.Enter => "ENTER",
        CcardAid.Clear => "CLEAR",
        CcardAid.Pa1 => "PA1  ",
        CcardAid.Pa2 => "PA2  ",
        CcardAid.Pfk01 => "PFK01",
        CcardAid.Pfk02 => "PFK02",
        CcardAid.Pfk03 => "PFK03",
        CcardAid.Pfk04 => "PFK04",
        CcardAid.Pfk05 => "PFK05",
        CcardAid.Pfk06 => "PFK06",
        CcardAid.Pfk07 => "PFK07",
        CcardAid.Pfk08 => "PFK08",
        CcardAid.Pfk09 => "PFK09",
        CcardAid.Pfk10 => "PFK10",
        CcardAid.Pfk11 => "PFK11",
        CcardAid.Pfk12 => "PFK12",
        _ => "     ",
    };
}
