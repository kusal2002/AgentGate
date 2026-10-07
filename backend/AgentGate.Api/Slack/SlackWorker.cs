using AgentGate.Application.Slack;
namespace AgentGate.Api.Slack;
public sealed class SlackWorker(IServiceScopeFactory scopes, ILogger<SlackWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISlackStore>().DispatchAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ISlackStore>().DispatchFeedbackAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Slack maintenance failed; delivery will retry. Dashboard approvals remain available."); }
        }
    }
}
