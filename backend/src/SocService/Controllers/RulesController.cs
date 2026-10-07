using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocService.Detection;
using SocService.Dtos;

namespace SocService.Controllers;

[ApiController]
[Route("api/soc/rules")]
[Authorize(Roles = "Admin")]
public class RulesController : ControllerBase
{
    private readonly IEnumerable<IDetectionRule> _rules;
    public RulesController(IEnumerable<IDetectionRule> rules) => _rules = rules;

    [HttpGet]
    public IActionResult Get() => Ok(_rules.Select(RuleDocResponse.From).ToList());
}
