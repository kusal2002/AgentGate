namespace AgentGate.Domain.Agents;

public enum AgentEnvironment { Development, Staging, Production }
public enum AgentStatus { Active, Disabled }

public sealed class Agent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public AgentEnvironment Environment { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Active;
    public string Version { get; set; } = "1.0.0";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid AgentId { get; set; }
    public string Name { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public AgentEnvironment Environment { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
