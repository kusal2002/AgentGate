using AgentGate.Application.Errors;
using AgentGate.Application.Policies;
using AgentGate.Domain.Policies;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgentGate.Infrastructure.Policies;

public sealed class PolicyStore(AgentGateDbContext db) : IPolicyStore
{
    public async Task<IReadOnlyList<Policy>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        var policies = await db.Policies.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToListAsync(ct);
        // Sort enum values in memory, matching evaluation rather than alphabetical DB strings.
        return policies.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Decision).ThenBy(x => x.Id).ToArray();
    }
    public Task<Policy?> GetAsync(Guid organizationId, Guid id, CancellationToken ct) => db.Policies.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
    public async Task<IReadOnlyList<Policy>> EnabledAsync(Guid organizationId, string actionType, CancellationToken ct) => await db.Policies.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.ActionType == actionType && x.Enabled).ToListAsync(ct);
    public void Add(Policy policy) => db.Policies.Add(policy);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RequestException(409, "This policy changed. Reload before saving."); }
    }
    public async Task SeedAsync(Guid organizationId, IReadOnlyList<Policy> policies, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.Policies.Where(x => x.OrganizationId == organizationId && x.SeedKey != null).Select(x => x.SeedKey).ToListAsync(ct);
        var missing = policies.Where(x => !existing.Contains(x.SeedKey)).ToArray();
        db.Policies.AddRange(missing);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_Policies_OrganizationId_SeedKey" })
        {
            await transaction.RollbackAsync(ct);
            foreach (var policy in missing) db.Entry(policy).State = EntityState.Detached;
            // Another seed request inserted the same complete set atomically.
        }
    }
}
