using System.Text.Json;
using System.Text.Json.Serialization;
using AgentGate.Domain.Actions;

namespace AgentGate.Application.Actions;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionResource(string Type, string Id);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluateActionRequest(string Action, ActionResource Resource, JsonElement Parameters,
    string IdempotencyKey, JsonElement Context = default);
public sealed record EvaluationDto(Guid ActionId, string Decision, string Status, string Reason, bool TestEvaluation,
    Guid? MatchedPolicyId, string? MatchedPolicyName, string? ReviewerRole, string? RiskLevel, DateTimeOffset? PolicyUpdatedAt,
    Guid? ApprovalId, string? ApprovalStatus, DateTimeOffset? ApprovalExpiresAt);
public sealed record ActionSummaryDto(Guid Id, Guid AgentId, string AgentName, string Action, ActionResource Resource,
    string Decision, string Status, string? RiskLevel, DateTimeOffset CreatedAt, bool TestEvaluation);
public sealed record ActionDetailDto(Guid Id, Guid AgentId, string AgentName, string Action, ActionResource Resource,
    string Decision, string Status, string? RiskLevel, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ExecutedAt, Guid? MatchedPolicyId, string IdempotencyKey, string Reason,
    JsonElement Parameters, JsonElement Context, bool TestEvaluation, string? MatchedPolicyName, string? ReviewerRole, DateTimeOffset? PolicyUpdatedAt,
    Guid? ApprovalId, string? ApprovalStatus);
public sealed record ActionPageDto(IReadOnlyList<ActionSummaryDto> Items, int Total, int Page, int PageSize);
public sealed record ActionEvaluation(ActionDecision Decision, ActionStatus Status, string Reason, bool TestEvaluation = false,
    Guid? MatchedPolicyId = null, string? MatchedPolicyName = null, string? ReviewerRole = null, ActionRiskLevel? RiskLevel = null, DateTimeOffset? PolicyUpdatedAt = null);

public interface ICurrentAgent
{
    Guid OrganizationId { get; }
    Guid AgentId { get; }
    string Environment { get; }
}
public interface IActionEvaluator
{
    Task<ActionEvaluation> EvaluateAsync(EvaluateActionRequest request, string agentEnvironment, CancellationToken ct);
    bool CanUseTestResults(string agentEnvironment);
}
public interface IActionService
{
    Task<EvaluationDto> EvaluateAsync(EvaluateActionRequest request, CancellationToken ct);
    Task<EvaluationDto> GetAgentActionAsync(Guid id, CancellationToken ct);
}
public interface IActionHistoryService
{
    Task<ActionPageDto> ListAsync(Guid? agentId, int page, int pageSize, CancellationToken ct);
    Task<ActionDetailDto> GetAsync(Guid id, CancellationToken ct);
}
public interface IActionStore
{
    Task<AgentAction?> FindAsync(Guid organizationId, Guid agentId, string idempotencyKey, CancellationToken ct);
    Task<AgentAction> CreateOrGetAsync(AgentAction action, CancellationToken ct);
    Task<AgentAction?> GetAgentActionAsync(Guid organizationId, Guid agentId, Guid id, CancellationToken ct);
    Task<ActionPageDto> ListAsync(Guid organizationId, Guid? agentId, int page, int pageSize, CancellationToken ct);
    Task<ActionDetailDto?> GetAsync(Guid organizationId, Guid id, CancellationToken ct);
}
