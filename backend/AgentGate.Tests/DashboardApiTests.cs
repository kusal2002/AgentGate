using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Domain.Actions;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentGate.Tests;

public sealed class DashboardApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private async Task<(HttpClient Owner, HttpClient Agent, Guid AgentId, JsonElement Session)> Setup()
    {
        var owner = factory.NewClient(); var response = await owner.PostAsJsonAsync("/api/auth/register", new { email = $"dashboard-{Guid.NewGuid():N}@example.test", password = "dashboard-tests-password-2026", name = "Dashboard tester", organizationName = "Dashboard workspace" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment = "Development", version = "1.0.0" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var key = await owner.PostAsJsonAsync($"/api/agents/{id}/keys", new { name = "Dashboard key" });
        var agent = factory.NewClient(); agent.DefaultRequestHeaders.Authorization = new("Bearer", (await key.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        return (owner, agent, id, session);
    }
    private static async Task<JsonElement> Evaluate(HttpClient agent, string key, decimal amount = 750, string customer = "CUS-DASH")
    {
        var response = await agent.PostAsJsonAsync("/v1/actions/evaluate", new { action = "refund", resource = new { type = "customer", id = customer },
            parameters = new { amount, currency = "USD", password = "synthetic-payload-secret" }, context = new { accessToken = "synthetic-context-secret" }, idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task EmptyWorkspaceUsesZeroCountsAndNullAverageRatherThanInventingActivity()
    {
        var (owner, _, id, _) = await Setup(); var response = await owner.GetAsync("/api/dashboard"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var overview = await response.Content.ReadFromJsonAsync<JsonElement>(); var stats = overview.GetProperty("statistics");
        Assert.Equal(0, stats.GetProperty("actionsEvaluated").GetInt32()); Assert.Equal(JsonValueKind.Null, stats.GetProperty("averageApprovalSeconds").ValueKind);
        Assert.Empty(overview.GetProperty("recentActivity").EnumerateArray()); Assert.Equal(1, overview.GetProperty("activeAgents").GetInt32());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/agents/{id}/statistics")).GetProperty("executedActions").GetInt32());
    }
    [Fact]
    public async Task CountsDecisionsAndCurrentOutcomesAndAveragesOnlyHumanResolutions()
    {
        var (owner, agent, agentId, _) = await Setup(); Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync("/api/policies/seed-refund-demo", null)).StatusCode);
        await Evaluate(agent, "allow", 50); await Evaluate(agent, "deny", 15000);
        var approved = await Evaluate(agent, "approved"); var rejected = await Evaluate(agent, "rejected"); var pending = await Evaluate(agent, "pending");
        var approvedId = approved.GetProperty("approvalId").GetGuid(); var rejectedId = rejected.GetProperty("approvalId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/approvals/{approvedId}/approve", new { comment = "synthetic-comment-secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/approvals/{rejectedId}/reject", new { comment = "reject" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.ApprovalRequests.Where(x => x.Id == approvedId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RequestedAt, x => x.ResolvedAt!.Value.AddSeconds(-120)));
        await db.ApprovalRequests.Where(x => x.Id == rejectedId).ExecuteUpdateAsync(set => set.SetProperty(x => x.RequestedAt, x => x.ResolvedAt!.Value.AddSeconds(-60)));
        var overview = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard"); var stats = overview.GetProperty("statistics");
        Assert.Equal(5, stats.GetProperty("actionsEvaluated").GetInt32()); Assert.Equal(1, stats.GetProperty("autoAllowed").GetInt32()); Assert.Equal(3, stats.GetProperty("humanReviews").GetInt32());
        Assert.Equal(1, stats.GetProperty("denied").GetInt32()); Assert.Equal(1, stats.GetProperty("pendingApprovals").GetInt32()); Assert.Equal(90, stats.GetProperty("averageApprovalSeconds").GetDouble(), 4);
        Assert.Equal(2, stats.GetProperty("approvedActions").GetInt32()); Assert.Equal(0, stats.GetProperty("executedActions").GetInt32()); Assert.DoesNotContain("synthetic-", overview.ToString());
        var item = overview.GetProperty("recentActivity").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == approved.GetProperty("actionId").GetGuid()); Assert.Equal("Dashboard tester", item.GetProperty("reviewerName").GetString());
        Assert.Equal(stats.ToString(), (await owner.GetFromJsonAsync<JsonElement>($"/api/agents/{agentId}/statistics")).ToString());
        var pendingId = pending.GetProperty("approvalId").GetGuid(); await db.ApprovalRequests.Where(x => x.Id == pendingId).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var fresh = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard"); Assert.Equal(0, fresh.GetProperty("statistics").GetProperty("pendingApprovals").GetInt32()); Assert.Equal(90, fresh.GetProperty("statistics").GetProperty("averageApprovalSeconds").GetDouble(), 4);
    }
    [Fact]
    public async Task LegacyTestResultsAndIdempotentRetriesDoNotInflateStatistics()
    {
        var (owner, agent, id, session) = await Setup(); var result = await Evaluate(agent, "real");
        await Evaluate(agent, "real");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        db.AgentActions.Add(new() { OrganizationId = session.GetProperty("organization").GetProperty("id").GetGuid(), AgentId = id,
            ActionType = "refund", ResourceType = "customer", ResourceId = "legacy", Decision = ActionDecision.Allow, Status = ActionStatus.Approved,
            TestEvaluation = true, IdempotencyKey = "legacy", RequestHash = new string('A', 64) }); await db.SaveChangesAsync();
        var overview = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard"); Assert.Equal(1, overview.GetProperty("statistics").GetProperty("actionsEvaluated").GetInt32()); Assert.Single(overview.GetProperty("recentActivity").EnumerateArray());
        Assert.Equal(result.GetProperty("actionId").GetGuid(), overview.GetProperty("recentActivity")[0].GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task TenantIsolationAppliesToOverviewAgentStatisticsAndFilterOptions()
    {
        var (owner, agent, agentId, session) = await Setup(); var result = await Evaluate(agent, "private", customer: "OTHER-CUSTOMER"); var id = result.GetProperty("actionId").GetGuid();
        var (other, _, otherAgentId, _) = await Setup(); var overview = await other.GetFromJsonAsync<JsonElement>($"/api/dashboard?organizationId={session.GetProperty("organization").GetProperty("id").GetGuid()}");
        Assert.Equal(0, overview.GetProperty("statistics").GetProperty("actionsEvaluated").GetInt32()); Assert.Empty(overview.GetProperty("recentActivity").EnumerateArray()); Assert.DoesNotContain("OTHER-CUSTOMER", overview.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/agents/{agentId}/statistics")).StatusCode); Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>($"/api/agents/{otherAgentId}/statistics")).GetProperty("actionsEvaluated").GetInt32());
        var options = await other.GetFromJsonAsync<JsonElement>("/api/audit/options"); Assert.Empty(options.GetProperty("actions").EnumerateArray());
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>($"/api/audit?search={id}")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/dashboard")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync($"/api/agents/{agentId}/statistics")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/audit/options")).StatusCode);
    }
    [Fact]
    public async Task ExpandedFiltersAndExactIdentifierSearchDoNotModifyAuditEvents()
    {
        var (owner, agent, agentId, session) = await Setup(); await owner.PostAsync("/api/policies/seed-refund-demo", null);
        var result = await Evaluate(agent, "review"); await Evaluate(agent, "allow", 50, "CUS-OTHER"); var id = result.GetProperty("actionId").GetGuid(); var approval = result.GetProperty("approvalId").GetGuid(); var reviewer = session.GetProperty("user").GetProperty("id").GetGuid();
        await owner.PostAsJsonAsync($"/api/approvals/{approval}/approve", new { comment = "approve" });
        var combined = await owner.GetFromJsonAsync<JsonElement>($"/api/audit?actionType=refund&decision=review&riskLevel=Medium&reviewerId={reviewer}"); Assert.Equal(5, combined.GetProperty("total").GetInt32());
        Assert.All(combined.GetProperty("items").EnumerateArray(), x => { Assert.Equal(id, x.GetProperty("actionId").GetGuid()); Assert.Equal("RefundAgent", x.GetProperty("agentName").GetString()); Assert.Equal("Dashboard tester", x.GetProperty("reviewerName").GetString()); });
        var eventId = combined.GetProperty("items")[0].GetProperty("id").GetGuid();
        foreach (var search in new[] { id.ToString(), approval.ToString(), "CUS-DASH" }) Assert.Equal(5, (await owner.GetFromJsonAsync<JsonElement>($"/api/audit?search={search}")).GetProperty("total").GetInt32());
        Assert.True((await owner.GetFromJsonAsync<JsonElement>($"/api/audit?search={agentId}")).GetProperty("total").GetInt32() > 5);
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>($"/api/audit?search={eventId}")).GetProperty("total").GetInt32());
        foreach (var search in new[] { "CUS", "%", "' OR 1=1 --" }) Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/audit?search={Uri.EscapeDataString(search)}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/audit?reviewerId={Guid.NewGuid()}")).GetProperty("total").GetInt32());
        var options = await owner.GetFromJsonAsync<JsonElement>("/api/audit/options"); Assert.Contains(options.GetProperty("reviewers").EnumerateArray(), x => x.GetProperty("id").GetGuid() == reviewer);
        foreach (var query in new[] { "decision=0", "decision=invalid", "riskLevel=0", "riskLevel=invalid", "actionType=BadAction", "search=" + new string('a', 201) }) Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/audit?{query}")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>(); var row = await db.AuditEvents.AsNoTracking().SingleAsync(x => x.Id == eventId);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/audit/{eventId}"); Assert.Equal(JsonSerializer.Serialize(JsonDocument.Parse(row.MetadataJson).RootElement), JsonSerializer.Serialize(detail.GetProperty("metadata")));
    }
    [Fact]
    public async Task RecentActivityIsBoundedOrderedAndAgentTotalsDoNotIncludeOtherAgents()
    {
        var (owner, agent, id, _) = await Setup();
        for (var i = 0; i < 12; i++) await Evaluate(agent, $"recent-{i}");
        var created = await owner.PostAsJsonAsync("/api/agents", new { name = "Unused agent", environment = "Development", version = "1.0.0" });
        var otherId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var overview = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard"); var rows = overview.GetProperty("recentActivity").EnumerateArray().ToArray(); Assert.Equal(10, rows.Length);
        var times = rows.Select(x => x.GetProperty("createdAt").GetDateTimeOffset()).ToArray(); Assert.Equal(times.OrderDescending(), times);
        Assert.Equal(12, (await owner.GetFromJsonAsync<JsonElement>($"/api/agents/{id}/statistics")).GetProperty("actionsEvaluated").GetInt32());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/agents/{otherId}/statistics")).GetProperty("actionsEvaluated").GetInt32());
    }
    [Fact]
    public async Task DueApprovalsAreNeverCountedAsPendingBeyondTheMaintenanceBatch()
    {
        var (owner, _, agentId, session) = await Setup(); var org = session.GetProperty("organization").GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>(); var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 201; i++)
        {
            var action = new AgentGate.Domain.Actions.AgentAction { OrganizationId = org, AgentId = agentId, ActionType = "refund", ResourceType = "customer", ResourceId = "due-batch",
                Decision = ActionDecision.Review, Status = ActionStatus.AwaitingApproval, IdempotencyKey = $"due-{i}", RequestHash = new string('A', 64) };
            action.Approval = new() { OrganizationId = org, ActionId = action.Id, ExpiresAt = now.AddDays(-1), RequestedAt = now.AddDays(-2) };
            db.AgentActions.Add(action);
        }
        await db.SaveChangesAsync(); var overview = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard"); var stats = overview.GetProperty("statistics");
        Assert.Equal(201, stats.GetProperty("humanReviews").GetInt32()); Assert.Equal(0, stats.GetProperty("pendingApprovals").GetInt32()); Assert.Equal(JsonValueKind.Null, stats.GetProperty("averageApprovalSeconds").ValueKind);
    }
    [Theory][InlineData("Viewer")][InlineData("Reviewer")][InlineData("Developer")]
    public async Task NonAdminMembersCanReadOrganizationDashboard(string role)
    {
        var (owner, agent, _, session) = await Setup(); await Evaluate(agent, "member-access");
        var reader = factory.NewClient(); var email = $"reader-{Guid.NewGuid():N}@example.test";
        await reader.PostAsJsonAsync("/api/auth/register", new { email, password = "dashboard-reader-password", name = "Reader", organizationName = "Reader home" });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
        var login = await reader.PostAsJsonAsync("/api/auth/login", new { email, password = "dashboard-reader-password", organizationId = session.GetProperty("organization").GetProperty("id").GetGuid() });
        reader.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        Assert.Equal(1, (await reader.GetFromJsonAsync<JsonElement>("/api/dashboard")).GetProperty("statistics").GetProperty("actionsEvaluated").GetInt32());
    }
}
