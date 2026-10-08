using AgentGate.Application.Audit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/audit"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditController(AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AuditFilter filter, CancellationToken ct) => Ok(await audit.ListAsync(filter, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await audit.GetAsync(id, ct));
    [HttpGet("/api/actions/{id:guid}/timeline")]
    public async Task<IActionResult> Action(Guid id, int page = 1, int pageSize = 25, CancellationToken ct = default) => Ok(await audit.TimelineAsync(id, false, page, pageSize, ct));
    [HttpGet("/api/approvals/{id:guid}/timeline")]
    public async Task<IActionResult> Approval(Guid id, int page = 1, int pageSize = 25, CancellationToken ct = default) => Ok(await audit.TimelineAsync(id, true, page, pageSize, ct));
}
