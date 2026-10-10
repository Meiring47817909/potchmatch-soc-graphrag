using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagService.Options;
using RagService.Pipeline;

namespace RagService.Controllers;

public sealed record RagStatus(bool Enabled, string? Model, string? Reason);

/// <summary>Error body for 400/503 responses: { error, message } (spec §5.2).</summary>
public sealed record RagError(string Error, string Message);

[ApiController]
[Route("api/rag")]
[Authorize(Roles = "Admin")]
public class RagController(IOptions<GeminiOptions> gemini) : ControllerBase
{
    public const int MaxQuestionLength = 500;
    public const int MaxContextIdLength = 200;

    [HttpGet("status")]
    public RagStatus Status()
    {
        var o = gemini.Value;
        return o.Enabled
            ? new RagStatus(true, o.Model, null)
            : new RagStatus(false, null, "GEMINI_API_KEY is not set");
    }

    /// <summary>Answers a question from the SOC knowledge graph (text2cypher GraphRAG) with its full evidence.</summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, [FromServices] RagPipeline pipeline, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null) return BadRequest(new RagError("invalid_question", invalid));

        try
        {
            return Ok(await pipeline.AskAsync(request, ct));
        }
        catch (RagDisabledException)
        {
            return Unavailable("rag_disabled", RagMessages.RagDisabled);
        }
        catch (LlmUnavailableException ex)
        {
            return Unavailable("llm_unavailable", ex.Message);
        }
        catch (GraphUnavailableException)
        {
            return Unavailable("graph_unavailable", RagMessages.GraphUnavailable);
        }
    }

    private static string? Validate(AskRequest? r)
    {
        if (r is null || string.IsNullOrWhiteSpace(r.Question)) return "Question is required.";
        if (r.Question.Length > MaxQuestionLength) return $"Question must be at most {MaxQuestionLength} characters.";
        if (r.AlertId is { Length: > MaxContextIdLength } || r.IncidentId is { Length: > MaxContextIdLength })
            return $"alertId and incidentId must be at most {MaxContextIdLength} characters.";
        return null;
    }

    private ObjectResult Unavailable(string error, string message) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new RagError(error, message));
}
