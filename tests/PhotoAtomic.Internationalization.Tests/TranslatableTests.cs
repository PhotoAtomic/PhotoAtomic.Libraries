using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable]
public enum Color
{
    Red,
    Blue,
    Green,
}

[Translatable]
public enum Sentiment
{
    Love,
    Fear,
}

[Translatable]
public sealed class Creature
{
    public required string Species { get; init; }

    public override string ToString() => Species;
}

public class TranslatableTests
{
    [Fact]
    public void The_founding_example_reorders_the_sentence_and_translates_the_values()
    {
        SetTranslation("{0} is the color of {1}", "it-IT", "Il colore di {1} è il {0}");
        SetTranslation("Red", "it-IT", "Rosso");
        SetTranslation("Love", "it-IT", "Amore");

        Language = "it-IT";
        var color = Color.Red;
        var sentiment = Sentiment.Love;

        Assert.Equal("Il colore di Amore è il Rosso", T($"{color} is the color of {sentiment}"));
    }

    [Fact]
    public void A_translatable_value_without_a_row_keeps_its_source_text()
    {
        SetTranslation("The sky turns {0}", "it-IT", "Il cielo diventa {0}");

        Language = "it-IT";
        var color = Color.Green; // deliberately never translated

        Assert.Equal("Il cielo diventa Green", T($"The sky turns {color}"));
    }

    [Fact]
    public void Values_translate_even_when_the_sentence_has_no_translation_yet()
    {
        SetTranslation("Blue", "it-IT", "Blu");

        Language = "it-IT";
        var color = Color.Blue;

        // Transitional mixed rendering: the sentence waits for its row (or the
        // future AI fill), the value is already translated.
        Assert.Equal("A Blu shade", T($"A {color} shade"));
    }

    [Fact]
    public void Unmarked_types_are_never_translated_by_content()
    {
        SetTranslation("42", "it-IT", "quarantadue");
        SetTranslation("word", "it-IT", "parola");

        Language = "it-IT";
        var number = 42;
        var text = "word";

        Assert.Equal("42 word", T($"{number} {text}"));
    }

    [Fact]
    public void A_translatable_class_translates_through_its_ToString()
    {
        SetTranslation("Three-headed monkey", "it-IT", "Scimmia a tre teste");

        Language = "it-IT";
        var creature = new Creature { Species = "Three-headed monkey" };

        Assert.Equal("Beware of the Scimmia a tre teste!", T($"Beware of the {creature}!"));
    }

    [Fact]
    public void In_the_source_language_translatable_values_render_untouched()
    {
        SetTranslation("Fear", "it-IT", "Paura");

        var sentiment = Sentiment.Fear;

        Assert.Equal("Fear grips you", T($"{sentiment} grips you"));
    }
}
