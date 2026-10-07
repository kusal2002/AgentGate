using System.Text.Json.Serialization;
using AgentGate.Application.Actions;
using AgentGate.Domain.Approvals;

namespace AgentGate.Application.Approvals;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResolveApprovalRequest(string Comment = "");
public sealed record ApprovalSettings(int TimeoutMinutes = 1440);
public sealed record ApprovalSummaryDto(Guid Id, Guid ActionId, Guid AgentId, string AgentName, string Action,
    ActionResource Resource, string Status, string ReviewerRole, string? RiskLevel, string? MatchedPolicyName,
    DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ResolvedAt, decimal? Amount, string? Currency);
public sealed record ApprovalDecisionDto(Guid Id, Guid ReviewerUserId, string ReviewerName, string Decision, string Comment, DateTimeOffset CreatedAt);
public sealed record ApprovalDetailDto(ApprovalSummaryDto Approval, ActionDetailDto Action, bool CanReview,
    Guid? ResolvedByUserId, string ReviewerComment, IReadOnlyList<ApprovalDecisionDto> Decisions);
public sealed record ApprovalPageDto(IReadOnlyList<ApprovalSummaryDto> Items, int Total, int Page, int PageSize);
public sealed record AgentApprovalDto(Guid Id, Guid ActionId, string Status, string ActionStatus, string ReviewerRole,
    DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ResolvedAt);
public interface IApprovalService
{
    Task<ApprovalPageDto> ListAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<ApprovalDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<ApprovalDetailDto> ResolveAsync(Guid id, bool approve, ResolveApprovalRequest request, CancellationToken ct);
    Task<AgentApprovalDto> GetAgentAsync(Guid id, CancellationToken ct);
}
public interface IApprovalStore
{
    Task<ApprovalPageDto> ListAsync(Guid organizationId, ApprovalStatus? status, int page, int pageSize, CancellationToken ct);
    Task<ApprovalDetailDto?> GetAsync(Guid organizationId, Guid id, CancellationToken ct);
    Task<AgentApprovalDto?> GetAgentAsync(Guid organizationId, Guid agentId, Guid id, CancellationToken ct);
    Task ResolveAsync(Guid organizationId, Guid userId, Guid id, bool approve, string comment, CancellationToken ct);
    Task ExpireDueAsync(Guid? organizationId, CancellationToken ct, Guid? actionId = null, Guid? approvalId = null);
}
