namespace PhotoAtomic.Tests;

/// <summary>
/// The brace rules, pinned. A phrase is written by hand or by a model, so the
/// parser meets every shape sooner or later — and it must agree with C#, since
/// the whole promise is that a data sentence and a compiled one are the same
/// sentence. The awkward case is "{{{name}}}": a literal brace, a hole, a
/// literal brace. A pattern with lookaround got it wrong (the first version of
/// this parser did); scanning left to right, consuming a doubled brace as one
/// literal, gets it right for the same reason the compiler does.
/// </summary>
public class PhraseBraceTests
{
    [Theory]
    [InlineData("{{rule}}", "")]                      // all literal: no hole at all
    [InlineData("{{{name}}}", "name")]                // brace, hole, brace
    [InlineData("a {{ {name} }} b", "name")]
    [InlineData("{a}{b}", "a,b")]                     // holes may touch
    [InlineData("{ name }", "name")]                  // a model writes spaces; they are not part of the name
    [InlineData("{}", "")]                            // nothing to name: content, not a hole
    [InlineData("100% of {what", "")]                 // never closed: content, not a crash
    [InlineData("The {liquid} in the {vessel:item}", "liquid,vessel")]
    public void Holes(string text, string expected) =>
        Assert.Equal(expected, string.Join(",", new Phrase(text).Holes));

    // Nothing here gets a capital it did not have: every one of these is
    // template text, and a template opens the way it was written.
    [Theory]
    [InlineData("{{rule}}", "{rule}")]
    [InlineData("a {{ {name} }} b", "a { ok } b")]
    [InlineData("{}", "{}")]
    [InlineData("100% of {what", "100% of {what")]
    public void Rendering(string text, string expected) =>
        Assert.Equal(expected, new Phrase(text).Render(("name", "ok")));

    [Fact]
    public void A_literal_brace_survives_into_the_key_doubled_as_a_format_string_needs()
    {
        var phrase = new Phrase("The {{rule}} named {name} fired");

        Assert.Equal("The {{rule}} named {0} fired", phrase.Key);
        Assert.Equal("The {rule} named boiling fired", phrase.Render(("name", "boiling")));
    }

    [Fact]
    public void A_literal_brace_wrapping_a_hole_keeps_both_apart()
    {
        // Asked by the user, and worth pinning: braces around a sentence that
        // itself has a hole. The literal pair survives, the hole in the middle
        // is still a hole, and the key carries the braces doubled.
        var phrase = new Phrase("{{ this is a value: {var} }}");

        Assert.Equal("{{ this is a value: {0} }}", phrase.Key);
        Assert.Equal(["var"], phrase.Holes);
        Assert.Equal("{ this is a value: 3 }", phrase.Render(("var", 3)));
    }
}
