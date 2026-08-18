using PhotoAtomic;

namespace PhotoAtomic.Tests;

/// <summary>
/// How much of what a scene has already settled is worth putting in front of
/// the model. Too little and the same object gets two names; too much and the
/// model starts working unrelated words into the answer.
/// </summary>
public class LexiconTests
{
    private static readonly GlossaryTerm[] Settled =
    [
        new("Heavy stone press", "pressa pesante in pietra"),
        new("Ceramic mortar", "mortaio di ceramica"),
        new("Bundle of herbs", "mazzo di erbe"),
    ];

    [Fact]
    public void Only_the_terms_sharing_a_word_with_the_key_come_along()
    {
        var relevant = Lexicon.RelevantTo(Settled, "Stone press base");

        var term = Assert.Single(relevant);
        Assert.Equal("Heavy stone press", term.Source);
    }

    [Fact]
    public void A_key_with_nothing_in_common_travels_alone()
    {
        Assert.Empty(Lexicon.RelevantTo(Settled, "Rusty lantern"));
    }

    [Fact]
    public void The_order_terms_were_settled_in_is_kept()
    {
        var relevant = Lexicon.RelevantTo(Settled, "Stone mortar and press");

        Assert.Equal(["Heavy stone press", "Ceramic mortar"], relevant.Select(term => term.Source));
    }

    [Theory]
    [InlineData("Stone press", "Press base", true)]
    [InlineData("Stone press", "STONE bowl", true)]     // the shared word is not case-sensitive
    [InlineData("Base of press", "Lid of jar", false)]  // a shared "of" would drag in the whole room
    [InlineData("Oil lamp", "Lamp-post", true)]         // a hyphen separates words too
    // Only words of one or two letters are discarded, so "the" still binds two
    // names that have nothing else in common. It costs a term too many in the
    // prompt, never a term too few.
    [InlineData("Base of the press", "Lid of the jar", true)]
    public void Short_words_carry_no_meaning_of_their_own(string one, string other, bool shares) =>
        Assert.Equal(shares, Lexicon.SharesAWord(one, other));
}
