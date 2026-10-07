using AgentGate.Application.Agents;
using AgentGate.Domain.Agents;

namespace AgentGate.Tests;

public sealed class AgentKeyCodecTests
{
    [Theory]
    [InlineData(AgentEnvironment.Development, "ag_test_")]
    [InlineData(AgentEnvironment.Staging, "ag_test_")]
    [InlineData(AgentEnvironment.Production, "ag_live_")]
    public void KeysAreRandomAndHaveEnvironmentPrefix(AgentEnvironment environment, string prefix)
    {
        var first = AgentKeyCodec.Generate(environment);
        var second = AgentKeyCodec.Generate(environment);
        Assert.StartsWith(prefix, first);
        Assert.True(AgentKeyCodec.IsWellFormed(first));
        Assert.NotEqual(first, second);
        Assert.Equal(64, AgentKeyCodec.Hash(first).Length);
        Assert.NotEqual(first, AgentKeyCodec.Hash(first));
    }
    [Theory]
    [InlineData("")]
    [InlineData("ag_test_short")]
    [InlineData("Bearer ag_test_fake")]
    [InlineData("ag_live_0000000000000000000000000000000000000000000000000000000000000000extra")]
    [InlineData("ag_test_Z000000000000000000000000000000000000000000000000000000000000000")]
    public void MalformedKeysFailClosed(string key) => Assert.False(AgentKeyCodec.IsWellFormed(key));
}
