using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

/// <summary>
/// A number written in words is a translated VALUE like any other — and the
/// one thing it must not lose on the way is the plural category it gives to
/// the sentence around it.
/// </summary>
public class SpelledTests : IDisposable
{
    public SpelledTests() => ClearTranslations();

    public void Dispose()
    {
        Language = SourceLanguage;
        ClearTranslations();
    }

    [Fact]
    public void The_number_comes_out_in_words_from_the_table()
    {
        SetTranslation("2", "it-IT", "due", context: "text");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");

        Language = "it-IT";

        Assert.Equal("Hai trovato due monete", T($"You found {new Spelled(2)} coins"));
    }

    [Fact]
    public void The_sentence_still_agrees_with_the_amount()
    {
        // The trap this type had to avoid: wrapping a number in a type hides
        // it, and the sentence stops knowing it holds a one.
        SetTranslation("1", "it-IT", "una", context: "text");
        SetTranslation("2", "it-IT", "due", context: "text");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} moneta", context: "0:CLDR-one");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");

        Language = "it-IT";

        Assert.Equal("Hai trovato una moneta", T($"You found {new Spelled(1)} coins"));
        Assert.Equal("Hai trovato due monete", T($"You found {new Spelled(2)} coins"));
    }

    [Fact]
    public void A_language_with_a_category_for_two_gets_it()
    {
        // Scottish Gaelic is the reason this exists: it has a category for
        // TWO, and the noun after it goes into the lenited singular.
        Assert.Equal(PluralRules.Two, PluralRules.CategoryOf(new Spelled(2), "gd"));
        Assert.Equal(PluralRules.Two, PluralRules.CategoryOf(2, "gd"));

        // ...and a bare number and a spelled one are the same fact everywhere.
        Assert.Equal(PluralRules.CategoryOf(5, "gd"), PluralRules.CategoryOf(new Spelled(5), "gd"));
        Assert.Equal(PluralRules.CategoryOf(1, "it-IT"), PluralRules.CategoryOf(new Spelled(1), "it-IT"));
    }

    [Fact]
    public void A_number_nobody_spelled_out_is_still_a_number()
    {
        // Seven has no word in the table. The sentence is translated all the
        // same and the digits stand in: less charming, never wrong.
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");
        Language = "it-IT";

        Assert.Equal("Hai trovato 7 monete", T($"You found {new Spelled(7)} coins"));
    }

    [Fact]
    public void The_key_does_not_move_with_the_ambient_language()
    {
        // A thousand is "1000" whoever is reading: if the key were formatted
        // for the reader, every language would be looking up a different word.
        var thousand = new Spelled(1000);

        Language = "it-IT";
        var italian = thousand.ToString();

        Language = "en-US";
        Assert.Equal(italian, thousand.ToString());
        Assert.Equal("1000", italian);
    }

    [Fact]
    public void It_takes_a_number_without_ceremony()
    {
        Spelled three = 3;
        Spelled many = 12L;

        Assert.Equal("3", three.ToString());
        Assert.Equal("12", many.ToString());
    }

    [Fact]
    public void As_a_label_it_is_the_word_alone()
    {
        SetTranslation("3", "it-IT", "tre", context: "text");
        Language = "it-IT";

        Assert.Equal("tre", Value(new Spelled(3)));
    }
}
