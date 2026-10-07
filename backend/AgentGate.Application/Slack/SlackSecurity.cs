using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentGate.Application.Errors;
namespace AgentGate.Application.Slack;
public static class SlackSecurity
{
    public static bool Verify(string secret, string timestamp, string signature, byte[] body, DateTimeOffset now)
    {
        if (secret.Length == 0 || timestamp.Length > 20 || !long.TryParse(timestamp, out var seconds)
            || seconds < now.ToUnixTimeSeconds() - 300 || seconds > now.ToUnixTimeSeconds() + 300
            || signature.Length != 67 || !signature.StartsWith("v0=", StringComparison.Ordinal)) return false;
        var prefix = Encoding.UTF8.GetBytes($"v0:{timestamp}:");
        var data = new byte[prefix.Length + body.Length]; prefix.CopyTo(data, 0); body.CopyTo(data, prefix.Length);
        var expected = "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }
    public static bool IsId(string? value, string prefixes) => value is { Length: >= 2 and <= 40 }
        && prefixes.Contains(value[0]) && value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');
    public static SlackClick Parse(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 }); var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "block_actions") throw new FormatException();
            var actions = root.GetProperty("actions"); if (actions.GetArrayLength() != 1) throw new FormatException();
            var action = actions[0]; var actionId = action.GetProperty("action_id").GetString();
            if (actionId is not ("agentgate_approve" or "agentgate_reject") || !Guid.TryParseExact(action.GetProperty("value").GetString(), "D", out var id)) throw new FormatException();
            var container = root.GetProperty("container"); if (container.GetProperty("type").GetString() != "message") throw new FormatException();
            var team = root.GetProperty("team").GetProperty("id").GetString()!;
            var app = root.GetProperty("api_app_id").GetString()!;
            var channel = container.GetProperty("channel_id").GetString()!;
            var user = root.GetProperty("user").GetProperty("id").GetString()!;
            var ts = container.GetProperty("message_ts").GetString()!;
            if (!IsId(team, "T") || !IsId(app, "A") || !IsId(channel, "CG") || !IsId(user, "UW")
                || string.IsNullOrEmpty(ts) || ts.Length > 40 || !ts.All(c => char.IsAsciiDigit(c) || c == '.')) throw new FormatException();
            return new(id, actionId == "agentgate_approve", team, app, channel, ts, user);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentNullException or NullReferenceException)
        { throw new RequestException(400, "Invalid Slack interaction."); }
    }
}
