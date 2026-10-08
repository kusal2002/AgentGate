using System.Data;
using AgentGate.Application.Approvals;
using AgentGate.Application.Dashboard;
using AgentGate.Application.Actions;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Agents;
using AgentGate.Domain.Approvals;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentGate.Infrastructure.Dashboard;

public sealed class DashboardStore(AgentGateDbContext db, IApprovalStore approvals) : IDashboardStore
{
    public async Task<DashboardOverviewDto> OverviewAsync(Guid org, CancellationToken ct)
    {
        await approvals.ExpireDueAsync(org, ct);
        // Counts and recent activity share a snapshot even while another request is resolving.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = await Now(ct);
        var stats = await Statistics(org, null, now, ct);
        var agents = db.Agents.AsNoTracking().Where(x => x.OrganizationId == org);
        var totalAgents = await agents.CountAsync(ct);
        var activeAgents = await agents.CountAsync(x => x.Status == AgentStatus.Active, ct);
        var enabledPolicies = await db.Policies.CountAsync(x => x.OrganizationId == org && x.Enabled, ct);
        var recent = await db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == org && !x.TestEvaluation)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(10)
            .Join(agents, x => x.AgentId, x => x.Id, (action, agent) => new { Action = action, AgentName = agent.Name })
            .Select(x => new { x.Action.Id, x.Action.AgentId, x.AgentName, x.Action.ActionType, x.Action.ResourceType, x.Action.ResourceId,
                x.Action.Decision, x.Action.Status, x.Action.RiskLevel, x.Action.CreatedAt,
                ApprovalId = x.Action.Approval == null ? (Guid?)null : x.Action.Approval.Id,
                ReviewerId = x.Action.Approval == null ? null : x.Action.Approval.ResolvedByUserId,
                ReviewerName = x.Action.Approval == null ? null : db.Users.Where(u => u.Id == x.Action.Approval.ResolvedByUserId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new(stats, totalAgents, activeAgents, enabledPolicies, recent.Select(x => new RecentActivityDto(x.Id, x.AgentId, x.AgentName,
            x.ActionType, new(x.ResourceType, x.ResourceId), x.Decision.ToString().ToLowerInvariant(), ActionService.StatusName(x.Status),
            x.RiskLevel?.ToString(), x.CreatedAt, x.ApprovalId, x.ReviewerId, x.ReviewerName)).ToArray(), now);
    }
    public async Task<ActionStatisticsDto?> AgentStatisticsAsync(Guid org, Guid agentId, CancellationToken ct)
    {
        if (!await db.Agents.AnyAsync(x => x.OrganizationId == org && x.Id == agentId, ct)) return null;
        await approvals.ExpireDueAsync(org, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var stats = await Statistics(org, agentId, await Now(ct), ct);
        await transaction.CommitAsync(ct);
        return stats;
    }
    private async Task<ActionStatisticsDto> Statistics(Guid org, Guid? agentId, DateTimeOffset now, CancellationToken ct)
    {
        var actions = db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == org && !x.TestEvaluation && (agentId == null || x.AgentId == agentId));
        var counts = await actions.GroupBy(_ => 1).Select(group => new {
            Total = group.Count(), Allowed = group.Count(x => x.Decision == ActionDecision.Allow), Reviews = group.Count(x => x.Decision == ActionDecision.Review),
            Denied = group.Count(x => x.Decision == ActionDecision.Deny), Approved = group.Count(x => x.Status == ActionStatus.Approved || x.Status == ActionStatus.Executed),
            Executed = group.Count(x => x.Status == ActionStatus.Executed) }).SingleOrDefaultAsync(ct);
        var requests = db.ApprovalRequests.AsNoTracking().Where(x => x.OrganizationId == org && actions.Any(a => a.Id == x.ActionId));
        var pending = await requests.CountAsync(x => x.Status == ApprovalStatus.Pending && x.ExpiresAt > now, ct);
        // Only actual human decisions contribute: expiration is not approval latency.
        var average = await requests.Where(x => (x.Status == ApprovalStatus.Approved || x.Status == ApprovalStatus.Rejected)
            && x.ResolvedByUserId != null && x.ResolvedAt != null && x.ResolvedAt >= x.RequestedAt)
            .Select(x => (double?)(x.ResolvedAt!.Value - x.RequestedAt).TotalSeconds).AverageAsync(ct);
        return new(counts?.Total ?? 0, counts?.Allowed ?? 0, counts?.Reviews ?? 0, counts?.Denied ?? 0,
            pending, average, counts?.Approved ?? 0, counts?.Executed ?? 0);
    }
    private Task<DateTimeOffset> Now(CancellationToken ct) => db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
}
