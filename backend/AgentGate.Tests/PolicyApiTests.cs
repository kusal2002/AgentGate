using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentGate.Tests;

public sealed class PolicyApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private async Task<(HttpClient Owner, HttpClient Agent, Guid AgentId, JsonElement Session, string Email)> Setup(string environment = "Development")
    {
        var owner = factory.NewClient(); var email = $"policies-{Guid.NewGuid():N}@example.test";
        var registered = await owner.PostAsJsonAsync("/api/auth/register", new { email, password = "policy-tests-password-2026", name = "Policy tester", organizationName = "Policy workspace" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode); var session = await registered.Content.ReadFromJsonAsync<JsonElement>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment, version = "1.0.0" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var generated = await owner.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Policy key" });
        var agent = factory.NewClient(); agent.DefaultRequestHeaders.Authorization = new("Bearer", (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        return (owner, agent, id, session, email);
    }
    private static object Request(string name = "Refund rule", string decision = "review", bool enabled = true, uint? version = null) => new
    {
        name, description = "Rule under test", actionType = "refund", priority = 100, enabled,
        conditions = new[] { new { field = "parameters.amount", @operator = "greater_than", value = 100 } }, decision,
        reviewerRole = decision == "review" ? "Reviewer" : null, riskLevel = "Medium", version
    };
    private static object Action(decimal amount = 750, string key = "refund-1") => new
    {
        action = "refund", resource = new { type = "customer", id = "CUS-102" }, parameters = new { amount, currency = "USD" }, idempotencyKey = key
    };
    private static async Task<JsonElement> Create(HttpClient owner, object? request = null)
    {
        var reply = await owner.PostAsJsonAsync("/api/policies", request ?? Request()); Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        return await reply.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task PolicyLifecycleAndActionSnapshotPreserveOriginalDecisionOnRetry()
    {
        var (owner, agent, id, _, _) = await Setup(); var policy = await Create(owner); var policyId = policy.GetProperty("id").GetGuid();
        Assert.True(policy.GetProperty("version").GetUInt32() > 0);
        var evaluated = await agent.PostAsJsonAsync("/v1/actions/evaluate", Action()); Assert.Equal(HttpStatusCode.OK, evaluated.StatusCode);
        var result = await evaluated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("review", result.GetProperty("decision").GetString()); Assert.Equal("awaiting_approval", result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("testEvaluation").GetBoolean()); Assert.Equal(policyId, result.GetProperty("matchedPolicyId").GetGuid());
        Assert.Equal("Reviewer", result.GetProperty("reviewerRole").GetString()); Assert.Equal("Medium", result.GetProperty("riskLevel").GetString());
        var changed = await owner.PutAsJsonAsync($"/api/policies/{policyId}", Request("Changed rule", "deny", version: policy.GetProperty("version").GetUInt32())); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = await changed.Content.ReadFromJsonAsync<JsonElement>(); Assert.NotEqual(policy.GetProperty("version").GetUInt32(), updated.GetProperty("version").GetUInt32());
        var replay = await agent.PostAsJsonAsync("/v1/actions/evaluate", Action());
        Assert.Equal(result.ToString(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).ToString());
        var next = await agent.PostAsJsonAsync("/v1/actions/evaluate", Action(key: "new-request")); Assert.Equal("deny", (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{result.GetProperty("actionId").GetGuid()}");
        Assert.Equal("Refund rule", detail.GetProperty("matchedPolicyName").GetString()); Assert.Equal("Reviewer", detail.GetProperty("reviewerRole").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("executedAt").ValueKind);
        var disabled = await owner.PostAsJsonAsync($"/api/policies/{policyId}/status", new { enabled = false, version = updated.GetProperty("version").GetUInt32() }); Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var preview = await owner.PostAsJsonAsync("/api/policies/test", new { agentId = id, action = "refund", resource = new { type = "customer", id = "CUS-1" }, parameters = new { amount = 750, currency = "USD" } });
        var defaultResult = await preview.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("review", defaultResult.GetProperty("decision").GetString()); Assert.Equal(JsonValueKind.Null, defaultResult.GetProperty("matchedPolicyId").ValueKind);
        var disabledPolicy = await disabled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/policies/{policyId}/status", new { enabled = true, version = disabledPolicy.GetProperty("version").GetUInt32() })).StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ListOrderAndEvaluationAgreeOnEqualPriorityDenyPrecedence()
    {
        var (owner, agent, _, _, _) = await Setup();
        await Create(owner, Request("Allow", "allow"));
        await Create(owner, Request("Review", "review"));
        var deny = await Create(owner, Request("Deny", "deny"));
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/policies");
        Assert.Equal("deny", list[0].GetProperty("decision").GetString());
        Assert.Equal("review", list[1].GetProperty("decision").GetString());
        Assert.Equal("allow", list[2].GetProperty("decision").GetString());
        var result = await (await agent.PostAsJsonAsync("/v1/actions/evaluate", Action())).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(deny.GetProperty("id").GetGuid(), result.GetProperty("matchedPolicyId").GetGuid());
    }
    [Fact]
    public async Task ConcurrentEditsAndStaleStatusChangesReturnConflict()
    {
        var (owner, _, _, _, _) = await Setup(); var policy = await Create(owner); var id = policy.GetProperty("id").GetGuid(); var version = policy.GetProperty("version").GetUInt32();
        var replies = await Task.WhenAll(owner.PutAsJsonAsync($"/api/policies/{id}", Request("One", version: version)), owner.PutAsJsonAsync($"/api/policies/{id}", Request("Two", version: version)));
        Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.OK); Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/policies/{id}/status", new { enabled = false, version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/policies/{id}", Request())).StatusCode);
    }
    [Fact]
    public async Task CrossTenantPoliciesAndPreviewAgentIdsAreNotAccessible()
    {
        var (a, aAgent, aId, _, _) = await Setup(); var (b, bAgent, bId, _, _) = await Setup();
        var policy = await Create(a, Request(decision: "allow")); var id = policy.GetProperty("id").GetGuid(); var version = policy.GetProperty("version").GetUInt32();
        Assert.Empty((await b.GetFromJsonAsync<JsonElement>("/api/policies")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/policies/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/policies/{id}", Request(version: version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/policies/{id}/status", new { enabled = false, version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/policies/test", new { agentId = aId, action = "refund", resource = new { type = "customer", id = "CUS-1" }, parameters = new { amount = 750, currency = "USD" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.GetAsync("/api/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.PostAsJsonAsync("/api/policies", Request())).StatusCode);
        Assert.Equal("allow", (await (await aAgent.PostAsJsonAsync("/v1/actions/evaluate", Action())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
        Assert.Equal("review", (await (await bAgent.PostAsJsonAsync("/v1/actions/evaluate", Action())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
    }
    [Theory]
    [InlineData("Owner", true, true)]
    [InlineData("Admin", true, true)]
    [InlineData("Developer", false, true)]
    [InlineData("Reviewer", false, false)]
    [InlineData("Viewer", false, false)]
    public async Task RolePermissionsSeparateManagementTestingAndReadAccess(string role, bool manage, bool test)
    {
        var (owner, _, agentId, ownerSession, _) = await Setup(); var policy = await Create(owner); var id = policy.GetProperty("id").GetGuid();
        var client = owner;
        if (role != "Owner")
        {
            var (member, _, _, _, email) = await Setup();
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
            var switched = await member.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = ownerSession.GetProperty("organization").GetProperty("id").GetGuid() });
            member.DefaultRequestHeaders.Authorization = new("Bearer", (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); client = member;
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/policies/{id}")).StatusCode);
        Assert.Equal(manage ? HttpStatusCode.Created : HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/policies", Request())).StatusCode);
        Assert.Equal(manage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/policies/{id}", Request(version: policy.GetProperty("version").GetUInt32()))).StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/api/policies/{id}");
        Assert.Equal(manage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/policies/{id}/status", new { enabled = false, version = current.GetProperty("version").GetUInt32() })).StatusCode);
        Assert.Equal(test ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/policies/test", new { agentId, action = "refund", resource = new { type = "customer", id = "CUS-1" }, parameters = new { amount = 750, currency = "USD" } })).StatusCode);
        Assert.Equal(manage ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.PostAsync("/api/policies/seed-refund-demo", null)).StatusCode);
    }
    [Fact]
    public async Task ConcurrentDemoSeedingIsIdempotentAndDoesNotOverwriteEdits()
    {
        var (owner, _, agentId, _, _) = await Setup();
        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => owner.PostAsync("/api/policies/seed-refund-demo", null)));
        Assert.All(replies, reply => Assert.Equal(HttpStatusCode.OK, reply.StatusCode));
        var policies = await owner.GetFromJsonAsync<JsonElement>("/api/policies"); Assert.Equal(4, policies.GetArrayLength());
        var first = policies[0];
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/policies/{first.GetProperty("id").GetGuid()}/status", new { enabled = false, version = first.GetProperty("version").GetUInt32() })).StatusCode);
        await owner.PostAsync("/api/policies/seed-refund-demo", null);
        var preserved = await owner.GetFromJsonAsync<JsonElement>($"/api/policies/{first.GetProperty("id").GetGuid()}"); Assert.False(preserved.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Theory]
    [InlineData(50, "allow", "Low")]
    [InlineData(750, "review", "Medium")]
    [InlineData(2000, "review", "High")]
    [InlineData(15000, "deny", "Critical")]
    public async Task PreviewAndPersistedEvaluationAgreeForDemoRefunds(decimal amount, string decision, string risk)
    {
        var (owner, agent, agentId, _, _) = await Setup(); await owner.PostAsync("/api/policies/seed-refund-demo", null);
        var preview = await owner.PostAsJsonAsync("/api/policies/test", new { agentId, action = "refund", resource = new { type = "customer", id = "CUS-102" }, parameters = new { amount, currency = "USD" } });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode); var simulation = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(decision, simulation.GetProperty("decision").GetString()); Assert.Equal(risk, simulation.GetProperty("riskLevel").GetString());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
        var actual = await agent.PostAsJsonAsync("/v1/actions/evaluate", Action(amount)); var result = await actual.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(simulation.GetProperty("matchedPolicyId").GetGuid(), result.GetProperty("matchedPolicyId").GetGuid()); Assert.Equal(decision, result.GetProperty("decision").GetString());
    }
    [Fact]
    public async Task ProductionUsesRealPoliciesAndDevelopmentDemoSeedIsUnavailable()
    {
        var (owner, agent, _, _, _) = await Setup("Production"); await Create(owner, Request(decision: "allow"));
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }); client.DefaultRequestHeaders.Authorization = agent.DefaultRequestHeaders.Authorization;
        Assert.Equal("allow", (await (await client.PostAsJsonAsync("/v1/actions/evaluate", Action())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/policies/seed-refund-demo", null)).StatusCode);
    }
    [Fact]
    public async Task DefaultsCanBeConfiguredToReviewOrDenyWithoutImplicitAllow()
    {
        var (_, agent, _, _, _) = await Setup();
        using var deniedDefault = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyDefaults:Development"] = "Deny" })));
        var client = deniedDefault.CreateClient(); client.DefaultRequestHeaders.Authorization = agent.DefaultRequestHeaders.Authorization;
        Assert.Equal("deny", (await (await client.PostAsJsonAsync("/v1/actions/evaluate", Action())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
        using var badDefault = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyDefaults:Development"] = "Allow" })));
        Assert.Throws<InvalidOperationException>(() => badDefault.CreateClient());
    }
    [Fact]
    public async Task PolicyInputValidationRejectsUnsafeDefinitionsAndForgedContext()
    {
        var (owner, _, id, _, _) = await Setup();
        var baseRequest = JsonSerializer.SerializeToElement(Request());
        foreach (var (field, value) in new (string, object?)[] { ("name", " "), ("actionType", "INVALID"), ("priority", -1), ("decision", "0"), ("riskLevel", "wrong"), ("reviewerRole", "Developer"), ("conditions", new[] { new { field = "parameters.amount", @operator = ">", value = 100 } }), ("organizationId", Guid.NewGuid()) })
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(baseRequest.GetRawText())!; body[field] = JsonSerializer.SerializeToElement(value);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/policies", body)).StatusCode);
        }
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>("/api/policies")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/policies/test", new { agentId = id, action = "refund", resource = new { type = "customer", id = "CUS-1" }, parameters = new { amount = 750, currency = "USD" }, environment = "Production" })).StatusCode);
    }
}
