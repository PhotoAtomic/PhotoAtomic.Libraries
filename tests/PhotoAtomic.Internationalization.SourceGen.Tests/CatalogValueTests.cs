using PhotoAtomic;

namespace PhotoAtomic.SourceGen.Tests;

public class CatalogValueTests
{
    private const string Sample = """
        using PhotoAtomic;
        using static PhotoAtomic.Internationalization;

        [Translatable("tool")]
        public enum Item
        {
            Hammer,
            [Translatable("music")]
            Organ,
        }

        public enum Plain
        {
            Alpha,
        }

        public static class Game
        {
            public static string Broken(Item item) => T($"The {item} is broken");
            public static string AlsoBroken(Item item) => T($"Still the {item}");
            public static string Count(int count) => T($"You found {count} coins");
            public static string Boring(Plain value) => T($"Nothing to see in {value}");
        }
        """;

    [Fact]
    public void A_translatable_hole_contributes_its_type_contexts_to_the_sentence_facts()
    {
        var entries = CompilationHarness.CatalogOf(Sample);

        var sentence = Assert.Single(entries, entry => entry.Key == "The {0} is broken");
        Assert.Equal(CatalogEntryKind.Sentence, sentence.Kind);
        Assert.Contains("0:tool", sentence.Facts);
    }

    [Fact]
    public void A_numeric_hole_contributes_the_representative_plural_fact()
    {
        var entries = CompilationHarness.CatalogOf(Sample);

        var sentence = Assert.Single(entries, entry => entry.Key == "You found {0} coins");

        // The contract with the runtime vocabulary: the generator hardcodes
        // the same constant PluralRules exposes.
        Assert.Contains($"0:{PluralRules.Other}", sentence.Facts);
    }

    [Fact]
    public void Every_member_of_a_translatable_enum_becomes_a_value_entry()
    {
        var entries = CompilationHarness.CatalogOf(Sample);

        var hammer = Assert.Single(entries, entry => entry.Key == "Hammer");
        Assert.Equal(CatalogEntryKind.Value, hammer.Kind);
        Assert.Equal(["tool"], hammer.Facts);

        var organ = Assert.Single(entries, entry => entry.Key == "Organ");
        Assert.Equal(CatalogEntryKind.Value, organ.Kind);
        Assert.Equal(["tool", "music"], organ.Facts);
    }

    [Fact]
    public void Value_entries_are_deduplicated_across_call_sites()
    {
        var entries = CompilationHarness.CatalogOf(Sample);

        // Item flows through two different sentences but its members appear once.
        Assert.Single(entries, entry => entry.Key == "Hammer");
    }

    [Fact]
    public void Unmarked_types_contribute_nothing()
    {
        var entries = CompilationHarness.CatalogOf(Sample);

        var sentence = Assert.Single(entries, entry => entry.Key == "Nothing to see in {0}");
        Assert.Empty(sentence.Facts);
        Assert.DoesNotContain(entries, entry => entry.Key == "Alpha");
    }
}

public class CatalogDeduplicationTests
{
    [Fact]
    public void The_same_key_reached_with_different_legends_is_one_translation_unit()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                public static string A(int coins) => T($"You found {coins} golden coins");
                public static string B(int one) => T($"You found {one} golden coins");
            }
            """;

        var entries = CompilationHarness.CatalogOf(source);

        Assert.Single(entries, entry => entry.Key == "You found {0} golden coins");
    }
}

public class CatalogExtractorTests
{
    [Fact]
    public void Extracts_from_every_tree_of_a_compilation_and_deduplicates()
    {
        var treeA = """
            using static PhotoAtomic.Internationalization;

            public static class PageA
            {
                public static string Render(int count) => T($"You found {count} coins");
            }
            """;

        // Mimics the shape of Razor-generated code: the T() call sits inside
        // a builder call in a compiler-style method — still plain C# syntax.
        var treeB = """
            using static PhotoAtomic.Internationalization;

            public static class PageB
            {
                public static void BuildRenderTree(System.Action<int, string> addContent)
                {
                    addContent(0, T($"Welcome to the party, adventurer!"));
                    addContent(1, T($"You found {3} coins"));
                }
            }
            """;

        var compilation = CompilationHarness.Compile(treeA)
            .AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(treeB));

        var entries = CatalogExtractor.ExtractFrom(compilation);

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, entry => entry.Key == "You found {0} coins");
        Assert.Single(entries, entry => entry.Key == "Welcome to the party, adventurer!");
    }
}
