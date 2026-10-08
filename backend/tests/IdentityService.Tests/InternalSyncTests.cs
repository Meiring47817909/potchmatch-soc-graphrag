using IdentityService.Controllers;
using IdentityService.Data;
using IdentityService.Logging;
using IdentityService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace IdentityService.Tests;

public class InternalSyncTests
{
    private const string Key = "test-sync-key";

    private static InternalSyncController Build(IdentityDbContext db, string? configuredKey, string? headerKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GraphSync:Key"] = configuredKey })
            .Build();
        var http = new DefaultHttpContext();
        if (headerKey is not null) http.Request.Headers["X-Graph-Sync-Key"] = headerKey;
        return new InternalSyncController(db, config)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static async Task SeedAsync(IdentityDbContext db)
    {
        db.Players.AddRange(
            new Player { Name = "Zed", Surname = "Z", Email = "zed@x.com", Role = PlayerRole.Coach },
            new Player { Name = "Admin", Surname = "A", Email = "admin@x.com", Role = PlayerRole.Admin },
            new Player { Name = "Bob", Surname = "B", Email = "bob@x.com", Role = PlayerRole.Player });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SyncPlayers_ReturnsAllIncludingAdmin()
    {
        using var db = TestDb.Create();
        await SeedAsync(db);
        var result = await Build(db, Key, Key).Players(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<SyncPlayerResponse>>(ok.Value).ToList();
        Assert.Equal(new[] { "admin@x.com", "bob@x.com", "zed@x.com" }, list.Select(p => p.Email));
        Assert.Equal(new[] { "Admin", "Player", "Coach" }, list.Select(p => p.Role));
        Assert.Equal("Bob", list[1].Name);
        Assert.Equal("B", list[1].Surname);
    }

    [Fact]
    public async Task SyncPlayers_WrongKey_401()
    {
        using var db = TestDb.Create();
        await SeedAsync(db);
        Assert.IsType<UnauthorizedResult>(await Build(db, Key, "wrong").Players(default));
        Assert.IsType<UnauthorizedResult>(await Build(db, Key, null).Players(default));
    }

    [Fact]
    public async Task SyncPlayers_EmptyConfiguredKey_401()
    {
        using var db = TestDb.Create();
        Assert.IsType<UnauthorizedResult>(await Build(db, "", "").Players(default));
        Assert.IsType<UnauthorizedResult>(await Build(db, null, "anything").Players(default));
    }

    [Fact]
    public void SyncKeyValidator_RejectsNullEmptyWrong_AcceptsExact()
    {
        Assert.False(SyncKeyValidator.IsValid(null, "k"));
        Assert.False(SyncKeyValidator.IsValid("", "k"));
        Assert.False(SyncKeyValidator.IsValid("x", "k"));
        Assert.False(SyncKeyValidator.IsValid("k", ""));
        Assert.True(SyncKeyValidator.IsValid("k", "k"));
    }
}
