using AgentGate.Application.Accounts;
using AgentGate.Application.Errors;
using AgentGate.Domain.Audit;
using AgentGate.Domain.Actions;

namespace AgentGate.Application.Audit;

public sealed class AuditService(IAuditStore store, ICurrentAccount current)
{
    public Task<AuditPageDto> ListAsync(AuditFilter filter, CancellationToken ct)
    {
        Validate(filter);
        return store.ListAsync(current.OrganizationId, filter, false, ct);
    }
    public async Task<AuditEventDto> GetAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(current.OrganizationId, id, ct) ?? throw new RequestException(404, "Audit event not found.");
    public Task<AuditOptionsDto> OptionsAsync(CancellationToken ct) => store.OptionsAsync(current.OrganizationId, ct);
    public async Task<AuditPageDto> TimelineAsync(Guid id, bool approval, int page, int pageSize, CancellationToken ct)
    {
        Validate(new(Page: page, PageSize: pageSize));
        var org = current.OrganizationId;
        var action = approval ? await store.ApprovalActionAsync(org, id, ct) : await store.ActionExistsAsync(org, id, ct) ? id : (Guid?)null;
        if (action is null) throw new RequestException(404, approval ? "Approval not found." : "Action not found.");
        return await store.ListAsync(org, new(ActionId: action, Page: page, PageSize: pageSize), true, ct);
    }
    private static void Validate(AuditFilter filter)
    {
        if (filter.Page is < 1 or > 1_000_000 || filter.PageSize is < 1 or > 100)
            throw new RequestException(400, "Page must be 1–1000000 and page size 1–100.");
        if (filter.EventType is { Length: > 100 } || filter.EventType is { } type && type.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_'))
            throw new RequestException(400, "Invalid event type.");
        if (filter.ActorType is { } actor && (!Enum.TryParse<AuditActorType>(actor, false, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(actor, out _)))
            throw new RequestException(400, "Invalid actor type.");
        if (filter.From > filter.To) throw new RequestException(400, "From must be before To.");
        if (filter.ActionType is { } action && (action.Length is < 1 or > 100 || !System.Text.RegularExpressions.Regex.IsMatch(action, "^[a-z][a-z0-9_.:-]*$")))
            throw new RequestException(400, "Invalid action type.");
        if (filter.Decision is { } decision && (!Enum.TryParse<ActionDecision>(decision, true, out var parsedDecision) || !Enum.IsDefined(parsedDecision) || int.TryParse(decision, out _)))
            throw new RequestException(400, "Invalid decision.");
        if (filter.RiskLevel is { } risk && (!Enum.TryParse<ActionRiskLevel>(risk, true, out var parsedRisk) || !Enum.IsDefined(parsedRisk) || int.TryParse(risk, out _)))
            throw new RequestException(400, "Invalid risk level.");
        if (filter.Search is { } search && (search.Length > 200 || search.Any(char.IsControl)))
            throw new RequestException(400, "Search must be an identifier of at most 200 characters.");
    }
}
