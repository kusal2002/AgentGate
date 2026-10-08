using AgentGate.Application.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/dashboard"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await dashboard.OverviewAsync(ct));
    [HttpGet("/api/agents/{id:guid}/statistics")]
    public async Task<IActionResult> AgentStatistics(Guid id, CancellationToken ct) => Ok(await dashboard.AgentStatisticsAsync(id, ct));
}
