using System.Globalization;

namespace CardDemo.Domain.Services;

/// <summary>
/// Field-level validation rules observed in the online programs (COACTUPC,
/// COCRDUPC, COTRN02C, COUSR01C/02C). Pure and culture-invariant.
/// </summary>
public static class FieldValidation
{
    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>All characters are ASCII digits (COBOL IS NUMERIC on a display field).</summary>
    public static bool IsAllDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var c in value)
            if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>Numeric, required, and non-zero (the 1245-EDIT-NUM-REQD pattern).</summary>
    public static bool IsRequiredNonZeroNumber(string? value) =>
        IsAllDigits(value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n != 0;

    /// <summary>FICO credit score range check (COACTUPC): 300–850 inclusive.</summary>
    public static bool IsValidFico(int score) => score is >= 300 and <= 850;

    /// <summary>Expiration month 1–12 (COCRDUPC).</summary>
    public static bool IsValidMonth(int month) => month is >= 1 and <= 12;

    /// <summary>Expiration year 1950–2099 (COCRDUPC).</summary>
    public static bool IsValidYear(int year) => year is >= 1950 and <= 2099;

    /// <summary>Active-status flag accepts 'Y' or 'N' (case-insensitive).</summary>
    public static bool IsYesNo(string? value)
    {
        var v = value?.Trim();
        return string.Equals(v, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "N", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>User type is 'A' or 'U' (COUSR01C add validation).</summary>
    public static bool IsValidUserType(string? value)
    {
        var v = value?.Trim();
        return string.Equals(v, "A", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "U", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses a whole yyyy-MM-dd string as a valid calendar date (leap-year aware).</summary>
    public static bool IsValidIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// US Social Security Number check (COACTUPC 1265-EDIT-US-SSN + CSUTLDPY):
    /// exactly 9 digits; the area (first 3) must not be "000", "666" or 900–999;
    /// the group (digits 4–5) must not be "00"; the serial (last 4) must not be "0000".
    /// </summary>
    public static bool IsValidSsn(string? ssn)
    {
        if (!IsAllDigits(ssn) || ssn!.Length != 9) return false;

        var area = int.Parse(ssn.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture);
        if (area == 0 || area == 666 || area is >= 900 and <= 999) return false;

        var group = int.Parse(ssn.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture);
        if (group == 0) return false;

        var serial = int.Parse(ssn.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture);
        if (serial == 0) return false;

        return true;
    }

    /// <summary>
    /// Date-of-birth check (COACTUPC EDIT-DATE-OF-BIRTH): a valid yyyy-MM-dd
    /// calendar date (leap-year aware) that is strictly before <paramref name="today"/>
    /// and not absurdly old (on or after <c>today.AddYears(-120)</c>).
    /// </summary>
    public static bool IsValidDateOfBirth(string? dob, DateOnly today)
    {
        if (!DateOnly.TryParseExact(dob, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return false;

        return value < today && value >= today.AddYears(-120);
    }

    /// <summary>US state code check (COACTUPC 1270-EDIT-US-STATE-CD against CSLKPCDY).</summary>
    public static bool IsValidUsState(string? stateCode)
    {
        if (string.IsNullOrWhiteSpace(stateCode)) return false;
        return UsLookupTables.States.Contains(stateCode.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// State / ZIP consistency (COACTUPC 1280-EDIT-US-STATE-ZIP-CD): the state code
    /// concatenated with the first two characters of the ZIP must be a known combination.
    /// </summary>
    public static bool IsStateZipConsistent(string? stateCode, string? zip)
    {
        if (string.IsNullOrWhiteSpace(stateCode) || zip is null || zip.Length < 2) return false;
        var token = string.Concat(stateCode.Trim().ToUpperInvariant(), zip.AsSpan(0, 2));
        return UsLookupTables.StateZipPrefixes.Contains(token);
    }

    /// <summary>US phone area-code check (COACTUPC EDIT-AREA-CODE against CSLKPCDY).</summary>
    public static bool IsValidPhoneAreaCode(string? areaCode)
    {
        if (areaCode is null) return false;
        return UsLookupTables.PhoneAreaCodes.Contains(areaCode);
    }

    /// <summary>
    /// Embossed / card name character rule (COCRDUPC): non-blank and containing only
    /// letters and spaces.
    /// </summary>
    public static bool IsValidEmbossedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var c in name)
            if (!char.IsLetter(c) && c != ' ') return false;
        return true;
    }

    /// <summary>
    /// Full expiration-date check (COCRDUPC): month 1–12, year 1950–2099, and the
    /// composed year/month/first-of-month is a real calendar date.
    /// </summary>
    public static bool IsValidCalendarExpiration(int month, int year)
    {
        if (!IsValidMonth(month) || !IsValidYear(year)) return false;
        return DateOnly.TryParse(
            FormattableString.Invariant($"{year:D4}-{month:D2}-01"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }
}
