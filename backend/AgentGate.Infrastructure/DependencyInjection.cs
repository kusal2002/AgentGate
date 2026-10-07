using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgentGate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentGate");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var postgres = new NpgsqlConnectionStringBuilder
            {
                Host = configuration["POSTGRES_HOST"] ?? "localhost",
                Port = int.Parse(configuration["POSTGRES_PORT"] ?? "5432"),
                Database = configuration["POSTGRES_DB"] ?? "agentgate",
                Username = configuration["POSTGRES_USER"] ?? "agentgate",
                Password = configuration["POSTGRES_PASSWORD"] ?? "",
                Timeout = 3,
                CommandTimeout = 5
            };
            connectionString = postgres.ConnectionString;
        }

        services.AddDbContext<AgentGateDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
