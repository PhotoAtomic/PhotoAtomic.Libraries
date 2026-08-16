using Microsoft.Extensions.AI;
using PhotoAtomic;

namespace PhotoAtomic.Tests;

public class AiTranslatorTests
{
    private sealed class FakeChatClient(string reply) : IChatClient
    {
        public List<ChatMessage> LastMessages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastMessages.Clear();
            LastMessages.AddRange(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Answers from a queue and remembers every user prompt it saw.</summary>
    private sealed class ScriptedClient(Queue<string> replies) : IChatClient
    {
        public List<string> Prompts { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Prompts.Add(messages.Last(message => message.Role == ChatRole.User).Text);
            var reply = replies.Count > 0 ? replies.Dequeue() : """{"rows":[]}""";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static TranslationRequest Request(string key, string[] legend, string[] facts) =>
        new(key, "en-US", "it-IT", legend, facts);

    [Fact]
    public async Task Parses_rows_and_stamps_our_key_and_language()
    {
        var reply = """
            {"rows":[
              {"template":"Hai trovato {0} moneta","criteria":"0:CLDR-one","traits":""},
              {"template":"Hai trovato {0} monete","criteria":"0:CLDR-other","traits":""}
            ]}
            """;
        var translator = new AiTranslator(new FakeChatClient(reply));

        var rows = await translator.TranslateAsync(Request("You found {0} coins", ["coins"], ["0:CLDR-other"]));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("You found {0} coins", row.Key));
        Assert.All(rows, row => Assert.Equal("it-IT", row.Language));
        Assert.Equal("0:CLDR-one", rows[0].Context);
        Assert.Null(rows[0].Traits);
    }

    [Fact]
    public async Task Parses_value_rows_with_traits()
    {
        var reply = """{"rows":[{"template":"chiave","criteria":"","traits":"GENDER-female"}]}""";
        var translator = new AiTranslator(new FakeChatClient(reply));

        var rows = await translator.TranslateAsync(Request("Key", [], ["tool"]));

        var row = Assert.Single(rows);
        Assert.Equal("chiave", row.Template);
        Assert.Null(row.Context);
        Assert.Equal("GENDER-female", row.Traits);
    }

    [Fact]
    public async Task Survives_markdown_fences_around_the_json()
    {
        var reply = """
            ```json
            {"rows":[{"template":"La porta cigola","criteria":"","traits":""}]}
            ```
            """;
        var translator = new AiTranslator(new FakeChatClient(reply));

        var rows = await translator.TranslateAsync(Request("The door creaks", [], []));

        Assert.Equal("La porta cigola", Assert.Single(rows).Template);
    }

    [Fact]
    public async Task Garbage_answers_produce_no_rows_instead_of_exceptions()
    {
        var translator = new AiTranslator(new FakeChatClient("sorry, I cannot help with that"));

        var rows = await translator.TranslateAsync(Request("Anything", [], []));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_prompt_carries_key_legend_and_facts()
    {
        var fake = new FakeChatClient("""{"rows":[]}""");
        var translator = new AiTranslator(fake);

        await translator.TranslateAsync(Request("You found {0} coins", ["coins"], ["0:CLDR-other", "menu"]));

        var userMessage = fake.LastMessages.Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("You found {0} coins", userMessage);
        Assert.Contains("{0} = coins", userMessage);
        Assert.Contains("0:CLDR-other", userMessage);
        Assert.Contains("it-IT", userMessage);

        // The explicit checklists that nudge complete variant coverage.
        Assert.Contains("CLDR-one, CLDR-other", userMessage);
        Assert.Contains("GENDER-male, GENDER-female", userMessage);
    }

    [Fact]
    public void The_program_owns_the_combinatorics_not_the_model()
    {
        // With nothing translated yet, the plain genders are assumed.
        var two = VariantCases.For(new TranslationRequest(
            "{0} takes the {1}", "en-US", "it-IT", ["actor", "item"], ["0:item", "1:item"]));
        Assert.Equal(4, two.Count);
        Assert.Contains(two, variant => variant.Criteria == "0:GENDER-female,1:GENDER-male");

        // A numeric hole varies by the target language's plural categories.
        var plural = VariantCases.For(new TranslationRequest(
            "You found {0} coins", "en-US", "it-IT", ["coins"], ["0:CLDR-other"]));
        Assert.Equal(["0:CLDR-one", "0:CLDR-other"], plural.Select(variant => variant.Criteria));

        // Nothing grammatical to vary: no cases, the model answers freely.
        Assert.Empty(VariantCases.For(new TranslationRequest(
            "Setting up the room", "en-US", "it-IT", [], [])));
    }

    [Fact]
    public void The_cases_come_from_what_the_language_actually_declared()
    {
        // A language whose words carry a trait we never anticipated: the
        // vocabulary observes it, so it becomes a case — with a real example
        // ("lo specchio" but "il tavolo": no mechanical rule of ours knows that).
        var vocabulary = ValueVocabulary.FromRows(
        [
            new TranslationRow("Table", null, "it-IT", "tavolo", "GENDER-male"),
            new TranslationRow("Mirror", null, "it-IT", "specchio", "GENDER-male,impure-s"),
            new TranslationRow("Water", null, "it-IT", "acqua", "GENDER-female,starts-with-vowel"),
            new TranslationRow("Pot", null, "it-IT", "pentola", "GENDER-female"),
            new TranslationRow("Hammer", null, "fr-FR", "marteau", "GENDER-male"),
        ]);

        var cases = VariantCases.For(
            new TranslationRequest("You take the {0}", "en-US", "it-IT", ["item"], ["0:item"]),
            vocabulary);

        Assert.Equal(4, cases.Count);
        var impure = Assert.Single(cases, variant => variant.Criteria.Contains("impure-s"));
        Assert.Equal("specchio", impure.Examples[0]);
        var vowel = Assert.Single(cases, variant => variant.Criteria.Contains("starts-with-vowel"));
        Assert.Equal("acqua", vowel.Examples[0]);

        // Another language, its own states — the two never mix.
        var french = VariantCases.For(
            new TranslationRequest("You take the {0}", "en-US", "fr-FR", ["item"], ["0:item"]),
            vocabulary);
        Assert.Equal("0:GENDER-male", Assert.Single(french).Criteria);
    }

    [Fact]
    public void Too_many_combinations_are_simplified_axis_by_axis_never_truncated()
    {
        var vocabulary = ValueVocabulary.FromRows(
        [
            new TranslationRow("A", null, "it-IT", "tavolo", "GENDER-male"),
            new TranslationRow("B", null, "it-IT", "specchio", "GENDER-male,impure-s"),
            new TranslationRow("C", null, "it-IT", "acqua", "GENDER-female,starts-with-vowel"),
            new TranslationRow("D", null, "it-IT", "pentola", "GENDER-female"),
        ]);

        // Four states over three holes would be 64: the widest axes fall back
        // to their plainest states until the set fits under the ceiling.
        var cases = VariantCases.For(
            new TranslationRequest("{0} puts the {1} into the {2}", "en-US", "it-IT",
                ["actor", "item", "container"], ["0:item", "1:item", "2:item"]),
            vocabulary);

        Assert.InRange(cases.Count, 1, VariantCases.MaxCases);
        Assert.Equal(cases.Count, cases.Select(variant => variant.Criteria).Distinct().Count());
    }

    [Fact]
    public async Task The_holes_carry_their_word_and_come_back_as_plain_placeholders()
    {
        // The sentence is handed over annotated — "{1:'borsa'}" — and the
        // model answers around the braces. We strip them back to "{1}".
        var vocabulary = ValueVocabulary.FromRows(
            [new TranslationRow("Bag", null, "it-IT", "borsa", "GENDER-female")]);
        var fake = new ScriptedClient(new Queue<string>(
            [$$"""{"rows":[{"template":"Hai {0:'3'} monete nella tua {1:'borsa'}","criteria":"0:CLDR-other,1:GENDER-female"}]}"""]));
        var translator = new AiTranslator(fake, vocabulary: vocabulary);

        var rows = await translator.TranslateAsync(new TranslationRequest(
            "You have {0} coins in your {1}", "en-US", "it-IT", ["coins", "bag"],
            ["0:CLDR-other", "1:item"]));

        var row = Assert.Single(rows, candidate => candidate.Context!.Contains("CLDR-other"));
        Assert.Equal("Hai {0} monete nella tua {1}", row.Template);

        // The prompt showed the annotated sentence, with a real number for the
        // plural category and the vocabulary's word for the value hole.
        Assert.Contains(fake.Prompts, prompt => prompt.Contains("{0:'3'}") && prompt.Contains("{1:'borsa'}"));
    }

    [Fact]
    public async Task A_leaked_example_word_or_a_dissolved_hole_is_rejected_and_asked_again()
    {
        var vocabulary = ValueVocabulary.FromRows(
        [
            new TranslationRow("Candle", null, "it-IT", "candela", "GENDER-female"),
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"),
        ]);

        // First answer keeps the example word in the template (the real defect
        // seen in the game: "infila la {1} nel falò"), second one is clean.
        var fake = new ScriptedClient(new Queue<string>(
        [
            """{"rows":[{"template":"{0} infila la {1:'candela'} nel falò","criteria":"1:GENDER-female"}]}""",
            """{"rows":[{"template":"{0} mette in tasca la {1:'candela'}","criteria":"1:GENDER-female"}]}""",
        ]));
        var translator = new AiTranslator(fake, vocabulary: vocabulary);

        var rows = await translator.TranslateAsync(new TranslationRequest(
            "{0} pockets the {1}", "en-US", "it-IT", ["actor", "item"], ["1:item"]));

        Assert.Equal("{0} mette in tasca la {1}", Assert.Single(rows).Template);
        Assert.True(fake.Prompts.Count > 1, "the rejected case must be asked again");
    }

    [Fact]
    public async Task Later_variants_are_told_how_the_first_one_was_worded()
    {
        var vocabulary = ValueVocabulary.FromRows(
        [
            new TranslationRow("Candle", null, "it-IT", "candela", "GENDER-female"),
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"),
        ]);
        var fake = new ScriptedClient(new Queue<string>(
        [
            """{"rows":[{"template":"{0} mette in tasca la {1:'candela'}","criteria":"1:GENDER-female"}]}""",
            """{"rows":[{"template":"{0} mette in tasca il {1:'falò'}","criteria":"1:GENDER-male"}]}""",
        ]));
        var translator = new AiTranslator(fake, vocabulary: vocabulary);

        await translator.TranslateAsync(new TranslationRequest(
            "{0} pockets the {1}", "en-US", "it-IT", ["actor", "item"], ["1:item"]));

        // The second call carries the first wording, so the game does not
        // switch verbs depending on the gender of the object.
        Assert.Contains("{0} mette in tasca la {1}", fake.Prompts[1]);
        Assert.Contains("Keep EXACTLY the same words", fake.Prompts[1]);
    }

    [Fact]
    public async Task A_sentence_that_lost_a_placeholder_is_rejected()
    {
        var fake = new ScriptedClient(new Queue<string>(
            ["""{"rows":[{"template":"{0} apre la porta","criteria":"1:GENDER-female"}]}"""]));
        var translator = new AiTranslator(fake);

        var rows = await translator.TranslateAsync(new TranslationRequest(
            "{0} opens the {1}", "en-US", "it-IT", ["actor", "item"], ["1:item"]));

        Assert.Empty(rows); // {1} never came home: better nothing than a broken row
    }

    [Fact]
    public async Task A_sentence_that_INVENTED_a_placeholder_is_rejected()
    {
        // A hole the sentence does not have throws at render time and takes
        // the whole screen down — seen for real in the game.
        var fake = new ScriptedClient(new Queue<string>(
            ["""{"rows":[{"template":"{0} apre il {1} con {2}","criteria":"1:GENDER-male"}]}"""]));
        var translator = new AiTranslator(fake);

        var rows = await translator.TranslateAsync(new TranslationRequest(
            "{0} opens the {1}", "en-US", "it-IT", ["actor", "item"], ["1:item"]));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_prompt_hands_the_model_a_real_word_for_each_hole()
    {
        var vocabulary = ValueVocabulary.FromRows(
        [
            new TranslationRow("Water", null, "it-IT", "acqua", "GENDER-female,starts-with-vowel"),
            new TranslationRow("Pot", null, "it-IT", "pentola", "GENDER-female"),
        ]);
        var fake = new ScriptedClient(new Queue<string>());
        var translator = new AiTranslator(fake, vocabulary: vocabulary);

        await translator.TranslateAsync(new TranslationRequest(
            "You take the {0}", "en-US", "it-IT", ["item"], ["0:item"]));

        // The sentence reaches the model with the word already in the hole —
        // the instruction that works for grammars we could never encode.
        Assert.Contains(fake.Prompts, prompt => prompt.Contains("You take the {0:'acqua'}"));
        Assert.Contains(fake.Prompts, prompt => prompt.Contains("COPY THE BRACES OVER UNCHANGED"));
    }

    [Fact]
    public async Task Each_case_is_dictated_and_missing_ones_are_asked_again_alone()
    {
        // The model answers the first batch but drops one case; the translator
        // must come back for it instead of shipping a hole in the table.
        var replies = new Queue<string>(
        [
            """{"rows":[{"template":"{0} prende il {1}","criteria":"1:GENDER-male"}]}""",
            """{"rows":[{"template":"{0} prende la {1}","criteria":"1:GENDER-female"}]}""",
        ]);
        var fake = new ScriptedClient(replies);
        var translator = new AiTranslator(fake);

        var rows = await translator.TranslateAsync(new TranslationRequest(
            "{0} takes the {1}", "en-US", "it-IT", ["actor", "item"],
            ["0:GENDER-male", "1:item"]));

        // Hole 0 is already pinned by the facts, hole 1 varies: 4 cases asked,
        // and the two the model answered come back as rows.
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Template == "{0} prende la {1}");

        // Every call names its cases explicitly, in words.
        Assert.All(fake.Prompts, prompt => Assert.Contains("Translate the sentence", prompt));
        Assert.Contains(fake.Prompts, prompt => prompt.Contains("the value in {1} is feminine"));
    }

    [Fact]
    public async Task A_value_answered_only_in_plural_variants_still_gets_its_generic_row()
    {
        // Without an unconditional row, an item name in a sentence hole would
        // match nothing and silently fall back to English.
        var reply = """
            {"rows":[
              {"template":"falò","criteria":"CLDR-one","traits":"GENDER-male"},
              {"template":"falò","criteria":"CLDR-other","traits":"GENDER-male"}
            ]}
            """;
        var translator = new AiTranslator(new FakeChatClient(reply));

        var rows = await translator.TranslateAsync(Request("Bonfire", [], ["item"]));

        Assert.Equal(3, rows.Count);
        var generic = Assert.Single(rows, row => row.Context is null);
        Assert.Equal("falò", generic.Template);
    }

    [Fact]
    public async Task A_value_starting_with_a_vowel_gets_the_elision_trait_even_if_the_model_forgets()
    {
        // Mechanical facts are derived, not asked: without this trait every
        // elided sentence row would sit unreachable in the table.
        var translator = new AiTranslator(
            new FakeChatClient("""{"rows":[{"template":"acqua","criteria":"","traits":"GENDER-female"}]}"""));

        var row = Assert.Single(await translator.TranslateAsync(Request("Water", [], ["item"])));

        Assert.Contains("GENDER-female", row.Traits);
        Assert.Contains("starts-with-vowel", row.Traits);
    }

    [Fact]
    public async Task Value_holes_are_listed_with_the_decline_the_article_instruction()
    {
        var fake = new FakeChatClient("""{"rows":[]}""");
        var translator = new AiTranslator(fake);

        await translator.TranslateAsync(
            Request("{0} puts the {1} into the {2}", ["actor", "item", "container"], ["0:item", "1:item", "2:item"]));

        var userMessage = fake.LastMessages.Single(m => m.Role == ChatRole.User).Text;

        // The holes that receive translated values, named one by one...
        Assert.Contains("Value holes: {0} (item), {1} (item), {2} (item)", userMessage);

        // ...and the rule that turns criteria into actually different sentences.
        Assert.Contains("MUST DIFFER IN THEIR TEXT", userMessage);
        Assert.Contains("starts-with-vowel", userMessage);
    }

    [Fact]
    public async Task A_sentence_without_value_holes_gets_no_such_checklist()
    {
        var fake = new FakeChatClient("""{"rows":[]}""");
        var translator = new AiTranslator(fake);

        await translator.TranslateAsync(Request("You found {0} coins", ["coins"], ["0:CLDR-other"]));

        var userMessage = fake.LastMessages.Single(m => m.Role == ChatRole.User).Text;
        Assert.DoesNotContain("Value holes", userMessage);
    }
}

public class AiTranslatorPromptConfigurationTests
{
    private sealed class CapturingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public List<Microsoft.Extensions.AI.ChatMessage> LastMessages { get; } = [];

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages.Clear();
            LastMessages.AddRange(messages);
            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, """{"rows":[]}""")));
        }

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static TranslationRequest AnyRequest() => new("Hello", "en-US", "it-IT", [], []);

    [Fact]
    public async Task By_default_the_built_in_system_prompt_is_used()
    {
        var fake = new CapturingChatClient();

        await new AiTranslator(fake).TranslateAsync(AnyRequest());

        var system = fake.LastMessages.Single(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text;
        Assert.Equal(AiTranslator.DefaultSystemPrompt, system);
    }

    [Fact]
    public async Task A_system_prompt_override_replaces_the_default_entirely()
    {
        var fake = new CapturingChatClient();

        await new AiTranslator(fake, systemPrompt: "CUSTOM PROMPT").TranslateAsync(AnyRequest());

        var system = fake.LastMessages.Single(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text;
        Assert.Equal("CUSTOM PROMPT", system);
    }

    [Fact]
    public async Task An_application_context_is_appended_to_the_default_prompt()
    {
        var fake = new CapturingChatClient();

        await new AiTranslator(fake, applicationContext: "A cookbook full of Italian recipes")
            .TranslateAsync(AnyRequest());

        var system = fake.LastMessages.Single(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text;
        Assert.StartsWith(AiTranslator.DefaultSystemPrompt, system);
        Assert.Contains("A cookbook full of Italian recipes", system);
    }

    /// <summary>Fails the first few calls the way a service does, then answers.</summary>
    private sealed class FlakyChatClient(int failures, Exception failure) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Calls <= failures
                ? throw failure
                : Task.FromResult(new ChatResponse(new ChatMessage(
                    ChatRole.Assistant, """{"rows":[{"template":"Ciao","criteria":"","traits":""}]}""")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task A_service_that_did_not_answer_is_asked_again()
    {
        // Measured the hard way: a unit failed three runs in a row with
        // "Service request failed" while the same sentence, asked by hand
        // against the same endpoint, came back immediately.
        var flaky = new FlakyChatClient(2, new HttpRequestException("Service request failed"));

        var rows = await new AiTranslator(flaky, retry: new TransportRetry(3, TimeSpan.Zero))
            .TranslateAsync(AnyRequest());

        Assert.Equal(3, flaky.Calls);
        Assert.Equal("Ciao", Assert.Single(rows).Template);
    }

    [Fact]
    public async Task A_service_that_keeps_failing_gives_up_after_its_attempts()
    {
        var flaky = new FlakyChatClient(99, new HttpRequestException("Service request failed"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new AiTranslator(flaky, retry: new TransportRetry(3, TimeSpan.Zero)).TranslateAsync(AnyRequest()));

        Assert.Equal(3, flaky.Calls);
    }

    [Fact]
    public async Task A_failure_that_is_not_the_line_is_not_worth_a_second_ask()
    {
        // A malformed request or a model that does not exist fails the same way
        // every time: insisting only spends the same failure four times.
        var flaky = new FlakyChatClient(99, new InvalidOperationException("no such model"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AiTranslator(flaky, retry: new TransportRetry(4, TimeSpan.Zero)).TranslateAsync(AnyRequest()));

        Assert.Equal(1, flaky.Calls);
    }

    [Fact]
    public async Task The_reason_an_answer_was_rejected_reaches_the_model()
    {
        // Without it a second attempt is the same question at temperature 0,
        // and comes back with the same defect.
        var fake = new CapturingChatClient();

        await new AiTranslator(fake).TranslateAsync(
            AnyRequest() with { Feedback = "the sentence agrees with hole {1} in some cases but not in others" });

        var user = fake.LastMessages.Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("REJECTED", user, StringComparison.Ordinal);
        Assert.Contains("agrees with hole {1}", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Backoff_doubles_and_starts_at_zero_for_the_first_try()
    {
        var retry = new TransportRetry(4, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, retry.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(2), retry.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(4), retry.DelayBefore(3));
        Assert.Equal(TimeSpan.FromSeconds(8), retry.DelayBefore(4));
    }
}
