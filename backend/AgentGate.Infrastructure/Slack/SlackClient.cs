using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
namespace AgentGate.Infrastructure.Slack;

public sealed class SlackClient(HttpClient http, SlackSettings settings) : ISlackClient
{
    private async Task<JsonElement> Call(string method, object? body, CancellationToken ct, bool get = false)
    {
        try { return await CallCore(method, body, ct, get); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        { ct.ThrowIfCancellationRequested(); throw new SlackApiException(); }
    }
    private async Task<JsonElement> CallCore(string method, object? body, CancellationToken ct, bool get)
    {
        using var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, "https://slack.com/api/" + method);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.BotToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new SlackApiException((int)Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60, 1, 3600));
        if (!response.IsSuccessStatusCode) throw new RequestException(502, "Slack is unavailable. Check app configuration and try again.");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!result.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new RequestException(502, "Slack rejected the request. Check bot scopes, workspace, channel membership, and credentials.");
        return result;
    }
    public async Task VerifyWorkspaceAsync(CancellationToken ct)
    {
        var result = await Call("auth.test", new { }, ct);
        if (result.GetProperty("team_id").GetString() != settings.TeamId || !result.TryGetProperty("bot_id", out _))
            throw new RequestException(400, "Bot token does not belong to the configured workspace.");
    }
    public async Task VerifyReviewerAsync(string userId, CancellationToken ct)
    {
        var result = await Call("users.info?user=" + Uri.EscapeDataString(userId), null, ct, get: true); var user = result.GetProperty("user");
        if (user.GetProperty("id").GetString() != userId || user.GetProperty("team_id").GetString() != settings.TeamId
            || user.GetProperty("deleted").GetBoolean() || user.GetProperty("is_bot").GetBoolean() || userId == "USLACKBOT")
            throw new RequestException(400, "Choose an active human member of the configured Slack workspace.");
    }
    public async Task<string> SendAsync(string channelId, string? messageTs, ApprovalDetailDto detail, Guid deliveryId, CancellationToken ct)
    {
        var a = detail.Approval;
        static string Clip(string value) => value.Length > 250 ? value[..250] + "…" : value;
        var text = $"AgentGate approval: {a.Status}\nAgent: {Clip(a.AgentName)}\nAction: {a.Action}\nResource: {a.Resource.Type} / {Clip(a.Resource.Id)}"
            + (a.Amount is null ? "" : $"\nAmount: {a.Amount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {a.Currency}")
            + $"\nRisk: {a.RiskLevel}\nReviewer: {a.ReviewerRole}\nExpires: {a.ExpiresAt:O}\nApproval: {a.Id}\nAgentGate has not executed this action.";
        var blocks = new List<object> { new { type = "section", text = new { type = "plain_text", text, emoji = false } } };
        if (a.Status == "pending") blocks.Add(new { type = "actions", elements = new object[] {
            new { type = "button", text = new { type = "plain_text", text = "Approve" }, style = "primary", action_id = "agentgate_approve", value = a.Id.ToString("D") },
            new { type = "button", text = new { type = "plain_text", text = "Reject" }, style = "danger", action_id = "agentgate_reject", value = a.Id.ToString("D") }
        } });
        var body = new Dictionary<string, object> { ["channel"] = channelId, ["text"] = $"AgentGate approval {a.Id}: {a.Status}", ["blocks"] = blocks,
            ["unfurl_links"] = false, ["unfurl_media"] = false, ["parse"] = "none" };
        if (messageTs is not null) body["ts"] = messageTs; else body["client_msg_id"] = deliveryId.ToString("D");
        var response = await Call(messageTs is null ? "chat.postMessage" : "chat.update", body, ct);
        var ts = response.GetProperty("ts").GetString();
        if (string.IsNullOrEmpty(ts) || ts.Length > 40) throw new RequestException(502, "Slack returned an invalid message reference.");
        return ts;
    }
    public async Task FeedbackAsync(string channelId, string userId, string text, CancellationToken ct) =>
        _ = await Call("chat.postEphemeral", new { channel = channelId, user = userId, text, parse = "none" }, ct);
}
