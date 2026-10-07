using Microsoft.EntityFrameworkCore;
using AgentGate.Domain.Accounts;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AgentGateDbContext(DbContextOptions<AgentGateDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentGateDbContext).Assembly);
    }
}
