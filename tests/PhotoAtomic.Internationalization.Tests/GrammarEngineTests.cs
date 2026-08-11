using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable("tool")]
public enum Item
{
    Hammer,
    Key,
}

[Translatable("fruit")]
public enum Fruit
{
    Orange,
}

public class GrammarEngineTests
{
    public GrammarEngineTests()
    {
        // Value rows: the translation of a word carries its grammatical traits.
        SetTranslation("Hammer", "it-IT", "martello", context: "tool", traits: "GENDER-male");
        SetTranslation("Key", "it-IT", "chiave", context: "tool", traits: "GENDER-female");
        SetTranslation("Key", "it-IT", "chiavi", context: "tool,CLDR-other", traits: "GENDER-female");
        SetTranslation("Orange", "it-IT", "arancia", context: "fruit", traits: "GENDER-female,starts-with-vowel");
    }

    [Fact]
    public void Gender_agreement_picks_article_and_participle_from_value_traits()
    {
        SetTranslation("The {0} is broken", "it-IT", "Il {0} è rotto", context: "0:GENDER-male");
        SetTranslation("The {0} is broken", "it-IT", "La {0} è rotta", context: "0:GENDER-female");

        Language = "it-IT";
        var broken = Item.Key;
        var alsoBroken = Item.Hammer;

        Assert.Equal("La chiave è rotta", T($"The {broken} is broken"));
        Assert.Equal("Il martello è rotto", T($"The {alsoBroken} is broken"));
    }

    [Fact]
    public void Elision_wins_because_it_satisfies_more_criteria()
    {
        SetTranslation("The {0} is ripe", "it-IT", "La {0} è matura", context: "0:GENDER-female");
        SetTranslation("The {0} is ripe", "it-IT", "L'{0} è matura", context: "0:GENDER-female,0:starts-with-vowel");

        Language = "it-IT";
        var fruit = Fruit.Orange;

        Assert.Equal("L'arancia è matura", T($"The {fruit} is ripe"));
    }

    [Fact]
    public void Plural_categories_select_the_variant_row_in_both_languages()
    {
        SetTranslation("You found {0} coins", "en-US", "You found {0} coin", context: "0:CLDR-one");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} moneta", context: "0:CLDR-one");
        SetTranslation("You found {0} coins", "it-IT", "Hai trovato {0} monete", context: "0:CLDR-other");

        var one = 1;
        var many = 3;

        // Even the source language corrects itself through its own variant rows.
        Assert.Equal("You found 1 coin", T($"You found {one} coins"));
        Assert.Equal("You found 3 coins", T($"You found {many} coins"));

        Language = "it-IT";
        Assert.Equal("Hai trovato 1 moneta", T($"You found {one} coins"));
        Assert.Equal("Hai trovato 3 monete", T($"You found {many} coins"));
    }

    [Fact]
    public void Plurality_and_gender_compose_and_reach_the_value_too()
    {
        SetTranslation("{0} broken {1}", "it-IT", "{0} {1} rotta", context: "0:CLDR-one,1:GENDER-female");
        SetTranslation("{0} broken {1}", "it-IT", "{0} {1} rotte", context: "0:CLDR-other,1:GENDER-female");
        SetTranslation("{0} broken {1}", "it-IT", "{0} {1} rotti", context: "0:CLDR-other,1:GENDER-male");

        Language = "it-IT";
        var count = 2;
        var item = Item.Key;

        // The plural fact selects "chiavi" in the value lookup AND the
        // feminine-plural row in the sentence lookup: "2 chiavi rotte".
        Assert.Equal("2 chiavi rotte", T($"{count} broken {item}"));
    }

    [Fact]
    public void Only_cldr_category_names_are_recognized_no_aliases()
    {
        SetTranslation("{0} apples in the basket", "it-IT", "{0} mele nel cesto");
        SetTranslation("{0} apples in the basket", "it-IT", "{0} mela nel cesto", context: "0:singular");

        Language = "it-IT";
        var one = 1;

        // "singular" is not a CLDR category: the fact is "0:CLDR-one", so the row
        // demanding "0:singular" never matches and the generic row wins.
        Assert.Equal("1 mele nel cesto", T($"{one} apples in the basket"));
    }

    [Fact]
    public void On_equal_specificity_the_last_registered_row_wins()
    {
        SetTranslation("An old saying", "it-IT", "Un vecchio detto");
        SetTranslation("An old saying", "it-IT", "Un antico proverbio");

        Language = "it-IT";

        Assert.Equal("Un antico proverbio", T($"An old saying"));
    }

    [Fact]
    public void A_row_demanding_an_unsatisfied_criterion_never_matches()
    {
        SetTranslation("Take it", "it-IT", "Prendila", context: "0:GENDER-female");
        SetTranslation("Take it", "it-IT", "Prendilo");

        Language = "it-IT";

        // No holes, so no facts satisfy "0:GENDER-female": only the generic row can win.
        Assert.Equal("Prendilo", T($"Take it"));
    }
}
