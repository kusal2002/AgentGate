using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Domain.Approvals;
using AgentGate.Domain.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using AgentGate.Application.Approvals;

namespace AgentGate.Tests;

public sealed class ApprovalApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private async Task<(HttpClient Owner, HttpClient Agent, Guid AgentId, JsonElement Session, string Email)> Setup()
    {
        var owner = factory.NewClient(); var email = $"approvals-{Guid.NewGuid():N}@example.test";
        var registered = await owner.PostAsJsonAsync("/api/auth/register", new { email, password = "approval-tests-password-2026", name = "Approval tester", organizationName = "Approval workspace" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode); var session = await registered.Content.ReadFromJsonAsync<JsonElement>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment = "Development", version = "1.0.0" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var generated = await owner.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Approval key" });
        var agent = factory.NewClient(); agent.DefaultRequestHeaders.Authorization = new("Bearer", (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        return (owner, agent, id, session, email);
    }
    private static object Action(string key = "approval-refund", decimal amount = 750) => new { action = "refund", resource = new { type = "customer", id = "CUS-102" }, parameters = new { amount, currency = "USD" }, idempotencyKey = key };
    private static async Task<JsonElement> Evaluate(HttpClient agent, string key = "approval-refund", decimal amount = 750)
    {
        var reply = await agent.PostAsJsonAsync("/v1/actions/evaluate", Action(key, amount)); Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        return await reply.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<HttpClient> Member(HttpClient owner, JsonElement ownerSession, string role)
    {
        var (member, _, _, _, email) = await Setup();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
        var switched = await member.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = ownerSession.GetProperty("organization").GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode); member.DefaultRequestHeaders.Authorization = new("Bearer", (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()); return member;
    }
    [Theory]
    [InlineData("approve", "approved", "api")]
    [InlineData("reject", "rejected", "api")]
    [InlineData("approve", "approved", "v1")]
    [InlineData("reject", "rejected", "v1")]
    public async Task HumanDecisionUpdatesActionAndAgentPollingWithOneAppendOnlyRecord(string decision, string status, string prefix)
    {
        var (owner, agent, _, session, _) = await Setup(); var result = await Evaluate(agent); var id = result.GetProperty("approvalId").GetGuid(); var actionId = result.GetProperty("actionId").GetGuid();
        Assert.Equal("pending", result.GetProperty("approvalStatus").GetString());
        var pending = await owner.GetFromJsonAsync<JsonElement>($"/api/approvals/{id}"); Assert.True(pending.GetProperty("canReview").GetBoolean());
        Assert.Equal(id, pending.GetProperty("action").GetProperty("approvalId").GetGuid());
        var reply = await owner.PostAsJsonAsync($"/{prefix}/approvals/{id}/{decision}", new { comment = "  Verified the request.  " }); Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var detail = await reply.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(status, detail.GetProperty("approval").GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean()); Assert.Equal("Verified the request.", detail.GetProperty("reviewerComment").GetString());
        Assert.Single(detail.GetProperty("decisions").EnumerateArray()); Assert.Equal(session.GetProperty("user").GetProperty("id").GetGuid(), detail.GetProperty("resolvedByUserId").GetGuid());
        var poll = await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}"); Assert.Equal(status, poll.GetProperty("status").GetString()); Assert.Equal(status, poll.GetProperty("actionStatus").GetString());
        var replay = await Evaluate(agent); Assert.Equal(actionId, replay.GetProperty("actionId").GetGuid()); Assert.Equal(status, replay.GetProperty("status").GetString()); Assert.Equal(id, replay.GetProperty("approvalId").GetGuid()); Assert.Equal("review", replay.GetProperty("decision").GetString());
        var action = await owner.GetFromJsonAsync<JsonElement>($"/api/actions/{actionId}"); Assert.Equal(JsonValueKind.Null, action.GetProperty("executedAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = "Again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/approvals/{id}/reject", new { comment = "Again" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        Assert.Equal(1, await db.ApprovalDecisions.CountAsync(x => x.ApprovalRequestId == id));
        var recorded = await db.ApprovalDecisions.SingleAsync(x => x.ApprovalRequestId == id); recorded.Comment = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var updateError = await Assert.ThrowsAsync<PostgresException>(() => db.ApprovalDecisions.Where(x => x.ApprovalRequestId == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Comment, "Tampered")));
        Assert.Equal("23514", updateError.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.ApprovalDecisions.Where(x => x.ApprovalRequestId == id).ExecuteDeleteAsync());
    }
    [Fact]
    public async Task ConcurrentRetriesCreateOneActionAndOneApproval()
    {
        var (owner, agent, _, _, _) = await Setup(); var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => agent.PostAsJsonAsync("/v1/actions/evaluate", Action())));
        Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var rows = await Task.WhenAll(replies.Select(x => x.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Single(rows.Select(x => x.GetProperty("approvalId").GetGuid()).Distinct());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/approvals")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task TwoReviewersAndMixedDecisionsHaveExactlyOneWinner()
    {
        var (owner, agent, _, session, _) = await Setup(); var reviewer = await Member(owner, session, "Reviewer");
        var id = (await Evaluate(agent)).GetProperty("approvalId").GetGuid();
        var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => (i % 2 == 0 ? owner : reviewer).PostAsJsonAsync($"/api/approvals/{id}/{(i % 3 == 0 ? "reject" : "approve")}", new { comment = $"Reviewer {i}" })));
        Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.OK); Assert.Equal(11, replies.Count(reply => reply.StatusCode == HttpStatusCode.Conflict));
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/approvals/{id}"); Assert.Single(detail.GetProperty("decisions").EnumerateArray());
        Assert.Equal(detail.GetProperty("approval").GetProperty("status").GetString(), detail.GetProperty("action").GetProperty("status").GetString());
    }
    [Theory]
    [InlineData("Owner", "Owner", true)]
    [InlineData("Admin", "Owner", false)]
    [InlineData("Admin", "Admin", true)]
    [InlineData("Reviewer", "Admin", false)]
    [InlineData("Reviewer", "Reviewer", true)]
    [InlineData("Developer", "Reviewer", false)]
    [InlineData("Viewer", "Reviewer", false)]
    public async Task RolesCanReadButOnlyEligibleReviewersCanResolve(string role, string required, bool allowed)
    {
        var (owner, agent, _, session, _) = await Setup();
        var policy = await owner.PostAsJsonAsync("/api/policies", new { name = "Review rule", description = "", actionType = "refund", priority = 100, enabled = true, conditions = Array.Empty<object>(), decision = "review", reviewerRole = required, riskLevel = "Medium" }); Assert.Equal(HttpStatusCode.Created, policy.StatusCode);
        var id = (await Evaluate(agent)).GetProperty("approvalId").GetGuid(); var member = role == "Owner" ? owner : await Member(owner, session, role);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/approvals")).StatusCode);
        var detail = await member.GetFromJsonAsync<JsonElement>($"/api/approvals/{id}"); Assert.Equal(allowed, detail.GetProperty("canReview").GetBoolean());
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = "Reviewed" })).StatusCode);
    }
    [Fact]
    public async Task TenantAgentAndAuthenticationBoundariesProtectApprovals()
    {
        var (aOwner, aAgent, _, _, _) = await Setup(); var (bOwner, bAgent, _, _, _) = await Setup();
        var id = (await Evaluate(aAgent)).GetProperty("approvalId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await bOwner.GetAsync($"/api/approvals/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bOwner.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = "Foreign" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bAgent.GetAsync($"/v1/approvals/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.GetAsync("/api/approvals")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aAgent.PostAsJsonAsync($"/v1/approvals/{id}/approve", new { comment = "Agent" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aOwner.GetAsync($"/v1/approvals/{id}")).StatusCode);
        var second = await aOwner.PostAsJsonAsync("/api/agents", new { name = "Other agent", environment = "Development", version = "1" });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var generated = await aOwner.PostAsJsonAsync($"/api/agents/{secondId}/keys", new { name = "Second" }); var other = factory.NewClient(); other.DefaultRequestHeaders.Authorization = new("Bearer", (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/v1/approvals/{id}")).StatusCode);
    }
    [Fact]
    public async Task ExpiryAndResolutionRaceCannotApproveAfterDeadline()
    {
        var (owner, agent, _, _, _) = await Setup(); var result = await Evaluate(agent); var id = result.GetProperty("approvalId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.ApprovalRequests.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var replies = await Task.WhenAll(owner.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = "Late" }), owner.PostAsJsonAsync($"/api/approvals/{id}/reject", new { comment = "Late" }), owner.GetAsync($"/api/approvals/{id}"));
        Assert.Equal(HttpStatusCode.Conflict, replies[0].StatusCode); Assert.Equal(HttpStatusCode.Conflict, replies[1].StatusCode); Assert.Equal(HttpStatusCode.OK, replies[2].StatusCode);
        var poll = await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}"); Assert.Equal("expired", poll.GetProperty("status").GetString()); Assert.Equal("cancelled", poll.GetProperty("actionStatus").GetString());
        var replay = await Evaluate(agent); Assert.Equal("cancelled", replay.GetProperty("status").GetString()); Assert.Equal("expired", replay.GetProperty("approvalStatus").GetString());
        Assert.Equal(0, await db.ApprovalDecisions.CountAsync(x => x.ApprovalRequestId == id));
    }
    [Fact]
    public async Task ListsPaginateAndReadRefreshExpiresPendingRequests()
    {
        var (owner, agent, _, _, _) = await Setup(); var first = await Evaluate(agent, "one"); await Evaluate(agent, "two");
        var page = await owner.GetFromJsonAsync<JsonElement>("/api/approvals?status=pending&pageSize=1"); Assert.Equal(2, page.GetProperty("total").GetInt32()); Assert.Single(page.GetProperty("items").EnumerateArray());
        var next = await owner.GetFromJsonAsync<JsonElement>("/api/approvals?status=pending&pageSize=1&page=2"); Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetGuid(), next.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>(); var id = first.GetProperty("approvalId").GetGuid();
        await db.ApprovalRequests.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/approvals?status=expired")).GetProperty("total").GetInt32());
        foreach (var query in new[] { "status=invalid", "status=0", "page=0", "pageSize=101" }) Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/approvals?{query}")).StatusCode);
    }
    [Fact]
    public async Task DeniedAndAllowedActionsNeverCreateApprovals()
    {
        var (owner, agent, _, _, _) = await Setup(); await owner.PostAsync("/api/policies/seed-refund-demo", null);
        Assert.Equal(JsonValueKind.Null, (await Evaluate(agent, "allow", 50)).GetProperty("approvalId").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Evaluate(agent, "deny", 15000)).GetProperty("approvalId").ValueKind);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/approvals")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ApprovalAndPollingSurviveANewHostAndTimeoutIsConfigurable()
    {
        var (_, agent, _, _, _) = await Setup();
        using var custom = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Approvals:TimeoutMinutes"] = "60" })));
        var client = custom.CreateClient(); client.DefaultRequestHeaders.Authorization = agent.DefaultRequestHeaders.Authorization;
        var result = await Evaluate(client); var id = result.GetProperty("approvalId").GetGuid();
        var poll = await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}"); Assert.Equal(TimeSpan.FromHours(1), poll.GetProperty("expiresAt").GetDateTimeOffset() - poll.GetProperty("requestedAt").GetDateTimeOffset());
        Assert.Equal(id, (await Evaluate(agent)).GetProperty("approvalId").GetGuid());
    }
    [Fact]
    public async Task ActionAndApprovalCreationRollbackTogetherIfApprovalInsertFails()
    {
        var (owner, agent, _, _, _) = await Setup();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION phase6_test_block_approval() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic insert failure' USING ERRCODE = '23514'; END; $$;
            CREATE TRIGGER phase6_test_block_approval BEFORE INSERT ON "ApprovalRequests" FOR EACH ROW EXECUTE FUNCTION phase6_test_block_approval();
            """);
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await agent.PostAsJsonAsync("/v1/actions/evaluate", Action())).StatusCode);
            Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/actions")).GetProperty("total").GetInt32());
            Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/approvals")).GetProperty("total").GetInt32());
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER phase6_test_block_approval ON \"ApprovalRequests\"; DROP FUNCTION phase6_test_block_approval();"); }
        Assert.Equal("pending", (await Evaluate(agent)).GetProperty("approvalStatus").GetString());
    }
    [Fact]
    public async Task MigrationBackfillsExistingPhase5ReviewRequests()
    {
        var (_, agent, agentId, session, _) = await Setup();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261007104059_DeterministicPolicies");
        var action = new AgentAction { OrganizationId = session.GetProperty("organization").GetProperty("id").GetGuid(), AgentId = agentId,
            ActionType = "send_email", ResourceType = "customer", ResourceId = "old-request", Decision = ActionDecision.Review, Status = ActionStatus.AwaitingApproval,
            ReviewerRole = "Reviewer", IdempotencyKey = "phase5-backfill", RequestHash = new string('A', 64), CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        db.AgentActions.Add(action); await db.SaveChangesAsync();
        await migrator.MigrateAsync(); db.ChangeTracker.Clear();
        var approval = await db.ApprovalRequests.SingleAsync(x => x.ActionId == action.Id);
        Assert.Equal(ApprovalStatus.Pending, approval.Status); Assert.Equal(TimeSpan.FromHours(24), approval.ExpiresAt - approval.RequestedAt);
        Assert.True(approval.RequestedAt > action.CreatedAt);
        Assert.Equal("pending", (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{approval.Id}")).GetProperty("status").GetString());
    }
    [Fact]
    public async Task GlobalMaintenanceExpiresDueRequestsAcrossOrganizations()
    {
        var (_, a, _, _, _) = await Setup(); var (_, b, _, _, _) = await Setup();
        var ids = new[] { (await Evaluate(a)).GetProperty("approvalId").GetGuid(), (await Evaluate(b)).GetProperty("approvalId").GetGuid() };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.ApprovalRequests.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await scope.ServiceProvider.GetRequiredService<IApprovalStore>().ExpireDueAsync(null, default);
        Assert.Equal(2, await db.ApprovalRequests.CountAsync(x => ids.Contains(x.Id) && x.Status == ApprovalStatus.Expired));
        Assert.Equal(0, await db.ApprovalDecisions.CountAsync(x => ids.Contains(x.ApprovalRequestId)));
    }
    [Fact]
    public async Task InvalidCommentsAndForgedDecisionFieldsDoNotResolve()
    {
        var (owner, agent, _, _, _) = await Setup(); var id = (await Evaluate(agent)).GetProperty("approvalId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = new string('x', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/approvals/{id}/approve", new { comment = "Checked", reviewerUserId = Guid.NewGuid() })).StatusCode);
        Assert.Equal("pending", (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}")).GetProperty("status").GetString());
    }
}
