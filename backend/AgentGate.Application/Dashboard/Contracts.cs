using AgentGate.Application.Actions;

namespace AgentGate.Application.Dashboard;

public sealed record ActionStatisticsDto(int ActionsEvaluated, int AutoAllowed, int HumanReviews, int Denied,
    int PendingApprovals, double? AverageApprovalSeconds, int ApprovedActions, int ExecutedActions);
public sealed record RecentActivityDto(Guid Id, Guid AgentId, string AgentName, string Action, ActionResource Resource,
    string Decision, string Status, string? RiskLevel, DateTimeOffset CreatedAt, Guid? ApprovalId,
    Guid? ReviewerId, string? ReviewerName);
public sealed record DashboardOverviewDto(ActionStatisticsDto Statistics, int TotalAgents, int ActiveAgents,
    int EnabledPolicies, IReadOnlyList<RecentActivityDto> RecentActivity, DateTimeOffset AsOf);
public interface IDashboardStore
{
    Task<DashboardOverviewDto> OverviewAsync(Guid org, CancellationToken ct);
    Task<ActionStatisticsDto?> AgentStatisticsAsync(Guid org, Guid agentId, CancellationToken ct);
}
