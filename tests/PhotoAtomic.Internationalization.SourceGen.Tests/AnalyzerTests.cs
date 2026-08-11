namespace PhotoAtomic.SourceGen.Tests;

public class AnalyzerTests
{
    [Fact]
    public async Task A_context_from_a_method_parameter_is_reported()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                public static string Render(string ctx) => T($"Open", ctx);
            }
            """;

        var ids = await CompilationHarness.AnalyzeAsync(source);

        Assert.Contains("PAI18N001", ids);
    }

    [Fact]
    public async Task Resolvable_contexts_and_missing_contexts_are_not_reported()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                private const string MenuContext = "menu";

                public static string A() => T($"Plain sentence");
                public static string B() => T($"Open", "verb");
                public static string C(bool isVerb) => T($"Open", isVerb ? "verb" : "state");
                public static string D() => T($"Inventory", MenuContext);
            }
            """;

        var ids = await CompilationHarness.AnalyzeAsync(source);

        Assert.DoesNotContain("PAI18N001", ids);
    }

    [Fact]
    public async Task A_reassigned_local_context_is_reported()
    {
        var source = """
            using static PhotoAtomic.Internationalization;

            public static class Game
            {
                public static string Render(bool late)
                {
                    var ctx = "menu";
                    if (late)
                    {
                        ctx = ComputeContext();
                    }

                    return T($"Inventory", ctx);
                }

                private static string ComputeContext() => "computed";
            }
            """;

        var ids = await CompilationHarness.AnalyzeAsync(source);

        Assert.Contains("PAI18N001", ids);
    }
}
