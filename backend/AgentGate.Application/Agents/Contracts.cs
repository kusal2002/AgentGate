using System.ComponentModel.DataAnnotations;
using AgentGate.Domain.Agents;

namespace AgentGate.Application.Agents;

public sealed record CreateAgentRequest(
    [Required, StringLength(100)] string Name,
    [Required, StringLength(20)] string Environment,
    [Required, StringLength(50)] string Version,
    [Required(AllowEmptyStrings = true), StringLength(2000)] string Description = "");
public sealed record UpdateAgentRequest([Required, StringLength(100)] string Name,
    [Required, StringLength(50)] string Version, [Required(AllowEmptyStrings = true), StringLength(2000)] string Description = "");
public sealed record CreateKeyRequest([Required, StringLength(100)] string Name, DateTimeOffset? ExpiresAt = null);
public sealed record AgentDto(Guid Id, Guid OrganizationId, string Name, string Slug, string Description,
    string Environment, string Status, string Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastActivityAt);
public sealed record ApiKeyDto(Guid Id, string Name, string KeyPrefix, string Environment, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt, string Status);
public sealed record GeneratedKeyDto(string Key, ApiKeyDto ApiKey);
public sealed record AgentIdentityDto(Guid AgentId, Guid OrganizationId, Guid ApiKeyId, string Name, string Environment, string Version);

public interface IAgentService
{
    Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken ct);
    Task<AgentDto> GetAsync(Guid id, CancellationToken ct);
    Task<AgentDto> CreateAsync(CreateAgentRequest request, CancellationToken ct);
    Task<AgentDto> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct);
    Task DisableAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ApiKeyDto>> KeysAsync(Guid agentId, CancellationToken ct);
    Task<GeneratedKeyDto> GenerateKeyAsync(Guid agentId, CreateKeyRequest request, CancellationToken ct);
    Task RevokeKeyAsync(Guid agentId, Guid keyId, CancellationToken ct);
}
public interface IAgentKeyAuthenticator { Task<AgentIdentityDto?> AuthenticateAsync(string presentedKey, CancellationToken ct); }
public interface IAgentStore
{
    Task<Agent?> GetAsync(Guid organizationId, Guid agentId, CancellationToken ct);
    Task<IReadOnlyList<AgentDto>> ListAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<AgentApiKey>> KeysAsync(Guid organizationId, Guid agentId, CancellationToken ct);
    Task<AgentApiKey?> FindKeyAsync(string prefix, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken ct);
    Task<bool> RevokeAsync(Guid organizationId, Guid agentId, Guid keyId, CancellationToken ct);
    Task<AgentIdentityDto?> RecordAuthenticatedUseAsync(AgentApiKey key, CancellationToken ct);
}
