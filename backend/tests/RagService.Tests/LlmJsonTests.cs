using RagService.Pipeline;

namespace RagService.Tests;

public class LlmJsonTests
{
    private sealed record Q(string Cypher);

    [Theory]
    [InlineData("```json\n{\"cypher\":\"MATCH (n) RETURN n\"}\n```")]
    [InlineData("Here you go: {\"cypher\":\"MATCH (n) RETURN n\"} thanks")]
    [InlineData("{\"cypher\":\"MATCH (n) RETURN n\"}")]
    public void GeminiJson_FencedOrPrefixed_Parsed(string text)
    {
        var q = LlmJson.Parse<Q>(text);
        Assert.NotNull(q);
        Assert.Equal("MATCH (n) RETURN n", q!.Cypher);
    }

    [Theory]
    [InlineData("no json")]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("} reversed {")]
    public void GeminiJson_Invalid_ReturnsNull(string text) => Assert.Null(LlmJson.Parse<Q>(text));

    [Fact]
    public void Nested_Braces_TakesOutermost()
    {
        var q = LlmJson.Parse<Q>("x {\"cypher\":\"RETURN {a:1}\", \"o\":{\"k\":1}} y");
        Assert.Equal("RETURN {a:1}", q!.Cypher);
    }
}
