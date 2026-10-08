using System.Text.Json;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;
using AgentGate.Domain.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentGate.Infrastructure.Audit;

public sealed class AuditStore(AgentGateDbContext db) : IAuditStore
{
    public async Task<AuditPageDto> ListAsync(Guid org, AuditFilter filter, bool ascending, CancellationToken ct)
    {
        var query = db.AuditEvents.AsNoTracking().Where(x => x.OrganizationId == org);
        if (filter.AgentId is { } agent) query = query.Where(x => x.AgentId == agent);
        if (filter.ActionId is { } action) query = query.Where(x => x.ActionId == action);
        if (filter.ApprovalRequestId is { } approval) query = query.Where(x => x.ApprovalRequestId == approval);
        if (!string.IsNullOrEmpty(filter.EventType)) query = query.Where(x => x.EventType == filter.EventType);
        if (filter.ActorType is { } actor) { var parsed = Enum.Parse<AuditActorType>(actor); query = query.Where(x => x.ActorType == parsed); }
        if (filter.From is { } from) query = query.Where(x => x.CreatedAt >= from.ToUniversalTime());
        if (filter.To is { } to) query = query.Where(x => x.CreatedAt <= to.ToUniversalTime());
        var actions = db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == org);
        if (filter.ActionType is { } type) actions = actions.Where(x => x.ActionType == type);
        if (filter.Decision is { } decision) { var parsed = Enum.Parse<ActionDecision>(decision, true); actions = actions.Where(x => x.Decision == parsed); }
        if (filter.RiskLevel is { } risk) { var parsed = Enum.Parse<ActionRiskLevel>(risk, true); actions = actions.Where(x => x.RiskLevel == parsed); }
        if (filter.ReviewerId is { } reviewer) actions = actions.Where(x => x.Approval != null && x.Approval.ResolvedByUserId == reviewer);
        if (filter.ActionType != null || filter.Decision != null || filter.RiskLevel != null || filter.ReviewerId != null)
            query = query.Where(x => actions.Any(a => a.Id == x.ActionId));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim(); var isGuid = Guid.TryParse(search, out var id);
            query = query.Where(x => (isGuid && (x.Id == id || x.AgentId == id || x.ActionId == id || x.ApprovalRequestId == id))
                || db.AgentActions.Any(a => a.OrganizationId == org && a.Id == x.ActionId && a.ResourceType == "customer" && a.ResourceId == search));
        }
        var total = await query.CountAsync(ct);
        var sorted = ascending ? query.OrderBy(x => x.CreatedAt).ThenBy(x => x.EventOrder).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.EventOrder).ThenByDescending(x => x.Id);
        var rows = await sorted.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return new(await Enrich(rows, org, ct), total, filter.Page, filter.PageSize);
    }
    public async Task<AuditEventDto?> GetAsync(Guid org, Guid id, CancellationToken ct)
    {
        var row = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id, ct);
        return row is null ? null : (await Enrich([row], org, ct))[0];
    }
    public Task<bool> ActionExistsAsync(Guid org, Guid id, CancellationToken ct) => db.AgentActions.AnyAsync(x => x.OrganizationId == org && x.Id == id, ct);
    public Task<Guid?> ApprovalActionAsync(Guid org, Guid id, CancellationToken ct) => db.ApprovalRequests.Where(x => x.OrganizationId == org && x.Id == id).Select(x => (Guid?)x.ActionId).SingleOrDefaultAsync(ct);
    public async Task<AuditOptionsDto> OptionsAsync(Guid org, CancellationToken ct)
    {
        var types = await db.AgentActions.Where(x => x.OrganizationId == org).Select(x => x.ActionType).Distinct().OrderBy(x => x).ToArrayAsync(ct);
        var people = await db.ApprovalDecisions.Where(x => x.OrganizationId == org).Join(db.Users, x => x.ReviewerUserId, x => x.Id,
            (decision, user) => new { user.Id, user.Name }).Distinct().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        return new(types, people.Select(x => new AuditReviewerDto(x.Id, x.Name)).ToArray());
    }
    private sealed record ActionContext(Guid Id, string Type, string ResourceType, string ResourceId, ActionDecision Decision, ActionRiskLevel? Risk, Guid? ReviewerId);
    private async Task<AuditEventDto[]> Enrich(IReadOnlyList<AuditEvent> rows, Guid org, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        // Bounded page enrichment: no request payloads and no query per event.
        var actionIds = rows.Where(x => x.ActionId != null).Select(x => x.ActionId!.Value).Distinct().ToArray();
        var actions = await db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == org && actionIds.Contains(x.Id))
            .Select(x => new ActionContext(x.Id, x.ActionType, x.ResourceType, x.ResourceId, x.Decision, x.RiskLevel,
                x.Approval == null ? null : x.Approval.ResolvedByUserId)).ToDictionaryAsync(x => x.Id, ct);
        var agentIds = rows.Where(x => x.AgentId != null).Select(x => x.AgentId!.Value)
            .Concat(rows.Where(x => x.ActorType == AuditActorType.Agent && x.ActorId != null).Select(x => x.ActorId!.Value)).Distinct().ToArray();
        var agents = await db.Agents.Where(x => x.OrganizationId == org && agentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var userIds = actions.Values.Where(x => x.ReviewerId != null).Select(x => x.ReviewerId!.Value)
            .Concat(rows.Where(x => x.ActorType is AuditActorType.User or AuditActorType.Slack && x.ActorId != null).Select(x => x.ActorId!.Value)).Distinct().ToArray();
        var users = await db.Users.Where(x => userIds.Contains(x.Id) && (db.OrganizationUsers.Any(m => m.OrganizationId == org && m.UserId == x.Id)
            || db.ApprovalDecisions.Any(d => d.OrganizationId == org && d.ReviewerUserId == x.Id))).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var policyIds = rows.Where(x => x.ActorType == AuditActorType.Policy && x.ActorId != null).Select(x => x.ActorId!.Value).Distinct().ToArray();
        var policies = await db.Policies.Where(x => x.OrganizationId == org && policyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.Select(row =>
        {
            var action = row.ActionId is { } actionId ? actions.GetValueOrDefault(actionId) : null;
            var actorName = row.ActorId is { } actor ? row.ActorType switch {
                AuditActorType.Agent => agents.GetValueOrDefault(actor), AuditActorType.Policy => policies.GetValueOrDefault(actor),
                AuditActorType.User or AuditActorType.Slack => users.GetValueOrDefault(actor), _ => null } : null;
            return new AuditEventDto(row.Id, row.OrganizationId, row.AgentId, row.ActionId, row.ApprovalRequestId, row.EventType,
                row.ActorType.ToString(), row.ActorId, JsonSerializer.Deserialize<JsonElement>(row.MetadataJson), row.IPAddress, row.CreatedAt,
                row.AgentId is { } agent ? agents.GetValueOrDefault(agent) : null, action?.Type, action?.ResourceType, action?.ResourceId,
                action?.Decision.ToString().ToLowerInvariant(), action?.Risk?.ToString(), action?.ReviewerId,
                action?.ReviewerId is { } reviewer ? users.GetValueOrDefault(reviewer) : null, actorName);
        }).ToArray();
    }
}
