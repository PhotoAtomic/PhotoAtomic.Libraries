using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

/// <summary>
/// A sentence written as data has to be indistinguishable from one written at
/// a call site — same key, same facts, same table — or the content a model
/// writes would live in a second-class world with its own bugs. Most of these
/// tests are that claim, checked from different angles.
/// </summary>
public class PhraseTests : IDisposable
{
    public PhraseTests() => ClearTranslations();

    public void Dispose()
    {
        Language = SourceLanguage;
        ClearTranslations();
    }

    [Fact]
    public void The_key_is_the_one_the_compiler_would_have_derived()
    {
        var phrase = new Phrase("The {liquid} boils away in the {vessel}");

        Assert.Equal("The {0} boils away in the {1}", phrase.Key);
        Assert.Equal(["liquid", "vessel"], phrase.Holes);

        // The proof, rather than the claim: a real call site with the same
        // shape lands on the same row.
        SetTranslation("The {0} boils away in the {1}", "it-IT", "Il {0} evapora nella {1}");
        Language = "it-IT";

        var written = T($"The {"vino"} boils away in the {"pentola"}");
        var data = phrase.Render(("liquid", "vino"), ("vessel", "pentola"));

        Assert.Equal(written, data);
    }

    [Fact]
    public void An_untranslated_phrase_renders_as_written()
    {
        Language = "it-IT";

        Assert.Equal(
            "The cook shrugs.",
            new Phrase("The {who} shrugs.").Render(("who", "cook")));
    }

    [Fact]
    public void A_hole_can_declare_the_context_its_value_cannot()
    {
        // "cook" is a person, and no type says so: the phrase does. The engine
        // receives 0:person exactly as it would from a [Translatable] type.
        SetTranslation("{0} has nothing to say", "it-IT", "{0} non ha nulla da dire",
            context: "0:person");
        SetTranslation("{0} has nothing to say", "it-IT", "Da {0} non esce un suono");

        Language = "it-IT";

        Assert.Equal(
            "Il cuoco non ha nulla da dire",
            new Phrase("{cook:person} has nothing to say").Render(("cook", "il cuoco")));

        Assert.Equal(
            "Da la pentola non esce un suono",
            new Phrase("{thing} has nothing to say").Render(("thing", "la pentola")));
    }

    [Fact]
    public void The_names_of_the_holes_become_the_legend_a_translator_reads()
    {
        // What an AI is told about slot 0. Compiled call sites give it an
        // expression; a data phrase gives it a word chosen to be read.
        string? legend = null;
        UseTranslator(new CapturingTranslator(request => legend = string.Join(", ", request.Legend)));
        Language = "it-IT";

        new Phrase("The {liquid} boils away in the {vessel}").Render(("liquid", "vino"), ("vessel", "pentola"));
        WhenIdleAsync().GetAwaiter().GetResult();

        Assert.Equal("liquid, vessel", legend);
        UseTranslator(null);
    }

    [Fact]
    public void Doubled_braces_are_a_literal_brace_and_not_a_hole()
    {
        var phrase = new Phrase("The {{rule}} named {name} fired");

        Assert.Equal(["name"], phrase.Holes);
        Assert.Equal("The {{rule}} named {0} fired", phrase.Key);
        Assert.Equal("The {rule} named boiling fired", phrase.Render(("name", "boiling")));
    }

    [Fact]
    public void A_hole_that_nobody_bound_shows_its_own_name_rather_than_breaking_the_sentence()
    {
        Assert.Equal(
            "The vessel is empty",
            new Phrase("The {vessel} is empty").Render([]));
    }

    [Fact]
    public void The_same_hole_twice_is_filled_twice()
    {
        // ...and the sentence still opens with a capital: values are lowercase
        // by design and GrammarRules puts the capital there mechanically, for
        // a phrase written as data exactly as for one written in code.
        Assert.Equal(
            "Cook looks at cook",
            new Phrase("{who} looks at {who}").Render(("who", "cook")));
    }

    [Fact]
    public void A_phrase_with_no_holes_is_still_a_translatable_sentence()
    {
        SetTranslation("Nothing happens.", "it-IT", "Non succede nulla.");
        Language = "it-IT";

        Assert.Equal("Non succede nulla.", new Phrase("Nothing happens.").Render([]));
    }

    /// <summary>Records what a fill would have been asked, without asking anyone.</summary>
    private sealed class CapturingTranslator(Action<TranslationRequest> seen) : ITranslator
    {
        public Task<IReadOnlyList<TranslationRow>> TranslateAsync(
            TranslationRequest request, CancellationToken cancellationToken = default)
        {
            seen(request);
            return Task.FromResult<IReadOnlyList<TranslationRow>>([]);
        }
    }
}
