using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PhotoAtomic.SourceGen;

/// <summary>
/// Warns when the context passed to T(...) cannot be resolved at compile time:
/// that call site renders fine at runtime but cannot be pre-translated by the
/// catalog tool. Severity is configurable per repository via .editorconfig
/// (dotnet_diagnostic.PAI18N001.severity = error for the strict stance);
/// intentional dynamic contexts can suppress the warning locally.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnresolvableContextAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UnresolvableContext = new(
        id: "PAI18N001",
        title: "Translation context is not statically resolvable",
        messageFormat: "The context passed to T() cannot be resolved at compile time, so this call site will not be pre-translated",
        category: "Internationalization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UnresolvableContext];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!TranslationCall.IsCandidate(invocation)
            || !TranslationCall.IsTranslationInvocation(invocation, context.SemanticModel))
        {
            return;
        }

        var contextArgument = TranslationCall.ContextArgument(invocation);
        if (contextArgument is null)
        {
            return;
        }

        if (!ContextResolver.TryResolve(contextArgument, context.SemanticModel, out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnresolvableContext, contextArgument.GetLocation()));
        }
    }
}
