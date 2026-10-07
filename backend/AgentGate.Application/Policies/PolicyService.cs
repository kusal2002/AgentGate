using System.Text.Json;
using AgentGate.Application.Accounts;
using AgentGate.Application.Actions;
using AgentGate.Application.Agents;
using AgentGate.Application.Errors;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Policies;

namespace AgentGate.Application.Policies;

public sealed class PolicyService(IPolicyStore store, IAccountStore accounts, IAgentStore agents, ICurrentAccount current, IPolicyEvaluator evaluator) : IPolicyService
{
    public async Task<IReadOnlyList<PolicyDto>> ListAsync(CancellationToken ct) => (await store.ListAsync(current.OrganizationId, ct)).Select(Map).ToArray();
    public async Task<PolicyDto> GetAsync(Guid id, CancellationToken ct) => Map(await Find(id, ct));
    public async Task<PolicyDto> CreateAsync(PolicyRequest request, CancellationToken ct)
    {
        await RequireRole(false, ct); PolicyRules.Validate(request);
        var policy = new Policy { OrganizationId = current.OrganizationId };
        Apply(policy, request); store.Add(policy); await store.SaveAsync(ct); return Map(policy);
    }
    public async Task<PolicyDto> UpdateAsync(Guid id, PolicyRequest request, CancellationToken ct)
    {
        await RequireRole(false, ct); PolicyRules.Validate(request);
        var policy = await Find(id, ct); RequireVersion(policy, request.Version);
        Apply(policy, request); await store.SaveAsync(ct); return Map(policy);
    }
    public async Task<PolicyDto> SetStatusAsync(Guid id, PolicyStatusRequest request, CancellationToken ct)
    {
        await RequireRole(false, ct); var policy = await Find(id, ct); RequireVersion(policy, request.Version);
        policy.Enabled = request.Enabled; policy.UpdatedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(ct); return Map(policy);
    }
    public async Task<PolicyEvaluationResult> TestAsync(PolicyTestRequest request, CancellationToken ct)
    {
        await RequireRole(true, ct);
        var agent = await agents.GetAsync(current.OrganizationId, request.AgentId, ct) ?? throw new RequestException(404, "Agent not found.");
        var action = new EvaluateActionRequest(request.Action, request.Resource, request.Parameters, "policy-preview", request.Context);
        ActionPayload.ValidateAndCanonicalize(action);
        return await evaluator.EvaluateAsync(new(current.OrganizationId, agent.Id, agent.Environment.ToString(), action), ct);
    }
    public async Task<IReadOnlyList<PolicyDto>> SeedDemoAsync(CancellationToken ct)
    {
        await RequireRole(false, ct);
        await store.SeedAsync(current.OrganizationId, DemoRefundPolicies.Create(current.OrganizationId), ct);
        return await ListAsync(ct);
    }
    private Task<Policy?> Lookup(Guid id, CancellationToken ct) => store.GetAsync(current.OrganizationId, id, ct);
    private async Task<Policy> Find(Guid id, CancellationToken ct) => await Lookup(id, ct) ?? throw new RequestException(404, "Policy not found.");
    private async Task RequireRole(bool test, CancellationToken ct)
    {
        var role = (await accounts.GetMembershipAsync(current.OrganizationId, current.UserId, ct))?.Role;
        if (role is OrganizationRole.Owner or OrganizationRole.Admin || test && role == OrganizationRole.Developer) return;
        throw new RequestException(403, test ? "Owner, Admin, or Developer role required to test policies." : "Owner or Admin role required to manage policies.");
    }
    private static void RequireVersion(Policy policy, uint? version)
    {
        if (version is null || version == 0) throw new RequestException(400, "The current policy version is required.");
        if (policy.VersionStamp != version) throw new RequestException(409, "This policy changed. Reload before saving.");
    }
    private static void Apply(Policy policy, PolicyRequest request)
    {
        policy.Name = request.Name; policy.Description = request.Description; policy.ActionType = request.ActionType; policy.Priority = request.Priority;
        policy.Enabled = request.Enabled; policy.ConditionsJson = JsonSerializer.Serialize(request.Conditions, PolicyRules.JsonOptions);
        policy.Decision = PolicyRules.Parse<AgentGate.Domain.Actions.ActionDecision>(request.Decision, "decision");
        policy.ReviewerRole = request.ReviewerRole; policy.RiskLevel = PolicyRules.Parse<AgentGate.Domain.Actions.ActionRiskLevel>(request.RiskLevel, "risk level");
        policy.UpdatedAt = DateTimeOffset.UtcNow;
    }
    public static PolicyDto Map(Policy policy) => new(policy.Id, policy.Name, policy.Description, policy.ActionType, policy.Priority, policy.Enabled,
        JsonSerializer.Deserialize<PolicyCondition[]>(policy.ConditionsJson, PolicyRules.JsonOptions) ?? [], policy.Decision.ToString().ToLowerInvariant(),
        policy.ReviewerRole, policy.RiskLevel.ToString(), policy.VersionStamp, policy.CreatedAt, policy.UpdatedAt);
}

public static class DemoRefundPolicies
{
    public static IReadOnlyList<Policy> Create(Guid organizationId)
    {
        PolicyCondition Condition(string field, string op, object value) => new(field, op, JsonSerializer.SerializeToElement(value));
        Policy Rule(string key, string name, int priority, string decision, string risk, params PolicyCondition[] amountConditions)
        {
            var conditions = new[] { Condition("agent.environment", "equals", "Development"), Condition("parameters.currency", "equals", "USD") }.Concat(amountConditions).ToArray();
            return new() { OrganizationId = organizationId, SeedKey = key, Name = name, Description = "Sample rule for Development agents issuing USD refunds.", ActionType = "refund", Enabled = true, Priority = priority,
                Decision = PolicyRules.Parse<AgentGate.Domain.Actions.ActionDecision>(decision, "decision"), RiskLevel = PolicyRules.Parse<AgentGate.Domain.Actions.ActionRiskLevel>(risk, "risk"), ReviewerRole = decision == "review" ? "Reviewer" : null,
                ConditionsJson = JsonSerializer.Serialize(conditions, PolicyRules.JsonOptions) };
        }
        return [
            Rule("refund-small", "Small refund", 100, "allow", "Low", Condition("parameters.amount", "less_than_or_equal", 100)),
            Rule("refund-medium", "Medium refund", 200, "review", "Medium", Condition("parameters.amount", "greater_than", 100), Condition("parameters.amount", "less_than_or_equal", 1000)),
            Rule("refund-large", "Large refund", 300, "review", "High", Condition("parameters.amount", "greater_than", 1000)),
            Rule("refund-extreme", "Extremely large refund", 1000, "deny", "Critical", Condition("parameters.amount", "greater_than", 10000))
        ];
    }
}
