using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Application.Errors;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Policies;

namespace AgentGate.Application.Policies;

public sealed class PolicyEvaluator(IPolicyStore store, PolicyDefaults defaults) : IPolicyEvaluator
{
    public async Task<PolicyEvaluationResult> EvaluateAsync(AgentActionContext context, CancellationToken ct)
    {
        var policies = await store.EnabledAsync(context.OrganizationId, context.Request.Action, ct);
        foreach (var policy in policies.OrderByDescending(x => x.Priority).ThenByDescending(x => x.Decision).ThenBy(x => x.Id))
        {
            PolicyCondition[] conditions;
            try
            {
                conditions = JsonSerializer.Deserialize<PolicyCondition[]>(policy.ConditionsJson, PolicyRules.JsonOptions) ?? throw new JsonException();
                // Corrupt stored rules must deny, rather than silently falling through to allow.
                PolicyRules.Validate(new(policy.Name, policy.Description, policy.ActionType, policy.Priority, policy.Enabled, conditions,
                    policy.Decision.ToString(), policy.ReviewerRole, policy.RiskLevel.ToString()));
            }
            catch (Exception error) when (error is JsonException or RequestException)
            {
                return Result(ActionDecision.Deny, ActionRiskLevel.Critical, null, "A stored policy is invalid. Evaluation denied until it is repaired.");
            }
            if (PolicyMatcher.Matches(conditions, context))
                return Result(policy.Decision, policy.RiskLevel, policy, $"Matched policy: {policy.Name}.");
        }
        return Result(defaults.For(context.Environment), ActionRiskLevel.High, null, $"No enabled policy matched. The {context.Environment} default applies.");
    }
    private static PolicyEvaluationResult Result(ActionDecision decision, ActionRiskLevel risk, Policy? policy, string reason) => new(
        decision.ToString().ToLowerInvariant(), ActionService.StatusName(decision switch { ActionDecision.Allow => ActionStatus.Approved, ActionDecision.Review => ActionStatus.AwaitingApproval, _ => ActionStatus.Denied }),
        policy?.Id, policy?.Name, policy?.ReviewerRole ?? (decision == ActionDecision.Review ? "Reviewer" : null), risk.ToString(), reason, policy?.UpdatedAt);
}

public static class PolicyMatcher
{
    public static bool Matches(IReadOnlyList<PolicyCondition> conditions, AgentActionContext context) => conditions.All(condition => Match(condition, context));
    private static bool Match(PolicyCondition condition, AgentActionContext context)
    {
        if (!TryRead(condition.Field, context, out var actual)) return false;
        var expected = condition.Value;
        if (condition.Operator is "in" or "not_in")
        {
            var candidates = expected.EnumerateArray().ToArray();
            if (candidates.Length == 0 || PolicyRules.ScalarType(actual) != PolicyRules.ScalarType(candidates[0])) return false;
            var contained = candidates.Any(candidate => EqualsValue(actual, candidate));
            return condition.Operator == "in" ? contained : !contained;
        }
        // Missing/null/wrongly typed inputs never match even negative operators.
        if (PolicyRules.ScalarType(actual) != PolicyRules.ScalarType(expected)) return false;
        return condition.Operator switch
        {
            "equals" => EqualsValue(actual, expected), "not_equals" => !EqualsValue(actual, expected),
            "contains" => actual.ValueKind == JsonValueKind.String && actual.GetString()!.Contains(expected.GetString()!, StringComparison.Ordinal),
            "not_contains" => actual.ValueKind == JsonValueKind.String && !actual.GetString()!.Contains(expected.GetString()!, StringComparison.Ordinal),
            "greater_than" => Number(actual, expected, comparison => comparison > 0),
            "greater_than_or_equal" => Number(actual, expected, comparison => comparison >= 0),
            "less_than" => Number(actual, expected, comparison => comparison < 0),
            "less_than_or_equal" => Number(actual, expected, comparison => comparison <= 0),
            _ => false
        };
    }
    private static bool Number(JsonElement actual, JsonElement expected, Func<int, bool> compare) => actual.ValueKind == JsonValueKind.Number
        && actual.TryGetDecimal(out var left) && expected.TryGetDecimal(out var right) && compare(left.CompareTo(right));
    private static bool EqualsValue(JsonElement actual, JsonElement expected) => actual.ValueKind switch
    {
        JsonValueKind.String => string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal),
        JsonValueKind.Number => actual.TryGetDecimal(out var left) && expected.TryGetDecimal(out var right) && left == right,
        JsonValueKind.True or JsonValueKind.False => actual.GetBoolean() == expected.GetBoolean(),
        _ => false
    };
    private static bool TryRead(string field, AgentActionContext context, out JsonElement value)
    {
        switch (field)
        {
            case "resource.type": value = JsonSerializer.SerializeToElement(context.Request.Resource.Type); return true;
            case "resource.id": value = JsonSerializer.SerializeToElement(context.Request.Resource.Id); return true;
            case "agent.id": value = JsonSerializer.SerializeToElement(context.AgentId.ToString()); return true;
            case "agent.environment": value = JsonSerializer.SerializeToElement(context.Environment); return true;
        }
        var parts = field.Split('.');
        value = parts[0] == "parameters" ? context.Request.Parameters : context.Request.Context;
        foreach (var part in parts.Skip(1))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return false;
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
    }
}

public sealed class PolicyActionEvaluator(IPolicyEvaluator policies, ICurrentAgent current) : IActionEvaluator
{
    // Phase 4 test results remain visible in history, but cannot authorize new policy-era work.
    public bool CanUseTestResults(string agentEnvironment) => false;
    public async Task<ActionEvaluation> EvaluateAsync(EvaluateActionRequest request, string agentEnvironment, CancellationToken ct)
    {
        var result = await policies.EvaluateAsync(new(current.OrganizationId, current.AgentId, agentEnvironment, request), ct);
        return new(Enum.Parse<ActionDecision>(result.Decision, true), result.Decision switch { "allow" => ActionStatus.Approved, "review" => ActionStatus.AwaitingApproval, _ => ActionStatus.Denied },
            result.Reason, false, result.MatchedPolicyId, result.MatchedPolicyName, result.ReviewerRole, Enum.Parse<ActionRiskLevel>(result.RiskLevel), result.PolicyUpdatedAt);
    }
}
