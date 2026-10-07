using AgentGate.Application.Accounts;
using AgentGate.Domain.Accounts;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgentGate.Infrastructure.Accounts;

public sealed class AccountStore(AgentGateDbContext db) : IAccountStore
{
    public Task<User?> FindUserAsync(string email, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
    public Task<User?> GetUserAsync(Guid id, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Organization?> GetOrganizationAsync(Guid id, CancellationToken ct) => db.Organizations.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<OrganizationUser?> GetMembershipAsync(Guid orgId, Guid userId, CancellationToken ct) => db.OrganizationUsers.SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.UserId == userId, ct);
    public Task<OrganizationUser?> GetMemberAsync(Guid orgId, Guid id, CancellationToken ct) => db.OrganizationUsers.SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.Id == id, ct);
    public async Task<IReadOnlyList<OrganizationDto>> GetOrganizationsAsync(Guid userId, CancellationToken ct) => await (
        from member in db.OrganizationUsers where member.UserId == userId
        join org in db.Organizations on member.OrganizationId equals org.Id
        where org.Status == OrganizationStatus.Active
        orderby org.CreatedAt
        select new OrganizationDto(org.Id, org.Name, org.Slug, org.Status.ToString(), member.Role.ToString())).ToListAsync(ct);
    public async Task<IReadOnlyList<MemberDto>> GetMembersAsync(Guid orgId, CancellationToken ct) => await (
        from member in db.OrganizationUsers where member.OrganizationId == orgId
        join user in db.Users on member.UserId equals user.Id
        orderby user.Name
        select new MemberDto(member.Id, user.Id, user.Email, user.Name, member.Role.ToString())).ToListAsync(ct);
    public Task<AuthSession?> GetSessionAsync(Guid id, CancellationToken ct) => db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<AuthSession?> FindSessionAsync(string hash, CancellationToken ct) => db.AuthSessions.AsNoTracking().SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new AccountException(409, "The account or membership already exists."); }
        catch (DbUpdateConcurrencyException) { throw new AccountException(409, "This record changed. Reload and retry."); }
    }
    public async Task<bool> RotateSessionAsync(Guid oldId, AuthSession next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var changed = await db.AuthSessions.Where(x => x.Id == oldId && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, now), ct);
        if (changed != 1) return false;
        db.AuthSessions.Add(next); await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    public async Task RevokeSessionAsync(Guid id, CancellationToken ct) => await db.AuthSessions.Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
    public async Task RecordFailureAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.FailedLoginAttempts, x => x.FailedLoginAttempts + 1)
            .SetProperty(x => x.LockedUntil, x => x.FailedLoginAttempts + 1 >= 5 ? now.AddMinutes(15) : x.LockedUntil), ct);
    }
}
