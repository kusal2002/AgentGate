using System.Text.Json;
using AgentGate.Application.Actions;
using AgentGate.Application.Errors;
using AgentGate.Application.Policies;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Policies;

namespace AgentGate.Tests;

public sealed class PolicyEvaluatorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static AgentActionContext Context(decimal amount = 750, string environment = "Development", string action = "refund", string currency = "USD") =>
        new(OrgId, Guid.NewGuid(), environment, new(action, new("customer", "CUS-1"), JsonSerializer.SerializeToElement(new { amount, currency }), "test"));
    private static PolicyCondition Condition(string field, string op, object value) => new(field, op, JsonSerializer.SerializeToElement(value));
    private static Policy Rule(ActionDecision decision, int priority, params PolicyCondition[] conditions) => new()
    {
        OrganizationId = OrgId, Name = "Test policy", ActionType = "refund", Enabled = true, Priority = priority,
        Decision = decision, RiskLevel = ActionRiskLevel.Medium, ReviewerRole = decision == ActionDecision.Review ? "Reviewer" : null,
        ConditionsJson = JsonSerializer.Serialize(conditions, PolicyRules.JsonOptions)
    };
    private static PolicyEvaluator Evaluator(params Policy[] policies) => new(new MemoryPolicyStore(policies), new(ActionDecision.Review, ActionDecision.Deny, ActionDecision.Deny));
    [Theory]
    [InlineData(50, "allow", "Low")]
    [InlineData(100, "allow", "Low")]
    [InlineData(100.01, "review", "Medium")]
    [InlineData(750, "review", "Medium")]
    [InlineData(1000, "review", "Medium")]
    [InlineData(1000.01, "review", "High")]
    [InlineData(2000, "review", "High")]
    [InlineData(10000, "review", "High")]
    [InlineData(10000.01, "deny", "Critical")]
    [InlineData(15000, "deny", "Critical")]
    public async Task DemoRefundRulesCoverBoundaries(decimal amount, string decision, string risk)
    {
        var result = await Evaluator(DemoRefundPolicies.Create(OrgId).ToArray()).EvaluateAsync(Context(amount), default);
        Assert.Equal(decision, result.Decision); Assert.Equal(risk, result.RiskLevel); Assert.NotNull(result.MatchedPolicyId);
        Assert.Equal(decision == "review" ? "Reviewer" : null, result.ReviewerRole);
        Assert.Equal(decision == "review" ? "awaiting_approval" : decision == "allow" ? "approved" : "denied", result.Status);
    }
    [Theory]
    [InlineData("equals", 5, 5, true)]
    [InlineData("equals", 5, 6, false)]
    [InlineData("not_equals", 5, 6, true)]
    [InlineData("greater_than", 6, 5, true)]
    [InlineData("greater_than", 5, 5, false)]
    [InlineData("greater_than_or_equal", 5, 5, true)]
    [InlineData("less_than", 4, 5, true)]
    [InlineData("less_than_or_equal", 5, 5, true)]
    public void NumericOperatorsMatchExactly(string op, decimal actual, decimal expected, bool matches) =>
        Assert.Equal(matches, PolicyMatcher.Matches([Condition("parameters.amount", op, expected)], Context(actual)));
    [Theory]
    [InlineData("equals", "business", true)]
    [InlineData("not_equals", "personal", true)]
    [InlineData("contains", "ness", true)]
    [InlineData("contains", "NESS", false)]
    [InlineData("not_contains", "personal", true)]
    public void StringsAreOrdinalAndCaseSensitive(string op, string expected, bool matches)
    {
        var context = Context() with { Request = Context().Request with { Context = JsonSerializer.SerializeToElement(new { customer = new { tier = "business" } }) } };
        Assert.Equal(matches, PolicyMatcher.Matches([Condition("context.customer.tier", op, expected)], context));
    }
    [Theory]
    [InlineData("in", true)]
    [InlineData("not_in", false)]
    public void MembershipOperatorsSupportSameTypedSets(string op, bool matches)
    {
        Assert.Equal(matches, PolicyMatcher.Matches([Condition("parameters.amount", op, new[] { 50, 750 })], Context()));
        Assert.Equal(matches, PolicyMatcher.Matches([Condition("parameters.currency", op, new[] { "USD", "EUR" })], Context()));
    }
    [Theory]
    [InlineData("equals")]
    [InlineData("not_equals")]
    [InlineData("not_contains")]
    [InlineData("greater_than")]
    [InlineData("not_in")]
    public void MissingNullOrWronglyTypedFieldsNeverMatchNegativeOrPositiveOperators(string op)
    {
        var expected = op == "not_in" ? JsonSerializer.SerializeToElement(new[] { "USD" }) : JsonSerializer.SerializeToElement("USD");
        Assert.False(PolicyMatcher.Matches([new("parameters.missing", op, expected)], Context()));
        Assert.False(PolicyMatcher.Matches([new("parameters.amount", op, expected)], Context()));
        var context = Context() with { Request = Context().Request with { Parameters = JsonSerializer.SerializeToElement(new { currency = (string?)null }) } };
        Assert.False(PolicyMatcher.Matches([new("parameters.currency", op, expected)], context));
    }
    [Fact]
    public void BooleansAndServerResourceAndAgentFieldsAreSupported()
    {
        var context = Context() with { Request = Context().Request with { Context = JsonSerializer.SerializeToElement(new { vip = true }) } };
        Assert.True(PolicyMatcher.Matches([Condition("context.vip", "equals", true), Condition("context.vip", "not_equals", false), Condition("resource.type", "equals", "customer"), Condition("resource.id", "equals", "CUS-1"), Condition("agent.id", "equals", context.AgentId.ToString()), Condition("agent.environment", "equals", "Development")], context));
    }
    [Fact]
    public async Task PriorityTiesAndDisabledPoliciesHaveDeterministicResults()
    {
        var allow = Rule(ActionDecision.Allow, 100); var deny = Rule(ActionDecision.Deny, 100); var review = Rule(ActionDecision.Review, 100);
        Assert.Equal(deny.Id, (await Evaluator(allow, review, deny).EvaluateAsync(Context(), default)).MatchedPolicyId);
        allow.Priority = 101;
        Assert.Equal(allow.Id, (await Evaluator(deny, allow).EvaluateAsync(Context(), default)).MatchedPolicyId);
        allow.Enabled = false;
        Assert.Equal(deny.Id, (await Evaluator(allow, deny).EvaluateAsync(Context(), default)).MatchedPolicyId);
        var other = Rule(ActionDecision.Deny, 100); var expected = new[] { deny.Id, other.Id }.Order().First();
        Assert.Equal(expected, (await Evaluator(other, deny).EvaluateAsync(Context(), default)).MatchedPolicyId);
        Assert.Equal(expected, (await Evaluator(deny, other).EvaluateAsync(Context(), default)).MatchedPolicyId);
    }
    [Theory]
    [InlineData("Development", "review")]
    [InlineData("Staging", "deny")]
    [InlineData("Production", "deny")]
    [InlineData("unknown", "deny")]
    public async Task UnknownActionsAndOtherCurrenciesNeverSilentlyAllow(string environment, string decision)
    {
        var evaluator = Evaluator(DemoRefundPolicies.Create(OrgId).ToArray());
        Assert.Equal(decision, (await evaluator.EvaluateAsync(Context(environment: environment, action: "delete_data"), default)).Decision);
        Assert.Equal(decision, (await evaluator.EvaluateAsync(Context(environment: environment, currency: "EUR"), default)).Decision);
    }
    [Fact]
    public async Task InvalidStoredPolicyDeniesInsteadOfFallingThroughToAllow()
    {
        var corrupt = Rule(ActionDecision.Deny, 200); corrupt.ConditionsJson = "[{\"field\":\"parameters.amount\",\"operator\":\"unsupported\",\"value\":1}]";
        var result = await Evaluator(corrupt, Rule(ActionDecision.Allow, 100)).EvaluateAsync(Context(), default);
        Assert.Equal("deny", result.Decision); Assert.Equal("Critical", result.RiskLevel); Assert.Null(result.MatchedPolicyId);
    }
    [Theory]
    [InlineData("contains", "1")]
    [InlineData("greater_than", "\"1\"")]
    [InlineData("in", "[]")]
    [InlineData("in", "[1,\"1\"]")]
    [InlineData("equals", "null")]
    [InlineData("equals", "{}")]
    [InlineData("unsupported", "1")]
    public void InvalidConditionTypesAreRejected(string op, string json) => Assert.Throws<RequestException>(() => PolicyRules.ValidateCondition(new("parameters.amount", op, JsonSerializer.Deserialize<JsonElement>(json))));
    private sealed class MemoryPolicyStore(Policy[] policies) : IPolicyStore
    {
        public Task<IReadOnlyList<Policy>> EnabledAsync(Guid orgId, string action, CancellationToken ct) => Task.FromResult<IReadOnlyList<Policy>>(policies.Where(x => x.OrganizationId == orgId && x.ActionType == action && x.Enabled).ToArray());
        public Task<IReadOnlyList<Policy>> ListAsync(Guid orgId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Policy?> GetAsync(Guid orgId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public void Add(Policy policy) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task SeedAsync(Guid orgId, IReadOnlyList<Policy> rows, CancellationToken ct) => throw new NotSupportedException();
    }
}
