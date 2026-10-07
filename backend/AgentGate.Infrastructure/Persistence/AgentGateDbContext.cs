using Microsoft.EntityFrameworkCore;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AgentGateDbContext(DbContextOptions<AgentGateDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Add entity configurations here with the first domain models in Phase 2.
    }
}
