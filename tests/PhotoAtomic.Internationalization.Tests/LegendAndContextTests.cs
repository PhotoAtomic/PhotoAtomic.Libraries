using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

public class LegendAndContextTests
{
    [Fact]
    public void The_legend_records_the_source_expression_of_each_slot()
    {
        var color = "Red";
        var sentiment = "Love";

        var legend = LegendOf($"{color} is the color of {sentiment}");

        Assert.Equal(["color", "sentiment"], legend);
    }

    [Fact]
    public void The_legend_captures_arbitrary_expressions_not_just_variable_names()
    {
        var count = 2;

        var legend = LegendOf($"You see {count + 1} shadows");

        Assert.Equal(["count + 1"], legend);
    }

    [Fact]
    public void The_legend_survives_format_specifiers_and_alignment()
    {
        var price = 9.99;

        Assert.Equal(["price"], LegendOf($"Cost: {price:F2}"));
        Assert.Equal(["price"], LegendOf($"Cost: {price,10:F2}"));
    }

    [Fact]
    public void A_context_disambiguates_identical_sentences()
    {
        SetTranslation("Open", "it-IT", "Apri", context: "verb");
        SetTranslation("Open", "it-IT", "Aperto", context: "state");

        Language = "it-IT";

        Assert.Equal("Apri", T($"Open", "verb"));
        Assert.Equal("Aperto", T($"Open", "state"));
    }

    [Fact]
    public void A_contextless_row_is_the_fallback_when_the_context_has_no_specific_translation()
    {
        SetTranslation("Inventory", "it-IT", "Inventario");

        Language = "it-IT";

        Assert.Equal("Inventario", T($"Inventory", "menu-title"));
    }

    [Fact]
    public void Asking_with_a_context_never_breaks_the_source_fallback()
    {
        Language = "it-IT";

        Assert.Equal("Never translated sentence", T($"Never translated sentence", "whatever"));
    }
}
