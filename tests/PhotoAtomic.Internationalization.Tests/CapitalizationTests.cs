using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable("weapon")]
public enum Blade
{
    Katana,
    Claymore,
}

/// <summary>
/// Where the automatic capital goes, and — just as important — where it does
/// not. It exists for ONE reason: values are stored lowercase so they read
/// right inside a sentence, so a value landing at the front needs a capital
/// nobody wrote. A template needs nothing: whoever wrote it already decided
/// how it opens, and correcting that was both presumptuous and a way of hiding
/// bad translations from the lint.
/// </summary>
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
    public void A_template_opens_exactly_as_it_was_written()
    {
        // Someone wrote this row. Maybe it is a fragment, maybe a label, maybe
        // a style: not our business to correct it — and if it IS a mistake, the
        // lint is the one that gets to say so.
        SetTranslation("Done. now go! quickly? yes", "it-IT", "fatto. ora vai! di corsa? certo");

        Language = "it-IT";

        Assert.Equal("fatto. ora vai! di corsa? certo", T($"Done. now go! quickly? yes"));
    }

    [Fact]
    public void A_value_that_opens_a_later_sentence_is_capitalized_too()
    {
        SetTranslation("Katana", "it-IT", "katana", context: "weapon", traits: "GENDER-female");
        SetTranslation("It ends. {0} remains", "it-IT", "È finita. {0} resta là", context: "0:GENDER-female");

        Language = "it-IT";
        var blade = Blade.Katana;

        Assert.Equal("È finita. Katana resta là", T($"It ends. {blade} remains"));
    }

    [Fact]
    public void A_value_opening_the_sentence_inside_quotes_is_capitalized_through_them()
    {
        SetTranslation("Katana", "it-IT", "katana", context: "weapon", traits: "GENDER-female");
        SetTranslation("she says {0}", "it-IT", "\"{0}\" disse lei", context: "0:GENDER-female");

        Language = "it-IT";
        var blade = Blade.Katana;

        Assert.Equal("\"Katana\" disse lei", T($"she says {blade}"));
    }

    [Fact]
    public void A_sentence_opening_with_a_digit_stays_untouched()
    {
        SetTranslation("count: {0} shards", "it-IT", "{0} frammenti restanti", context: "0:CLDR-other");

        Language = "it-IT";
        var shards = 2;

        Assert.Equal("2 frammenti restanti", T($"count: {shards} shards"));
    }
}
