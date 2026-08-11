using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

public class KeyDerivationTests
{
    [Fact]
    public void The_key_of_a_holeless_string_is_the_string_itself()
    {
        Assert.Equal("Hello adventurer", KeyOf($"Hello adventurer"));
    }

    [Fact]
    public void Each_hole_becomes_a_positional_slot_in_source_order()
    {
        var color = "Red";
        var sentiment = "Love";

        Assert.Equal("{0} is the color of {1}", KeyOf($"{color} is the color of {sentiment}"));
    }

    [Fact]
    public void Literal_braces_are_escaped_so_the_key_stays_a_valid_template()
    {
        var action = "jump";

        Assert.Equal("press {{X}} to {0}", KeyOf($"press {{X}} to {action}"));
    }

    [Fact]
    public void Format_specifiers_do_not_change_the_key()
    {
        var price = 12.5;

        // "{price:F2}" and "{price}" are the same sentence to a translator.
        Assert.Equal("{0} gold", KeyOf($"{price:F2} gold"));
        Assert.Equal("{0} gold", KeyOf($"{price} gold"));
    }

    [Fact]
    public void The_same_sentence_shape_yields_the_same_key_whatever_the_values()
    {
        var n1 = 1;
        var n2 = 99;

        Assert.Equal(KeyOf($"You found {n1} coins"), KeyOf($"You found {n2} coins"));
    }
}
