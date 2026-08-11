using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable("weapon")]
public enum Blade
{
    Katana,
    Claymore,
}

public class CapitalizationTests
{
    [Fact]
    public void A_lowercase_value_opening_the_sentence_is_capitalized_mechanically()
    {
        SetTranslation("Katana", "it-IT", "katana", context: "weapon", traits: "GENDER-female");
        SetTranslation("The {0} fell", "it-IT", "{0} è caduta a terra", context: "0:GENDER-female");

        Language = "it-IT";
        var blade = Blade.Katana;

        Assert.Equal("Katana è caduta a terra", T($"The {blade} fell"));
    }

    [Fact]
    public void The_Capitalize_trait_keeps_the_uppercase_initial_anywhere_in_the_sentence()
    {
        SetTranslation("Claymore", "it-IT", "durlindana", context: "weapon", traits: "GENDER-female,Capitalize");
        SetTranslation("Grab the {0}", "it-IT", "Afferra la {0}", context: "0:GENDER-female");

        Language = "it-IT";
        var blade = Blade.Claymore;

        Assert.Equal("Afferra la Durlindana", T($"Grab the {blade}"));
    }

    [Fact]
    public void Letters_after_sentence_ending_punctuation_are_capitalized()
    {
        SetTranslation("Done. now go! quickly? yes", "it-IT", "fatto. ora vai! di corsa? certo");

        Language = "it-IT";

        Assert.Equal("Fatto. Ora vai! Di corsa? Certo", T($"Done. now go! quickly? yes"));
    }

    [Fact]
    public void A_sentence_opening_with_a_digit_stays_untouched()
    {
        SetTranslation("count: {0} shards", "it-IT", "{0} frammenti restanti", context: "0:CLDR-other");

        Language = "it-IT";
        var shards = 2;

        Assert.Equal("2 frammenti restanti", T($"count: {shards} shards"));
    }

    [Fact]
    public void A_sentence_opening_inside_quotes_is_capitalized_through_them()
    {
        SetTranslation("she says hello", "it-IT", "\"ciao\" disse lei");

        Language = "it-IT";

        Assert.Equal("\"Ciao\" disse lei", T($"she says hello"));
    }
}
