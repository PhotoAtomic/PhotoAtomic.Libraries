namespace PhotoAtomic;

/// <summary>
/// CLDR plural categories: a deterministic map from a number to a category
/// name, different per language. Categories become facts ("0:CLDR-few") that
/// translation rows can require as criteria. The CLDR- prefix makes the tags
/// self-describing for whoever reads them — human translators in the CSV and
/// AI translators in the prompt alike.
/// Rules cover the representative plural families; unlisted languages fall
/// back to the simplest rule (one/other). Non-integer values are Other.
/// </summary>
public static class PluralRules
{
    public const string Zero = "CLDR-zero";
    public const string One = "CLDR-one";
    public const string Two = "CLDR-two";
    public const string Few = "CLDR-few";
    public const string Many = "CLDR-many";
    public const string Other = "CLDR-other";

    /// <summary>
    /// The categories a language actually distinguishes — every value
    /// CategoryOf can produce for it. Prompts list them explicitly so the
    /// translator emits exactly one row per category, no more guessing.
    /// </summary>
    public static IReadOnlyList<string> CategoriesOf(string language) =>
        language.Split('-')[0].ToLowerInvariant() switch
        {
            "ar" or "cy" => [Zero, One, Two, Few, Many, Other],
            "gd" => [One, Two, Few, Other],
            "ru" or "uk" or "pl" => [One, Few, Many, Other],
            _ => [One, Other],
        };

    /// <summary>The CLDR category of a numeric value in a language, or null when the value is not numeric.</summary>
    public static string? CategoryOf(object? value, string language)
    {
        double? number = value switch
        {
            sbyte x => x,
            byte x => x,
            short x => x,
            ushort x => x,
            int x => x,
            uint x => x,
            long x => x,
            ulong x => x,
            float x => x,
            double x => x,
            decimal x => (double)x,
            _ => null,
        };

        if (number is not { } n)
        {
            return null;
        }

        if (double.IsNaN(n) || double.IsInfinity(n) || n != Math.Floor(n))
        {
            return Other;
        }

        var i = (long)Math.Abs(n);
        var family = language.Split('-')[0].ToLowerInvariant();

        return family switch
        {
            // Arabic: the only major language using all six categories.
            "ar" => i switch
            {
                0 => Zero,
                1 => One,
                2 => Two,
                _ when i % 100 is >= 3 and <= 10 => Few,
                _ when i % 100 is >= 11 and <= 99 => Many,
                _ => Other,
            },

            // Welsh: six categories too — famously, exactly 6 is Many.
            "cy" => i switch
            {
                0 => Zero,
                1 => One,
                2 => Two,
                3 => Few,
                6 => Many,
                _ => Other,
            },

            // Scottish Gaelic: vigesimal heritage — 11 counts as One, 12 as Two.
            "gd" => i switch
            {
                1 or 11 => One,
                2 or 12 => Two,
                (>= 3 and <= 10) or (>= 13 and <= 19) => Few,
                _ => Other,
            },

            // Russian and Ukrainian: modulo rules — 21 is One, 22 is Few.
            "ru" or "uk" => (i % 10, i % 100) switch
            {
                (1, not 11) => One,
                ( >= 2 and <= 4, not (>= 12 and <= 14)) => Few,
                _ => Many,
            },

            // Polish: like Russian, but only exactly 1 is One.
            "pl" => i switch
            {
                1 => One,
                _ => (i % 10, i % 100) switch
                {
                    ( >= 2 and <= 4, not (>= 12 and <= 14)) => Few,
                    _ => Many,
                },
            },

            // French: zero takes the singular ("0 pomme").
            "fr" => i is 0 or 1 ? One : Other,

            // The simplest and most common rule: en, it, de, es, and everyone else.
            _ => i == 1 ? One : Other,
        };
    }
}
