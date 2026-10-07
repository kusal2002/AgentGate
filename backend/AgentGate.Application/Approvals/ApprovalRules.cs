using AgentGate.Domain.Accounts;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Approvals;
using AgentGate.Application.Errors;

namespace AgentGate.Application.Approvals;

public static class ApprovalRules
{
    public static bool CanReview(OrganizationRole role, string requiredRole) => requiredRole is "Owner" or "Admin" or "Reviewer"
        && (role == OrganizationRole.Owner || role == OrganizationRole.Admin && requiredRole is "Admin" or "Reviewer"
            || role == OrganizationRole.Reviewer && requiredRole == "Reviewer");
    public static string ValidateComment(string? comment)
    {
        if (comment is null || comment.Length > 2000 || comment.Contains('\0')) throw new RequestException(400, "Comment must be at most 2000 characters and cannot contain null characters.");
        return comment.Trim();
    }
    public static ApprovalRequest Create(AgentAction action, ApprovalSettings settings)
    {
        if (action.Decision != ActionDecision.Review || action.Status != ActionStatus.AwaitingApproval || action.TestEvaluation)
            throw new InvalidOperationException("Only policy-controlled review actions can create approvals.");
        return new() { OrganizationId = action.OrganizationId, ActionId = action.Id, ReviewerRole = action.ReviewerRole ?? "Reviewer",
            RequestedAt = action.CreatedAt, CreatedAt = action.CreatedAt, UpdatedAt = action.CreatedAt, ExpiresAt = action.CreatedAt.AddMinutes(settings.TimeoutMinutes) };
    }
}
