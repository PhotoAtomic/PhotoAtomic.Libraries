using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;

namespace PhotoAtomic;

/// <summary>
/// ITranslator backed by any Microsoft.Extensions.AI chat client. The model
/// receives the source template, the legend naming each hole and the facts of
/// the call that missed, and answers with one or more rows — template,
/// criteria, traits — in the exact vocabulary of the matching engine: CLDR
/// category names for plurals, free trait tags (male, female,
/// starts-with-vowel) for grammar.
/// </summary>
public sealed class AiTranslator(IChatClient chatClient, string? systemPrompt = null, string? applicationContext = null) : ITranslator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record RowDto(string? Template, string? Criteria, string? Traits);

    /// <summary>
    /// Connects to any OpenAI-compatible endpoint (e.g. an Azure AI Foundry
    /// deployment). Accepts the endpoint with or without the trailing
    /// /chat/completions segment.
    /// </summary>
    public static AiTranslator ForOpenAiCompatibleEndpoint(Uri endpoint, string apiKey, string model, string? systemPrompt = null, string? applicationContext = null)
    {
        var baseEndpoint = new Uri(endpoint.AbsoluteUri.Replace("/chat/completions", string.Empty).TrimEnd('/'));

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = baseEndpoint })
            .GetChatClient(model)
            .AsIChatClient();

        return new AiTranslator(client, systemPrompt, applicationContext);
    }

    public async Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, effectiveSystemPrompt),
            new(ChatRole.User, BuildUserPrompt(request)),
        ];

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0 };
        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

        return Parse(response.Text, request);
    }

private readonly string effectiveSystemPrompt = ComposeSystemPrompt(systemPrompt, applicationContext);

    /// <summary>
    /// Builds the system message: the override replaces the default prompt
    /// entirely (expert use — format rules included); the application context
    /// is additive and just tells the model what the app is about, so it can
    /// pick the right senses and tone (recipes, a WWII book, a pirate game...).
    /// </summary>
    private static string ComposeSystemPrompt(string? systemPrompt, string? applicationContext)
    {
        var basePrompt = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultSystemPrompt : systemPrompt;

        return string.IsNullOrWhiteSpace(applicationContext)
            ? basePrompt
            : basePrompt + "\n\nGeneral context of the application (choose word senses and tone accordingly): " + applicationContext;
    }

    /// <summary>The built-in prompt teaching the row format; visible so callers can extend instead of replacing.</summary>
    public const string DefaultSystemPrompt =
        """
        You translate user-interface sentences for a computer program. Sentences are
        templates where {0}, {1}... are holes filled at runtime; a legend names each
        hole and facts describe the call with hole-prefixed tags:
        - "0:CLDR-one" means hole 0 currently holds a number whose Unicode CLDR
          plural category is "one" (the full set is CLDR-zero, CLDR-one, CLDR-two,
          CLDR-few, CLDR-many, CLDR-other).
        - "1:GENDER-female" means the value in hole 1 has feminine grammatical gender.
        - other tags ("1:tool", "menu") are semantic contexts describing what the
          value or the sentence is about.

        Answer with STRICT JSON only: an object {"rows": [...]} where each row is
        {"template": string, "criteria": string, "traits": string}.

        Rules:
        - Keep every {n} placeholder; reorder them freely to follow the target grammar.
        - criteria: comma-separated tags a call must satisfy for the row to apply
          (e.g. "0:CLDR-one" or "0:CLDR-other,1:GENDER-female"); empty string for a
          generic row.
        - When a hole carries a CLDR plural category fact, emit one row per plural
          category of the TARGET language — always all of its categories, not only
          the one seen in the facts.
        - When the key is a single word or short phrase (a value, not a sentence),
          translate it and declare its grammatical traits in "traits":
          "GENDER-male" or "GENDER-female" for grammatical gender, plus
          "starts-with-vowel" when the translation starts with a vowel sound; emit
          plural variants as extra rows with criteria "CLDR-other" (and "CLDR-few",
          "CLDR-many" where the target language needs them).
        - Values are inserted into sentence holes BARE: never with an article.
          Sentence templates own every article and preposition, declined through
          variant rows (e.g. criteria "0:GENDER-female" -> "La {0} è rotta",
          criteria "0:GENDER-female,0:starts-with-vowel" -> "L'{0} è matura").
        - Translate single-word values in lowercase. Exception: proper names, and
          nouns in languages that always capitalize them (like German) — for those
          add the trait "Capitalize", and the engine will uppercase the word's
          first letter wherever it appears. The engine also capitalizes sentence
          openings mechanically, so lowercase values are safe at the start of a
          sentence.
        - When the key is a single word or short phrase, its facts state its
          SEMANTIC DOMAIN (e.g. "tool" means the word names a physical tool):
          translate that sense of the word, never a homonym from another domain.
        - Sentence rows leave "traits" empty.
        - Use exactly the vocabulary above for tags; never invent new tag names.
        """;

    private static string BuildUserPrompt(TranslationRequest request)
    {
        var prompt = new StringBuilder()
            .Append("Source language: ").AppendLine(request.SourceLanguage)
            .Append("Target language: ").AppendLine(request.TargetLanguage)
            .Append("Key to translate: ").AppendLine(request.Key);

        if (request.Legend.Count > 0)
        {
            prompt.Append("Legend: ");
            for (var i = 0; i < request.Legend.Count; i++)
            {
                if (i > 0)
                {
                    prompt.Append(", ");
                }

                prompt.Append('{').Append(i).Append("} = ").Append(request.Legend[i]);
            }

            prompt.AppendLine();
        }

        if (request.Facts.Count > 0)
        {
            prompt.Append("Facts of the call that missed: ").AppendLine(string.Join(", ", request.Facts));
        }

        // The explicit checklist: listing the categories the target language
        // distinguishes nudges the model to emit every required variant row
        // instead of remembering CLDR on its own.
        prompt.Append("Plural categories of the target language: ")
            .AppendLine(string.Join(", ", PluralRules.CategoriesOf(request.TargetLanguage)));
        prompt.AppendLine(
            "When plurality applies (a numeric hole, or a countable single word), emit exactly one row per category listed above.");

        prompt.Append("Gender traits: ")
            .Append(WellKnownTraits.GenderMale).Append(", ").AppendLine(WellKnownTraits.GenderFemale);
        prompt.AppendLine(
            "A single-word value that is a noun must declare its gender trait; "
            + "when a sentence hole hosts a gendered value, emit one row per gender (criteria like 0:GENDER-female).");

        return prompt.ToString();
    }

    private static IReadOnlyList<TranslationRow> Parse(string responseText, TranslationRequest request)
    {
        var json = StripFences(responseText);

        List<RowDto>? dtos;
        try
        {
            using var document = JsonDocument.Parse(json);
            var rowsElement = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("rows", out var rows)
                    ? rows
                    : document.RootElement;

            dtos = JsonSerializer.Deserialize<List<RowDto>>(rowsElement.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (dtos is null)
        {
            return [];
        }

        return dtos
            .Where(dto => !string.IsNullOrWhiteSpace(dto.Template))
            .Select(dto => new TranslationRow(
                request.Key,
                NullIfEmpty(dto.Criteria),
                request.TargetLanguage,
                dto.Template!,
                NullIfEmpty(dto.Traits)))
            .ToArray();
    }

    private static string? NullIfEmpty(string? tags) =>
        string.IsNullOrWhiteSpace(tags) ? null : tags;

    /// <summary>Models sometimes wrap JSON in markdown fences despite instructions.</summary>
    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
