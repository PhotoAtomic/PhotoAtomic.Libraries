using PhotoAtomic;

namespace PhotoAtomic.Tests;

public class PluralRulesTests
{
    [Theory]
    [InlineData(0, "CLDR-zero")]
    [InlineData(1, "CLDR-one")]
    [InlineData(2, "CLDR-two")]
    [InlineData(3, "CLDR-few")]
    [InlineData(10, "CLDR-few")]
    [InlineData(103, "CLDR-few")]   // modulo 100: 103 counts like 3
    [InlineData(11, "CLDR-many")]
    [InlineData(99, "CLDR-many")]
    [InlineData(111, "CLDR-many")]
    [InlineData(100, "CLDR-other")]
    [InlineData(102, "CLDR-other")]
    public void Arabic_uses_all_six_categories(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "ar-SA"));

    [Theory]
    [InlineData(0, "CLDR-zero")]
    [InlineData(1, "CLDR-one")]
    [InlineData(2, "CLDR-two")]
    [InlineData(3, "CLDR-few")]
    [InlineData(6, "CLDR-many")]    // the famous Welsh six
    [InlineData(4, "CLDR-other")]
    [InlineData(7, "CLDR-other")]
    public void Welsh_reserves_a_form_for_exactly_six(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "cy"));

    [Theory]
    [InlineData(1, "CLDR-one")]
    [InlineData(11, "CLDR-one")]    // vigesimal: 11 behaves as singular
    [InlineData(2, "CLDR-two")]
    [InlineData(12, "CLDR-two")]    // and 12 takes the dual
    [InlineData(3, "CLDR-few")]
    [InlineData(13, "CLDR-few")]
    [InlineData(19, "CLDR-few")]
    [InlineData(20, "CLDR-other")]
    [InlineData(0, "CLDR-other")]
    public void Scottish_Gaelic_counts_in_twenties(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "gd"));

    [Theory]
    [InlineData(1, "CLDR-one")]
    [InlineData(21, "CLDR-one")]    // modulo: 21 is grammatically singular
    [InlineData(2, "CLDR-few")]
    [InlineData(4, "CLDR-few")]
    [InlineData(22, "CLDR-few")]
    [InlineData(5, "CLDR-many")]
    [InlineData(11, "CLDR-many")]
    [InlineData(12, "CLDR-many")]   // teens are always many
    [InlineData(111, "CLDR-many")]
    [InlineData(0, "CLDR-many")]
    public void Russian_applies_modulo_rules(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "ru-RU"));

    [Theory]
    [InlineData(1, "CLDR-one")]
    [InlineData(21, "CLDR-many")]   // unlike Russian: only exactly 1 is one
    [InlineData(22, "CLDR-few")]
    [InlineData(5, "CLDR-many")]
    public void Polish_differs_from_Russian_on_twentyone(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "pl"));

    [Theory]
    [InlineData(0, "CLDR-one")]     // "0 pomme": zero takes the singular in French
    [InlineData(1, "CLDR-one")]
    [InlineData(2, "CLDR-other")]
    public void French_treats_zero_as_singular(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "fr-FR"));

    [Theory]
    [InlineData(1, "CLDR-one")]
    [InlineData(0, "CLDR-other")]
    [InlineData(5, "CLDR-other")]
    public void Unlisted_languages_fall_back_to_the_simplest_rule(int number, string expected) =>
        Assert.Equal(expected, PluralRules.CategoryOf(number, "xx-XX"));

    [Fact]
    public void Fractions_are_always_other()
    {
        Assert.Equal("CLDR-other", PluralRules.CategoryOf(1.5, "en-US"));
        Assert.Equal("CLDR-other", PluralRules.CategoryOf(2.5, "ar-SA"));
    }

    [Fact]
    public void Non_numeric_values_have_no_category()
    {
        Assert.Null(PluralRules.CategoryOf("three", "en-US"));
        Assert.Null(PluralRules.CategoryOf(null, "en-US"));
    }
}

public class PluralCategoriesOfTests
{
    [Fact]
    public void Arabic_and_Welsh_distinguish_all_six_categories() =>
        Assert.Equal(
            ["CLDR-zero", "CLDR-one", "CLDR-two", "CLDR-few", "CLDR-many", "CLDR-other"],
            PluralRules.CategoriesOf("ar-SA"));

    [Fact]
    public void Scottish_Gaelic_distinguishes_four() =>
        Assert.Equal(["CLDR-one", "CLDR-two", "CLDR-few", "CLDR-other"], PluralRules.CategoriesOf("gd"));

    [Fact]
    public void Slavic_languages_distinguish_four() =>
        Assert.Equal(["CLDR-one", "CLDR-few", "CLDR-many", "CLDR-other"], PluralRules.CategoriesOf("ru-RU"));

    [Fact]
    public void Everyone_else_distinguishes_two() =>
        Assert.Equal(["CLDR-one", "CLDR-other"], PluralRules.CategoriesOf("it-IT"));
}
