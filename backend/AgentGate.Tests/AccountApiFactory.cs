using AgentGate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Security.Cryptography;

namespace AgentGate.Tests;

public sealed class AccountApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string databaseName = "agentgate_tests_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    private string testConnection = "";
    private readonly string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Supply settings before the minimal API's service composition executes.
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AgentGate"] = testConnection, ["JWT_SECRET"] = secret,
            ["Auth:PermitLimit"] = "1000", ["AgentAuth:PermitLimit"] = "1000", ["Logging:LogLevel:Default"] = "None"
        }));
        return base.CreateHost(builder);
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AgentGate"] = testConnection,
            ["JWT_SECRET"] = secret,
            ["Auth:PermitLimit"] = "1000",
            ["AgentAuth:PermitLimit"] = "1000",
            ["Logging:LogLevel:Default"] = "None"
        }));
    }
    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("AGENTGATE_TEST_CONNECTION") ?? throw new InvalidOperationException("Set AGENTGATE_TEST_CONNECTION to a local PostgreSQL connection with permission to create an isolated test database.");
        var admin = new NpgsqlConnectionStringBuilder(source) { Database = "postgres" };
        adminConnection = admin.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        admin.Database = databaseName; testConnection = admin.ConnectionString;
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentGateDbContext>().Database.MigrateAsync();
    }
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (string.IsNullOrEmpty(adminConnection)) return;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        if (!databaseName.StartsWith("agentgate_tests_", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid test database name.");
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
    public HttpClient NewClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-AgentGate-Client", "dashboard");
        return client;
    }
}
