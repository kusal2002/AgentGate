using AgentGate.Application.Actions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentGate.Api.Controllers;

[ApiController, Route("v1/actions"), Authorize(Policy = "AgentIdentity"), EnableRateLimiting("agent")]
public sealed class ActionsController(IActionService actions) : ControllerBase
{
    [HttpPost("evaluate"), RequestSizeLimit(65_536)]
    public async Task<IActionResult> Evaluate(EvaluateActionRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await actions.EvaluateAsync(request, ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await actions.GetAgentActionAsync(id, ct));
}

[ApiController, Route("api/actions"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ActionHistoryController(IActionHistoryService history) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid? agentId, int page = 1, int pageSize = 25, CancellationToken ct = default)
        => Ok(await history.ListAsync(agentId, page, pageSize, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await history.GetAsync(id, ct));
    }
}
