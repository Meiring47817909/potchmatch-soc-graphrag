using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagService.Options;

namespace RagService.Controllers;

public sealed record RagStatus(bool Enabled, string? Model, string? Reason);

[ApiController]
[Route("api/rag")]
[Authorize(Roles = "Admin")]
public class RagController(IOptions<GeminiOptions> gemini) : ControllerBase
{
    [HttpGet("status")]
    public RagStatus Status()
    {
        var o = gemini.Value;
        return o.Enabled
            ? new RagStatus(true, o.Model, null)
            : new RagStatus(false, null, "GEMINI_API_KEY is not set");
    }
}
