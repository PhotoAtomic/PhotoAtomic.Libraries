namespace PhotoAtomic;

/// <summary>One grammatical case to ask for: its criteria, and a word to picture in each hole.</summary>
public sealed record VariantCase(string Criteria, IReadOnlyDictionary<int, string> Examples);

/// <summary>
/// Computes the grammatical cases a sentence needs — the rows that must exist
/// — from the facts of the call and from what the language's already
/// translated VALUES look like. The program owns this, not the model:
/// enumerating combinations is bookkeeping, and a model asked to do it either
/// forgets cases or invents hundreds.
///
/// The states of a hole are NOT a hardcoded list of genders: they are the
/// trait combinations the values of that language actually declared. So a
/// language whose words carry traits we never anticipated — vowel harmony,
/// impure S, a noun class system — still gets one case per real situation,
/// with a real example word attached for the model to write against.
/// </summary>
public static class VariantCases
{
    /// <summary>
    /// Ceiling on the cases asked for one sentence. Kept modest on purpose:
    /// asking a model for dozens of near-identical variants measurably
    /// degrades it (it starts mixing up which hole is which).
    /// </summary>
    public const int MaxCases = 16;

    /// <summary>The genders assumed when nothing has been translated yet — the seed of an empty vocabulary.</summary>
    private static readonly (string[] Traits, string Example)[] DefaultStates =
    [
        ([WellKnownTraits.GenderMale], string.Empty),
        ([WellKnownTraits.GenderFemale], string.Empty),
    ];

    public static IReadOnlyList<VariantCase> For(TranslationRequest request, ValueVocabulary? vocabulary = null)
    {
        var axes = Axes(request, vocabulary);
        if (axes.Count == 0)
        {
            return [];
        }

        // Shrink until it fits. Giving up is not an option: without cases the
        // sentence would be translated freely, and a free answer is the one
        // that comes back with no articles at all.
        while (Size(axes) > MaxCases && Simplify(axes))
        {
        }

        return Product(axes);
    }

    private sealed record Axis(int Hole, List<(string[] Traits, string Example)> States);

    private static List<Axis> Axes(TranslationRequest request, ValueVocabulary? vocabulary)
    {
        var axes = new List<Axis>();

        foreach (var group in request.Facts
            .Select(fact => fact.Split(':', 2))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _))
            .GroupBy(parts => int.Parse(parts[0]))
            .OrderBy(group => group.Key))
        {
            var contexts = group.Select(parts => parts[1]).ToList();

            if (contexts.Any(context => context.StartsWith("CLDR-", StringComparison.Ordinal)))
            {
                // A real number of that category is the example: "3" teaches
                // the plural better than the words "CLDR-other" ever could.
                axes.Add(new Axis(group.Key, PluralRules.CategoriesOf(request.TargetLanguage)
                    .Select(category => (new[] { category },
                        NumberOfCategory(category, request.TargetLanguage)))
                    .ToList()));
            }
            else if (contexts.Any(IsSemantic))
            {
                var states = vocabulary?.StatesOf(request.TargetLanguage) ?? [];
                axes.Add(new Axis(group.Key, (states.Count > 0 ? states : DefaultStates).ToList()));
            }
        }

        return axes;
    }

    /// <summary>
    /// Narrows the widest axis: first to its plainest states (the ones with
    /// fewest traits), then, if that was not enough, to the two leading ones.
    /// False only when every axis is already down to two states.
    /// </summary>
    private static bool Simplify(List<Axis> axes)
    {
        var widest = axes.OrderByDescending(axis => axis.States.Count).First();
        if (widest.States.Count <= 2)
        {
            return false;
        }

        var plainest = widest.States.Min(state => state.Traits.Length);
        var kept = widest.States.Where(state => state.Traits.Length == plainest).ToList();
        if (kept.Count == widest.States.Count)
        {
            kept = kept.Take(2).ToList();
        }

        widest.States.Clear();
        widest.States.AddRange(kept);
        return true;
    }

    private static int Size(List<Axis> axes) =>
        axes.Aggregate(1, (total, axis) => total * Math.Max(1, axis.States.Count));

    private static List<VariantCase> Product(List<Axis> axes)
    {
        var cases = new List<VariantCase> { new(string.Empty, new Dictionary<int, string>()) };

        foreach (var axis in axes)
        {
            cases = cases
                .SelectMany(prefix => axis.States.Select(state =>
                {
                    var criteria = string.Join(',', state.Traits.Select(trait => $"{axis.Hole}:{trait}"));
                    var examples = new Dictionary<int, string>(prefix.Examples);
                    if (state.Example.Length > 0)
                    {
                        examples[axis.Hole] = state.Example;
                    }

                    return new VariantCase(
                        prefix.Criteria.Length == 0 ? criteria : $"{prefix.Criteria},{criteria}",
                        examples);
                }))
                .ToList();
        }

        return cases;
    }

    /// <summary>Criteria compared as a SET of tags: the model may reorder them, the meaning is the same.</summary>
    public static string Normalize(string? criteria) =>
        string.Join(',', (criteria ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(tag => tag, StringComparer.Ordinal));

    /// <summary>A number that really belongs to the category in that language ("1" for one, "3" for other).</summary>
    private static string NumberOfCategory(string category, string language)
    {
        foreach (var candidate in new[] { 1, 2, 0, 3, 5, 11, 21, 101, 1000 })
        {
            if (PluralRules.CategoryOf(candidate, language) == category)
            {
                return candidate.ToString();
            }
        }

        return string.Empty;
    }

    private static bool IsSemantic(string context) =>
        !context.StartsWith("CLDR-", StringComparison.Ordinal)
        && !context.StartsWith("GENDER-", StringComparison.Ordinal)
        && context != WellKnownTraits.StartsWithVowel
        && context != WellKnownTraits.Capitalize;
}
