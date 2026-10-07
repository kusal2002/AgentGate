using AgentGate.Application.Accounts;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AgentGate.Api.Controllers;

[ApiController, Route("api/integrations/slack"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SlackIntegrationController(ISlackStore store, ICurrentAccount account) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store"; return Ok(await store.GetAsync(account.OrganizationId, ct));
    }
    [HttpPut, Authorize(Policy = "ManageOrganization"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Configure(SlackIntegrationRequest request, CancellationToken ct)
    {
        await store.ConfigureAsync(account.OrganizationId, request, ct); return Ok(await store.GetAsync(account.OrganizationId, ct));
    }
    [HttpPut("reviewers"), Authorize(Policy = "ManageOrganization"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Map(SlackReviewerRequest request, CancellationToken ct)
    {
        await store.MapAsync(account.OrganizationId, request, ct); return Ok(await store.GetAsync(account.OrganizationId, ct));
    }
    [HttpDelete("reviewers/{userId:guid}"), Authorize(Policy = "ManageOrganization")]
    public async Task<IActionResult> Unmap(Guid userId, CancellationToken ct)
    {
        await store.UnmapAsync(account.OrganizationId, userId, ct); return NoContent();
    }
}

[ApiController, Route("api/integrations/slack/actions")]
public sealed class SlackActionsController(ISlackStore store) : ControllerBase
{
    [HttpPost, AllowAnonymous, RequestSizeLimit(65536), ServiceFilter(typeof(AgentGate.Api.Slack.SlackSignatureFilter))]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var form = await Request.ReadFormAsync(ct);
        if (form["payload"].Count != 1) return BadRequest();
        var click = SlackSecurity.Parse(form["payload"].ToString());
        // Ack quickly without a Slack HTTP call; durable maintenance updates the shared message afterward.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { return Ok(new { text = await store.HandleAsync(click, deadline.Token, Request.Headers["X-Slack-Signature"].ToString()[3..]) }); }
        catch (RequestException ex) when (ex.StatusCode is 403 or 404 or 409)
        { return Ok(new { text = ex.Message }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(503); }
    }
}
