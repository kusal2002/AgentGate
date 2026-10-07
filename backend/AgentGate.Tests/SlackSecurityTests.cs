using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
namespace AgentGate.Tests;
public sealed class SlackSecurityTests
{
    [Fact]
    public void OfficialSlackSignatureVectorIsAccepted()
    {
        const string body = "token=xyzz0WbapA4vBCDEFasx0q6G&team_id=T1DC2JH3J&team_domain=testteamnow&channel_id=G8PSS9T3V&channel_name=foobar&user_id=U2CERLKJA&user_name=roadrunner&command=%2Fwebhook-collect&text=&response_url=https%3A%2F%2Fhooks.slack.com%2Fcommands%2FT1DC2JH3J%2F397700885554%2F96rGlfmibIGlgcZRskXaIFfN&trigger_id=398738663015.47445629121.803a0bc887a14d10d2c447fce8b6703c";
        Assert.True(SlackSecurity.Verify("8f742231b10e8888abcd99yyyzzz85a5", "1531420618", "v0=a2114d57b48eac39b9ad189dd8316235a7b4a8d21a10bd27519666489c69b503", Encoding.UTF8.GetBytes(body), DateTimeOffset.FromUnixTimeSeconds(1531420618)));
    }
    [Theory]
    [InlineData(-301)] [InlineData(301)] [InlineData(long.MaxValue)] [InlineData(long.MinValue)]
    public void StaleFutureAndExtremeTimestampsFail(long offset)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(10000); var timestamp = offset is long.MaxValue or long.MinValue ? offset.ToString() : (10000 + offset).ToString();
        var body = Encoding.UTF8.GetBytes("payload=test");
        var signature = "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes($"v0:{timestamp}:payload=test"))).ToLowerInvariant();
        Assert.False(SlackSecurity.Verify("secret", timestamp, signature, body, now));
    }
    [Fact]
    public void ChangedRawBodyAndMalformedSignaturesFail()
    {
        var now = DateTimeOffset.UtcNow; var ts = now.ToUnixTimeSeconds().ToString(); var body = Encoding.UTF8.GetBytes("payload=%20");
        var sig = "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes($"v0:{ts}:payload=%20"))).ToLowerInvariant();
        Assert.True(SlackSecurity.Verify("secret", ts, sig, body, now));
        Assert.False(SlackSecurity.Verify("secret", ts, sig, Encoding.UTF8.GetBytes("payload=+"), now));
        foreach (var invalid in new[] { "", "v0=bad", "v1=" + sig[3..], new string('x', 67) }) Assert.False(SlackSecurity.Verify("secret", ts, invalid, body, now));
        Assert.False(SlackSecurity.Verify("", ts, sig, body, now));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"type\":\"block_actions\",\"actions\":[]}")]
    [InlineData("{\"type\":\"block_actions\",\"actions\":[{\"action_id\":\"execute\",\"value\":\"{}\"}]}")]
    public void MalformedAndUnsupportedInteractionsFail(string payload) => Assert.Throws<RequestException>(() => SlackSecurity.Parse(payload));
}
