using RagService.Controllers;
using RagService.Options;

namespace RagService.Tests;

public class StatusTests
{
    private static RagStatus Status(GeminiOptions o) =>
        new RagController(Microsoft.Extensions.Options.Options.Create(o)).Status();

    [Fact]
    public void Status_NoKey_Disabled()
    {
        var s = Status(new GeminiOptions());
        Assert.False(s.Enabled);
        Assert.Null(s.Model);
        Assert.Equal("GEMINI_API_KEY is not set", s.Reason);
    }

    [Fact]
    public void Status_WithKey_EnabledWithModel()
    {
        var s = Status(new GeminiOptions { ApiKey = "k", Model = "gemini-2.5-flash" });
        Assert.True(s.Enabled);
        Assert.Equal("gemini-2.5-flash", s.Model);
        Assert.Null(s.Reason);
    }

    [Fact]
    public void Status_RequiresAdminRole()
    {
        var attr = typeof(RagController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.Equal("Admin", attr.Roles);
    }
}
