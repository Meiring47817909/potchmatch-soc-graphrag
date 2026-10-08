using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SocService.Controllers;
using SocService.Queries;

namespace SocService.Tests;

public class InternalSyncKeyTests
{
    private static InternalSyncController Make(string? header, string configuredKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GraphSync:Key"] = configuredKey }).Build();
        var http = new DefaultHttpContext();
        if (header is not null) http.Request.Headers["X-Graph-Sync-Key"] = header;
        return new InternalSyncController(new SyncQuery(TestDb.Create(), TimeProvider.System), config)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    [Theory]
    [InlineData(null, "k")]
    [InlineData("wrong", "k")]
    [InlineData("", "")]
    [InlineData("anything", "")]
    public async Task InternalSync_KeyValidation_Rejects(string? header, string configured)
    {
        var c = Make(header, configured);
        Assert.IsType<UnauthorizedResult>(await c.Alerts(null, null, null, default));
        Assert.IsType<UnauthorizedResult>(await c.Events(null, null, null, default));
    }

    [Fact]
    public async Task InternalSync_KeyValidation_AcceptsCorrect()
    {
        var c = Make("k", "k");
        Assert.IsType<OkObjectResult>(await c.Alerts(null, null, null, default));
        Assert.IsType<OkObjectResult>(await c.Events(null, null, null, default));
    }
}
