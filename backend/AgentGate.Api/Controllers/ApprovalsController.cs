using AgentGate.Application.Approvals;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/approvals"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ApprovalsController(IApprovalService approvals) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? status = null, int page = 1, int pageSize = 25, CancellationToken ct = default) => Ok(await approvals.ListAsync(status, page, pageSize, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await approvals.GetAsync(id, ct));
    }
    [HttpPost("{id:guid}/approve"), HttpPost("/v1/approvals/{id:guid}/approve"), Authorize(Policy = "ReviewActions"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Approve(Guid id, ResolveApprovalRequest request, CancellationToken ct) => Ok(await approvals.ResolveAsync(id, true, request, ct));
    [HttpPost("{id:guid}/reject"), HttpPost("/v1/approvals/{id:guid}/reject"), Authorize(Policy = "ReviewActions"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Reject(Guid id, ResolveApprovalRequest request, CancellationToken ct) => Ok(await approvals.ResolveAsync(id, false, request, ct));
}

[ApiController, Route("v1/approvals"), Authorize(Policy = "AgentIdentity"), EnableRateLimiting("agent")]
public sealed class AgentApprovalsController(IApprovalService approvals) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await approvals.GetAgentAsync(id, ct));
    }
}
