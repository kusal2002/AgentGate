using Microsoft.EntityFrameworkCore;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AgentGateDbContext(DbContextOptions<AgentGateDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentApiKey> AgentApiKeys => Set<AgentApiKey>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentGateDbContext).Assembly);
    }
}
