using AgentGate.Application.Approvals;

namespace AgentGate.Api.Approvals;

public sealed class ApprovalExpiryWorker(IServiceScopeFactory scopes, ILogger<ApprovalExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IApprovalStore>().ExpireDueAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Approval expiry maintenance failed; pending requests still enforce expiry during resolution and reads."); }
        }
    }
}
