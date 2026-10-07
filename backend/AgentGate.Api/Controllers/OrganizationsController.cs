using AgentGate.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/organizations"), Authorize]
public sealed class OrganizationsController(AccountService accounts, ICurrentAccount current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await accounts.OrganizationsAsync(current, ct));
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Ok((await accounts.MeAsync(current, ct)).Organization);
    [HttpPatch("current"), Authorize(Policy = "ManageOrganization")]
    public async Task<IActionResult> Rename(OrganizationRequest request, CancellationToken ct) { await accounts.RenameAsync(current, request, ct); return NoContent(); }
    [HttpGet("current/members")]
    public async Task<IActionResult> Members(CancellationToken ct) => Ok(await accounts.MembersAsync(current, ct));
    [HttpPost("current/members"), Authorize(Policy = "ManageOrganization")]
    public async Task<IActionResult> AddMember(MemberRequest request, CancellationToken ct) { await accounts.AddMemberAsync(current, request, ct); return NoContent(); }
    [HttpPatch("current/members/{id:guid}"), Authorize(Policy = "ManageOrganization")]
    public async Task<IActionResult> ChangeRole(Guid id, RoleRequest request, CancellationToken ct) { await accounts.ChangeRoleAsync(current, id, request, ct); return NoContent(); }
}
