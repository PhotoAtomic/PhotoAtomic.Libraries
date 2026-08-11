using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Walks every syntax tree of a full Compilation — including trees produced
/// by other source generators, Razor above all — and extracts every T(...)
/// call site. This is what a workspace-based caller uses to see the Blazor
/// markup that the incremental generator, by pipeline rules, cannot.
/// </summary>
internal static class CatalogExtractor
{
    public static IReadOnlyList<ExtractedEntry> ExtractFrom(Compilation compilation)
    {
        var entries = new List<ExtractedEntry>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            SemanticModel? model = null;

            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TranslationCall.IsCandidate(invocation))
                {
                    continue;
                }

                model ??= compilation.GetSemanticModel(tree);
                entries.AddRange(SiteExtraction.FromInvocation(invocation, model));
            }
        }

        return entries
            .GroupBy(entry => (entry.Key, entry.Context, entry.IsValue))
            .Select(group => group.First())
            .ToList();
    }
}
