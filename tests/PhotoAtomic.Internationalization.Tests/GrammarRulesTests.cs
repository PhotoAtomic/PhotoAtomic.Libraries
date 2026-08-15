using PhotoAtomic;

namespace PhotoAtomic.Tests;

/// <summary>
/// The deterministic grammar the engine applies after rendering, so that
/// translation rows stay few and translators (human or AI) keep their
/// attention for meaning and gender.
/// </summary>
public class GrammarRulesTests
{
    [Theory]
    // The everyday case: a value brought a vowel, the article must contract.
    [InlineData("mette la acqua nella pentola", "mette l'acqua nella pentola")]
    [InlineData("mette il alcol nel bicchiere", "mette l'alcol nel bicchiere")]
    [InlineData("versa il alcol nella acqua", "versa l'alcol nell'acqua")]
    [InlineData("una arancia matura", "un'arancia matura")]
    // Silent h counts as a vowel in Italian.
    [InlineData("la hostess sorride", "l'hostess sorride")]
    // Consonants are left alone, and so are words that merely start like an article.
    [InlineData("mette la pentola sul fuoco", "mette la pentola sul fuoco")]
    [InlineData("illumina alcol", "illumina alcol")]
    public void Italian_elision(string rendered, string expected) =>
        Assert.Equal(expected, GrammarRules.ApplyElision(rendered, "it-IT"));

    [Theory]
    [InlineData("met la eau sur le feu", "met l'eau sur le feu")]
    [InlineData("le alcool brûle", "l'alcool brûle")]
    [InlineData("le pot de eau", "le pot d'eau")]
    [InlineData("la casserole chauffe", "la casserole chauffe")]
    public void French_elision(string rendered, string expected) =>
        Assert.Equal(expected, GrammarRules.ApplyElision(rendered, "fr-FR"));

    [Fact]
    public void Languages_without_elision_are_untouched()
    {
        Assert.Equal("the alcohol burns", GrammarRules.ApplyElision("the alcohol burns", "en-US"));
        Assert.Equal("das Öl brennt", GrammarRules.ApplyElision("das Öl brennt", "de-DE"));
    }

    [Fact]
    public void Elision_keeps_the_capitalization_of_the_word_it_replaces()
    {
        Assert.Equal("L'acqua bolle", GrammarRules.ApplyElision("La acqua bolle", "it-IT"));
    }

    [Fact]
    public void A_broken_row_never_takes_the_screen_down()
    {
        // A machine translation that invented a hole used to throw a
        // FormatException straight through the UI: now the source renders.
        Internationalization.ClearTranslations();
        Internationalization.SetTranslation("You open the {0}", "it-IT", "Apri il {0} con {1}");
        Internationalization.Language = "it-IT";

        var door = "door";
        Assert.Equal("You open the door", Internationalization.T($"You open the {door}"));

        Internationalization.ClearTranslations();
        Internationalization.Language = Internationalization.SourceLanguage;
    }

    [Theory]
    [InlineData("acqua", "it-IT", true)]
    [InlineData("hotel", "it-IT", true)]      // silent h
    [InlineData("pentola", "it-IT", false)]
    [InlineData("eau", "fr-FR", true)]
    [InlineData("Öl", "de-DE", true)]         // accented vowels count
    [InlineData("hammer", "en-US", false)]    // no silent h in English
    public void Vowel_sound_detection(string text, string language, bool expected) =>
        Assert.Equal(expected, GrammarRules.StartsWithVowelSound(text, language));
}
