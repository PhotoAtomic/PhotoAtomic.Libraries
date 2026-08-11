using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using PhotoAtomic.SourceGen;

namespace PhotoAtomic;

/// <summary>
/// Reads the catalog straight from a project instead of a compiled assembly:
/// MSBuildWorkspace opens the csproj and produces a Compilation in which every
/// source generator has run — the Razor generator included — so T(...) calls
/// written in .razor MARKUP are visible here, which the incremental generator
/// can never see (generators do not observe each other's output).
/// </summary>
public static class ProjectCatalogReader
{
    public static IReadOnlyList<CatalogEntry> Read(string projectPath, Action<string>? log = null)
    {
        RegisterMsBuild();
        return ReadCore(projectPath, log);
    }

    // MSBuildLocator must run before any Microsoft.Build type is loaded, so
    // the workspace code lives in a separate, never-inlined method.
    private static void RegisterMsBuild()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<CatalogEntry> ReadCore(string projectPath, Action<string>? log)
    {
        using var workspace = MSBuildWorkspace.Create();
        using var failureHandler = workspace.RegisterWorkspaceFailedHandler(failure =>
        {
            if (failure.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                log?.Invoke($"  workspace: {failure.Diagnostic.Message}");
            }
        });

        var project = workspace.OpenProjectAsync(Path.GetFullPath(projectPath)).GetAwaiter().GetResult();
        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"No compilation for {projectPath}");

        return CatalogExtractor.ExtractFrom(compilation)
            .Select(entry => new CatalogEntry(
                entry.Key,
                entry.Context,
                entry.Legend,
                entry.Facts,
                entry.IsValue ? CatalogEntryKind.Value : CatalogEntryKind.Sentence))
            .ToList();
    }
}
