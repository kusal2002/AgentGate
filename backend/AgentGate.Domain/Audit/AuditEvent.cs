namespace AgentGate.Domain.Audit;

public enum AuditActorType { Agent, User, Policy, System, Slack }

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ActionId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public string EventType { get; set; } = "";
    public AuditActorType ActorType { get; set; }
    public Guid? ActorId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public string? IPAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    // Deterministic ordering of events emitted by the same transition.
    public int EventOrder { get; set; }
}
