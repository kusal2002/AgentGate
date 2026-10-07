using AgentGate.Application.Policies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/policies"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PoliciesController(IPolicyService policies, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await policies.ListAsync(ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await policies.GetAsync(id, ct));
    [HttpPost, Authorize(Policy = "ManagePolicies"), RequestSizeLimit(32_768)]
    public async Task<IActionResult> Create(PolicyRequest request, CancellationToken ct)
    {
        var policy = await policies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = policy.Id }, policy);
    }
    [HttpPut("{id:guid}"), Authorize(Policy = "ManagePolicies"), RequestSizeLimit(32_768)]
    public async Task<IActionResult> Update(Guid id, PolicyRequest request, CancellationToken ct) => Ok(await policies.UpdateAsync(id, request, ct));
    [HttpPost("{id:guid}/status"), Authorize(Policy = "ManagePolicies")]
    public async Task<IActionResult> Status(Guid id, PolicyStatusRequest request, CancellationToken ct) => Ok(await policies.SetStatusAsync(id, request, ct));
    [HttpPost("test"), Authorize(Policy = "TestPolicies"), RequestSizeLimit(65_536)]
    public async Task<IActionResult> Test(PolicyTestRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await policies.TestAsync(request, ct));
    }
    [HttpPost("seed-refund-demo"), Authorize(Policy = "ManagePolicies")]
    public async Task<IActionResult> Seed(CancellationToken ct) => environment.IsDevelopment() ? Ok(await policies.SeedDemoAsync(ct)) : NotFound();
}
