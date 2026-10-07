using SocService.Health;

namespace SocService.Tests;

public class ConsulHealthSourceTests
{
    [Fact]
    public void Aggregate_StaleCriticalPlusPassingInstance_IsPassing()
    {
        var result = ConsulHealthSource.Aggregate([
            ("payment-service", "critical"),
            ("payment-service", "passing"),
        ]);

        Assert.Equal("passing", result["payment-service"]);
    }

    [Theory]
    [InlineData("critical", "warning", "warning")]
    [InlineData("warning", "critical", "warning")]
    [InlineData("critical", "critical", "critical")]
    [InlineData("passing", "warning", "passing")]
    public void Aggregate_BestStatusWins(string a, string b, string expected)
    {
        var result = ConsulHealthSource.Aggregate([("svc", a), ("svc", b)]);

        Assert.Equal(expected, result["svc"]);
    }

    [Fact]
    public void Aggregate_KeepsServicesSeparate()
    {
        var result = ConsulHealthSource.Aggregate([("a", "passing"), ("b", "critical")]);

        Assert.Equal("passing", result["a"]);
        Assert.Equal("critical", result["b"]);
    }
}
