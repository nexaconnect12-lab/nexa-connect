using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public class TouchInputEditTests
{
    [Fact]
    public void Selection_replacement_preserves_surrounding_reference()
        => Assert.Equal("REF-123-Z", TouchInputEdit.Replace("REF-old-Z", 4, 3, "123", 64, false));

    [Theory]
    [InlineData("12.34", 5, 0, "5")]
    [InlineData("12.34", 5, 0, ".")]
    [InlineData("12", 2, 0, "e3")]
    [InlineData("12", 0, 0, "-")]
    [InlineData("12", 2, 0, ",")]
    public void Amount_keypad_rejects_invalid_drafts(string text, int start, int length, string key)
        => Assert.Null(TouchInputEdit.Replace(text, start, length, key, 0, true));

    [Theory]
    [InlineData("", "")]
    [InlineData(".", ".")]
    [InlineData("0.01", "0.01")]
    [InlineData("1234567890.99", "1234567890.99")]
    public void Amount_editing_allows_incomplete_drafts_without_confirming_payment(string input, string expected)
        => Assert.Equal(expected, TouchInputEdit.Replace("0", 0, 1, input, 0, true));

    [Fact]
    public void Amount_separator_matches_existing_current_culture_parsers()
    {
        Assert.Equal("12,34", TouchInputEdit.Replace("12", 2, 0, ",34", 0, true, ","));
        Assert.Null(TouchInputEdit.Replace("12", 2, 0, ".34", 0, true, ","));
    }

    [Fact]
    public void Maximum_and_invalid_selection_are_enforced()
    {
        Assert.Null(TouchInputEdit.Replace("1234", 4, 0, "5", 4, false));
        Assert.Null(TouchInputEdit.Replace("1234", -1, 0, "5", 4, false));
        Assert.Null(TouchInputEdit.Replace("1234", 4, 1, "5", 4, false));
        Assert.Null(TouchInputEdit.Replace("", 0, 0, new string('x', 4097), 0, false));
    }

    [Theory]
    [InlineData("A😀", 3, 1)]
    [InlineData("Aé", 3, 1)]
    [InlineData("ABC", 3, 2)]
    [InlineData("", 0, 0)]
    public void Backspace_removes_previous_text_element(string text, int caret, int expected)
        => Assert.Equal(expected, TouchInputEdit.PreviousElement(text, caret));
}
