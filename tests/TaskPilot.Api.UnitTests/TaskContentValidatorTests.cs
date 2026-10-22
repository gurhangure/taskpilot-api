using TaskPilot.Api.Services.Tasks;
using Xunit;

namespace TaskPilot.Api.UnitTests;

/// <summary>Checks pure task content rules without creating a host, database, or EF Core context.</summary>
public sealed class TaskContentValidatorTests
{
    /// <summary>Rejects absent titles and whitespace-only text, including Unicode whitespace.</summary>
    /// <param name="title">A title that contains no meaningful content.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    [InlineData("\u2003")]
    public void Validate_BlankTitle_IdentifiesTitle(string? title)
    {
        var exception = Assert.Throws<ArgumentException>(() => TaskContentValidator.Validate(title, null));

        Assert.Equal("title", exception.ParamName);
    }

    /// <summary>Accepts nonblank titles at the minimum and immediately below and at the maximum length.</summary>
    /// <param name="length">The number of UTF-16 code units in the title.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(200)]
    public void Validate_ValidTitleLength_AcceptsContent(int length)
    {
        var exception = Record.Exception(() => TaskContentValidator.Validate(new string('t', length), null));

        Assert.Null(exception);
    }

    /// <summary>Rejects content immediately beyond the title limit without trimming it to fit.</summary>
    [Fact]
    public void Validate_TitleOverLimit_RejectsUntrimmedContent()
    {
        var title = new string('t', 200) + " ";

        var exception = Assert.Throws<ArgumentException>(() => TaskContentValidator.Validate(title, null));

        Assert.Equal("title", exception.ParamName);
    }

    /// <summary>Accepts surrounding whitespace when the title still contains nonblank content.</summary>
    [Fact]
    public void Validate_TitleWithSurroundingWhitespace_AcceptsContent()
    {
        var exception = Record.Exception(() => TaskContentValidator.Validate(" \tTask\r\n ", null));

        Assert.Null(exception);
    }

    /// <summary>Preserves the existing string-length rule for supplementary Unicode characters.</summary>
    [Fact]
    public void Validate_SupplementaryCharacters_CountsUtf16CodeUnits()
    {
        var title = string.Concat(Enumerable.Repeat("\U0001F680", 100));

        Assert.Null(Record.Exception(() => TaskContentValidator.Validate(title, null)));
        var exception = Assert.Throws<ArgumentException>(() => TaskContentValidator.Validate(title + "x", null));
        Assert.Equal("title", exception.ParamName);
    }

    /// <summary>Allows optional descriptions and does not impose a nonblank description requirement.</summary>
    /// <param name="description">An absent, empty, or whitespace-only description.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Validate_OptionalDescription_AcceptsContent(string? description)
    {
        var exception = Record.Exception(() => TaskContentValidator.Validate("Task", description));

        Assert.Null(exception);
    }

    /// <summary>Accepts descriptions immediately below and at the maximum permitted length.</summary>
    /// <param name="length">The description length to validate.</param>
    [Theory]
    [InlineData(1999)]
    [InlineData(2000)]
    public void Validate_ValidDescriptionLength_AcceptsContent(int length)
    {
        var exception = Record.Exception(() => TaskContentValidator.Validate("Task", new string('d', length)));

        Assert.Null(exception);
    }

    /// <summary>Identifies the description when it exceeds its limit by one character.</summary>
    [Fact]
    public void Validate_DescriptionOverLimit_IdentifiesDescription()
    {
        var exception = Assert.Throws<ArgumentException>(() => TaskContentValidator.Validate("Task", new string('d', 2001)));

        Assert.Equal("description", exception.ParamName);
    }

    /// <summary>Reports the title error first when both fields violate their rules.</summary>
    [Fact]
    public void Validate_BothFieldsInvalid_ReportsTitleFirst()
    {
        var exception = Assert.Throws<ArgumentException>(() => TaskContentValidator.Validate("", new string('d', 2001)));

        Assert.Equal("title", exception.ParamName);
    }
}
