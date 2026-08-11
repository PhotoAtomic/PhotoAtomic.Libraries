using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Derives the canonical key and the legend from an interpolated string in the
/// syntax tree, replicating exactly what TranslationInterpolatedStringHandler
/// does at runtime (literal braces re-escaped, holes as {n}, legend from the
/// source text of each hole expression). Contract tests pin the two
/// implementations together: if they ever diverge, a test goes red.
/// </summary>
internal static class StructuralKey
{
    public static (string Key, string[] Legend) Derive(InterpolatedStringExpressionSyntax interpolated)
    {
        var key = new StringBuilder();
        var legend = new List<string>();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    // Asymmetry with the runtime handler: AppendLiteral receives
                    // the UNescaped literal and re-escapes braces, while the
                    // syntax token ValueText keeps doubled braces exactly as
                    // written - already the composite-format form the key needs.
                    key.Append(text.TextToken.ValueText);
                    break;

                case InterpolationSyntax interpolation:
                    key.Append('{').Append(legend.Count).Append('}');
                    legend.Add(interpolation.Expression.ToString());
                    break;
            }
        }

        return (key.ToString(), legend.ToArray());
    }
}
