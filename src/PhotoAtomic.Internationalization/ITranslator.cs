namespace PhotoAtomic;

/// <summary>
/// Everything a translator — human tooling or AI — needs to translate one key:
/// the source template, the languages, the legend naming each hole, and the
/// facts of the call that missed (context tags, CLDR categories, traits).
/// </summary>
public sealed record TranslationRequest(
    string Key,
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<string> Legend,
    IReadOnlyList<string> Facts)
{
    /// <summary>
    /// What was wrong with the last answer to this same request, in the words
    /// of whoever rejected it ("the sentence agrees with hole {1} in some cases
    /// but not in others").
    ///
    /// Asking again is otherwise pointless: the models we use answer at
    /// temperature 0, so the same prompt gives back the same defect forever.
    /// The complaint is the only thing that changes the question.
    /// </summary>
    public string? Feedback { get; init; }

    /// <summary>
    /// Terms already settled in the SAME body of content, source term next to
    /// the translation that was accepted for it.
    ///
    /// Values are translated one at a time, each its own question, and that is
    /// how the same thing ends up with two names: a room came back with a
    /// "pressa pesante in pietra" standing next to the "base di torchio in
    /// pietra" that belongs to it, and in French a "presse a fruits" beside a
    /// "pressoir" (found by the user PLAYING, 2026-08-17). Nothing was wrong
    /// with either answer on its own — the question simply never mentioned the
    /// other one.
    ///
    /// The same bargain the sentence variants already make: the first accepted
    /// wording leads the rest, so the choice a language forces gets made ONCE
    /// and then held to. Wrong preposition, wrong synonym and wrong register
    /// are one defect wearing three hats.
    /// </summary>
    public IReadOnlyList<GlossaryTerm> Glossary { get; init; } = [];

    /// <summary>
    /// Where this term lives, in one line of prose: the scene around it, the
    /// company it keeps. A name is ambiguous alone and obvious in place — a
    /// "press" among mortars, pestles and herbs in an alchemist's cellar is
    /// not a printing press, and its "base" is the base OF something rather
    /// than something made of it.
    ///
    /// Deliberately not the SENTENCES the term appears in, which was the first
    /// idea: for a value those are the narrator's generic lines, identical for
    /// every object, and handing the model a sentence to look at invites it to
    /// copy the sentence's own prepositions into the value — the defect this
    /// codebase already knows as example-left-in.
    /// </summary>
    public string? Setting { get; init; }

    /// <summary>
    /// Whether this key is a SENTENCE or a VALUE, when the caller knows.
    ///
    /// Left to itself the model guesses from the shape, and it guesses by
    /// length: "Steam" is obviously a name, "The taste of that stew" reads like
    /// a sentence, so it came back with no gender declared — three times, and
    /// the repair gave up. A value with no gender makes every sentence naming
    /// it fall back to English, so the whole galley spoke Italian except the
    /// thing you had just won.
    ///
    /// Whoever built the catalog KNOWS which it is. Saying so costs one line
    /// and removes a guess.
    /// </summary>
    public CatalogEntryKind? Kind { get; init; }
}

/// <param name="Source">The term as written in the source language.</param>
/// <param name="Translation">What it was already translated to, and must keep being called.</param>
public sealed record GlossaryTerm(string Source, string Translation);

/// <summary>
/// Produces translation rows for a missing key. Implementations may return
/// several rows (e.g. one per CLDR plural category) with criteria and traits;
/// the engine registers them all and persists them through the attached store.
/// </summary>
public interface ITranslator
{
    Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
