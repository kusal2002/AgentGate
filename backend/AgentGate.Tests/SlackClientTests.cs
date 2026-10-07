using System.Net;
using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
using AgentGate.Infrastructure.Slack;
namespace AgentGate.Tests;
public sealed class SlackClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private static SlackClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => new(new HttpClient(new Handler(send)), new("synthetic-token", "synthetic-secret", "ATEST", "TTEST", Guid.NewGuid()));
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    [Fact]
    public async Task PostAndUpdateOnlyContainSafeSummaryAndOpaqueButtons()
    {
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var summary = new ApprovalSummaryDto(id, Guid.NewGuid(), Guid.NewGuid(), "<@U123> Agent", "refund", new("customer", "CUS-102"), "pending", "Reviewer", "Medium", "Refund", now, now.AddHours(24), null, 750, "USD");
        var detail = new ApprovalDetailDto(summary, null!, false, null, "secret-comment", []); var calls = 0;
        var client = Client(async request =>
        {
            Assert.Equal("slack.com", request.RequestUri!.Host); Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("synthetic-token", request.Headers.Authorization.Parameter);
            var raw = await request.Content!.ReadAsStringAsync(); Assert.DoesNotContain("secret-comment", raw);
            var body = JsonSerializer.Deserialize<JsonElement>(raw); var blocks = body.GetProperty("blocks");
            Assert.Equal("plain_text", blocks[0].GetProperty("text").GetProperty("type").GetString());
            if (calls++ == 0)
            {
                Assert.EndsWith("chat.postMessage", request.RequestUri.AbsolutePath); Assert.Equal(2, blocks.GetArrayLength());
                foreach (var button in blocks[1].GetProperty("elements").EnumerateArray()) Assert.Equal(id.ToString("D"), button.GetProperty("value").GetString());
                Assert.True(body.TryGetProperty("client_msg_id", out _));
            }
            else { Assert.EndsWith("chat.update", request.RequestUri.AbsolutePath); Assert.Single(blocks.EnumerateArray()); Assert.Equal("123.456", body.GetProperty("ts").GetString()); }
            return Json(new { ok = true, ts = "123.456" });
        });
        Assert.Equal("123.456", await client.SendAsync("CTEST", null, detail, Guid.NewGuid(), default));
        await client.SendAsync("CTEST", "123.456", detail with { Approval = summary with { Status = "approved" } }, Guid.NewGuid(), default);
    }
    [Fact]
    public async Task RateLimitAndMalformedResponsesAreSanitized()
    {
        var client = Client(_ => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(120)); return Task.FromResult(response); });
        var error = await Assert.ThrowsAsync<SlackApiException>(() => client.VerifyWorkspaceAsync(default)); Assert.Equal(120, error.RetryAfterSeconds);
        var malformed = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("secret response: malformed") }));
        var bad = await Assert.ThrowsAsync<SlackApiException>(() => malformed.VerifyWorkspaceAsync(default)); Assert.DoesNotContain("secret response", bad.Message);
        var rejected = Client(_ => Task.FromResult(Json(new { ok = false, error = "sensitive-untrusted-error" })));
        var denied = await Assert.ThrowsAsync<RequestException>(() => rejected.VerifyWorkspaceAsync(default)); Assert.DoesNotContain("sensitive", denied.Message);
    }
    [Fact]
    public async Task ForeignWorkspaceTokenAndBotReviewerAreRejected()
    {
        var foreign = Client(_ => Task.FromResult(Json(new { ok = true, team_id = "TOTHER", bot_id = "BTEST" })));
        Assert.Equal(400, (await Assert.ThrowsAsync<RequestException>(() => foreign.VerifyWorkspaceAsync(default))).StatusCode);
        var bot = Client(_ => Task.FromResult(Json(new { ok = true, user = new { id = "UTEST", team_id = "TTEST", deleted = false, is_bot = true } })));
        Assert.Equal(400, (await Assert.ThrowsAsync<RequestException>(() => bot.VerifyReviewerAsync("UTEST", default))).StatusCode);
    }
    [Fact]
    public async Task PrivateFeedbackUsesFixedSlackApiWithStoredChannelAndUser()
    {
        var client = Client(async request =>
        {
            Assert.Equal("https://slack.com/api/chat.postEphemeral", request.RequestUri!.AbsoluteUri);
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync());
            Assert.Equal("CTEST", body.GetProperty("channel").GetString()); Assert.Equal("UTEST", body.GetProperty("user").GetString());
            Assert.Equal("Approval recorded.", body.GetProperty("text").GetString()); return Json(new { ok = true, message_ts = "123.456" });
        });
        await client.FeedbackAsync("CTEST", "UTEST", "Approval recorded.", default);
    }
}
