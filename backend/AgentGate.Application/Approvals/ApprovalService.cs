using AgentGate.Application.Accounts;
using AgentGate.Application.Actions;
using AgentGate.Application.Errors;
using AgentGate.Domain.Approvals;

namespace AgentGate.Application.Approvals;

public sealed class ApprovalService(IApprovalStore store, ICurrentAccount account, ICurrentAgent agent, IAccountStore accounts) : IApprovalService
{
    public Task<ApprovalPageDto> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new RequestException(400, "Page must be between 1 and 1000000 and page size between 1 and 100.");
        ApprovalStatus? parsed = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ApprovalStatus>(status, true, out var value) || !Enum.IsDefined(value) || int.TryParse(status, out _)) throw new RequestException(400, "Invalid approval status.");
            parsed = value;
        }
        return store.ListAsync(account.OrganizationId, parsed, page, pageSize, ct);
    }
    public async Task<ApprovalDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var detail = await store.GetAsync(account.OrganizationId, id, ct) ?? throw new RequestException(404, "Approval not found.");
        var membership = await accounts.GetMembershipAsync(account.OrganizationId, account.UserId, ct);
        return detail with { CanReview = detail.Approval.Status == "pending" && detail.Approval.ExpiresAt > DateTimeOffset.UtcNow && membership is not null && ApprovalRules.CanReview(membership.Role, detail.Approval.ReviewerRole) };
    }
    public async Task<ApprovalDetailDto> ResolveAsync(Guid id, bool approve, ResolveApprovalRequest request, CancellationToken ct)
    {
        var comment = ApprovalRules.ValidateComment(request.Comment);
        // Store rechecks current membership after obtaining the approval lock.
        await store.ResolveAsync(account.OrganizationId, account.UserId, id, approve, comment, ct);
        return await GetAsync(id, ct);
    }
    public async Task<AgentApprovalDto> GetAgentAsync(Guid id, CancellationToken ct) => await store.GetAgentAsync(agent.OrganizationId, agent.AgentId, id, ct)
        ?? throw new RequestException(404, "Approval not found.");
}
