using SocService.Ingestion;

namespace SocService.Tests;

public class IngestKeyValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void KeyValidator_RejectsNullEmptyAndWrong(string? provided)
        => Assert.False(IngestKeyValidator.IsValid(provided, "secret"));

    [Fact]
    public void KeyValidator_AcceptsExact()
        => Assert.True(IngestKeyValidator.IsValid("secret", "secret"));

    [Fact]
    public void KeyValidator_RejectsWhenExpectedEmpty()
        => Assert.False(IngestKeyValidator.IsValid("", ""));
}
