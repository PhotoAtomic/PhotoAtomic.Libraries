using System.Collections.Concurrent;
using System.Globalization;

namespace PhotoAtomic;

/// <summary>
/// Structural-key internationalization. T($"...") derives a canonical key from
/// the shape of an interpolated string (literals + hole positions) and picks,
/// among the rows registered for the ambient <see cref="Language"/>, the one
/// whose criteria are best satisfied by the facts of the call. Facts are
/// collected from the sentence context, from [Translatable] type contexts, from
/// CLDR plural categories of numeric holes, and from the traits declared by
/// the chosen value translations. A row matches only when ALL its criteria are
/// satisfied; among matches the most specific (most criteria) wins, and the
/// last registered wins ties. With no matching row the key itself renders the
/// source language, so untranslated text always displays correctly — and when
/// a translator is attached the miss is queued for background fill, so the
/// next call finds the row.
///
/// Import with `using static PhotoAtomic.Internationalization;` to write bare
/// T(...), like Sin/Cos with System.Math.
/// </summary>
public static class Internationalization
{
    /// <summary>The language templates are written in; also the ultimate fallback.</summary>
    public const string SourceLanguage = "en-US";

    // AsyncLocal instead of a plain static: a future server will translate for
    // two players with different languages inside the same process, so the
    // ambient language must follow the async flow, not the whole appdomain.
    private static readonly AsyncLocal<string?> AmbientLanguage = new();

    private sealed record Row(string[] Criteria, string Template, string[] Traits);

    // Rows per (key, language), in registration order: ties resolve to the
    // last row, which pairs naturally with the append-only store.
    private static readonly ConcurrentDictionary<(string Key, string Language), List<Row>> Table = new();

    private static readonly ConcurrentDictionary<Type, string[]?> TranslatableContexts = new();

    private static readonly ConcurrentDictionary<(Type Type, string Member), string[]?> TranslatableMemberContexts = new();

    // One background fill per missing (key, language); failed fills stay in
    // the map on purpose so a broken endpoint is not hammered on every render.
    private static readonly ConcurrentDictionary<(string Key, string Language), Task> PendingFills = new();

    private static ITranslationStore? store;

    private static ITranslator? translator;

    /// <summary>Target language for the current async context (e.g. "it-IT").</summary>
    public static string Language
    {
        get => AmbientLanguage.Value ?? SourceLanguage;
        set => AmbientLanguage.Value = value;
    }

    /// <summary>
    /// Attaches a persistent store: its rows are loaded into the table (later
    /// rows win) and every future registration is written through to it.
    /// Pass null to detach.
    /// </summary>
    public static void UseStore(ITranslationStore? translationStore)
    {
        store = translationStore;
        if (translationStore is null)
        {
            return;
        }

        foreach (var row in translationStore.LoadAll())
        {
            AddRow(row.Key, row.Language, row.Context, row.Template, row.Traits);
        }
    }

    /// <summary>
    /// Attaches a translator used to fill missing rows in the background.
    /// Pass null to detach.
    /// </summary>
    public static void UseTranslator(ITranslator? missingRowTranslator) => translator = missingRowTranslator;

    /// <summary>
    /// Teaches the engine to write numbers in words — "two", "due", "два" —
    /// for values wrapped in <see cref="Spelled"/>. Given the amount and the
    /// target language, return the words, or null to decline.
    ///
    /// A hook rather than a dependency because this library ships inside a
    /// WebAssembly client: whoever wants a spelling library pays for it, and
    /// whoever does not gets digits. DECLINING IS PART OF THE CONTRACT — a
    /// speller that does not know a language must return null rather than
    /// guess, since a library that quietly answers in English (some do, for
    /// Gaelic and Welsh) puts an English word in the middle of a Gaelic
    /// sentence, which is worse than the digit it replaced.
    ///
    /// The TABLE always wins: a row for "1" is how a language says the thing
    /// its rules cannot state — a form that agrees with the noun after it, an
    /// irregularity, a word no library knows.
    /// </summary>
    public static void UseNumberWords(Func<decimal, string, string?>? speller) => numberWords = speller;

    private static Func<decimal, string, string?>? numberWords;

    /// <summary>Completes when every queued background fill has finished. Meant for tests, demos and shutdown.</summary>
    public static Task WhenIdleAsync() => Task.WhenAll(PendingFills.Values.ToArray());

    /// <summary>
    /// Registers a translation row. Context lists the row's criteria
    /// ("menu,0:one,1:female"); traits lists the facts the row declares about
    /// its own text ("female,starts-with-vowel"). Comma-separated, order-free.
    /// </summary>
    public static void SetTranslation(string key, string language, string template, string? context = null, string? traits = null)
    {
        AddRow(key, language, context, template, traits);
        store?.Save(new TranslationRow(key, context, language, template, traits));
    }

    /// <summary>Removes every registered translation and forgets past fill attempts. Meant for tests and tooling.</summary>
    public static void ClearTranslations()
    {
        Table.Clear();
        PendingFills.Clear();
    }

    /// <summary>The canonical key an interpolated string reduces to. Exposed for tests and tooling.</summary>
    public static string KeyOf(TranslationInterpolatedStringHandler text) => text.Key;

    /// <summary>
    /// The legend of an interpolated string: for each positional slot, the
    /// source expression that fills it (key "{0} is the color of {1}" gives
    /// ["color", "sentiment"]). Meant for tooling and as semantic context for
    /// translators, especially AI ones.
    /// </summary>
    public static IReadOnlyList<string> LegendOf(TranslationInterpolatedStringHandler text)
    {
        var legend = new string[text.Arguments.Count];
        for (var i = 0; i < legend.Length; i++)
        {
            legend[i] = text.Arguments[i].Expression;
        }

        return legend;
    }

    /// <summary>
    /// Translates a VALUE on its own — a label, a list entry, an item name in
    /// the UI — instead of inside a sentence. Values live in the table by
    /// content, with their traits; asking for one through a sentence key like
    /// "{0}" would invite a translator to wrap it in an article, which is
    /// exactly what values must never carry. Untranslated values render as
    /// they are, so English always works.
    /// </summary>
    public static string Value(object? value, string? context = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var language = Language;
        var rendered = Convert.ToString(value, ResolveCulture(language)) ?? string.Empty;

        var facts = new HashSet<string>(ParseTags(context), StringComparer.Ordinal);
        if (ContextsOf(value) is { } contexts)
        {
            foreach (var typeContext in contexts)
            {
                facts.Add(typeContext);
            }
        }

        if (BestRow(rendered, language, facts) is not { } row)
        {
            QueueFill(rendered, language, [], facts);
            return Spell(value, language) ?? rendered;
        }

        return row.Traits.Contains(WellKnownTraits.Capitalize) && row.Template.Length > 0
            ? ResolveCulture(language).TextInfo.ToUpper(row.Template[0]) + row.Template[1..]
            : row.Template;
    }

    /// <summary>Translates and renders an interpolated string for the ambient language.</summary>
    public static string T(TranslationInterpolatedStringHandler text, string? context = null)
    {
        var language = Language;
        var culture = ResolveCulture(language);
        var arguments = text.Arguments;
        var sentenceContexts = ParseTags(context);

        // Facts of this call, the currency of all matching. Sentence-level
        // facts carry the hole position as prefix ("1:female"); value-level
        // lookups use them unprefixed.
        var facts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sentenceContext in sentenceContexts)
        {
            facts.Add(sentenceContext);
        }

        var legend = new string[arguments.Count];
        var typeContexts = new string[]?[arguments.Count];
        var categories = new List<string>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            legend[i] = argument.Expression;
            var value = argument.Value;

            if (PluralRules.CategoryOf(value, language) is { } category)
            {
                categories.Add(category);
                facts.Add($"{i}:{category}");
            }

            typeContexts[i] = value is null ? null : ContextsOf(value);
            if (typeContexts[i] is { } contexts)
            {
                foreach (var typeContext in contexts)
                {
                    facts.Add($"{i}:{typeContext}");
                }
            }
        }

        // Format every hole; values of [Translatable] types translate by
        // content, and the traits of their chosen row become sentence facts.
        var holes = new object?[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var rendered = FormatValue(argument, culture);

            if (typeContexts[i] is { } ownContexts)
            {
                var valueFacts = new HashSet<string>(StringComparer.Ordinal);
                foreach (var ownContext in ownContexts)
                {
                    valueFacts.Add(ownContext);
                }

                foreach (var sentenceContext in sentenceContexts)
                {
                    valueFacts.Add(sentenceContext);
                }

                // A value's plurality is governed by the numbers around it in
                // the sentence ("2 keys" -> "chiavi"), so every category flows
                // into its lookup unprefixed.
                foreach (var category in categories)
                {
                    valueFacts.Add(category);
                }

                if (BestRow(rendered, language, valueFacts) is { } valueRow)
                {
                    rendered = valueRow.Template;
                    foreach (var declared in valueRow.Traits)
                    {
                        facts.Add($"{i}:{declared}");
                    }

                    // Proper names and always-capitalized nouns keep their
                    // uppercase initial wherever they appear in the sentence.
                    if (valueRow.Traits.Contains(WellKnownTraits.Capitalize) && rendered.Length > 0)
                    {
                        rendered = culture.TextInfo.ToUpper(rendered[0]) + rendered[1..];
                    }
                }
                else
                {
                    QueueFill(rendered, language, [argument.Expression], valueFacts);
                    rendered = Spell(argument.Value, language) ?? rendered;
                }
            }

            holes[i] = ApplyAlignment(rendered, argument.Alignment);
        }

        var sentenceRow = BestRow(text.Key, language, facts);
        if (sentenceRow is null)
        {
            QueueFill(text.Key, language, legend, facts);
        }

        var template = sentenceRow?.Template ?? text.Key;

        // The capital goes on the VALUE that opens the sentence, never on the
        // template: values are stored lowercase and need one, a template was
        // written by someone who already decided how it opens. Which holes are
        // in that position depends on the template chosen — a language may put
        // the subject last — so it is asked of the row that will actually be
        // rendered, this one or the fallback.
        string Render(string source)
        {
            var values = (object?[])holes.Clone();
            foreach (var index in GrammarRules.HolesOpeningASentence(source))
            {
                if (index < values.Length && values[index] is string opening)
                {
                    values[index] = GrammarRules.CapitalizeInitial(opening, language);
                }
            }

            return string.Format(culture, source, values);
        }

        string sentence;
        try
        {
            sentence = Render(template);
        }
        catch (FormatException)
        {
            // A malformed row — typically a machine translation that invented
            // a hole the sentence does not have — must never take the screen
            // down: the source template always renders.
            sentence = Render(text.Key);
        }

        // Elision is the one mechanic left to apply to the whole sentence: it
        // contracts the little words in front of the vowels the values happened
        // to bring, which is knowable only once they are in place.
        return GrammarRules.ApplyElision(sentence, language);
    }

    /// <summary>
    /// The words for a number nobody translated, when a speller was
    /// registered and knows this language. Only for values that ASKED to be
    /// spelled: a bare number in a sentence stays a numeral, because most of
    /// them should.
    /// </summary>
    private static string? Spell(object? value, string language) =>
        value is Spelled spelled ? numberWords?.Invoke(spelled.Amount, language) : null;

    /// <summary>
    /// Queues a background translation for a missing row, once per
    /// (key, language). The render that missed returns the fallback
    /// immediately; a later render finds the filled row.
    /// </summary>
    private static void QueueFill(string key, string language, string[] legend, IReadOnlySet<string> facts)
    {
        if (translator is null || language == SourceLanguage)
        {
            return;
        }

        PendingFills.GetOrAdd((key, language), missing => Task.Run(async () =>
        {
            try
            {
                var request = new TranslationRequest(missing.Key, SourceLanguage, missing.Language, legend, facts.ToArray());
                var rows = await (translator?.TranslateAsync(request) ?? Task.FromResult<IReadOnlyList<TranslationRow>>([]));

                // The key and language are ours, whatever the model echoed back.
                foreach (var row in rows)
                {
                    SetTranslation(missing.Key, missing.Language, row.Template, row.Context, row.Traits);
                }
            }
            catch
            {
                // Best effort: a failed fill leaves the fallback rendering in
                // place and stays recorded so the endpoint is not hammered.
            }
        }));
    }

    private static void AddRow(string key, string language, string? context, string template, string? traits)
    {
        var row = new Row(ParseTags(context), template, ParseTags(traits));
        var rows = Table.GetOrAdd((key, language), static _ => []);
        lock (rows)
        {
            rows.Add(row);
        }
    }

    /// <summary>All criteria satisfied, most criteria wins, last registered wins ties.</summary>
    private static Row? BestRow(string key, string language, HashSet<string> facts)
    {
        if (!Table.TryGetValue((key, language), out var rows))
        {
            return null;
        }

        Row? best = null;
        lock (rows)
        {
            foreach (var row in rows)
            {
                if (row.Criteria.Length < (best?.Criteria.Length ?? -1))
                {
                    continue;
                }

                var satisfied = true;
                foreach (var criterion in row.Criteria)
                {
                    if (!facts.Contains(criterion))
                    {
                        satisfied = false;
                        break;
                    }
                }

                if (satisfied)
                {
                    best = row;
                }
            }
        }

        return best;
    }

    private static string[] ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        return tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// The translatable contexts of a value: the contexts of its type plus,
    /// for enum values, those of the specific member — they sum as facts.
    /// Null when neither the type nor the member is marked [Translatable].
    /// </summary>
    private static string[]? ContextsOf(object value)
    {
        var type = value.GetType();
        var typeContexts = ContextsOf(type);

        if (!type.IsEnum)
        {
            return typeContexts;
        }

        var memberContexts = TranslatableMemberContexts.GetOrAdd((type, value.ToString()!), static key =>
        {
            var field = key.Type.GetField(key.Member);
            if (field is null || !field.IsDefined(typeof(TranslatableAttribute), inherit: false))
            {
                return null;
            }

            return field
                .GetCustomAttributes(typeof(TranslatableAttribute), inherit: false)
                .Cast<TranslatableAttribute>()
                .Where(attribute => attribute.Context is not null)
                .Select(attribute => attribute.Context!.Trim())
                .ToArray();
        });

        return (typeContexts, memberContexts) switch
        {
            (null, null) => null,
            (null, { } member) => member,
            ({ } fromType, null) => fromType,
            ({ } fromType, { } member) => [.. fromType, .. member],
        };
    }

    private static string[]? ContextsOf(Type type) =>        TranslatableContexts.GetOrAdd(type, static t =>
        {
            var attributes = t.GetCustomAttributes(typeof(TranslatableAttribute), inherit: true);
            if (attributes.Length == 0)
            {
                return null;
            }

            return attributes
                .Cast<TranslatableAttribute>()
                .Where(attribute => attribute.Context is not null)
                .Select(attribute => attribute.Context!.Trim())
                .ToArray();
        });

    private static string FormatValue(TranslationArgument argument, CultureInfo culture) =>
        argument.Value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(argument.Format, culture),
            var value => value.ToString() ?? string.Empty,
        };

    private static string ApplyAlignment(string text, int alignment) =>
        alignment switch
        {
            > 0 => text.PadLeft(alignment),
            < 0 => text.PadRight(-alignment),
            _ => text,
        };

    private static CultureInfo ResolveCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
