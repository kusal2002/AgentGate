using AgentGate.Domain.Actions;

namespace AgentGate.Domain.Policies;

public sealed class Policy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionType { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public string ConditionsJson { get; set; } = "[]";
    public ActionDecision Decision { get; set; }
    public string? ReviewerRole { get; set; }
    public ActionRiskLevel RiskLevel { get; set; }
    public string? SeedKey { get; set; }
    public uint VersionStamp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
