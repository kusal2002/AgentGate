using System.Security.Claims;
using AgentGate.Application.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentGate.Api.Controllers;

[ApiController, Route("v1/agents"), Authorize(Policy = "AgentIdentity"), EnableRateLimiting("agent")]
public sealed class AgentIdentityController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult Me() => Ok(new AgentIdentityDto(Guid.Parse(User.FindFirstValue("agent_id")!), Guid.Parse(User.FindFirstValue("org")!), Guid.Parse(User.FindFirstValue("key_id")!), User.Identity!.Name!, User.FindFirstValue("environment")!, User.FindFirstValue("version")!));
}
