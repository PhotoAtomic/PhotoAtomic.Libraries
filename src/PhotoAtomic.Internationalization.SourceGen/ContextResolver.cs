using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Resolves the set of values a context expression can take at compile time:
/// literals and anything constant-foldable, ternaries and switch expressions
/// over resolvable branches, and locals with a single resolvable initializer
/// and no reassignment. The single shared brain of both the catalog generator
/// (which enumerates the resolved values) and the analyzer (which warns when
/// resolution fails) — they can never disagree by construction.
/// </summary>
internal static class ContextResolver
{
    public static bool TryResolve(ExpressionSyntax? expression, SemanticModel semanticModel, out ImmutableArray<string?> values)
    {
        var collected = new HashSet<string?>();
        if (!Collect(expression, semanticModel, collected, depth: 0))
        {
            values = ImmutableArray<string?>.Empty;
            return false;
        }

        values = collected.ToImmutableArray();
        return true;
    }

    private static bool Collect(ExpressionSyntax? expression, SemanticModel semanticModel, HashSet<string?> values, int depth)
    {
        if (depth > 8)
        {
            return false; // defensive cap against pathological chains
        }

        if (expression is null)
        {
            values.Add(null);
            return true;
        }

        expression = Unwrap(expression);

        // Level 1: literals, const fields and locals, folded concatenations —
        // the compiler's own constant evaluator covers them all.
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.HasValue)
        {
            if (constant.Value is string text)
            {
                values.Add(text);
                return true;
            }

            if (constant.Value is null)
            {
                values.Add(null);
                return true;
            }

            return false;
        }

        // Level 2: finite unions and single-assignment locals.
        switch (expression)
        {
            case ConditionalExpressionSyntax conditional:
                return Collect(conditional.WhenTrue, semanticModel, values, depth + 1)
                    && Collect(conditional.WhenFalse, semanticModel, values, depth + 1);

            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    if (!Collect(arm.Expression, semanticModel, values, depth + 1))
                    {
                        return false;
                    }
                }

                return true;

            case IdentifierNameSyntax identifier
                when semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local:
                return CollectLocal(local, semanticModel, values, depth);

            default:
                return false;
        }
    }

    private static bool CollectLocal(ILocalSymbol local, SemanticModel semanticModel, HashSet<string?> values, int depth)
    {
        if (local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator
            || declarator.Initializer is null)
        {
            return false;
        }

        // A reassignment anywhere in the enclosing member disqualifies the
        // local: its value at the call site is no longer the initializer.
        var enclosingMember = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (enclosingMember is null)
        {
            return false;
        }

        foreach (var assignment in enclosingMember.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax target
                && SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(target).Symbol, local))
            {
                return false;
            }
        }

        return Collect(declarator.Initializer.Value, semanticModel, values, depth + 1);
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized
            ? Unwrap(parenthesized.Expression)
            : expression;
}
