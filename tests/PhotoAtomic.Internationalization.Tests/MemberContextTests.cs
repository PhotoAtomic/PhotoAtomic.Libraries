using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable("instrument")]
public enum Instrument
{
    [Translatable("music")]
    Organ,
    Anvil,
}

public enum LooseWord
{
    [Translatable("verb")]
    Saw,
}

public class MemberContextTests
{
    [Fact]
    public void Member_contexts_sum_with_the_type_contexts()
    {
        SetTranslation("You play the {0}", "it-IT", "Suoni il {0}");
        SetTranslation("Organ", "it-IT", "canne", context: "instrument");
        SetTranslation("Organ", "it-IT", "organo", context: "instrument,music");

        Language = "it-IT";
        var played = Instrument.Organ;

        // Both rows match; the member context makes the two-criteria row win —
        // and the engine elides the article in front of the vowel it brought.
        Assert.Equal("Suoni l'organo", T($"You play the {played}"));
    }

    [Fact]
    public void Members_without_their_own_attribute_still_use_the_type_contexts()
    {
        SetTranslation("Anvil", "it-IT", "incudine", context: "instrument");

        Language = "it-IT";
        var hit = Instrument.Anvil;

        Assert.Equal("You strike the incudine", T($"You strike the {hit}"));
    }

    [Fact]
    public void A_marked_member_makes_its_value_translatable_even_when_the_type_is_not()
    {
        SetTranslation("Saw", "it-IT", "sega", context: "verb");

        Language = "it-IT";
        var word = LooseWord.Saw;

        Assert.Equal("Sega the plank", T($"{word} the plank"));
    }
}
