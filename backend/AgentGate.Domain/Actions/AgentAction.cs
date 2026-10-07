namespace AgentGate.Domain.Actions;

public enum ActionDecision { Allow, Review, Deny }
public enum ActionStatus { Created, AwaitingApproval, Approved, Rejected, Denied, Executing, Executed, Failed, Cancelled }
public enum ActionRiskLevel { Low, Medium, High, Critical }

public sealed class AgentAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid AgentId { get; set; }
    public string ActionType { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string ContextJson { get; set; } = "{}";
    public ActionRiskLevel? RiskLevel { get; set; }
    public ActionDecision Decision { get; set; }
    public Guid? MatchedPolicyId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public ActionStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public bool TestEvaluation { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExecutedAt { get; set; }
}
