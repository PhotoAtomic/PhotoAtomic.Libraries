namespace PhotoAtomic.Tests;

/// <summary>
/// The rules a translated value has to satisfy for the engine to be able to
/// use it — learned the hard way: a single value that forgot to say its gender
/// made every sentence naming it fall back to English, in a game where the
/// other forty names were perfect.
/// </summary>
public class ValueHygieneTests
{
    [Fact]
    public void Whether_a_language_declines_gender_is_read_from_the_corpus()
    {
        TranslationRow[] corpus =
        [
            new("Water", null, "it-IT", "acqua", "GENDER-female,starts-with-vowel"),
            new("Water", null, "en-US", "water", null),
        ];

        Assert.True(ValueHygiene.UsesGrammaticalGender(corpus, "it-IT"));
        Assert.False(ValueHygiene.UsesGrammaticalGender(corpus, "en-US"));

        // A language nobody has translated into yet makes no demands: the rule
        // grows with the table instead of with a hardcoded list.
        Assert.False(ValueHygiene.UsesGrammaticalGender(corpus, "hu-HU"));
    }

    [Fact]
    public void A_value_that_declares_no_gender_is_visible_as_such()
    {
        Assert.True(ValueHygiene.DeclaresGender(new TranslationRow("Pot", null, "it-IT", "pentola", "GENDER-female")));
        Assert.False(ValueHygiene.DeclaresGender(new TranslationRow("Base", null, "it-IT", "base", null)));
        Assert.False(ValueHygiene.DeclaresGender(new TranslationRow("Base", null, "it-IT", "base", "Capitalize")));
    }

    [Fact]
    public void A_common_noun_loses_the_capital_a_model_gave_it()
    {
        var shouted = new TranslationRow("Bonfire", null, "it-IT", "Falò", "GENDER-male");

        Assert.Equal("falò", ValueHygiene.AsCommonNoun(shouted).Template);

        // Unless it says the capital is part of the word (German nouns, names).
        var german = new TranslationRow("Bonfire", null, "de-DE", "Lagerfeuer", "Capitalize");

        Assert.Equal("Lagerfeuer", ValueHygiene.AsCommonNoun(german).Template);
    }

    [Fact]
    public void A_proper_noun_says_it_keeps_its_capital()
    {
        var room = new TranslationRow("The Pirate Galley", null, "it-IT", "La Galea Pirata", "GENDER-female");
        var marked = ValueHygiene.AsProperNoun(room);

        Assert.Contains("Capitalize", marked.Traits);
        Assert.Contains("GENDER-female", marked.Traits);
        Assert.Equal("La Galea Pirata", marked.Template);

        // Idempotent: asking twice does not stack the trait.
        Assert.Equal(marked.Traits, ValueHygiene.AsProperNoun(marked).Traits);
    }
}
