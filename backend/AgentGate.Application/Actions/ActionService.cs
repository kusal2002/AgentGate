using AgentGate.Application.Accounts;
using AgentGate.Application.Errors;
using AgentGate.Domain.Actions;

namespace AgentGate.Application.Actions;

public sealed class ActionService(IActionStore store, ICurrentAgent current, IActionEvaluator evaluator) : IActionService
{
    public async Task<EvaluationDto> EvaluateAsync(EvaluateActionRequest request, CancellationToken ct)
    {
        var payload = ActionPayload.ValidateAndCanonicalize(request);
        var existing = await store.FindAsync(current.OrganizationId, current.AgentId, request.IdempotencyKey, ct);
        if (existing is not null) return Replay(existing, payload.Hash);
        var result = evaluator.Evaluate(request, current.Environment);
        var action = new AgentAction
        {
            OrganizationId = current.OrganizationId, AgentId = current.AgentId, ActionType = request.Action,
            ResourceType = request.Resource.Type, ResourceId = request.Resource.Id,
            ParametersJson = payload.Parameters, ContextJson = payload.Context, RequestHash = payload.Hash,
            IdempotencyKey = request.IdempotencyKey, Decision = result.Decision, Status = result.Status, Reason = result.Reason, TestEvaluation = result.TestEvaluation
        };
        // The database resolves concurrent retries using the unique tenant/agent/key constraint.
        return Replay(await store.CreateOrGetAsync(action, ct), payload.Hash);
    }
    public async Task<EvaluationDto> GetAgentActionAsync(Guid id, CancellationToken ct)
    {
        var action = await store.GetAgentActionAsync(current.OrganizationId, current.AgentId, id, ct)
            ?? throw new RequestException(404, "Action not found.");
        EnsureTestBoundary(action);
        return Map(action);
    }
    private EvaluationDto Replay(AgentAction action, string hash)
    {
        if (action.RequestHash != hash) throw new RequestException(409, "This idempotency key was already used for a different request. Use a new key for a new action.");
        EnsureTestBoundary(action);
        return Map(action);
    }
    private void EnsureTestBoundary(AgentAction action)
    {
        if (action.TestEvaluation && !evaluator.CanUseTestResults(current.Environment))
            throw new RequestException(403, "Development test results cannot authorize actions in this environment.");
    }
    public static EvaluationDto Map(AgentAction action) => new(action.Id, action.Decision.ToString().ToLowerInvariant(), StatusName(action.Status), action.Reason, action.TestEvaluation);
    public static string StatusName(ActionStatus status) => status == ActionStatus.AwaitingApproval ? "awaiting_approval" : status.ToString().ToLowerInvariant();
}

// Phase 4's temporary test evaluator is deliberately restricted to local Development.
// Replace this implementation with deterministic policies in Phase 5.
public sealed class DevelopmentActionEvaluator(bool developmentHost) : IActionEvaluator
{
    public bool CanUseTestResults(string agentEnvironment) => developmentHost && agentEnvironment == "Development";
    public ActionEvaluation Evaluate(EvaluateActionRequest request, string agentEnvironment) => CanUseTestResults(agentEnvironment)
        ? new(ActionDecision.Allow, ActionStatus.Approved, "Development test evaluation only. No policy was evaluated and no action was executed.", true)
        : new(ActionDecision.Deny, ActionStatus.Denied, "The policy engine is not available yet. Actions outside local Development testing are denied.");
}

public sealed class ActionHistoryService(IActionStore store, ICurrentAccount current) : IActionHistoryService
{
    public Task<ActionPageDto> ListAsync(Guid? agentId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 1_000_000 || pageSize < 1 || pageSize > 100)
            throw new RequestException(400, "Page must be between 1 and 1000000 and page size between 1 and 100.");
        return store.ListAsync(current.OrganizationId, agentId, page, pageSize, ct);
    }
    public async Task<ActionDetailDto> GetAsync(Guid id, CancellationToken ct) => await store.GetAsync(current.OrganizationId, id, ct)
        ?? throw new RequestException(404, "Action not found.");
}
