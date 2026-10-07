using System.Text.Json;
using System.Text.Json.Serialization;
using AgentGate.Application.Actions;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Policies;

namespace AgentGate.Application.Policies;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyCondition(string Field, string Operator, JsonElement Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyRequest(string Name, string Description, string ActionType, int Priority, bool Enabled,
    PolicyCondition[] Conditions, string Decision, string? ReviewerRole, string RiskLevel, uint? Version = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyStatusRequest(bool Enabled, uint Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyTestRequest(Guid AgentId, string Action, ActionResource Resource, JsonElement Parameters, JsonElement Context = default);
public sealed record PolicyDto(Guid Id, string Name, string Description, string ActionType, int Priority, bool Enabled,
    PolicyCondition[] Conditions, string Decision, string? ReviewerRole, string RiskLevel, uint Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AgentActionContext(Guid OrganizationId, Guid AgentId, string Environment, EvaluateActionRequest Request);
public sealed record PolicyEvaluationResult(string Decision, string Status, Guid? MatchedPolicyId, string? MatchedPolicyName,
    string? ReviewerRole, string RiskLevel, string Reason, DateTimeOffset? PolicyUpdatedAt);
public interface IPolicyEvaluator
{
    Task<PolicyEvaluationResult> EvaluateAsync(AgentActionContext context, CancellationToken ct);
}
public interface IPolicyService
{
    Task<IReadOnlyList<PolicyDto>> ListAsync(CancellationToken ct);
    Task<PolicyDto> GetAsync(Guid id, CancellationToken ct);
    Task<PolicyDto> CreateAsync(PolicyRequest request, CancellationToken ct);
    Task<PolicyDto> UpdateAsync(Guid id, PolicyRequest request, CancellationToken ct);
    Task<PolicyDto> SetStatusAsync(Guid id, PolicyStatusRequest request, CancellationToken ct);
    Task<PolicyEvaluationResult> TestAsync(PolicyTestRequest request, CancellationToken ct);
    Task<IReadOnlyList<PolicyDto>> SeedDemoAsync(CancellationToken ct);
}
public interface IPolicyStore
{
    Task<IReadOnlyList<Policy>> ListAsync(Guid organizationId, CancellationToken ct);
    Task<Policy?> GetAsync(Guid organizationId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<Policy>> EnabledAsync(Guid organizationId, string actionType, CancellationToken ct);
    void Add(Policy policy);
    Task SaveAsync(CancellationToken ct);
    Task SeedAsync(Guid organizationId, IReadOnlyList<Policy> policies, CancellationToken ct);
}
public sealed record PolicyDefaults(ActionDecision Development, ActionDecision Staging, ActionDecision Production)
{
    public ActionDecision For(string environment) => environment switch
    {
        "Development" => Development, "Staging" => Staging, "Production" => Production,
        _ => ActionDecision.Deny
    };
}
