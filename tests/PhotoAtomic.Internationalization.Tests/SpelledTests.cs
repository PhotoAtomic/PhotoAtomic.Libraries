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

    [Fact]
    public void A_speller_writes_the_numbers_nobody_translated()
    {
        // The engine knows nothing about any spelling library: it knows that
        // someone MIGHT be able to write a number, and asks.
        UseNumberWords((amount, language) => language.StartsWith("it") && amount == 4 ? "quattro" : null);
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");
        Language = "it-IT";

        Assert.Equal("Hai trovato quattro monete", T($"You found {new Spelled(4)} coins"));

        // ...and a speller that declines leaves the digits, which is the point
        // of being allowed to decline: a library that answers in the wrong
        // language does more harm than a numeral.
        Assert.Equal("Hai trovato 5 monete", T($"You found {new Spelled(5)} coins"));

        UseNumberWords(null);
    }

    [Fact]
    public void The_table_wins_over_the_speller()
    {
        // A library knows the cardinals of a language; it does not know that
        // THIS sentence wants the feminine, or that this language has an
        // irregular form here. Whoever wrote a row meant it.
        UseNumberWords((_, _) => "uno");
        SetTranslation("1", "it-IT", "una", context: "text");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} moneta", context: "0:CLDR-one");
        Language = "it-IT";

        Assert.Equal("Hai trovato una moneta", T($"You found {new Spelled(1)} coins"));

        UseNumberWords(null);
    }

    [Fact]
    public void Only_numbers_that_asked_to_be_spelled_are_spelled()
    {
        // A bare number in a sentence stays a numeral, because most of them
        // should: "3 coins" is not a mistake, and nobody asked for words.
        UseNumberWords((_, _) => "SPELLED");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");
        Language = "it-IT";

        Assert.Equal("Hai trovato 3 monete", T($"You found {3} coins"));

        UseNumberWords(null);
    }

    [Fact]
    public void As_a_label_the_speller_answers_too()
    {
        UseNumberWords((amount, _) => amount == 9 ? "nove" : null);
        Language = "it-IT";

        Assert.Equal("nove", Value(new Spelled(9)));

        UseNumberWords(null);
    }
}
