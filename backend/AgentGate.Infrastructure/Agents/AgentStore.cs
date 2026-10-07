using AgentGate.Application.Agents;
using AgentGate.Application.Errors;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgentGate.Infrastructure.Agents;

public sealed class AgentStore(AgentGateDbContext db) : IAgentStore
{
    public Task<Agent?> GetAsync(Guid organizationId, Guid agentId, CancellationToken ct) => db.Agents.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == agentId, ct);
    public async Task<IReadOnlyList<AgentDto>> ListAsync(Guid organizationId, CancellationToken ct) => await db.Agents.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId).OrderByDescending(x => x.CreatedAt)
        .Select(x => new AgentDto(x.Id, x.OrganizationId, x.Name, x.Slug, x.Description, x.Environment.ToString(), x.Status.ToString(), x.Version, x.CreatedAt, x.UpdatedAt,
            db.AgentApiKeys.Where(key => key.OrganizationId == organizationId && key.AgentId == x.Id).Max(key => key.LastUsedAt)))
        .ToListAsync(ct);
    public async Task<IReadOnlyList<AgentApiKey>> KeysAsync(Guid organizationId, Guid agentId, CancellationToken ct) => await db.AgentApiKeys.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.AgentId == agentId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task<AgentApiKey?> FindKeyAsync(string prefix, CancellationToken ct) => db.AgentApiKeys.AsNoTracking().SingleOrDefaultAsync(x => x.KeyPrefix == prefix, ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RequestException(409, "This agent changed. Reload and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new RequestException(409, "This agent or key already exists. Retry the request."); }
    }
    public async Task<bool> RevokeAsync(Guid organizationId, Guid agentId, Guid keyId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.AgentApiKeys.Where(x => x.Id == keyId && x.OrganizationId == organizationId && x.AgentId == agentId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, x => x.RevokedAt ?? now), ct) == 1;
    }
    public async Task<AgentIdentityDto?> RecordAuthenticatedUseAsync(AgentApiKey key, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Recheck live authorization state in the same statement that records use.
        // The earlier prefix/hash lookup never grants access by itself.
        var valid = db.AgentApiKeys.Where(x => x.Id == key.Id && x.KeyHash == key.KeyHash && x.OrganizationId == key.OrganizationId && x.AgentId == key.AgentId && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now)
            && db.Agents.Any(agent => agent.Id == x.AgentId && agent.OrganizationId == x.OrganizationId && agent.Environment == x.Environment && agent.Status == AgentStatus.Active)
            && db.Organizations.Any(org => org.Id == x.OrganizationId && org.Status == OrganizationStatus.Active));
        if (await valid.ExecuteUpdateAsync(set => set.SetProperty(x => x.LastUsedAt, now), ct) != 1) return null;
        var agent = await db.Agents.AsNoTracking().SingleAsync(x => x.Id == key.AgentId && x.OrganizationId == key.OrganizationId, ct);
        return new(agent.Id, agent.OrganizationId, key.Id, agent.Name, agent.Environment.ToString(), agent.Version);
    }
}
