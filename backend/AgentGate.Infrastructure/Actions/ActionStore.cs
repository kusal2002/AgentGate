using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Domain.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AgentGate.Application.Approvals;
using AgentGate.Domain.Approvals;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;

namespace AgentGate.Infrastructure.Actions;

public sealed class ActionStore(AgentGateDbContext db, IApprovalStore approvals, ApprovalSettings settings, IAuditWriter audit) : IActionStore
{
    public async Task<AgentAction?> FindAsync(Guid organizationId, Guid agentId, string idempotencyKey, CancellationToken ct)
    {
        var id = await db.AgentActions.Where(x => x.OrganizationId == organizationId && x.AgentId == agentId && x.IdempotencyKey == idempotencyKey).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        return id is null ? null : await GetAgentActionAsync(organizationId, agentId, id.Value, ct);
    }
    public async Task<AgentAction> CreateOrGetAsync(AgentAction action, CancellationToken ct)
    {
        if (action.Decision == ActionDecision.Review && action.Status == ActionStatus.AwaitingApproval && !action.TestEvaluation)
            action.Approval = ApprovalRules.Create(action, settings);
        db.AgentActions.Add(action);
        var events = new List<AuditEvent>();
        events.Add(audit.Record(action.OrganizationId, "agent.action_requested", AuditActorType.Agent, action.AgentId,
            new { action.TestEvaluation }, action.AgentId, action.Id, action.Approval?.Id, action.CreatedAt, 10));
        if (!action.TestEvaluation)
            events.Add(audit.Record(action.OrganizationId, "policy.evaluated", AuditActorType.Policy, action.MatchedPolicyId,
                new { action.MatchedPolicyId, action.PolicyUpdatedAt, decision = action.Decision.ToString().ToLowerInvariant(), riskLevel = action.RiskLevel?.ToString(), action.ReviewerRole,
                    environmentDefault = action.MatchedPolicyId is null }, action.AgentId, action.Id, action.Approval?.Id, action.CreatedAt, 20));
        events.Add(audit.Record(action.OrganizationId, action.Decision switch { ActionDecision.Allow => "action.allowed", ActionDecision.Deny => "action.denied", _ => "action.review_required" },
            AuditActorType.System, null, new { status = ActionService.StatusName(action.Status), action.TestEvaluation }, action.AgentId, action.Id, action.Approval?.Id, action.CreatedAt, 30));
        if (action.Approval is { } approval)
            events.Add(audit.Record(action.OrganizationId, "approval.created", AuditActorType.System, null, new { approval.ReviewerRole, approval.ExpiresAt }, action.AgentId, action.Id, approval.Id, action.CreatedAt, 40));
        try
        {
            // EF saves action and its approval together in one transaction.
            await db.SaveChangesAsync(ct);
            return await GetAgentActionAsync(action.OrganizationId, action.AgentId, action.Id, ct) ?? throw new InvalidOperationException("Created action could not be retrieved.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_AgentActions_OrganizationId_AgentId_IdempotencyKey" })
        {
            db.Entry(action).State = EntityState.Detached;
            foreach (var row in events) db.Entry(row).State = EntityState.Detached;
            if (action.Approval is not null) db.Entry(action.Approval).State = EntityState.Detached;
            return await FindAsync(action.OrganizationId, action.AgentId, action.IdempotencyKey, ct) ?? throw new InvalidOperationException("Concurrent action could not be retrieved.");
        }
    }
    public async Task<AgentAction?> GetAgentActionAsync(Guid organizationId, Guid agentId, Guid id, CancellationToken ct)
    {
        if (!await db.AgentActions.AnyAsync(x => x.Id == id && x.OrganizationId == organizationId && x.AgentId == agentId, ct)) return null;
        await approvals.ExpireDueAsync(organizationId, ct, actionId: id);
        return await db.AgentActions.AsNoTracking().Include(x => x.Approval).SingleAsync(x => x.Id == id && x.OrganizationId == organizationId && x.AgentId == agentId, ct);
    }
    public async Task<ActionPageDto> ListAsync(Guid organizationId, Guid? agentId, int page, int pageSize, CancellationToken ct)
    {
        await approvals.ExpireDueAsync(organizationId, ct);
        var query = db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == organizationId && (agentId == null || x.AgentId == agentId));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.Agents.Where(x => x.OrganizationId == organizationId), x => x.AgentId, agent => agent.Id, (action, agent) => new { Action = action, agent.Name }).ToListAsync(ct);
        return new(rows.Select(x => new ActionSummaryDto(x.Action.Id, x.Action.AgentId, x.Name, x.Action.ActionType,
            new(x.Action.ResourceType, x.Action.ResourceId), x.Action.Decision.ToString().ToLowerInvariant(), ActionService.StatusName(x.Action.Status),
            x.Action.RiskLevel?.ToString(), x.Action.CreatedAt, x.Action.TestEvaluation)).ToArray(), total, page, pageSize);
    }
    public async Task<ActionDetailDto?> GetAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await approvals.ExpireDueAsync(organizationId, ct, actionId: id);
        var row = await db.AgentActions.AsNoTracking().Include(x => x.Approval).Where(x => x.OrganizationId == organizationId && x.Id == id)
            .Join(db.Agents.Where(x => x.OrganizationId == organizationId), x => x.AgentId, agent => agent.Id, (action, agent) => new { Action = action, agent.Name }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        return MapDetail(row.Action, row.Name);
    }
    public static ActionDetailDto MapDetail(AgentAction x, string agentName) => new(x.Id, x.AgentId, agentName, x.ActionType, new(x.ResourceType, x.ResourceId), x.Decision.ToString().ToLowerInvariant(),
            ActionService.StatusName(x.Status), x.RiskLevel?.ToString(), x.CreatedAt, x.UpdatedAt, x.ExecutedAt, x.MatchedPolicyId,
            x.IdempotencyKey, x.Reason, JsonSerializer.Deserialize<JsonElement>(x.ParametersJson), JsonSerializer.Deserialize<JsonElement>(x.ContextJson), x.TestEvaluation,
            x.MatchedPolicyName, x.ReviewerRole, x.PolicyUpdatedAt, x.Approval?.Id, x.Approval?.Status.ToString().ToLowerInvariant());
}
