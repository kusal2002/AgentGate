namespace AgentGate.Domain.Approvals;

public enum ApprovalStatus { Pending, Approved, Rejected, Expired, Cancelled }
public enum ApprovalDecisionType { Approve, Reject }
public enum ApprovalDecisionSource { Dashboard, Slack }
public sealed class ApprovalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ActionId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string ReviewerRole { get; set; } = "Reviewer";
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string ReviewerComment { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ApprovalRequestId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public ApprovalDecisionType Decision { get; set; }
    public ApprovalDecisionSource Source { get; set; } = ApprovalDecisionSource.Dashboard;
    public string Comment { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
