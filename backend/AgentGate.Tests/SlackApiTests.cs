using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentGate.Api.Slack;
using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
using AgentGate.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace AgentGate.Tests;

public sealed class SlackApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private const string Secret = "synthetic-slack-signing-secret";
    private sealed class FakeSlack : ISlackClient
    {
        public int Sent; public bool Fail; public string? LastTs; public ApprovalDetailDto? Detail;
        public int FeedbackCount;
        public bool FeedbackFail;
        public Task FeedbackAsync(string channel, string user, string text, CancellationToken ct)
        { if (FeedbackFail) throw new SlackApiException(120); FeedbackCount++; return Task.CompletedTask; }
        public Task VerifyWorkspaceAsync(CancellationToken ct) => Task.CompletedTask;
        public Task VerifyReviewerAsync(string userId, CancellationToken ct) => userId == "UINVALID" ? Task.FromException(new RequestException(400, "Inactive Slack member.")) : Task.CompletedTask;
        public Task<string> SendAsync(string channel, string? ts, ApprovalDetailDto detail, Guid id, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("synthetic failure");
            Interlocked.Increment(ref Sent); LastTs = ts; Detail = detail; return Task.FromResult(ts ?? "123456.789");
        }
    }
    private async Task<(WebApplicationFactory<Program> Host, HttpClient Owner, HttpClient Agent, FakeSlack Slack, Guid Org, Guid User, JsonElement Session)> Setup()
    {
        var owner = factory.NewClient(); var register = await owner.PostAsJsonAsync("/api/auth/register", new { email = $"slack-{Guid.NewGuid():N}@example.test", password = "slack-tests-password-2026", name = "Slack tester", organizationName = "Slack workspace" });
        var session = await register.Content.ReadFromJsonAsync<JsonElement>(); var org = session.GetProperty("organization").GetProperty("id").GetGuid(); var user = session.GetProperty("user").GetProperty("id").GetGuid();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var agentResult = await owner.PostAsJsonAsync("/api/agents", new { name = "RefundAgent", environment = "Development", version = "1.0.0" });
        var agentId = (await agentResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var keyResult = await owner.PostAsJsonAsync($"/api/agents/{agentId}/keys", new { name = "Slack key" });
        var key = (await keyResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();
        var fake = new FakeSlack(); var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<SlackSettings>(); services.AddSingleton(new SlackSettings("synthetic-bot-token", Secret, "ATEST", "TTEST", org));
            services.RemoveAll<ISlackClient>(); services.AddSingleton<ISlackClient>(fake);
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SlackWorker)).ToArray()) services.Remove(descriptor);
        }));
        owner = host.CreateClient(); owner.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var agent = host.CreateClient(); agent.DefaultRequestHeaders.Authorization = new("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/integrations/slack/reviewers", new { userId = user, slackUserId = "UTEST" })).StatusCode);
        return (host, owner, agent, fake, org, user, session);
    }
    private static async Task<Guid> Evaluate(HttpClient agent)
    {
        var result = await agent.PostAsJsonAsync("/v1/actions/evaluate", new { action = "refund", resource = new { type = "customer", id = "CUS-102" }, parameters = new { amount = 750, currency = "USD" }, idempotencyKey = "slack-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approvalId").GetGuid();
    }
    private static Task Dispatch(WebApplicationFactory<Program> host) => Run(host, async services => await services.GetRequiredService<ISlackStore>().DispatchAsync(default));
    private static async Task Run(WebApplicationFactory<Program> host, Func<IServiceProvider, Task> action)
    { using var scope = host.Services.CreateScope(); await action(scope.ServiceProvider); }
    private static HttpRequestMessage Callback(Guid id, string action = "approve", string team = "TTEST", string app = "ATEST", string channel = "CTEST", string user = "UTEST", string messageTs = "123456.789", int age = 0, bool tamper = false)
    {
        var payload = JsonSerializer.Serialize(new { type = "block_actions", api_app_id = app, team = new { id = team }, user = new { id = user },
            container = new { type = "message", channel_id = channel, message_ts = messageTs }, actions = new[] { new { action_id = "agentgate_" + action, value = id.ToString("D") } }, response_url = "https://attacker.example.test/do-not-call" });
        var raw = "payload=" + Uri.EscapeDataString(payload); var ts = DateTimeOffset.UtcNow.AddSeconds(age).ToUnixTimeSeconds().ToString();
        var signature = "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"v0:{ts}:{raw}"))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/slack/actions") { Content = new StringContent(raw + (tamper ? "x" : ""), Encoding.UTF8, "application/x-www-form-urlencoded") };
        request.Headers.Add("X-Slack-Signature", signature); request.Headers.Add("X-Slack-Request-Timestamp", ts); return request;
    }
    [Theory] [InlineData("approve", "approved")] [InlineData("reject", "rejected")]
    public async Task SignedClicksResolveOnceAndMessageRefreshes(string decision, string status)
    {
        var (host, owner, agent, fake, org, user, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host); Assert.Equal(1, fake.Sent); Assert.Equal("pending", fake.Detail!.Approval.Status);
            using var slackCaller = host.CreateClient();
            var reply = await slackCaller.SendAsync(Callback(id, decision)); Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            Assert.Equal(status, (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}")).GetProperty("status").GetString());
            var replay = await owner.SendAsync(Callback(id, decision == "approve" ? "reject" : "approve")); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Contains("already", await replay.Content.ReadAsStringAsync());
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>(); Assert.Equal(1, await db.ApprovalDecisions.CountAsync(x => x.OrganizationId == org && x.ApprovalRequestId == id));
                var decisionRow = await db.ApprovalDecisions.SingleAsync(x => x.ApprovalRequestId == id);
                Assert.Equal(user, decisionRow.ReviewerUserId); Assert.Equal(AgentGate.Domain.Approvals.ApprovalDecisionSource.Slack, decisionRow.Source);
                await db.SlackDeliveries.Where(x => x.ApprovalId == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1))); });
            await Dispatch(host); Assert.Equal(2, fake.Sent); Assert.Equal("123456.789", fake.LastTs); Assert.Equal(status, fake.Detail!.Approval.Status);
        }
    }
    [Theory]
    [InlineData("signature")] [InlineData("stale")] [InlineData("future")] [InlineData("team")] [InlineData("app")]
    [InlineData("channel")] [InlineData("user")] [InlineData("message")] [InlineData("approval")]
    public async Task ForgedOrUnboundCallbacksLeaveApprovalPending(string invalid)
    {
        var (host, owner, agent, fake, _, _, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host);
            var reply = await owner.SendAsync(Callback(invalid == "approval" ? Guid.NewGuid() : id, team: invalid == "team" ? "TOTHER" : "TTEST", app: invalid == "app" ? "AOTHER" : "ATEST",
                channel: invalid == "channel" ? "COTHER" : "CTEST", user: invalid == "user" ? "UOTHER" : "UTEST", messageTs: invalid == "message" ? "000.111" : "123456.789",
                age: invalid == "stale" ? -301 : invalid == "future" ? 301 : 0, tamper: invalid == "signature"));
            Assert.Equal(invalid is "signature" or "stale" or "future" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, reply.StatusCode);
            Assert.Equal("pending", (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}")).GetProperty("status").GetString()); Assert.Equal(1, fake.Sent);
        }
    }
    [Fact]
    public async Task DurableRetryAndConcurrentDispatchProduceOneTrackedMessage()
    {
        var (host, _, agent, fake, _, _, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); fake.Fail = true; await Dispatch(host);
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>(); var row = await db.SlackDeliveries.SingleAsync(x => x.ApprovalId == id);
                Assert.Null(row.MessageTs); Assert.NotNull(row.LastError); Assert.Equal(1, row.Attempts); row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); });
            fake.Fail = false; await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Dispatch(host)));
            Assert.Equal(1, fake.Sent);
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>(); var row = await db.SlackDeliveries.SingleAsync(x => x.ApprovalId == id); Assert.NotNull(row.MessageTs); Assert.Null(row.LastError); });
        }
    }
    [Fact]
    public async Task RoleChangesDisabledIntegrationExpiryAndUnmappingPreventSlackResolution()
    {
        var (host, owner, agent, _, org, user, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host);
            await Run(host, async s => await s.GetRequiredService<AgentGateDbContext>().OrganizationUsers.Where(x => x.OrganizationId == org && x.UserId == user)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Role, AgentGate.Domain.Accounts.OrganizationRole.Viewer)));
            var forbidden = await owner.SendAsync(Callback(id)); Assert.Contains("role", await forbidden.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = false })).StatusCode);
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>(); await db.OrganizationUsers.Where(x => x.OrganizationId == org && x.UserId == user).ExecuteUpdateAsync(set => set.SetProperty(x => x.Role, AgentGate.Domain.Accounts.OrganizationRole.Owner)); });
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = false })).StatusCode);
            Assert.Contains("disabled", await (await owner.SendAsync(Callback(id))).Content.ReadAsStringAsync());
            await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = true });
            await owner.DeleteAsync($"/api/integrations/slack/reviewers/{user}"); Assert.Contains("mapped", await (await owner.SendAsync(Callback(id))).Content.ReadAsStringAsync());
            await owner.PutAsJsonAsync("/api/integrations/slack/reviewers", new { userId = user, slackUserId = "UTEST" });
            await Run(host, async s => await s.GetRequiredService<AgentGateDbContext>().ApprovalRequests.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
            Assert.Contains("expired", await (await owner.SendAsync(Callback(id))).Content.ReadAsStringAsync());
            Assert.Equal("expired", (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}")).GetProperty("status").GetString());
        }
    }
    [Fact]
    public async Task ManagementValidatesTenantMappingsAndAuthentication()
    {
        var (host, owner, agent, _, _, user, _) = await Setup(); using (host)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/integrations/slack")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "https://attacker.test", enabled = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/integrations/slack/reviewers", new { userId = Guid.NewGuid(), slackUserId = "UTEST" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/integrations/slack/reviewers", new { userId = user, slackUserId = "UINVALID" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = true, organizationId = Guid.NewGuid() })).StatusCode);
            var other = host.CreateClient(); other.DefaultRequestHeaders.Add("X-AgentGate-Client", "dashboard"); var registration = await other.PostAsJsonAsync("/api/auth/register", new { email = $"other-{Guid.NewGuid():N}@example.test", password = "slack-tests-password-2026", name = "Other", organizationName = "Other" });
            var session = await registration.Content.ReadFromJsonAsync<JsonElement>(); other.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
            Assert.False((await other.GetFromJsonAsync<JsonElement>("/api/integrations/slack")).GetProperty("available").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await other.PutAsJsonAsync("/api/integrations/slack", new { channelId = "CTEST", enabled = true })).StatusCode);
        }
    }
    [Fact]
    public async Task ConcurrentSlackAndDashboardDecisionsHaveOneWinner()
    {
        var (host, owner, agent, _, _, _, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host);
            var requests = Enumerable.Range(0, 8).Select(i => owner.SendAsync(Callback(id, i % 2 == 0 ? "approve" : "reject"))).ToList();
            var dashboard = owner.PostAsJsonAsync($"/api/approvals/{id}/reject", new { comment = "Dashboard reviewer" });
            var replies = await Task.WhenAll(requests); var webReply = await dashboard;
            Assert.All(replies, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Contains(webReply.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await Run(host, async s => Assert.Equal(1, await s.GetRequiredService<AgentGateDbContext>().ApprovalDecisions.CountAsync(x => x.ApprovalRequestId == id)));
            var outcome = await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}"); Assert.Contains(outcome.GetProperty("status").GetString(), new[] { "approved", "rejected" });
        }
    }
    [Fact]
    public async Task DashboardDecisionAndExpiryUpdateMessagesAndUnsentResolvedRequestsAreSkipped()
    {
        var (host, owner, agent, fake, _, _, _) = await Setup(); using (host)
        {
            var first = await Evaluate(agent); await Dispatch(host);
            await owner.PostAsJsonAsync($"/api/approvals/{first}/approve", new { comment = "Dashboard" });
            await Run(host, async s => await s.GetRequiredService<AgentGateDbContext>().SlackDeliveries.Where(x => x.ApprovalId == first)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
            await Dispatch(host); Assert.Equal("approved", fake.Detail!.Approval.Status);
            var expired = await Evaluate(agent); await Dispatch(host);
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>();
                await db.ApprovalRequests.Where(x => x.Id == expired).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
                await db.SlackDeliveries.Where(x => x.ApprovalId == expired).ExecuteUpdateAsync(set => set.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1))); });
            await Dispatch(host); Assert.Equal("expired", fake.Detail!.Approval.Status);
            var unsent = await Evaluate(agent); fake.Fail = true; await Dispatch(host); fake.Fail = false;
            await owner.PostAsJsonAsync($"/api/approvals/{unsent}/reject", new { comment = "Already handled" }); var sentBefore = fake.Sent;
            await Run(host, async s => await s.GetRequiredService<AgentGateDbContext>().SlackDeliveries.Where(x => x.ApprovalId == unsent)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
            await Dispatch(host); Assert.Equal(sentBefore, fake.Sent);
            await Run(host, async s => Assert.Null((await s.GetRequiredService<AgentGateDbContext>().SlackDeliveries.SingleAsync(x => x.ApprovalId == unsent)).LastError));
        }
    }
    [Fact]
    public async Task MissingSignatureMalformedAndOversizedBodiesCannotReachApprovalHandling()
    {
        var (host, owner, agent, _, _, _, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host);
            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsync("/api/integrations/slack/actions", new StringContent("payload={}", Encoding.UTF8, "application/x-www-form-urlencoded"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/integrations/slack/actions", new { payload = "{}" })).StatusCode);
            var oversized = Callback(id); oversized.Content = new StringContent(new string('x', 65537), Encoding.UTF8, "application/x-www-form-urlencoded");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await owner.SendAsync(oversized)).StatusCode);
            Assert.Equal("pending", (await agent.GetFromJsonAsync<JsonElement>($"/v1/approvals/{id}")).GetProperty("status").GetString());
        }
    }
    [Fact]
    public async Task PrivateFeedbackIsDurableDeduplicatedAndRetriesWithoutRepeatingDecision()
    {
        var (host, owner, agent, fake, _, _, _) = await Setup(); using (host)
        {
            var id = await Evaluate(agent); await Dispatch(host);
            var callback = Callback(id); var signature = callback.Headers.GetValues("X-Slack-Signature").Single();
            var timestamp = callback.Headers.GetValues("X-Slack-Request-Timestamp").Single();
            Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(callback)).StatusCode);
            var replay = Callback(id); replay.Headers.Remove("X-Slack-Signature"); replay.Headers.Add("X-Slack-Signature", signature);
            replay.Headers.Remove("X-Slack-Request-Timestamp"); replay.Headers.Add("X-Slack-Request-Timestamp", timestamp);
            Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(replay)).StatusCode);
            fake.FeedbackFail = true;
            await Run(host, async s => await s.GetRequiredService<ISlackStore>().DispatchFeedbackAsync(default));
            await Run(host, async s => { var db = s.GetRequiredService<AgentGateDbContext>(); var row = await db.SlackFeedback.SingleAsync(x => x.RequestKey == signature.Substring(3));
                Assert.Null(row.SentAt); Assert.True(row.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(110));
                Assert.Equal(1, await db.ApprovalDecisions.CountAsync(x => x.ApprovalRequestId == id)); row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); });
            fake.FeedbackFail = false;
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Run(host, async s => await s.GetRequiredService<ISlackStore>().DispatchFeedbackAsync(default))));
            Assert.Equal(1, fake.FeedbackCount);
            await Run(host, async s => Assert.NotNull((await s.GetRequiredService<AgentGateDbContext>().SlackFeedback.SingleAsync(x => x.RequestKey == signature.Substring(3))).SentAt));
        }
    }
}
