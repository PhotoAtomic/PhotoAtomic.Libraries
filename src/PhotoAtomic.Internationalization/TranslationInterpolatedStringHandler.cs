using System.Runtime.CompilerServices;
using System.Text;

namespace PhotoAtomic;

/// <summary>
/// Captures an interpolated string as structure instead of text: literal
/// segments accumulate into a canonical template key ("{0} is the color of {1}")
/// while hole values are kept aside, unformatted, for later rendering. Each hole
/// also records the source expression that filled it ("color", "player.Name"):
/// positions keep the key stable under refactoring, expressions form a legend
/// that gives translators — human or AI — the meaning of every slot.
/// The compiler builds this incrementally at every T($"...") call site.
/// </summary>
[InterpolatedStringHandler]
public ref struct TranslationInterpolatedStringHandler
{
    private readonly StringBuilder template;
    private readonly List<TranslationArgument> arguments;

    public TranslationInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        // Capacity hint only: the key is the literals plus one "{n}" slot per
        // hole, and a slot is 3 chars ("{0}") for up to ten holes. Sizing the
        // builder up front just avoids reallocations while appending.
        template = new StringBuilder(literalLength + formattedCount * 3);
        arguments = new List<TranslationArgument>(formattedCount);
    }

    public void AppendLiteral(string literal) =>
        // Literal braces must survive a later string.Format on the template.
        template.Append(literal.Replace("{", "{{").Replace("}", "}}"));

    // Two overloads cover the four interpolation shapes the compiler emits:
    // {x} and {x:F2} land here...
    public void AppendFormatted<T>(
        T value,
        string? format = null,
        [CallerArgumentExpression(nameof(value))] string expression = "") =>
        Capture(value, 0, format, expression);

    // ...while {x,10} and {x,10:F2} land here (an int argument cannot convert
    // to the string format parameter above, so binding is unambiguous).
    public void AppendFormatted<T>(
        T value,
        int alignment,
        string? format = null,
        [CallerArgumentExpression(nameof(value))] string expression = "") =>
        Capture(value, alignment, format, expression);

    private void Capture<T>(T value, int alignment, string? format, string expression)
    {
        // The key only records the hole position: "{price:F2}" and "{price}"
        // are the same sentence to a translator. Format and alignment are
        // kept per-argument and applied at render time.
        template.Append('{').Append(arguments.Count).Append('}');
        arguments.Add(new TranslationArgument(value, alignment, format, expression));
    }

    internal string Key => template.ToString();

    internal IReadOnlyList<TranslationArgument> Arguments => arguments;
}

/// <summary>
/// One captured hole: its value, the formatting the call site asked for, and
/// the source expression that produced it (the legend entry for its slot).
/// </summary>
internal readonly record struct TranslationArgument(
    object? Value,
    int Alignment,
    string? Format,
    string Expression);
