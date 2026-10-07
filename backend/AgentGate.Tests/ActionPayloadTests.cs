using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Application.Errors;

namespace AgentGate.Tests;

public sealed class ActionPayloadTests
{
    private static EvaluateActionRequest Request(string parameters = "{}", string? context = null) => new("send_email", new("customer", "CUS-1"), JsonSerializer.Deserialize<JsonElement>(parameters), "email-1",
        context is null ? default : JsonSerializer.Deserialize<JsonElement>(context));
    [Fact]
    public void CanonicalHashIgnoresObjectOrderAndEquivalentNumbersButPreservesArrays()
    {
        var first = ActionPayload.ValidateAndCanonicalize(Request("{\"values\":[1,2],\"nested\":{\"b\":2,\"a\":1.0}}"));
        var second = ActionPayload.ValidateAndCanonicalize(Request("{\"nested\":{\"a\":1e0,\"b\":2.00},\"values\":[1.0,2]}"));
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, ActionPayload.ValidateAndCanonicalize(Request("{\"nested\":{\"a\":1,\"b\":2},\"values\":[2,1]}" )).Hash);
        Assert.Equal(ActionPayload.ValidateAndCanonicalize(Request()).Hash, ActionPayload.ValidateAndCanonicalize(Request(context: "{}")).Hash);
    }
    [Theory]
    [InlineData("{\"value\":1e100}")]
    [InlineData("{\"value\":1e-100}")]
    [InlineData("{\"value\":1.00000000000000000000000000001}")]
    [InlineData("{\"nested\":{\"a\":1,\"a\":2}}")]
    [InlineData("{\"value\":\"\\u0000\"}")]
    [InlineData("{\"\\u0000\":1}")]
    public void LossyNumbersAndNestedDuplicatePropertiesAreRejected(string json) => Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request(json)));
    [Fact]
    public void LengthObjectSizeAndNestingLimitsAreEnforced()
    {
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request() with { IdempotencyKey = new string('x', 201) }));
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request() with { IdempotencyKey = " " }));
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request() with { Resource = new("customer", new string('x', 201)) }));
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request(JsonSerializer.Serialize(new { value = new string('x', 32_768) }))));
        var nested = new string('[', 18) + "0" + new string(']', 18);
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request("{\"nested\":" + nested + "}")));
        Assert.Throws<RequestException>(() => ActionPayload.ValidateAndCanonicalize(Request(context: "null")));
    }
}
