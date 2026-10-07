using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AgentGate.Application.Accounts;
using AgentGate.Infrastructure.Accounts;
using AgentGate.Application.Agents;
using AgentGate.Infrastructure.Agents;

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
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<AccountService>();
        services.AddScoped<IAgentStore, AgentStore>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IAgentKeyAuthenticator, AgentKeyAuthenticator>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
