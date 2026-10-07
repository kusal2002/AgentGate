using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Domain.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgentGate.Infrastructure.Actions;

public sealed class ActionStore(AgentGateDbContext db) : IActionStore
{
    public Task<AgentAction?> FindAsync(Guid organizationId, Guid agentId, string idempotencyKey, CancellationToken ct) => db.AgentActions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.AgentId == agentId && x.IdempotencyKey == idempotencyKey, ct);
    public async Task<AgentAction> CreateOrGetAsync(AgentAction action, CancellationToken ct)
    {
        db.AgentActions.Add(action);
        try { await db.SaveChangesAsync(ct); return action; }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_AgentActions_OrganizationId_AgentId_IdempotencyKey" })
        {
            db.Entry(action).State = EntityState.Detached;
            return await FindAsync(action.OrganizationId, action.AgentId, action.IdempotencyKey, ct) ?? throw new InvalidOperationException("Concurrent action could not be retrieved.");
        }
    }
    public Task<AgentAction?> GetAgentActionAsync(Guid organizationId, Guid agentId, Guid id, CancellationToken ct) => db.AgentActions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId && x.AgentId == agentId, ct);
    public async Task<ActionPageDto> ListAsync(Guid organizationId, Guid? agentId, int page, int pageSize, CancellationToken ct)
    {
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
        var row = await db.AgentActions.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Id == id)
            .Join(db.Agents.Where(x => x.OrganizationId == organizationId), x => x.AgentId, agent => agent.Id, (action, agent) => new { Action = action, agent.Name }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var x = row.Action;
        return new(x.Id, x.AgentId, row.Name, x.ActionType, new(x.ResourceType, x.ResourceId), x.Decision.ToString().ToLowerInvariant(),
            ActionService.StatusName(x.Status), x.RiskLevel?.ToString(), x.CreatedAt, x.UpdatedAt, x.ExecutedAt, x.MatchedPolicyId,
            x.IdempotencyKey, x.Reason, JsonSerializer.Deserialize<JsonElement>(x.ParametersJson), JsonSerializer.Deserialize<JsonElement>(x.ContextJson), x.TestEvaluation);
    }
}
