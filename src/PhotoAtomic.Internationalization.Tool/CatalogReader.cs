using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

namespace PhotoAtomic;

/// <summary>
/// Loads a compiled assembly and reads its generated
/// PhotoAtomic.Generated.TranslationCatalog back as CatalogEntry instances.
/// Entries are copied via reflection rather than cast: the target assembly
/// carries its own copy of the i18n library, and type identity across load
/// contexts is a trap not worth stepping into.
/// </summary>
public static class CatalogReader
{
    public static IReadOnlyList<CatalogEntry> Read(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var directory = Path.GetDirectoryName(fullPath)!;

        var loadContext = new AssemblyLoadContext($"catalog:{fullPath}");
        loadContext.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        var assembly = loadContext.LoadFromAssemblyPath(fullPath);
        var catalog = assembly.GetType("PhotoAtomic.Generated.TranslationCatalog")
            ?? throw new InvalidOperationException(
                $"{Path.GetFileName(fullPath)} has no PhotoAtomic.Generated.TranslationCatalog: "
                + "is the PhotoAtomic.Internationalization.SourceGen analyzer attached to that project?");

        var rawEntries = (IEnumerable)catalog.GetField("Entries", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        var entries = new List<CatalogEntry>();
        foreach (var raw in rawEntries)
        {
            var type = raw.GetType();
            entries.Add(new CatalogEntry(
                Key: (string)type.GetProperty("Key")!.GetValue(raw)!,
                Context: (string?)type.GetProperty("Context")!.GetValue(raw),
                Legend: ((IEnumerable<string>)type.GetProperty("Legend")!.GetValue(raw)!).ToArray(),
                Facts: ((IEnumerable<string>)type.GetProperty("Facts")!.GetValue(raw)!).ToArray(),
                Kind: type.GetProperty("Kind")!.GetValue(raw)!.ToString() == "Value"
                    ? CatalogEntryKind.Value
                    : CatalogEntryKind.Sentence));
        }

        return entries;
    }
}
