using AgentGate.Application.Accounts;
using AgentGate.Application.Errors;

namespace AgentGate.Application.Dashboard;

public sealed class DashboardService(IDashboardStore store, ICurrentAccount current)
{
    public Task<DashboardOverviewDto> OverviewAsync(CancellationToken ct) => store.OverviewAsync(current.OrganizationId, ct);
    public async Task<ActionStatisticsDto> AgentStatisticsAsync(Guid id, CancellationToken ct) =>
        await store.AgentStatisticsAsync(current.OrganizationId, id, ct) ?? throw new RequestException(404, "Agent not found.");
}
