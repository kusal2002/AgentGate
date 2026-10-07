using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Domain.Accounts;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AgentGate.Tests;

public sealed class AgentApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private const string Password = "agent-tests-password-2026";
    private async Task<(HttpClient Client, JsonElement Session, string Email)> Register()
    {
        var client = factory.NewClient();
        var email = $"agents-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, name = "Agent Developer", organizationName = "Agent Test Organization" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        return (client, session, email);
    }
    private static async Task<JsonElement> Create(HttpClient client, string environment = "Development")
    {
        var response = await client.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment, version = "1.0.0", description = "Refund simulation" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> Generate(HttpClient client, Guid agentId, DateTimeOffset? expiresAt = null)
    {
        var response = await client.PostAsJsonAsync($"/api/agents/{agentId}/keys", new { name = "Development key", expiresAt });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private HttpClient AgentClient(string key)
    {
        var client = factory.NewClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", key); return client;
    }
    [Fact]
    public async Task AgentLifecycleStoresMetadataAndDisablesAuthentication()
    {
        var (client, _, _) = await Register();
        var agent = await Create(client); var id = agent.GetProperty("id").GetGuid();
        Assert.Equal("Active", agent.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, agent.GetProperty("lastActivityAt").ValueKind);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/agents"); Assert.Single(list.EnumerateArray());
        var get = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}"); Assert.Equal(id, get.GetProperty("id").GetGuid());
        var update = await client.PatchAsJsonAsync($"/api/agents/{id}", new { name = "RefundAgent v2", version = "2.0.0", description = "Updated", environment = "Production" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var edited = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Development", edited.GetProperty("environment").GetString());
        Assert.Equal("2.0.0", edited.GetProperty("version").GetString());
        Assert.Equal(agent.GetProperty("slug").GetString(), edited.GetProperty("slug").GetString());
        var generated = await Generate(client, id);
        var agentClient = AgentClient(generated.GetProperty("key").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await agentClient.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agentClient.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agentClient.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal("Active", (await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}")).GetProperty("status").GetString());
        await Generate(client, id);
    }
    [Theory]
    [InlineData("Development", "ag_test_")]
    [InlineData("Staging", "ag_test_")]
    [InlineData("Production", "ag_live_")]
    public async Task KeyIsReturnedOnlyOnceAndIdentityComesFromServer(string environment, string prefix)
    {
        var (client, session, _) = await Register();
        var agent = await Create(client, environment); var id = agent.GetProperty("id").GetGuid();
        var generated = await Generate(client, id);
        var raw = generated.GetProperty("key").GetString()!;
        Assert.StartsWith(prefix, raw);
        var keysResponse = await client.GetAsync($"/api/agents/{id}/keys");
        var keysText = await keysResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(raw, keysText); Assert.DoesNotContain("keyHash", keysText); Assert.DoesNotContain("\"key\":", keysText);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        var stored = await db.AgentApiKeys.SingleAsync(x => x.AgentId == id);
        Assert.NotEqual(raw, stored.KeyHash); Assert.Equal(64, stored.KeyHash.Length); Assert.Null(stored.LastUsedAt);
        var authenticated = AgentClient(raw); authenticated.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        var identity = await authenticated.GetFromJsonAsync<JsonElement>("/v1/agents/me");
        Assert.Equal(id, identity.GetProperty("agentId").GetGuid());
        Assert.Equal(session.GetProperty("organization").GetProperty("id").GetGuid(), identity.GetProperty("organizationId").GetGuid());
        Assert.Equal(environment, identity.GetProperty("environment").GetString());
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}"); Assert.NotEqual(JsonValueKind.Null, details.GetProperty("lastActivityAt").ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, (await authenticated.GetAsync("/api/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await authenticated.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/agents/me")).StatusCode);
    }
    [Fact]
    public async Task TenantIsolationProtectsAgentsAndKeys()
    {
        var (a, _, _) = await Register(); var (b, _, _) = await Register();
        var aId = (await Create(a)).GetProperty("id").GetGuid(); var bId = (await Create(b)).GetProperty("id").GetGuid();
        var keyId = (await Generate(a, aId)).GetProperty("apiKey").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/agents/{aId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/agents/{aId}/keys")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/agents/{aId}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/agents/{aId}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PatchAsJsonAsync($"/api/agents/{aId}", new { name = "Foreign", version = "1.0.0" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/agents/{aId}/keys", new { name = "Foreign" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/agents/{bId}/keys/{keyId}/revoke", null)).StatusCode);
        var list = await b.GetFromJsonAsync<JsonElement>("/api/agents"); Assert.Single(list.EnumerateArray()); Assert.Equal(bId, list[0].GetProperty("id").GetGuid());
    }
    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Developer", true)]
    [InlineData("Reviewer", false)]
    [InlineData("Viewer", false)]
    public async Task FiveRolesEnforceAgentAndKeyManagement(string role, bool canManage)
    {
        var (owner, ownerSession, _) = await Register(); var id = (await Create(owner)).GetProperty("id").GetGuid();
        var keyId = (await Generate(owner, id)).GetProperty("apiKey").GetProperty("id").GetGuid();
        var client = owner;
        if (role != "Owner")
        {
            var (member, _, email) = await Register();
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
            var switched = await member.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = ownerSession.GetProperty("organization").GetProperty("id").GetGuid() });
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            var session = await switched.Content.ReadFromJsonAsync<JsonElement>(); member.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString()); client = member;
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/agents/{id}/keys")).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/agents", new { name = "Agent", environment = "Development", version = "1" })).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/agents/{id}", new { name = "Edited", version = "2" })).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Key" })).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, (await client.PostAsync($"/api/agents/{id}/keys/{keyId}/revoke", null)).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, (await client.PostAsync($"/api/agents/{id}/disable", null)).StatusCode);
        Assert.Equal(canManage ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, (await client.PostAsync($"/api/agents/{id}/enable", null)).StatusCode);
    }
    [Fact]
    public async Task RevocationAndExpirationTakeEffectWithoutRestart()
    {
        var (client, _, _) = await Register(); var id = (await Create(client)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Expired", expiresAt = DateTimeOffset.UtcNow.AddDays(-1) })).StatusCode);
        var generated = await Generate(client, id, DateTimeOffset.UtcNow.AddHours(1)); var keyId = generated.GetProperty("apiKey").GetProperty("id").GetGuid();
        var agent = AgentClient(generated.GetProperty("key").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/keys/{keyId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agents/{id}/keys/{keyId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/v1/agents/me")).StatusCode);
        var another = await Generate(client, id); var anotherId = another.GetProperty("apiKey").GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.AgentApiKeys.Where(x => x.Id == anotherId).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentClient(another.GetProperty("key").GetString()!).GetAsync("/v1/agents/me")).StatusCode);
        await client.PostAsync($"/api/agents/{id}/disable", null);
        await client.PostAsync($"/api/agents/{id}/enable", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentClient(another.GetProperty("key").GetString()!).GetAsync("/v1/agents/me")).StatusCode);
    }
    [Theory]
    [InlineData("oneWeek")]
    [InlineData("oneMonth")]
    [InlineData("sixMonths")]
    [InlineData("never")]
    public async Task ExpiryPresetsUseServerTimeAndPersistTheResult(string preset)
    {
        var (client, _, _) = await Register(); var id = (await Create(client)).GetProperty("id").GetGuid();
        var before = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Preset key", expiryPreset = preset });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = DateTimeOffset.UtcNow;
        var key = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey");
        if (preset == "never") Assert.Equal(JsonValueKind.Null, key.GetProperty("expiresAt").ValueKind);
        else
        {
            DateTimeOffset Expected(DateTimeOffset time) => preset == "oneWeek" ? time.AddDays(7) : time.AddMonths(preset == "oneMonth" ? 1 : 6);
            Assert.InRange(key.GetProperty("expiresAt").GetDateTimeOffset(), Expected(before), Expected(after));
        }
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}/keys");
        if (preset == "never") Assert.Equal(JsonValueKind.Null, stored[0].GetProperty("expiresAt").ValueKind);
        else Assert.InRange((key.GetProperty("expiresAt").GetDateTimeOffset() - stored[0].GetProperty("expiresAt").GetDateTimeOffset()).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
    }
    [Fact]
    public async Task InvalidOrAmbiguousExpiryPresetIsRejected()
    {
        var (client, _, _) = await Register(); var id = (await Create(client)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Invalid", expiryPreset = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Ambiguous", expiryPreset = "never", expiresAt = DateTimeOffset.UtcNow.AddDays(1) })).StatusCode);
    }
    [Fact]
    public async Task MalformedTamperedUnknownKeysAndSuspensionFailClosed()
    {
        var (client, session, _) = await Register(); var id = (await Create(client)).GetProperty("id").GetGuid();
        var generated = await Generate(client, id); var raw = generated.GetProperty("key").GetString()!;
        foreach (var invalid in new[] { "short", "ag_test_" + new string('0', 64), raw[..^1] + (raw[^1] == '0' ? '1' : '0'), new string('a', 300) })
            Assert.Equal(HttpStatusCode.Unauthorized, (await AgentClient(invalid).GetAsync("/v1/agents/me")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        Assert.Null((await db.AgentApiKeys.SingleAsync(x => x.AgentId == id)).LastUsedAt);
        var orgId = session.GetProperty("organization").GetProperty("id").GetGuid();
        await db.Organizations.Where(x => x.Id == orgId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, OrganizationStatus.Suspended));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentClient(raw).GetAsync("/v1/agents/me")).StatusCode);
    }
    [Fact]
    public async Task InputsAreValidatedAndDevelopmentPrototypeRequiresDevelopmentAgent()
    {
        var (client, _, _) = await Register();
        foreach (var environment in new[] { "invalid", "0", "development" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/agents", new { name = "Agent", environment, version = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/agents", new { name = " ", environment = "Development", version = "1" })).StatusCode);
        var devId = (await Create(client)).GetProperty("id").GetGuid();
        var prodId = (await Create(client, "Production")).GetProperty("id").GetGuid();
        var request = new { action = "refund", parameters = new { amountMinor = 75000, currency = "USD" } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/actions/evaluate", request)).StatusCode);
        var dev = AgentClient((await Generate(client, devId)).GetProperty("key").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/v1/actions/evaluate", request)).StatusCode);
        var prod = AgentClient((await Generate(client, prodId)).GetProperty("key").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await prod.PostAsJsonAsync("/v1/actions/evaluate", request)).StatusCode);
    }
    [Fact]
    public async Task AgentRateLimitAppliesBeforeInvalidKeyAuthentication()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["AgentAuth:PermitLimit"] = "2" })));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/agents/me")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/v1/agents/me")).StatusCode);
    }
}
