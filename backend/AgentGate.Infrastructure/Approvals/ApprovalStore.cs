using AgentGate.Application.Actions;
using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Approvals;
using AgentGate.Infrastructure.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;

namespace AgentGate.Infrastructure.Approvals;

public sealed class ApprovalStore(AgentGateDbContext db, IAuditWriter audit) : IApprovalStore
{
    public async Task<ApprovalPageDto> ListAsync(Guid organizationId, ApprovalStatus? status, int page, int pageSize, CancellationToken ct)
    {
        await ExpireDueAsync(organizationId, ct);
        var query = db.ApprovalRequests.AsNoTracking().Where(x => x.OrganizationId == organizationId && (status == null || x.Status == status));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.AgentActions.Where(x => x.OrganizationId == organizationId), x => x.ActionId, x => x.Id, (approval, action) => new { Approval = approval, Action = action })
            .Join(db.Agents.Where(x => x.OrganizationId == organizationId), x => x.Action.AgentId, x => x.Id, (row, agent) => new { row.Approval, row.Action, agent.Name }).ToListAsync(ct);
        return new(rows.Select(x => Summary(x.Approval, x.Action, x.Name)).ToArray(), total, page, pageSize);
    }
    public async Task<ApprovalDetailDto?> GetAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await ExpireDueAsync(organizationId, ct, approvalId: id);
        var approval = await db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (approval is null) return null;
        var action = await db.AgentActions.AsNoTracking().Include(x => x.Approval).SingleAsync(x => x.OrganizationId == organizationId && x.Id == approval.ActionId, ct);
        var agentName = await db.Agents.Where(x => x.OrganizationId == organizationId && x.Id == action.AgentId).Select(x => x.Name).SingleAsync(ct);
        var decisions = await db.ApprovalDecisions.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ApprovalRequestId == id)
            .Join(db.Users, x => x.ReviewerUserId, x => x.Id, (decision, user) => new ApprovalDecisionDto(decision.Id, user.Id, user.Name,
                decision.Decision == ApprovalDecisionType.Approve ? "approve" : "reject", decision.Comment, decision.CreatedAt, decision.Source == ApprovalDecisionSource.Slack ? "slack" : "dashboard")).ToListAsync(ct);
        return new(Summary(approval, action, agentName), ActionStore.MapDetail(action, agentName), false, approval.ResolvedByUserId, approval.ReviewerComment, decisions);
    }
    public async Task<AgentApprovalDto?> GetAgentAsync(Guid organizationId, Guid agentId, Guid id, CancellationToken ct)
    {
        // Locate the owner before performing a targeted expiration write.
        var owned = await db.ApprovalRequests.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Id == id)
            .Join(db.AgentActions.Where(x => x.OrganizationId == organizationId && x.AgentId == agentId), x => x.ActionId, x => x.Id, (approval, action) => approval.Id).AnyAsync(ct);
        if (!owned) return null;
        await ExpireDueAsync(organizationId, ct, approvalId: id);
        var row = await db.ApprovalRequests.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Id == id)
            .Join(db.AgentActions.Where(x => x.OrganizationId == organizationId && x.AgentId == agentId), x => x.ActionId, x => x.Id, (approval, action) => new { Approval = approval, Action = action }).SingleAsync(ct);
        var x = row.Approval;
        return new(x.Id, x.ActionId, x.Status.ToString().ToLowerInvariant(), ActionService.StatusName(row.Action.Status), x.ReviewerRole, x.RequestedAt, x.ExpiresAt, x.ResolvedAt);
    }
    public async Task ResolveAsync(Guid organizationId, Guid userId, Guid id, bool approve, string comment, CancellationToken ct, ApprovalDecisionSource source = ApprovalDecisionSource.Dashboard)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // All human and expiry transitions lock the same row first.
        var locked = await db.ApprovalRequests.FromSqlInterpolated($"SELECT * FROM \"ApprovalRequests\" WHERE \"Id\" = {id} AND \"OrganizationId\" = {organizationId} FOR UPDATE").ToListAsync(ct);
        var approval = locked.SingleOrDefault() ?? throw new RequestException(404, "Approval not found.");
        var member = await db.OrganizationUsers.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId, ct);
        if (member is null || !ApprovalRules.CanReview(member.Role, approval.ReviewerRole)
            || !await db.Organizations.AnyAsync(x => x.Id == organizationId && x.Status == OrganizationStatus.Active, ct))
            throw new RequestException(403, "Your current role cannot resolve this approval.");
        if (approval.Status != ApprovalStatus.Pending) throw new RequestException(409, "This approval has already been resolved.");
        var action = await db.AgentActions.SingleAsync(x => x.OrganizationId == organizationId && x.Id == approval.ActionId, ct);
        if (action.Decision != ActionDecision.Review || action.Status != ActionStatus.AwaitingApproval || action.TestEvaluation)
            throw new RequestException(409, "The action is not awaiting human approval.");
        var now = await DatabaseNow(ct);
        if (approval.ExpiresAt <= now)
        {
            Expire(approval, action, now);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            throw new RequestException(409, "This approval has expired. Submit a new action request if review is still needed.");
        }
        approval.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        approval.ResolvedAt = now; approval.ResolvedByUserId = userId; approval.ReviewerComment = comment; approval.UpdatedAt = now;
        action.Status = approve ? ActionStatus.Approved : ActionStatus.Rejected; action.UpdatedAt = now;
        db.ApprovalDecisions.Add(new() { OrganizationId = organizationId, ApprovalRequestId = id, ReviewerUserId = userId,
            Decision = approve ? ApprovalDecisionType.Approve : ApprovalDecisionType.Reject, Source = source, Comment = comment, CreatedAt = now });
        audit.Record(organizationId, approve ? "approval.approved" : "approval.rejected", source == ApprovalDecisionSource.Slack ? AuditActorType.Slack : AuditActorType.User,
            userId, new { source = source.ToString().ToLowerInvariant(), approval.ReviewerRole, status = approval.Status.ToString().ToLowerInvariant() }, action.AgentId, action.Id, id, now);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task ExpireDueAsync(Guid? organizationId, CancellationToken ct, Guid? actionId = null, Guid? approvalId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Bounded maintenance batches; tenant reads pass an org, the worker is the only global caller.
        var approvals = await db.ApprovalRequests.FromSqlInterpolated($"""
            SELECT * FROM "ApprovalRequests"
            WHERE "Status" = 'Pending' AND "ExpiresAt" <= clock_timestamp()
              AND (CAST({organizationId} AS uuid) IS NULL OR "OrganizationId" = CAST({organizationId} AS uuid))
              AND (CAST({actionId} AS uuid) IS NULL OR "ActionId" = CAST({actionId} AS uuid))
              AND (CAST({approvalId} AS uuid) IS NULL OR "Id" = CAST({approvalId} AS uuid))
            ORDER BY "ExpiresAt", "Id" LIMIT 100 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        if (approvals.Count > 0)
        {
            var now = await DatabaseNow(ct);
            foreach (var approval in approvals)
            {
                var action = await db.AgentActions.SingleAsync(x => x.OrganizationId == approval.OrganizationId && x.Id == approval.ActionId, ct);
                Expire(approval, action, now);
            }
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
    private void Expire(ApprovalRequest approval, AgentAction action, DateTimeOffset now)
    {
        audit.Record(approval.OrganizationId, "approval.expired", AuditActorType.System, null, new { approval.ExpiresAt }, action.AgentId, action.Id, approval.Id, now);
        approval.Status = ApprovalStatus.Expired; approval.ResolvedAt = now; approval.UpdatedAt = now;
        if (action.Status == ActionStatus.AwaitingApproval) { action.Status = ActionStatus.Cancelled; action.UpdatedAt = now; }
    }
    private Task<DateTimeOffset> DatabaseNow(CancellationToken ct) => db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    internal static ApprovalSummaryDto Summary(ApprovalRequest x, AgentAction action, string name)
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(action.ParametersJson);
        decimal? amount = action.ActionType == "refund" && parameters.TryGetProperty("amount", out var value) && value.TryGetDecimal(out var number) ? number : null;
        var currency = amount is not null && parameters.TryGetProperty("currency", out var code) ? code.GetString() : null;
        return new(x.Id, action.Id, action.AgentId, name, action.ActionType, new(action.ResourceType, action.ResourceId),
            x.Status.ToString().ToLowerInvariant(), x.ReviewerRole, action.RiskLevel?.ToString(), action.MatchedPolicyName,
            x.RequestedAt, x.ExpiresAt, x.ResolvedAt, amount, currency);
    }
}
