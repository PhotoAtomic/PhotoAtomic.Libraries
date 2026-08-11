namespace PhotoAtomic;

/// <summary>Whether a catalog entry is a sentence template or a single translatable value.</summary>
public enum CatalogEntryKind
{
    Sentence,
    Value,
}

/// <summary>
/// One pre-translatable item discovered in source code by the catalog
/// generator. For sentences: the structural key, the (statically resolved)
/// context of the call site, the legend naming each hole, and the facts known
/// at compile time (hole-prefixed type contexts like "0:tool", "0:CLDR-other"
/// for numeric holes). For values (enum members of [Translatable] types): the
/// member text as key and its contexts — type-level plus member-level — as
/// facts. The pre-translation tool feeds these to the translator for every
/// configured target language, so shipped builds need no network for static
/// content.
/// </summary>
public sealed record CatalogEntry(
    string Key,
    string? Context,
    IReadOnlyList<string> Legend,
    IReadOnlyList<string> Facts,
    CatalogEntryKind Kind = CatalogEntryKind.Sentence);
