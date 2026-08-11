namespace PhotoAtomic;

/// <summary>Coverage check outcome: which (key, language) pairs have no row at all.</summary>
public sealed record VerifyReport(int Present, IReadOnlyList<(string Key, string Language)> Missing)
{
    public bool IsComplete => Missing.Count == 0;
}

/// <summary>
/// The CI gate: verifies that every translation unit of a catalog has at least
/// one row in the store for every configured language. Purely local — no
/// translator, no network — so a build pipeline can fail fast when someone
/// adds a T(...) and forgets to run the fill.
/// </summary>
public static class CatalogVerifier
{
    public static VerifyReport Verify(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> languages,
        ITranslationStore store)
    {
        var existing = store.LoadAll()
            .Select(row => (row.Key, row.Language))
            .ToHashSet();

        var keys = entries
            .Select(entry => entry.Key)
            .Distinct()
            .ToList();

        var missing = new List<(string Key, string Language)>();
        var present = 0;

        foreach (var key in keys)
        {
            foreach (var language in languages)
            {
                if (existing.Contains((key, language)))
                {
                    present++;
                }
                else
                {
                    missing.Add((key, language));
                }
            }
        }

        return new VerifyReport(present, missing);
    }
}
