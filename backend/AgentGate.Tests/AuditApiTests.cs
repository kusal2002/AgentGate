using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Domain.Audit;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgentGate.Tests;

public sealed class AuditApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private async Task<(HttpClient Owner, HttpClient Agent, Guid AgentId, JsonElement Session)> Setup()
    {
        var owner = factory.NewClient(); var response = await owner.PostAsJsonAsync("/api/auth/register", new { email = $"audit-{Guid.NewGuid():N}@example.test", password = "audit-tests-password-2026", name = "Audit tester", organizationName = "Audit workspace" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(); owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment = "Development", version = "1.0.0" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var key = await owner.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Audit key" });
        var agent = factory.NewClient(); agent.DefaultRequestHeaders.Authorization = new("Bearer", (await key.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        return (owner, agent, id, session);
    }
    private static object Request(string key, decimal amount = 750) => new { action = "refund", resource = new { type = "customer", id = "CUS-102" },
        parameters = new { amount, currency = "USD", password = "synthetic-secret-parameter", reason = "audit test" }, context = new { accessToken = "synthetic-secret-context" }, idempotencyKey = key };
    private static async Task<JsonElement> Evaluate(HttpClient agent, string key, decimal amount = 750)
    {
        var response = await agent.PostAsJsonAsync("/v1/actions/evaluate", Request(key, amount)); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task TimelineTracksRequestPolicyApprovalAndHumanActorWithoutPayloadOrComment()
    {
        var (owner, agent, agentId, session) = await Setup(); var result = await Evaluate(agent, "timeline"); var id = result.GetProperty("actionId").GetGuid(); var approval = result.GetProperty("approvalId").GetGuid();
        var initial = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline");
        Assert.Equal(new[] { "agent.action_requested", "policy.evaluated", "action.review_required", "approval.created" }, initial.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("eventType").GetString()));
        Assert.Equal(agentId, initial.GetProperty("items")[0].GetProperty("actorId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/approvals/{approval}/approve", new { comment = "synthetic-secret-comment" })).StatusCode);
        var timeline = await owner.GetFromJsonAsync<JsonElement>($"/api/approvals/{approval}/timeline"); Assert.Equal(5, timeline.GetProperty("total").GetInt32());
        var last = timeline.GetProperty("items")[4]; Assert.Equal("approval.approved", last.GetProperty("eventType").GetString()); Assert.Equal("User", last.GetProperty("actorType").GetString()); Assert.Equal(session.GetProperty("user").GetProperty("id").GetGuid(), last.GetProperty("actorId").GetGuid());
        Assert.DoesNotContain("synthetic-secret", timeline.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/approvals/{approval}/reject", new { comment = "repeat" })).StatusCode);
        Assert.Equal(5, (await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ConcurrentRetriesProduceOneEventSetAndNoChangedPayloadEvents()
    {
        var (owner, agent, _, _) = await Setup(); var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => agent.PostAsJsonAsync("/v1/actions/evaluate", Request("concurrent"))));
        Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode)); var id = (await replies[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actionId").GetGuid();
        Assert.Equal(4, (await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/v1/actions/evaluate", Request("concurrent", 751))).StatusCode);
        Assert.Equal(4, (await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task TenantIsolationAuthenticationFiltersPaginationAndReadOnlyApi()
    {
        var (owner, agent, agentId, _) = await Setup(); var result = await Evaluate(agent, "filters"); var id = result.GetProperty("actionId").GetGuid(); var approval = result.GetProperty("approvalId").GetGuid();
        var filtered = await owner.GetAsync($"/api/audit?agentId={agentId}&actionId={id}&eventType=policy.evaluated&actorType=Policy&pageSize=1"); Assert.Equal(HttpStatusCode.OK, filtered.StatusCode); Assert.Contains("no-store", filtered.Headers.CacheControl!.ToString());
        var page = await filtered.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, page.GetProperty("total").GetInt32()); var eventId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        var (other, _, _, _) = await Setup(); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/audit/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/actions/{id}/timeline")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/approvals/{approval}/timeline")).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>($"/api/audit?actionId={id}")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/audit")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/audit")).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "actorType=0", "actorType=Unknown", "eventType=bad!", "from=2027-01-01&to=2026-01-01" }) Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/audit?{query}")).StatusCode);
        var first = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?actionId={id}&pageSize=1"); var second = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?actionId={id}&pageSize=1&page=2"); Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await owner.DeleteAsync($"/api/audit/{eventId}")).StatusCode);
    }
    [Fact]
    public async Task ExpirationAndConcurrentResolutionAreRecordedOnce()
    {
        var (owner, agent, _, _) = await Setup(); var result = await Evaluate(agent, "expire"); var id = result.GetProperty("actionId").GetGuid(); var approval = result.GetProperty("approvalId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.ApprovalRequests.Where(x => x.Id == approval).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await Task.WhenAll(owner.GetAsync($"/api/approvals/{approval}"), owner.PostAsJsonAsync($"/api/approvals/{approval}/approve", new { comment = "late" }));
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.ActionId == id && x.EventType == "approval.expired")); Assert.Equal(0, await db.AuditEvents.CountAsync(x => x.ActionId == id && x.EventType == "approval.approved"));
        var next = await Evaluate(agent, "resolve-race"); var nextId = next.GetProperty("approvalId").GetGuid();
        var replies = await Task.WhenAll(owner.PostAsJsonAsync($"/api/approvals/{nextId}/approve", new { comment = "a" }), owner.PostAsJsonAsync($"/api/approvals/{nextId}/reject", new { comment = "b" }));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK); Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.ApprovalRequestId == nextId && (x.EventType == "approval.approved" || x.EventType == "approval.rejected")));
    }
    [Fact]
    public async Task AppendOnlyGuardAndDatabaseTriggersPreventMutationAndTruncate()
    {
        var (owner, agent, _, _) = await Setup(); var id = (await Evaluate(agent, "immutable")).GetProperty("actionId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>(); var row = await db.AuditEvents.FirstAsync(x => x.ActionId == id); var originalType = row.EventType;
        row.EventType = "forged"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        foreach (var command in new[] { $"UPDATE \"AuditEvents\" SET \"EventType\" = 'forged' WHERE \"Id\" = '{row.Id}'", $"DELETE FROM \"AuditEvents\" WHERE \"Id\" = '{row.Id}'", "TRUNCATE \"AuditEvents\"" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(command))).SqlState);
        Assert.Equal(originalType, (await owner.GetFromJsonAsync<JsonElement>($"/api/audit/{row.Id}")).GetProperty("eventType").GetString());
    }
    [Fact]
    public async Task AuditInsertFailureRollsBackActionApprovalAndHumanDecision()
    {
        var (owner, agent, _, _) = await Setup(); var existing = await Evaluate(agent, "before-failure"); var approval = existing.GetProperty("approvalId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION phase8_block_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic audit failure' USING ERRCODE = '23514'; END; $$;
            CREATE TRIGGER phase8_block_audit BEFORE INSERT ON "AuditEvents" FOR EACH ROW EXECUTE FUNCTION phase8_block_audit();
            """);
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await agent.PostAsJsonAsync("/v1/actions/evaluate", Request("rollback"))).StatusCode);
            Assert.False(await db.AgentActions.AnyAsync(x => x.IdempotencyKey == "rollback"));
            Assert.Equal(HttpStatusCode.InternalServerError, (await owner.PostAsJsonAsync($"/api/approvals/{approval}/approve", new { comment = "rollback" })).StatusCode);
            Assert.False(await db.ApprovalDecisions.AnyAsync(x => x.ApprovalRequestId == approval));
            Assert.Equal(AgentGate.Domain.Approvals.ApprovalStatus.Pending, (await db.ApprovalRequests.AsNoTracking().SingleAsync(x => x.Id == approval)).Status);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER phase8_block_audit ON \"AuditEvents\"; DROP FUNCTION phase8_block_audit();"); }
    }
    [Fact]
    public async Task ManagementEventsExcludeKeysAndRepeatedRevocationIsIdempotent()
    {
        var (owner, _, agentId, session) = await Setup();
        var keys = await owner.GetFromJsonAsync<JsonElement>($"/api/agents/{agentId}/keys"); var keyId = keys[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/agents/{agentId}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/agents/{agentId}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/agents/{agentId}/keys/{keyId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/agents/{agentId}/keys/{keyId}/revoke", null)).StatusCode);
        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?agentId={agentId}"); var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("eventType").GetString() == "agent.disabled"); Assert.Contains(items, x => x.GetProperty("eventType").GetString() == "agent.enabled");
        Assert.Single(items, x => x.GetProperty("eventType").GetString() == "agent.key_revoked");
        Assert.All(items, x => { Assert.Equal("User", x.GetProperty("actorType").GetString()); Assert.Equal(session.GetProperty("user").GetProperty("id").GetGuid(), x.GetProperty("actorId").GetGuid()); });
        Assert.DoesNotContain("keyHash", page.ToString()); Assert.DoesNotContain("keyPrefix", page.ToString());
        await owner.PostAsync("/api/policies/seed-refund-demo", null);
        var policies = await owner.GetFromJsonAsync<JsonElement>("/api/audit?eventType=policy.created"); Assert.True(policies.GetProperty("total").GetInt32() > 0);
        await owner.PostAsync("/api/policies/seed-refund-demo", null); Assert.Equal(policies.GetProperty("total").GetInt32(), (await owner.GetFromJsonAsync<JsonElement>("/api/audit?eventType=policy.created")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task AllowAndDenyTimelinesHaveNoHumanApprovalEvents()
    {
        var (owner, agent, _, _) = await Setup(); await owner.PostAsync("/api/policies/seed-refund-demo", null);
        foreach (var entry in new[] { ("allow", 50m, "action.allowed"), ("deny", 15000m, "action.denied") })
        {
            var result = await Evaluate(agent, entry.Item1, entry.Item2); var id = result.GetProperty("actionId").GetGuid();
            var page = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline"); Assert.Equal(3, page.GetProperty("total").GetInt32()); Assert.Equal(entry.Item3, page.GetProperty("items")[2].GetProperty("eventType").GetString());
        }
    }
    [Fact]
    public async Task MigrationPreservesOldRequestsWithExplicitSnapshotAndSurvivesRestart()
    {
        var (owner, agent, _, _) = await Setup(); var id = (await Evaluate(agent, "historical")).GetProperty("actionId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261007145926_SlackInteractionFeedback");
        await migrator.MigrateAsync(); db.ChangeTracker.Clear();
        using var restarted = factory.WithWebHostBuilder(_ => { }); var client = restarted.CreateClient(); client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var timeline = await client.GetFromJsonAsync<JsonElement>($"/api/actions/{id}/timeline"); Assert.Equal(1, timeline.GetProperty("total").GetInt32());
        var snapshot = timeline.GetProperty("items")[0]; Assert.Equal("action.imported", snapshot.GetProperty("eventType").GetString()); Assert.True(snapshot.GetProperty("metadata").GetProperty("historicalSnapshot").GetBoolean());
    }
}
