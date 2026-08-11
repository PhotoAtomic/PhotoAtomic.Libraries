using PhotoAtomic;
using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

[Translatable("gadget")]
public enum Gadget
{
    Wrench,
}

// UseTranslator mutates process-wide state: every handler answers only its own
// keys and detaches in finally, to stay friendly with parallel test classes.
public class BackgroundFillTests
{
    private sealed class FakeTranslator : ITranslator
    {
        private readonly List<TranslationRequest> requests = [];

        public Func<TranslationRequest, IReadOnlyList<TranslationRow>> Handler { get; init; } = request => [];

        public IReadOnlyList<TranslationRequest> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToArray();
                }
            }
        }

        public Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            return Task.FromResult(Handler(request));
        }
    }

    [Fact]
    public async Task A_miss_queues_a_background_fill_and_the_next_render_uses_it()
    {
        var translator = new FakeTranslator
        {
            Handler = request => request.Key == "The lantern glows"
                ? [new TranslationRow(request.Key, null, request.TargetLanguage, "La lanterna brilla")]
                : [],
        };

        UseTranslator(translator);
        try
        {
            Language = "it-IT";

            Assert.Equal("The lantern glows", T($"The lantern glows"));

            await WhenIdleAsync();

            Assert.Equal("La lanterna brilla", T($"The lantern glows"));
            Assert.Contains(translator.Requests, r => r.Key == "The lantern glows" && r.TargetLanguage == "it-IT");
        }
        finally
        {
            UseTranslator(null);
        }
    }

    [Fact]
    public async Task The_request_carries_legend_and_facts_of_the_missed_call()
    {
        var translator = new FakeTranslator();

        UseTranslator(translator);
        try
        {
            Language = "it-IT";
            var lanterns = 2;

            T($"You crave {lanterns} lanterns");

            await WhenIdleAsync();

            var request = Assert.Single(translator.Requests, r => r.Key == "You crave {0} lanterns");
            Assert.Equal(["lanterns"], request.Legend);
            Assert.Contains("0:CLDR-other", request.Facts);
        }
        finally
        {
            UseTranslator(null);
        }
    }

    [Fact]
    public async Task A_translatable_value_miss_queues_its_own_fill_with_its_contexts()
    {
        var translator = new FakeTranslator();

        UseTranslator(translator);
        try
        {
            Language = "it-IT";
            var tool = Gadget.Wrench;

            T($"Grab the {tool}");

            await WhenIdleAsync();

            var request = Assert.Single(translator.Requests, r => r.Key == "Wrench");
            Assert.Contains("gadget", request.Facts);
        }
        finally
        {
            UseTranslator(null);
        }
    }

    [Fact]
    public async Task No_fill_is_queued_for_the_source_language()
    {
        var translator = new FakeTranslator();

        UseTranslator(translator);
        try
        {
            Language = SourceLanguage;

            T($"A sentence that stays English");

            await WhenIdleAsync();

            Assert.DoesNotContain(translator.Requests, r => r.Key == "A sentence that stays English");
        }
        finally
        {
            UseTranslator(null);
        }
    }

    [Fact]
    public async Task Each_missing_key_is_translated_only_once()
    {
        var translator = new FakeTranslator();

        UseTranslator(translator);
        try
        {
            Language = "it-IT";

            T($"A rare untranslated gem");
            T($"A rare untranslated gem");
            T($"A rare untranslated gem");

            await WhenIdleAsync();

            Assert.Single(translator.Requests, r => r.Key == "A rare untranslated gem");
        }
        finally
        {
            UseTranslator(null);
        }
    }
}
