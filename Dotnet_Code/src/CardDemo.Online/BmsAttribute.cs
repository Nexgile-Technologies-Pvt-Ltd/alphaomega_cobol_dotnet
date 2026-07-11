namespace CardDemo.Online;

/// <summary>
/// The BMS field attribute (3270 field-attribute byte) modelled as a bit set, mirroring the
/// <c>ATTRB=(...)</c> operand of a <c>DFHMDF</c>. See <c>_design/CONSOLE_RUNTIME.md</c> §1.1.
/// </summary>
/// <remarks>
/// <para>Protection is mutually exclusive (<see cref="Protected"/> | <see cref="Unprotected"/>) and
/// <see cref="AutoSkip"/> implies <see cref="Protected"/>. Intensity is mutually exclusive
/// (<see cref="Bright"/> | <see cref="Normal"/> | <see cref="Dark"/>).</para>
/// <para>The values are pure logical attributes; the renderer (<see cref="TextRenderer"/>) and input
/// loop derive the observable behaviour from the helper predicates on <see cref="BmsAttributeExtensions"/>.</para>
/// </remarks>
[Flags]
public enum BmsAttribute
{
    /// <summary>No attribute bits set (a bare unprotected NORM field by default).</summary>
    None = 0,

    /// <summary>PROT — protected: the operator cannot key into the field (cursor may rest on it).</summary>
    Protected = 1 << 0,

    /// <summary>UNPROT — unprotected/keyable input field (the BMS default when no protection is given).</summary>
    Unprotected = 1 << 1,

    /// <summary>ASKIP — auto-skip: protected and the cursor jumps past the field when keyed. Implies <see cref="Protected"/>.</summary>
    AutoSkip = 1 << 2,

    /// <summary>NUM — numeric-only input (digits, with <c>JUSTIFY=(RIGHT,ZERO)</c> right-justify and zero-fill on RECEIVE).</summary>
    Numeric = 1 << 3,

    /// <summary>BRT — bright / high intensity.</summary>
    Bright = 1 << 4,

    /// <summary>NORM — normal intensity (default).</summary>
    Normal = 1 << 5,

    /// <summary>DRK — dark / non-display (e.g. password fields); not shown but still keyable.</summary>
    Dark = 1 << 6,

    /// <summary>FSET — MDT pre-set ON at SEND, so the field is returned on the next RECEIVE even if unkeyed.</summary>
    Fset = 1 << 7,

    /// <summary>IC — insert-cursor: this field receives the cursor on SEND (one per map).</summary>
    Ic = 1 << 8,
}

/// <summary>Derived, observable predicates over a <see cref="BmsAttribute"/> bit set.</summary>
public static class BmsAttributeExtensions
{
    /// <summary>True when the field cannot be keyed (PROT or ASKIP).</summary>
    public static bool IsProtected(this BmsAttribute a)
        => (a & (BmsAttribute.Protected | BmsAttribute.AutoSkip)) != 0;

    /// <summary>True when the operator may key into the field (the complement of <see cref="IsProtected"/>).</summary>
    public static bool IsKeyable(this BmsAttribute a) => !a.IsProtected();

    /// <summary>True for DRK fields: not displayed but still keyable (e.g. password).</summary>
    public static bool IsHidden(this BmsAttribute a) => (a & BmsAttribute.Dark) != 0;

    /// <summary>True for ASKIP fields: the cursor auto-skips past on type / at field end.</summary>
    public static bool IsAutoSkip(this BmsAttribute a) => (a & BmsAttribute.AutoSkip) != 0;

    /// <summary>True for NUM fields: numeric-only entry.</summary>
    public static bool IsNumeric(this BmsAttribute a) => (a & BmsAttribute.Numeric) != 0;

    /// <summary>True for BRT fields: high intensity.</summary>
    public static bool IsBright(this BmsAttribute a) => (a & BmsAttribute.Bright) != 0;

    /// <summary>True for FSET fields: MDT pre-set on at SEND.</summary>
    public static bool IsFset(this BmsAttribute a) => (a & BmsAttribute.Fset) != 0;

    /// <summary>True for the IC field: receives the cursor on SEND.</summary>
    public static bool IsCursor(this BmsAttribute a) => (a & BmsAttribute.Ic) != 0;
}
