using AgentGate.Application.Agents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/agents"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AgentsController(IAgentService agents) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await agents.ListAsync(ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await agents.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> Create(CreateAgentRequest request, CancellationToken ct)
    {
        var agent = await agents.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = agent.Id }, agent);
    }
    [HttpPatch("{id:guid}"), Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> Update(Guid id, UpdateAgentRequest request, CancellationToken ct) => Ok(await agents.UpdateAsync(id, request, ct));
    [HttpPost("{id:guid}/disable"), Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct) { await agents.DisableAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/enable"), Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct) { await agents.EnableAsync(id, ct); return NoContent(); }
    [HttpGet("{id:guid}/keys")]
    public async Task<IActionResult> Keys(Guid id, CancellationToken ct) => Ok(await agents.KeysAsync(id, ct));
    [HttpPost("{id:guid}/keys"), Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> GenerateKey(Guid id, CreateKeyRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return StatusCode(201, await agents.GenerateKeyAsync(id, request, ct));
    }
    [HttpPost("{id:guid}/keys/{keyId:guid}/revoke"), Authorize(Policy = "ManageAgents")]
    public async Task<IActionResult> Revoke(Guid id, Guid keyId, CancellationToken ct) { await agents.RevokeKeyAsync(id, keyId, ct); return NoContent(); }
}
