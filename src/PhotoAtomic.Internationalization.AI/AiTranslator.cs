using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public sealed class AiTranslator(
    IChatClient chatClient,
    string? systemPrompt = null,
    string? applicationContext = null,
    ValueVocabulary? vocabulary = null,
    TransportRetry? retry = null) : ITranslator
{
    private readonly TransportRetry retry = retry ?? TransportRetry.Default;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record RowDto(string? Template, string? Criteria, string? Traits);

    /// <summary>
    /// Connects to any OpenAI-compatible endpoint (e.g. an Azure AI Foundry
    /// deployment). Accepts the endpoint with or without the trailing
    /// /chat/completions segment.
    /// </summary>
    public static AiTranslator ForOpenAiCompatibleEndpoint(
        Uri endpoint,
        string apiKey,
        string model,
        string? systemPrompt = null,
        string? applicationContext = null,
        ValueVocabulary? vocabulary = null,
        TransportRetry? retry = null)
    {
        var baseEndpoint = new Uri(endpoint.AbsoluteUri.Replace("/chat/completions", string.Empty).TrimEnd('/'));

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = baseEndpoint })
            .GetChatClient(model)
            .AsIChatClient();

        return new AiTranslator(client, systemPrompt, applicationContext, vocabulary, retry);
    }

    public async Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        // Sentences whose holes host translated values need one row per
        // grammatical case, and the model cannot be trusted to enumerate them:
        // left free it either forgets to decline or explodes into every
        // combination it can imagine. So WE compute the cases and ask for them
        // by name, one small batch at a time, then chase the missing ones.
        var cases = VariantCases.For(request, vocabulary);
        return cases.Count > 0
            ? await TranslateByCasesAsync(request, cases, cancellationToken)
            : await AskAsync(request, BuildUserPrompt(request), cancellationToken);
    }

    private async Task<IReadOnlyList<TranslationRow>> TranslateByCasesAsync(
        TranslationRequest request,
        IReadOnlyList<VariantCase> cases,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, TranslationRow>(StringComparer.Ordinal);

        // The wording of the first accepted variant leads the others: asked in
        // isolation, a model happily picks a different verb every time
        // ("mette in tasca", "si intasca", "infila in tasca") and the game
        // starts speaking differently depending on the gender of an object.
        string? lead = null;

        foreach (var batch in cases.Chunk(CasesPerCall))
        {
            foreach (var row in await AskAsync(request, BuildCasesPrompt(request, batch, lead), cancellationToken))
            {
                if (Accept(row, request, batch) is { } accepted)
                {
                    rows[VariantCases.Normalize(accepted.Context)] = accepted;
                    lead ??= accepted.Template;
                }
            }

            // Whatever the batch skipped is asked again on its own: a single
            // case per call is the most explicit instruction we can give.
            foreach (var missing in batch.Where(variant => !rows.ContainsKey(VariantCases.Normalize(variant.Criteria))))
            {
                foreach (var row in await AskAsync(request, BuildCasesPrompt(request, [missing], lead), cancellationToken))
                {
                    if (Accept(row, request, [missing]) is { } accepted)
                    {
                        rows[VariantCases.Normalize(accepted.Context)] = accepted;
                        lead ??= accepted.Template;
                    }
                }
            }
        }

        // Keep only the cases we asked for, under OUR criteria: no stray rows.
        return cases
            .Where(variant => rows.ContainsKey(VariantCases.Normalize(variant.Criteria)))
            .Select(variant => rows[VariantCases.Normalize(variant.Criteria)] with { Context = variant.Criteria })
            .ToArray();
    }

    /// <summary>
    /// Checks a row against the case it answers and strips the annotations
    /// back to plain holes. Rejected — so the caller asks again — when a hole
    /// did not come home, or when an example word leaked outside its brace
    /// (the classic "infila la {1} nel falò", where the second example stayed
    /// in the template and every future sentence would mention that bonfire).
    /// </summary>
    private static TranslationRow? Accept(TranslationRow row, TranslationRequest request, IReadOnlyList<VariantCase> asked)
    {
        var template = Deannotate(row.Template);

        var holes = HolePattern.Matches(request.Key).Select(match => match.Value).Distinct().ToList();
        if (holes.Any(hole => !template.Contains(hole, StringComparison.Ordinal)))
        {
            return null; // a placeholder was dissolved into the sentence
        }

        // ...and none invented: a hole the sentence does not have would throw
        // at render time, taking the whole screen down with it.
        if (HolePattern.Matches(template).Any(match => !holes.Contains(match.Value, StringComparer.Ordinal)))
        {
            return null;
        }

        var examples = asked
            .SelectMany(variant => variant.Examples.Values)
            .Where(example => example.Length > 2) // digits and tiny words match too easily
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var bare = HolePattern.Replace(template, string.Empty);
        if (examples.Any(example => bare.Contains(example, StringComparison.OrdinalIgnoreCase)))
        {
            return null; // an example word stayed in the template
        }

        return row with { Template = template };
    }

    private async Task<IReadOnlyList<TranslationRow>> AskAsync(
        TranslationRequest request,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, effectiveSystemPrompt),
            new(ChatRole.User, WithFeedback(userPrompt, request)),
        ];

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0 };
        var response = await GetResponseAsync(messages, options, cancellationToken);

        return Parse(response.Text, request);
    }

    /// <summary>
    /// The complaint goes LAST, after every rule and example: it is the one
    /// thing about this call that is not true of every other call, and the last
    /// paragraph is the one a model weighs most.
    /// </summary>
    private static string WithFeedback(string userPrompt, TranslationRequest request) =>
        string.IsNullOrWhiteSpace(request.Feedback)
            ? userPrompt
            : userPrompt
                + Environment.NewLine
                + "A previous attempt at this same sentence was REJECTED for this reason: "
                + request.Feedback
                + Environment.NewLine
                + "Write it again, fixing exactly that, and keep everything else about the translation as it was.";

    /// <summary>
    /// Asking, with the line's own failures told apart from the model's. A
    /// refused answer is judged upstream and never retried here — the same
    /// question at temperature 0 returns the same answer. A service that broke,
    /// timed out or throttled gets asked again after a growing pause, because
    /// that is a different question only in when it is asked.
    /// </summary>
    private async Task<ChatResponse> GetResponseAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await chatClient.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (Exception exception) when (attempt < retry.Attempts
                && !cancellationToken.IsCancellationRequested
                && IsTransport(exception))
            {
                await Task.Delay(retry.DelayBefore(attempt + 1), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Whether the call never got an answer, as opposed to getting a bad one.
    /// Refusals with a verdict of their own — a wrong key, a model that does
    /// not exist, a request we malformed — are 4xx and stay final: insisting
    /// only spends the same failure four times.
    /// </summary>
    private static bool IsTransport(Exception exception) => exception switch
    {
        ClientResultException failure => failure.Status is 0 or 408 or 409 or 425 or 429 or >= 500,
        HttpRequestException => true,
        IOException => true,
        OperationCanceledException => true, // its own timeout, not our token: checked by the caller
        _ => false,
    };

    /// <summary>
    /// One case per call. Batching was cheaper but measurably worse: asked
    /// for four variants at once the model would copy the same wording into
    /// two of them ("dal" where "dalla" was due). Alone, with its example
    /// word in hand, it has exactly one sentence to get right.
    /// </summary>
    private const int CasesPerCall = 1;

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

        AppendValueHoleChecklist(prompt, request);

        return prompt.ToString();
    }

    /// <summary>
    /// The sentence with each hole carrying the word that will land in it:
    /// "You have {0:'3'} coins in your {1:'borsa'}". The model writes the
    /// translation AROUND those braces and leaves them untouched, so it never
    /// has to put placeholders back — and we can verify, hole by hole, that
    /// it did (idea of the user, and it removed a whole class of defects).
    /// </summary>
    private static string Annotate(string key, IReadOnlyDictionary<int, string> examples) =>
        HolePattern.Replace(key, match =>
        {
            var index = int.Parse(match.Groups[1].Value);
            return examples.TryGetValue(index, out var example) && example.Length > 0
                ? $"{{{index}:'{example}'}}"
                : match.Value;
        });

    /// <summary>Turns "{0:'monete'}" back into "{0}"; tolerant about how the model spaced or quoted it.</summary>
    internal static string Deannotate(string template) =>
        AnnotatedHolePattern.Replace(template, "{$1}");

    private static readonly Regex HolePattern = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static readonly Regex AnnotatedHolePattern = new(@"\{\s*(\d+)\s*:\s*[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// The prompt for a handful of specific grammatical cases: the criteria
    /// are dictated, so the model only has to write the sentence for each
    /// one — no counting, no inventing, and each case spelled out in words.
    /// </summary>
    private static string BuildCasesPrompt(TranslationRequest request, IReadOnlyList<VariantCase> cases, string? lead = null)
    {
        // The variant checklist stays even though the cases are explicit:
        // removing it was tried and measurably worse — the model dropped the
        // articles again. Redundant instructions, but they earn their place.
        var prompt = new StringBuilder(BuildUserPrompt(request));

        prompt.AppendLine();
        prompt.Append("Translate the sentence ").Append(cases.Count)
            .AppendLine(" times, once for each case below. Answer with EXACTLY one row per case,");
        prompt.AppendLine("copying its criteria string verbatim into \"criteria\", and leaving \"traits\" empty:");

        foreach (var variant in cases)
        {
            prompt.Append("- criteria \"").Append(variant.Criteria).Append("\": ")
                .Append(Describe(variant.Criteria)).AppendLine(". Sentence for this case:");

            // Each hole already carries the word that will land in it. The
            // model writes the translation AROUND these braces and copies
            // them over untouched — no placeholder to put back, and we can
            // check afterwards that every one of them came home.
            prompt.Append("    ").AppendLine(Annotate(request.Key, variant.Examples));
        }

        prompt.AppendLine();
        prompt.AppendLine(
            "Inside each brace you find the hole index and, after the colon, the EXACT term that will appear "
            + "there in the target language. Write the sentence so that it is grammatically perfect with those "
            + "terms in place — articles, prepositions, contractions, agreements, suffixes, mutations, word "
            + "order, whatever this language requires.");
        prompt.AppendLine(
            "COPY THE BRACES OVER UNCHANGED, exactly as given, including the term inside them. Never translate, "
            + "inflect, move or drop what is inside a brace, and never write the term outside its brace. "
            + "Everything a value needs in order to fit — the article in front of it, the preposition, the "
            + "case ending — belongs OUTSIDE the brace and is your job.");
        prompt.AppendLine(
            "Example (Italian): \"You have {0:'3'} coins in your {1:'borsa'}\" becomes "
            + "\"Hai {0:'3'} monete nella tua {1:'borsa'}\".");

        if (lead is not null)
        {
            prompt.AppendLine();
            prompt.Append("Another case of this same sentence was already translated as: ").AppendLine(lead);
            prompt.AppendLine(
                "Keep EXACTLY the same words and style — same verb, same turn of phrase. Only the grammar "
                + "around the holes may differ, as this case requires.");
        }

        return prompt.ToString();
    }

    /// <summary>Turns "0:GENDER-female,1:GENDER-male" into a sentence the model can act on.</summary>
    private static string Describe(string criteria)
    {
        var parts = criteria.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Split(':', 2))
            .Where(tag => tag.Length == 2)
            .GroupBy(tag => tag[0], StringComparer.Ordinal)
            .Select(group =>
            {
                var traits = group.Select(tag => tag[1]).ToList();
                var described = traits.Select(trait => trait switch
                {
                    WellKnownTraits.GenderMale => "masculine",
                    WellKnownTraits.GenderFemale => "feminine",
                    WellKnownTraits.StartsWithVowel => "starting with a vowel sound",
                    _ when trait.StartsWith("CLDR-", StringComparison.Ordinal) =>
                        $"a number of plural category {trait}",
                    // Unknown traits are passed through verbatim: a language may
                    // declare properties we never anticipated, and the model
                    // knows what they mean better than we do.
                    _ => $"marked \"{trait}\"",
                });

                return $"the value in {{{group.Key}}} is {string.Join(", ", described)}";
            });

        return string.Join("; ", parts);
    }

    /// <summary>
    /// The holes that receive translated VALUES, listed explicitly. Without
    /// this, models emit the right criteria with identical text — the variants
    /// exist but nothing is declined. Same lesson as the plural categories:
    /// what you want systematically, you enumerate.
    /// </summary>
    private static void AppendValueHoleChecklist(StringBuilder prompt, TranslationRequest request)
    {
        var holes = request.Facts
            .Select(fact => fact.Split(':', 2))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && IsSemanticContext(parts[1]))
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .OrderBy(group => int.Parse(group.Key))
            .Select(group => $"{{{group.Key}}} ({string.Join('/', group.Select(parts => parts[1]).Distinct())})")
            .ToList();

        if (holes.Count == 0)
        {
            return;
        }

        prompt.Append("Value holes: ").AppendLine(string.Join(", ", holes));
        prompt.AppendLine(
            "These holes are filled at runtime with translated values that arrive BARE and lowercase, "
            + "and whose grammatical gender is unknown in advance. Therefore:");
        prompt.AppendLine(
            "- emit one row per COMBINATION of genders of these holes "
            + "(criteria like \"0:GENDER-female,1:GENDER-male\");");
        prompt.AppendLine(
            "- the rows MUST DIFFER IN THEIR TEXT: what changes is the article, preposition or agreement "
            + "around the hole. Rows that carry different criteria but identical text are useless — "
            + "if the target language needs no change, emit a single generic row instead;");
        prompt.AppendLine(
            "- when the target language elides or contracts before a vowel, add the same rows again with the "
            + $"extra criterion \"<hole>:{WellKnownTraits.StartsWithVowel}\" and the elided wording.");
    }

    /// <summary>A hole's semantic domain ("item", "tool"), as opposed to the reserved grammatical vocabulary.</summary>
    private static bool IsSemanticContext(string context) =>
        !context.StartsWith("CLDR-", StringComparison.Ordinal)
        && !context.StartsWith("GENDER-", StringComparison.Ordinal)
        && context != WellKnownTraits.StartsWithVowel
        && context != WellKnownTraits.Capitalize;

    private static IReadOnlyList<TranslationRow> Parse(string responseText, TranslationRequest request)
    {
        var json = StripFences(responseText);

        List<RowDto>? dtos;
        try
        {
            using var document = JsonDocument.Parse(json);
            var rowsElement = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("rows", out var wrapped)
                    ? wrapped
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

        var rows = dtos
            .Where(dto => !string.IsNullOrWhiteSpace(dto.Template))
            .Select(dto => new TranslationRow(
                request.Key,
                NullIfEmpty(dto.Criteria),
                request.TargetLanguage,
                dto.Template!,
                WithDerivedTraits(NullIfEmpty(dto.Traits), dto.Template!, request)))
            .ToList();

        return WithGenericRow(rows, request);
    }

    private static string? NullIfEmpty(string? tags) =>
        string.IsNullOrWhiteSpace(tags) ? null : tags;

    /// <summary>
    /// A VALUE must always have an unconditional row: models sometimes answer
    /// only with plural variants ("CLDR-one", "CLDR-other"), and then a plain
    /// lookup — an item name in a sentence hole — matches nothing and falls
    /// back to English. The singular form is the natural generic one.
    /// </summary>
    private static IReadOnlyList<TranslationRow> WithGenericRow(List<TranslationRow> produced, TranslationRequest request)
    {
        if (request.Key.Contains('{') || produced.Count == 0 || produced.Any(row => row.Context is null))
        {
            return produced;
        }

        var singular = produced.FirstOrDefault(row =>
            row.Context!.Contains(PluralRules.One, StringComparison.Ordinal)) ?? produced[0];

        produced.Insert(0, singular with { Context = null });
        return produced;
    }

    /// <summary>
    /// Adds the traits we can decide ourselves. starts-with-vowel is one of
    /// them: it is a property of the translated text, models forget it, and
    /// without it every elided sentence row stays unreachable. Only for
    /// VALUES — a sentence template's first letter says nothing about the
    /// values that will fill its holes.
    /// </summary>
    private static string? WithDerivedTraits(string? traits, string template, TranslationRequest request)
    {
        if (request.Key.Contains('{') || !GrammarRules.StartsWithVowelSound(template, request.TargetLanguage))
        {
            return traits;
        }

        var tags = (traits ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!tags.Contains(WellKnownTraits.StartsWithVowel, StringComparer.Ordinal))
        {
            tags.Add(WellKnownTraits.StartsWithVowel);
        }

        return string.Join(',', tags);
    }

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
