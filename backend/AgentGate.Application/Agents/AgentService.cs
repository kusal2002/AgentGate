using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentGate.Application.Accounts;
using AgentGate.Application.Errors;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;

namespace AgentGate.Application.Agents;

public sealed class AgentService(IAgentStore store, IAccountStore accounts, ICurrentAccount current) : IAgentService
{
    public Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken ct) => store.ListAsync(current.OrganizationId, ct);
    public async Task<AgentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var agent = await Find(id, ct);
        var keys = await store.KeysAsync(current.OrganizationId, id, ct);
        return Map(agent, keys.Select(key => key.LastUsedAt).Max());
    }
    public async Task<AgentDto> CreateAsync(CreateAgentRequest request, CancellationToken ct)
    {
        await RequireManager(ct);
        if (!Enum.TryParse<AgentEnvironment>(request.Environment, false, out var environment) || !Enum.IsDefined(environment) || int.TryParse(request.Environment, out _))
            throw new RequestException(400, "Environment must be Development, Staging, or Production.");
        var name = request.Name.Trim();
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var agent = new Agent { OrganizationId = current.OrganizationId, Name = name, Description = request.Description.Trim(), Environment = environment, Version = request.Version.Trim(), Slug = (slug.Length > 0 ? slug : "agent") + "-" + Guid.NewGuid().ToString("N")[..8] };
        store.Add(agent); await store.SaveAsync(ct);
        return Map(agent, null);
    }
    public async Task<AgentDto> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct)
    {
        await RequireManager(ct);
        var agent = await Find(id, ct);
        agent.Name = request.Name.Trim(); agent.Description = request.Description.Trim(); agent.Version = request.Version.Trim(); agent.UpdatedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(ct);
        return await GetAsync(id, ct);
    }
    public async Task DisableAsync(Guid id, CancellationToken ct)
    {
        await RequireManager(ct);
        var agent = await Find(id, ct);
        agent.Status = AgentStatus.Disabled; agent.UpdatedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(ct);
    }
    public async Task<IReadOnlyList<ApiKeyDto>> KeysAsync(Guid agentId, CancellationToken ct)
    {
        await Find(agentId, ct);
        return (await store.KeysAsync(current.OrganizationId, agentId, ct)).Select(MapKey).ToArray();
    }
    public async Task<GeneratedKeyDto> GenerateKeyAsync(Guid agentId, CreateKeyRequest request, CancellationToken ct)
    {
        await RequireManager(ct);
        var agent = await Find(agentId, ct);
        if (agent.Status != AgentStatus.Active) throw new RequestException(409, "Disabled agents cannot receive new API keys.");
        if (request.ExpiresAt <= DateTimeOffset.UtcNow) throw new RequestException(400, "Expiry must be in the future.");
        var key = AgentKeyCodec.Generate(agent.Environment);
        var entity = new AgentApiKey { AgentId = agent.Id, OrganizationId = current.OrganizationId, Environment = agent.Environment, Name = request.Name.Trim(), KeyPrefix = key[..AgentKeyCodec.PrefixLength], KeyHash = AgentKeyCodec.Hash(key), ExpiresAt = request.ExpiresAt?.ToUniversalTime() };
        store.Add(entity); await store.SaveAsync(ct);
        return new(key, MapKey(entity));
    }
    public async Task RevokeKeyAsync(Guid agentId, Guid keyId, CancellationToken ct)
    {
        await RequireManager(ct);
        await Find(agentId, ct);
        if (!await store.RevokeAsync(current.OrganizationId, agentId, keyId, ct)) throw new RequestException(404, "API key not found.");
    }
    private async Task<Agent> Find(Guid id, CancellationToken ct) => await store.GetAsync(current.OrganizationId, id, ct) ?? throw new RequestException(404, "Agent not found.");
    private async Task RequireManager(CancellationToken ct)
    {
        var member = await accounts.GetMembershipAsync(current.OrganizationId, current.UserId, ct);
        if (member?.Role is not (OrganizationRole.Owner or OrganizationRole.Admin or OrganizationRole.Developer)) throw new RequestException(403, "Owner, Admin, or Developer role required.");
    }
    public static AgentDto Map(Agent agent, DateTimeOffset? activity) => new(agent.Id, agent.OrganizationId, agent.Name, agent.Slug, agent.Description, agent.Environment.ToString(), agent.Status.ToString(), agent.Version, agent.CreatedAt, agent.UpdatedAt, activity);
    private static ApiKeyDto MapKey(AgentApiKey key) => new(key.Id, key.Name, key.KeyPrefix, key.Environment.ToString(), key.CreatedAt, key.ExpiresAt, key.RevokedAt, key.LastUsedAt,
        key.RevokedAt is not null ? "Revoked" : key.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : "Active");
}

public static class AgentKeyCodec
{
    public const int PrefixLength = 24;
    public static string Generate(AgentEnvironment environment) => (environment == AgentEnvironment.Production ? "ag_live_" : "ag_test_") + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public static bool IsWellFormed(string key) => key.Length == 72 && Regex.IsMatch(key, "^ag_(test|live)_[0-9a-f]{64}$");
}

public sealed class AgentKeyAuthenticator(IAgentStore store) : IAgentKeyAuthenticator
{
    public async Task<AgentIdentityDto?> AuthenticateAsync(string presentedKey, CancellationToken ct)
    {
        if (!AgentKeyCodec.IsWellFormed(presentedKey)) return null;
        var key = await store.FindKeyAsync(presentedKey[..AgentKeyCodec.PrefixLength], ct);
        if (key is null || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(key.KeyHash), Convert.FromHexString(AgentKeyCodec.Hash(presentedKey)))) return null;
        return await store.RecordAuthenticatedUseAsync(key, ct);
    }
}
