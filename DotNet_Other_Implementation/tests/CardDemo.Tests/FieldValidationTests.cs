using CardDemo.Domain.Services;
using Xunit;

namespace CardDemo.Tests;

public sealed class FieldValidationTests
{
    [Theory]
    [InlineData(300, true)]
    [InlineData(850, true)]
    [InlineData(500, true)]
    [InlineData(299, false)]
    [InlineData(851, false)]
    [InlineData(0, false)]
    public void IsValidFico_RangeInclusive(int score, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidFico(score));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(0, false)]
    [InlineData(13, false)]
    public void IsValidMonth_Range(int month, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidMonth(month));
    }

    [Theory]
    [InlineData(1950, true)]
    [InlineData(2099, true)]
    [InlineData(2026, true)]
    [InlineData(1949, false)]
    [InlineData(2100, false)]
    public void IsValidYear_Range(int year, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidYear(year));
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("U", true)]
    [InlineData("a", true)]
    [InlineData("u", true)]
    [InlineData("X", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidUserType_AcceptsAorU(string? value, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidUserType(value));
    }

    [Theory]
    [InlineData("2024-02-29", true)]   // 2024 is a leap year
    [InlineData("2023-02-29", false)]  // 2023 is not a leap year
    [InlineData("2000-02-29", true)]   // century leap year
    [InlineData("1900-02-29", false)]  // century non-leap year
    [InlineData("2026-07-10", true)]
    [InlineData("2026-13-01", false)]  // invalid month
    [InlineData("2026-00-10", false)]  // invalid month
    [InlineData("2026-07-32", false)]  // invalid day
    [InlineData("not-a-date", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidIsoDate_LeapYearAware(string? value, bool expected)
    {
        Assert.Equal(expected, FieldValidation.IsValidIsoDate(value));
    }
}
