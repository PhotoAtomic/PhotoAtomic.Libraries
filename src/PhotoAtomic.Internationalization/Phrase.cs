using System.Text;

namespace PhotoAtomic;

/// <summary>
/// An interpolated string written as DATA — the same sentence a
/// <c>T($"...")</c> call site produces, except nobody compiled it.
///
/// <code>
/// new Phrase("The {liquid} boils away in the {vessel}").Render(values)
/// </code>
///
/// It exists because the content of this kind of program is increasingly
/// WRITTEN BY A MODEL, and a model cannot compile a call site. A generated room
/// brings its own rules and its own verbs; without this it could not bring its
/// own words, and everything it invented had to fall back on a generic
/// sentence written in advance by a programmer who could not know about it.
///
/// The holes are NAMED, not numbered, because the data around them already
/// names things: a rule binds "liquid" and "vessel", an action binds "actor"
/// and "target", and a model writing a sentence should say what goes in the
/// hole rather than count. The names become the LEGEND handed to the
/// translator, so an AI translating this sentence is told "{0} is the liquid" —
/// better than what a C# call site usually manages to say.
///
/// A hole may declare a context after a colon — <c>{cook:person}</c> — for the
/// times the value's own type cannot say it. Nothing else about the syntax is
/// invented: doubled braces are literal braces, exactly as in C#.
///
/// The English text IS the key AND the default: an untranslated phrase renders
/// as written, which is the same bargain T() has always made.
/// </summary>
public sealed record Phrase(string Text)
{
    /// <summary>The structural key — "The {0} boils away in the {1}" — identical to what the compiler derives.</summary>
    public string Key => parsed.Value.Key;

    /// <summary>The name of each hole, in the order they appear; repeats get one entry each.</summary>
    public IReadOnlyList<string> Holes => parsed.Value.Holes;

    /// <summary>
    /// The sentence in the ambient language, holes filled from the bindings.
    ///
    /// The rendering does not reimplement anything: it replays the phrase into
    /// the real interpolated-string handler and calls T(), so a data sentence
    /// and a compiled one take the exact same path — same key, same facts, same
    /// matching, same table, same lint. A second implementation would be a
    /// second set of bugs.
    /// </summary>
    public string Render(IReadOnlyDictionary<string, object?> values)
    {
        var (_, holes, segments, contexts) = parsed.Value;

        var handler = new TranslationInterpolatedStringHandler(Text.Length, holes.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            handler.AppendLiteral(segments[i]);

            if (i < holes.Count)
            {
                // A hole nobody bound renders as its own name: a sentence with
                // "{vessel}" showing through is odd, but it is still a sentence
                // the player can read, and T() has never thrown over content.
                var value = values.TryGetValue(holes[i], out var bound) ? bound : holes[i];
                handler.AppendFormatted(value, expression: holes[i]);
            }
        }

        return Internationalization.T(handler, contexts);
    }

    /// <summary>Convenience for the common shape: names and values in pairs.</summary>
    public string Render(params (string Hole, object? Value)[] values) =>
        Render(values.ToDictionary(pair => pair.Hole, pair => pair.Value, StringComparer.Ordinal));

    public override string ToString() => Text;

    private readonly Lazy<Parsed> parsed = new(() => Parse(Text));

    private sealed record Parsed(
        string Key,
        IReadOnlyList<string> Holes,
        IReadOnlyList<string> Segments,
        string? Contexts);

    /// <summary>
    /// Splits the text into literals and holes once, keeping what each of them
    /// needs: the key for the table, the names for the legend, the declared
    /// contexts as sentence facts ("0:person") — which is the very shape the
    /// engine already uses for a hole's own type, so declaring one by hand and
    /// having one derived from a [Translatable] type are the same thing to the
    /// matcher.
    /// </summary>
    private static Parsed Parse(string text)
    {
        var key = new StringBuilder();
        var holes = new List<string>();
        var segments = new List<string>();
        var contexts = new List<string>();
        var literal = new StringBuilder();

        // Scanned left to right rather than matched by a pattern, because the
        // rule being replicated is C#'s own and it is positional: a doubled
        // brace is a literal brace and is CONSUMED, so what follows starts
        // fresh. "{{{name}}}" is a literal brace, a hole, a literal brace —
        // which no amount of lookaround gets right, as the first version of
        // this proved.
        var position = 0;
        while (position < text.Length)
        {
            var character = text[position];

            if (character is '{' or '}' && position + 1 < text.Length && text[position + 1] == character)
            {
                literal.Append(character);
                position += 2;
                continue;
            }

            if (character != '{')
            {
                literal.Append(character);
                position++;
                continue;
            }

            var close = text.IndexOf('}', position + 1);
            if (close < 0)
            {
                // An unbalanced brace is content, not a hole: better a stray
                // character on screen than a sentence that refuses to render.
                literal.Append(character);
                position++;
                continue;
            }

            var inside = text[(position + 1)..close];
            var separator = inside.IndexOf(':');
            var name = (separator < 0 ? inside : inside[..separator]).Trim();
            var context = separator < 0 ? null : inside[(separator + 1)..].Trim();

            if (name.Length == 0)
            {
                literal.Append(character);
                position++;
                continue;
            }

            segments.Add(literal.ToString());
            key.Append(Escape(literal.ToString())).Append('{').Append(holes.Count).Append('}');
            literal.Clear();

            if (!string.IsNullOrEmpty(context))
            {
                contexts.Add($"{holes.Count}:{context}");
            }

            holes.Add(name);
            position = close + 1;
        }

        segments.Add(literal.ToString());
        key.Append(Escape(literal.ToString()));

        return new Parsed(
            key.ToString(),
            holes,
            segments,
            contexts.Count > 0 ? string.Join(',', contexts) : null);
    }

    /// <summary>The key is a format string, so a literal brace travels doubled inside it.</summary>
    private static string Escape(string literal) =>
        literal.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
}
