namespace PhotoAtomic;

/// <summary>
/// Marks a type whose rendered text is itself a translatable value: when such
/// a value fills a hole, its text is looked up in the translation table as a
/// key of its own ("Red" becomes "Rosso") before entering the sentence.
/// Sentences translate by structure; marked values translate by content.
///
/// The optional context contributes facts to the matching: it qualifies both
/// the value's own lookup and, prefixed with the hole position, the sentence
/// lookup. Multiple attributes stack their contexts as separate facts. On enum
/// members the attribute adds member-specific contexts to the type-level ones
/// (and a marked member makes its value translatable even if the type is not).
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Field,
    AllowMultiple = true)]
public sealed class TranslatableAttribute(string? context = null) : Attribute
{
    public string? Context { get; } = context;
}
