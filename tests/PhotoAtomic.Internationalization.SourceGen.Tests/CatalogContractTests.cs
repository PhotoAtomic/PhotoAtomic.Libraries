using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.SourceGen.Tests;

/// <summary>
/// The contract: the key the generator derives from syntax must be identical
/// to the key the runtime handler derives from the same interpolated string.
/// Every case states the expectation once through runtime KeyOf and once
/// through the generated catalog.
/// </summary>
public class CatalogContractTests
{
    private static string Sample(string call) =>
        $$"""
        using static PhotoAtomic.Internationalization;

        public static class Game
        {
            public static string Render(int count, double price, string action)
            {
                return {{call}};
            }
        }
        """;

    [Fact]
    public void A_plain_sentence_matches_the_runtime_key()
    {
        var count = 3;
        var runtimeKey = KeyOf($"You found {count} coins");

        var entry = Assert.Single(CompilationHarness.CatalogOf(Sample("""T($"You found {count} coins")""")));

        Assert.Equal(runtimeKey, entry.Key);
        Assert.Equal(["count"], entry.Legend);
        Assert.Null(entry.Context);
    }

    [Fact]
    public void Literal_braces_match_the_runtime_key()
    {
        var action = "jump";
        var runtimeKey = KeyOf($"press {{X}} to {action}");

        var entry = Assert.Single(CompilationHarness.CatalogOf(Sample("""T($"press {{X}} to {action}")""")));

        Assert.Equal(runtimeKey, entry.Key);
    }

    [Fact]
    public void Format_specifiers_and_alignment_match_the_runtime_key()
    {
        var price = 9.5;
        var runtimeKey = KeyOf($"Cost: {price,10:F2} gold");

        var entry = Assert.Single(CompilationHarness.CatalogOf(Sample("""T($"Cost: {price,10:F2} gold")""")));

        Assert.Equal(runtimeKey, entry.Key);
        Assert.Equal(["price"], entry.Legend);
    }

    [Fact]
    public void Expression_holes_match_runtime_legend_capture()
    {
        var count = 2;
        var runtimeKey = KeyOf($"You see {count + 1} shadows");
        var runtimeLegend = LegendOf($"You see {count + 1} shadows");

        var entry = Assert.Single(CompilationHarness.CatalogOf(Sample("""T($"You see {count + 1} shadows")""")));

        Assert.Equal(runtimeKey, entry.Key);
        Assert.Equal(runtimeLegend, entry.Legend);
    }

    [Fact]
    public void A_literal_context_is_captured()
    {
        var entry = Assert.Single(CompilationHarness.CatalogOf(Sample("""T($"Open", "verb")""")));

        Assert.Equal("Open", entry.Key);
        Assert.Equal("verb", entry.Context);
    }

    [Fact]
    public void A_ternary_context_produces_one_entry_per_branch()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                public static string Render(bool isVerb)
                {
                    return T($"Open", isVerb ? "verb" : "state");
                }
            }
            """;

        var entries = CompilationHarness.CatalogOf(source);

        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry.Context == "verb");
        Assert.Contains(entries, entry => entry.Context == "state");
    }

    [Fact]
    public void A_const_and_a_single_assignment_local_are_resolved()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                private const string MenuContext = "menu";

                public static string Render()
                {
                    var ctx = MenuContext;
                    return T($"Inventory", ctx);
                }
            }
            """;

        var entry = Assert.Single(CompilationHarness.CatalogOf(source));

        Assert.Equal("menu", entry.Context);
    }

    [Fact]
    public void Duplicate_call_sites_collapse_into_one_entry()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                public static string A(int count) => T($"You found {count} coins");
                public static string B(int count) => T($"You found {count} coins");
            }
            """;

        Assert.Single(CompilationHarness.CatalogOf(source));
    }
}
