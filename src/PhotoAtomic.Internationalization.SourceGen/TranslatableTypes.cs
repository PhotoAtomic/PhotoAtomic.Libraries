using Microsoft.CodeAnalysis;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Static-side knowledge about [Translatable] types: their contexts and, for
/// enums, the full universe of members with their additive member contexts —
/// what makes the catalog complete enough to pre-translate every value that
/// can ever flow through a hole.
/// </summary>
internal static class TranslatableTypes
{
    /// <summary>The contexts declared by [Translatable] attributes, or null when the symbol is not marked.</summary>
    public static string[]? ContextsOf(ISymbol symbol)
    {
        string[]? contexts = null;

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "TranslatableAttribute"
                || attribute.AttributeClass.ContainingNamespace?.ToDisplayString() != "PhotoAtomic")
            {
                continue;
            }

            contexts ??= [];
            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string context
                && context.Length > 0)
            {
                contexts = [.. contexts, context.Trim()];
            }
        }

        return contexts;
    }

    /// <summary>True for the numeric special types PluralRules recognizes at runtime.</summary>
    public static bool IsNumeric(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or
        SpecialType.System_Byte or
        SpecialType.System_Int16 or
        SpecialType.System_UInt16 or
        SpecialType.System_Int32 or
        SpecialType.System_UInt32 or
        SpecialType.System_Int64 or
        SpecialType.System_UInt64 or
        SpecialType.System_Single or
        SpecialType.System_Double or
        SpecialType.System_Decimal;

    /// <summary>Every member of a [Translatable] enum with its additive contexts (type-level plus member-level).</summary>
    public static IEnumerable<(string Member, string[] Contexts)> EnumMembers(ITypeSymbol type, string[] typeContexts)
    {
        if (type.TypeKind != TypeKind.Enum)
        {
            yield break;
        }

        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.HasConstantValue)
            {
                continue;
            }

            var memberContexts = ContextsOf(field);
            var combined = memberContexts is null ? typeContexts : [.. typeContexts, .. memberContexts];

            yield return (field.Name, combined);
        }
    }
}
