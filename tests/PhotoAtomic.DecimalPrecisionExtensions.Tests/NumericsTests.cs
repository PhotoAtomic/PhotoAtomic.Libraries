using System.Globalization;
using PhotoAtomic.Numerics;

namespace PhotoAtomic.DecimalPrecisionExtensions.Tests;

// Porting a xUnit dei test storici (MSTest + SharpTestsEx) del repo
// DecimalPrecisionExtensions, più le regressioni della modernizzazione.
public class NumericsTests
{
    private static readonly CultureInfo Provider = new("en-US");

    [Theory]
    [InlineData("123.4567")]
    [InlineData("123.4")]
    [InlineData("123")]
    [InlineData("123.4564")]
    [InlineData("123.4565")]
    [InlineData("123.4575")]
    [InlineData("666.9999")]
    public void ReportAndNumericSetPrecision_Expected_Equivalent(string text)
    {
        const int precision = 3;
        decimal value = decimal.Parse(text, Provider);

        Assert.Equal(GetSeedsReportFormat(value, precision), value.RoundWithPrecision(precision).ToString(Provider));
    }

    private static string GetSeedsReportFormat(decimal value, int precision)
    {
        NumberFormatInfo nfi = new CultureInfo("en-US", false).NumberFormat;
        nfi.NumberDecimalDigits = precision;
        nfi.NumberGroupSeparator = "";

        return value.ToString("N", nfi);
    }

    [Fact]
    public void VaringDecimalPrecision_TruncatesOrAddsCorrectDigits()
    {
        var value = 123.456m;

        Assert.Equal("123", value.SetPrecision(0).ToString(Provider));
        Assert.Equal("123.4", value.SetPrecision(1).ToString(Provider));
        Assert.Equal("123.45", value.SetPrecision(2).ToString(Provider));
        Assert.Equal("123.456", value.SetPrecision(3).ToString(Provider));
        Assert.Equal("123.4560", value.SetPrecision(4).ToString(Provider));
    }

    [Fact]
    public void VaringIntegralPrecision_ZeroesIntegralDigits()
    {
        var value = 123.456m;

        Assert.Equal("120", value.SetPrecision(-1).ToString(Provider));
        Assert.Equal("100", value.SetPrecision(-2).ToString(Provider));
        Assert.Equal("0", value.SetPrecision(-3).ToString(Provider));
        Assert.Equal("0", value.SetPrecision(-4).ToString(Provider));
    }

    [Fact]
    public void TestWithLongNumbers()
    {
        var value = 999988798765123.456m;

        Assert.Equal("999988798765120", value.SetPrecision(-1).ToString(Provider));
        Assert.Equal("999988798765100", value.SetPrecision(-2).ToString(Provider));
        Assert.Equal("999988798765000", value.SetPrecision(-3).ToString(Provider));
        Assert.Equal("999988798760000", value.SetPrecision(-4).ToString(Provider));
    }

    [Fact]
    public void GetPrecision_ReturnsTheScale()
    {
        Assert.Equal(0, 123m.GetPrecision());
        Assert.Equal(1, 123.4m.GetPrecision());
        Assert.Equal(4, 123.4000m.GetPrecision());
        Assert.Equal(2, (-0.25m).GetPrecision());
    }

    [Fact]
    public void HighPrecision_Works()
    {
        // Regressione: l'implementazione storica calcolava le potenze di dieci con
        // (int)Math.Pow e andava in overflow oltre 10^9.
        Assert.Equal("123.45600000000000000000", 123.456m.SetPrecision(20).ToString(Provider));
    }

    [Fact]
    public void NegativeValues_KeepTheirSign()
    {
        Assert.Equal("-123.4560", (-123.456m).SetPrecision(4).ToString(Provider));
        Assert.Equal("-120", (-123.456m).SetPrecision(-1).ToString(Provider));
    }

    [Fact]
    public void PrecisionBeyondDecimalCapacity_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => 123.456m.SetPrecision(29));
        Assert.Throws<InvalidOperationException>(() => decimal.MaxValue.SetPrecision(1));
    }
}
