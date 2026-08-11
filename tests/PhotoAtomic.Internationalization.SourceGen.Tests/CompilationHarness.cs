using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PhotoAtomic.SourceGen.Tests;

/// <summary>
/// Compiles sample source with a reference to the real i18n library, runs the
/// catalog generator on it, and can load the emitted assembly to read the
/// generated TranslationCatalog back as live objects.
/// </summary>
internal static class CompilationHarness
{
    public static CSharpCompilation Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(PhotoAtomic.Internationalization).Assembly.Location));

        return CSharpCompilation.Create(
            "CatalogSample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>Compiles, runs the generator, loads the result and returns the live catalog entries.</summary>
    public static CatalogEntry[] CatalogOf(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new TranslationCatalogGenerator());
        driver.RunGeneratorsAndUpdateCompilation(Compile(source), out var updated, out _);

        using var stream = new MemoryStream();
        var emitted = updated.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = Assembly.Load(stream.ToArray());
        var catalog = assembly.GetType("PhotoAtomic.Generated.TranslationCatalog");
        Assert.NotNull(catalog);

        return (CatalogEntry[])catalog.GetField("Entries")!.GetValue(null)!;
    }

    /// <summary>Runs the analyzer and returns the reported diagnostic ids.</summary>
    public static async Task<string[]> AnalyzeAsync(string source)
    {
        var compilation = Compile(source);
        var withAnalyzers = compilation.WithAnalyzers([new UnresolvableContextAnalyzer()]);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Select(diagnostic => diagnostic.Id).ToArray();
    }
}
