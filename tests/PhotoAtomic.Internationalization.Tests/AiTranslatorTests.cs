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
}
