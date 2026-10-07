using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentGate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentGate.Tests;

public sealed class ActionApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private async Task<(HttpClient Owner, HttpClient Agent, Guid AgentId, JsonElement Session)> Setup(string environment = "Development")
    {
        var owner = factory.NewClient();
        var registered = await owner.PostAsJsonAsync("/api/auth/register", new { email = $"actions-{Guid.NewGuid():N}@example.test", password = "action-tests-password-2026", name = "Action tester", organizationName = "Actions workspace" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var session = await registered.Content.ReadFromJsonAsync<JsonElement>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var (agent, id) = await AddAgent(owner, environment);
        return (owner, agent, id, session);
    }
    private async Task<(HttpClient Client, Guid Id)> AddAgent(HttpClient owner, string environment = "Development")
    {
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment, version = "1.0.0" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var generated = await owner.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Test key" });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var client = factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        return (client, id);
    }
    private static object Request(string key = "refund-order-8821", decimal amount = 750) => new
    {
        action = "refund", resource = new { type = "customer", id = "CUS-102" }, parameters = new { amount, currency = "USD", reason = "duplicate payment" },
        context = new { customerTier = "business" }, idempotencyKey = key
    };
    private static async Task<JsonElement> Evaluate(HttpClient agent, object request)
    {
        var response = await agent.PostAsJsonAsync("/v1/actions/evaluate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task RequestPersistsAndCanBeReadByAgentAndDashboardAfterHostRestart()
    {
        var (owner, agent, agentId, session) = await Setup();
        var result = await Evaluate(agent, Request()); var id = result.GetProperty("actionId").GetGuid();
        Assert.Equal("allow", result.GetProperty("decision").GetString());
        Assert.Equal("approved", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("testEvaluation").GetBoolean());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}");
        Assert.Equal(agentId, detail.GetProperty("agentId").GetGuid());
        Assert.Equal(750, detail.GetProperty("parameters").GetProperty("amount").GetDecimal());
        Assert.Equal("business", detail.GetProperty("context").GetProperty("customerTier").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("executedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("riskLevel").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("matchedPolicyId").ValueKind);
        Assert.Contains("No policy", detail.GetProperty("reason").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
            var row = await db.AgentActions.SingleAsync(x => x.Id == id);
            Assert.Equal(session.GetProperty("organization").GetProperty("id").GetGuid(), row.OrganizationId);
            Assert.Equal(64, row.RequestHash.Length);
        }
        // A separate host/service provider reading the same DB proves persistence independent of request memory.
        using var restarted = factory.WithWebHostBuilder(_ => { });
        var fresh = restarted.CreateClient(); fresh.DefaultRequestHeaders.Authorization = agent.DefaultRequestHeaders.Authorization;
        var replay = await Evaluate(fresh, Request());
        Assert.Equal(id, replay.GetProperty("actionId").GetGuid());
        Assert.Equal(result.ToString(), (await fresh.GetFromJsonAsync<JsonElement>($"/v1/actions/{id}")).ToString());
    }
    [Fact]
    public async Task RetriesCanonicalizeObjectsAndNumbersAndRejectChangedPayload()
    {
        var (owner, agent, _, _) = await Setup();
        var result = await Evaluate(agent, Request());
        var reordered = """
            {"idempotencyKey":"refund-order-8821","context":{"customerTier":"business"},"parameters":{"reason":"duplicate payment","currency":"USD","amount":750.00},"resource":{"id":"CUS-102","type":"customer"},"action":"refund"}
            """;
        var response = await agent.PostAsync("/v1/actions/evaluate", new StringContent(reordered, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(result.GetProperty("actionId").GetGuid(), (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actionId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/v1/actions/evaluate", Request(amount: 751))).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/actions");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ConcurrentIdenticalRequestsCreateExactlyOneAction()
    {
        var (owner, agent, _, _) = await Setup();
        var replies = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => agent.PostAsJsonAsync("/v1/actions/evaluate", Request())));
        Assert.All(replies, reply => Assert.Equal(HttpStatusCode.OK, reply.StatusCode));
        var ids = await Task.WhenAll(replies.Select(async reply => (await reply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actionId").GetGuid()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ConcurrentDifferentPayloadsWithSameKeyHaveOneWinner()
    {
        var (owner, agent, _, _) = await Setup();
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => agent.PostAsJsonAsync("/v1/actions/evaluate", Request(amount: 750 + index))));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, replies.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task TenantAgentAndAuthenticationBoundariesAreEnforced()
    {
        var (aOwner, aAgent, aId, _) = await Setup(); var (bOwner, bAgent, _, _) = await Setup();
        var (otherAgent, otherId) = await AddAgent(aOwner);
        var first = (await Evaluate(aAgent, Request())).GetProperty("actionId").GetGuid();
        var second = (await Evaluate(otherAgent, Request())).GetProperty("actionId").GetGuid();
        var third = (await Evaluate(bAgent, Request())).GetProperty("actionId").GetGuid();
        Assert.NotEqual(first, second); Assert.NotEqual(first, third);
        Assert.Equal(HttpStatusCode.NotFound, (await bOwner.GetAsync($"/api/actions/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bAgent.GetAsync($"/v1/actions/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAgent.GetAsync($"/v1/actions/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aOwner.PostAsJsonAsync("/v1/actions/evaluate", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aOwner.GetAsync($"/v1/actions/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.GetAsync("/api/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.GetAsync($"/api/actions/{first}")).StatusCode);
        Assert.Equal(0, (await bOwner.GetFromJsonAsync<JsonElement>($"/api/actions?agentId={aId}")).GetProperty("total").GetInt32());
        var scoped = await aOwner.GetFromJsonAsync<JsonElement>($"/api/actions?agentId={otherId}"); Assert.Equal(1, scoped.GetProperty("total").GetInt32());
        Assert.Equal(second, scoped.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task NonDevelopmentAgentsPersistDeniedActions(string environment)
    {
        var (_, agent, _, _) = await Setup(environment);
        var result = await Evaluate(agent, Request());
        Assert.Equal("deny", result.GetProperty("decision").GetString());
        Assert.Equal("denied", result.GetProperty("status").GetString());
    }
    [Fact]
    public async Task ProductionHostNeverUsesTestAllowEvenForDevelopmentAgent()
    {
        var (_, agent, _, _) = await Setup();
        var testResult = await Evaluate(agent, Request("existing-test"));
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = agent.DefaultRequestHeaders.Authorization;
        Assert.Equal("deny", (await Evaluate(client, Request())).GetProperty("decision").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/actions/evaluate", Request("existing-test"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/actions/{testResult.GetProperty("actionId").GetGuid()}")).StatusCode);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":{\"amount\":750,\"currency\":\"USD\"}}")]
    [InlineData("{\"action\":\"refund\",\"resource\":null,\"parameters\":{},\"idempotencyKey\":\"key\"}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":[],\"idempotencyKey\":\"key\"}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":{\"amount\":-1,\"currency\":\"USD\"},\"idempotencyKey\":\"key\"}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":{\"amount\":750,\"currency\":\"usd\"},\"idempotencyKey\":\"key\"}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":{\"amount\":750,\"amount\":751,\"currency\":\"USD\"},\"idempotencyKey\":\"key\"}")]
    [InlineData("{\"action\":\"refund\",\"resource\":{\"type\":\"customer\",\"id\":\"CUS-1\"},\"parameters\":{\"amount\":750,\"currency\":\"USD\"},\"idempotencyKey\":\"key\",\"decision\":\"allow\"}")]
    [InlineData("{")]
    public async Task InvalidPayloadsReturn400AndDoNotPersist(string json)
    {
        var (owner, agent, _, _) = await Setup();
        var reply = await agent.PostAsync("/v1/actions/evaluate", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task OptionalContextAndPaginationWorkWithoutPayloadsInList()
    {
        var (owner, agent, agentId, _) = await Setup();
        var result = await Evaluate(agent, new { action = "send_email", resource = new { type = "customer", id = "CUS-1" }, parameters = new { subject = "Hello" }, idempotencyKey = "email-1" });
        var id = result.GetProperty("actionId").GetGuid();
        await Evaluate(agent, Request("refund-2"));
        var first = await owner.GetFromJsonAsync<JsonElement>("/api/actions?pageSize=1");
        var second = await owner.GetFromJsonAsync<JsonElement>("/api/actions?pageSize=1&page=2");
        Assert.Equal(2, first.GetProperty("total").GetInt32()); Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.False(first.GetProperty("items")[0].TryGetProperty("parameters", out _));
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}"); Assert.Empty(detail.GetProperty("context").EnumerateObject());
        Assert.Equal(agentId, detail.GetProperty("agentId").GetGuid());
        foreach (var query in new[] { "page=0", "pageSize=101", "pageSize=0", "page=1000001", "agentId=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/actions?{query}")).StatusCode);
    }
    [Theory]
    [InlineData("Admin")]
    [InlineData("Developer")]
    [InlineData("Reviewer")]
    [InlineData("Viewer")]
    public async Task AllOrganizationRolesCanReadHistory(string role)
    {
        var (owner, agent, _, ownerSession) = await Setup(); var result = await Evaluate(agent, Request());
        var member = factory.NewClient(); var email = $"history-{Guid.NewGuid():N}@example.test";
        var registered = await member.PostAsJsonAsync("/api/auth/register", new { email, password = "action-tests-password-2026", name = "History reader", organizationName = "Other workspace" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        member.DefaultRequestHeaders.Authorization = new("Bearer", (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
        var switched = await member.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = ownerSession.GetProperty("organization").GetProperty("id").GetGuid() });
        member.DefaultRequestHeaders.Authorization = new("Bearer", (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/actions/{result.GetProperty("actionId").GetGuid()}")).StatusCode);
    }
}
