using System.Text.Json;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;
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
        var total = await query.CountAsync(ct);
        var sorted = ascending ? query.OrderBy(x => x.CreatedAt).ThenBy(x => x.EventOrder).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.EventOrder).ThenByDescending(x => x.Id);
        var rows = await sorted.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return new(rows.Select(Map).ToArray(), total, filter.Page, filter.PageSize);
    }
    public async Task<AuditEventDto?> GetAsync(Guid org, Guid id, CancellationToken ct)
    {
        var row = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id, ct);
        return row is null ? null : Map(row);
    }
    public Task<bool> ActionExistsAsync(Guid org, Guid id, CancellationToken ct) => db.AgentActions.AnyAsync(x => x.OrganizationId == org && x.Id == id, ct);
    public Task<Guid?> ApprovalActionAsync(Guid org, Guid id, CancellationToken ct) => db.ApprovalRequests.Where(x => x.OrganizationId == org && x.Id == id).Select(x => (Guid?)x.ActionId).SingleOrDefaultAsync(ct);
    private static AuditEventDto Map(AuditEvent row) => new(row.Id, row.OrganizationId, row.AgentId, row.ActionId, row.ApprovalRequestId,
        row.EventType, row.ActorType.ToString(), row.ActorId, JsonSerializer.Deserialize<JsonElement>(row.MetadataJson), row.IPAddress, row.CreatedAt);
}
