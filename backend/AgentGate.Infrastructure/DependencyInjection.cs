using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AgentGate.Application.Accounts;
using AgentGate.Infrastructure.Accounts;
using AgentGate.Application.Agents;
using AgentGate.Infrastructure.Agents;
using AgentGate.Application.Actions;
using AgentGate.Infrastructure.Actions;
using AgentGate.Application.Policies;
using AgentGate.Infrastructure.Policies;
using AgentGate.Domain.Actions;

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
        services.AddScoped<IActionStore, ActionStore>();
        services.AddScoped<IActionService, ActionService>();
        services.AddScoped<IActionHistoryService, ActionHistoryService>();
        services.AddSingleton(provider =>
        {
            var policyConfiguration = provider.GetRequiredService<IConfiguration>();
            ActionDecision Default(string environment, string fallback)
            {
                var value = policyConfiguration[$"PolicyDefaults:{environment}"] ?? fallback;
                if (!Enum.TryParse<ActionDecision>(value, true, out var decision) || decision is not (ActionDecision.Review or ActionDecision.Deny) || int.TryParse(value, out _))
                    throw new InvalidOperationException($"PolicyDefaults:{environment} must be Review or Deny.");
                return decision;
            }
            return new PolicyDefaults(Default("Development", "Review"), Default("Staging", "Deny"), Default("Production", "Deny"));
        });
        services.AddScoped<IPolicyStore, PolicyStore>();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        services.AddScoped<IPolicyService, PolicyService>();
        services.AddScoped<IActionEvaluator, PolicyActionEvaluator>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
