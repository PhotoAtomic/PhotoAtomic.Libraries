using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

// Each test uses its own unique sentences: the translation table is process-wide
// and xUnit runs test classes in parallel.
public class TranslationTests
{
    [Fact]
    public void Without_a_translation_the_source_text_renders_unchanged()
    {
        var item = "Rope";
        Language = "it-IT";

        Assert.Equal("You picked up Rope", T($"You picked up {item}"));
    }

    [Fact]
    public void A_registered_translation_replaces_the_template_for_the_ambient_language()
    {
        SetTranslation("The door is now {0}", "it-IT", "La porta ora è {0}");
        var state = "open";

        Language = "it-IT";

        Assert.Equal("La porta ora è open", T($"The door is now {state}"));
    }

    [Fact]
    public void A_translation_can_reorder_holes_to_follow_the_target_grammar()
    {
        SetTranslation("{0} coins in the {1}", "it-IT", "Nella {1} ci sono {0} monete");
        var count = 3;
        var place = "pouch";

        Language = "it-IT";

        Assert.Equal("Nella pouch ci sono 3 monete", T($"{count} coins in the {place}"));
    }

    [Fact]
    public void Switching_the_ambient_language_switches_the_rendering()
    {
        SetTranslation("Steam rises from the pot", "it-IT", "Il vapore sale dalla pentola");

        Assert.Equal("Steam rises from the pot", T($"Steam rises from the pot"));

        Language = "it-IT";
        Assert.Equal("Il vapore sale dalla pentola", T($"Steam rises from the pot"));

        Language = SourceLanguage;
        Assert.Equal("Steam rises from the pot", T($"Steam rises from the pot"));
    }

    [Fact]
    public void Values_render_with_the_culture_of_the_target_language()
    {
        SetTranslation("Weight: {0} kg", "it-IT", "Peso: {0} kg");
        var weight = 1234.5;

        Language = "it-IT";

        // Decimal comma: the hole is formatted with the target culture.
        Assert.Equal("Peso: 1234,5 kg", T($"Weight: {weight} kg"));
    }

    [Fact]
    public void Format_specifiers_survive_the_trip_through_a_translation()
    {
        SetTranslation("Price: {0} gold", "it-IT", "Prezzo: {0} monete d'oro");
        var price = 0.5;

        Language = "it-IT";

        Assert.Equal("Prezzo: 0,50 monete d'oro", T($"Price: {price:F2} gold"));
    }

    [Fact]
    public void An_unknown_language_code_still_renders_using_the_invariant_culture()
    {
        var value = 2.5;
        Language = "xx-INVALID";

        Assert.Equal("Depth: 2.5", T($"Depth: {value}"));
    }
}
