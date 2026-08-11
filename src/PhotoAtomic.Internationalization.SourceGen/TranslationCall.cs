using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Shared recognition of T(...) call sites: both the generator and the
/// analyzer must agree on what a translation call is.
/// </summary>
internal static class TranslationCall
{
    /// <summary>Cheap syntactic pre-filter: an invocation whose method name is T.</summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "T",
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == "T",
            _ => false,
        };

    /// <summary>Semantic confirmation: the T of PhotoAtomic.Internationalization.</summary>
    public static bool IsTranslationInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel) =>
        semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && method.Name == "T"
        && method.ContainingType?.Name == "Internationalization"
        && method.ContainingType.ContainingNamespace?.ToDisplayString() == "PhotoAtomic";

    /// <summary>The interpolated string argument, when written inline as one.</summary>
    public static InterpolatedStringExpressionSyntax? TextArgument(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Count > 0
            ? invocation.ArgumentList.Arguments[0].Expression as InterpolatedStringExpressionSyntax
            : null;

    /// <summary>The context argument expression, or null when omitted.</summary>
    public static ExpressionSyntax? ContextArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == "context")
            {
                return argument.Expression;
            }
        }

        return invocation.ArgumentList.Arguments.Count > 1
            ? invocation.ArgumentList.Arguments[1].Expression
            : null;
    }
}
