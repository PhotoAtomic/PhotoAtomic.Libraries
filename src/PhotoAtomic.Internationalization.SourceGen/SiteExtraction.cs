using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PhotoAtomic.SourceGen;

/// <summary>One extracted translation unit, before any joining or emission.</summary>
internal sealed record ExtractedEntry(string Key, string? Context, string[] Legend, string[] Facts, bool IsValue);

/// <summary>
/// The single shared brain that turns one T(...) call site into catalog
/// entries: structural key, legend, resolved contexts, hole facts and the
/// value universe of [Translatable] enums. Consumed by the incremental
/// generator at build time and by the workspace-based extractor of the tool
/// (which is how Razor-generated trees get covered).
/// </summary>
internal static class SiteExtraction
{
    // Mirrors PluralRules.Other; the contract test pins the two constants.
    internal const string PluralOtherFact = "CLDR-other";

    public static ImmutableArray<ExtractedEntry> FromInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (!TranslationCall.IsTranslationInvocation(invocation, semanticModel)
            || TranslationCall.TextArgument(invocation) is not { } interpolated)
        {
            return ImmutableArray<ExtractedEntry>.Empty;
        }

        if (!ContextResolver.TryResolve(TranslationCall.ContextArgument(invocation), semanticModel, out var contexts))
        {
            return ImmutableArray<ExtractedEntry>.Empty; // the analyzer reports this call site
        }

        var (key, legend) = StructuralKey.Derive(interpolated);

        var sentenceFacts = new List<string>();
        var found = ImmutableArray.CreateBuilder<ExtractedEntry>();

        var holeIndex = 0;
        foreach (var interpolation in interpolated.Contents.OfType<InterpolationSyntax>())
        {
            AnalyzeHole(holeIndex, interpolation, semanticModel, sentenceFacts, found);
            holeIndex++;
        }

        foreach (var contextValue in contexts)
        {
            found.Add(new ExtractedEntry(key, contextValue, legend, sentenceFacts.ToArray(), IsValue: false));
        }

        return found.ToImmutable();
    }

    private static void AnalyzeHole(
        int index,
        InterpolationSyntax interpolation,
        SemanticModel semanticModel,
        List<string> sentenceFacts,
        ImmutableArray<ExtractedEntry>.Builder found)
    {
        if (semanticModel.GetTypeInfo(interpolation.Expression).Type is not { } type)
        {
            return;
        }

        if (TranslatableTypes.IsNumeric(type))
        {
            // The representative category: the tool and the prompt expand it
            // to every category of each target language.
            sentenceFacts.Add($"{index}:{PluralOtherFact}");
            return;
        }

        if (TranslatableTypes.ContextsOf(type) is not { } typeContexts)
        {
            return;
        }

        foreach (var typeContext in typeContexts)
        {
            sentenceFacts.Add($"{index}:{typeContext}");
        }

        // The whole universe of values this hole can carry, each with its
        // additive contexts, becomes pre-translatable.
        foreach (var (member, memberContexts) in TranslatableTypes.EnumMembers(type, typeContexts))
        {
            found.Add(new ExtractedEntry(member, Context: null, Legend: [], Facts: memberContexts, IsValue: true));
        }
    }
}
