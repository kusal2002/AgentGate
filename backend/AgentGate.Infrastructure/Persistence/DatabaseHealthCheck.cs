using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentGate.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        return await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
            : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
    }
}
