using System.Text.Json.Serialization;
using AgentGate.Application.Approvals;
namespace AgentGate.Application.Slack;
public sealed record SlackSettings(string BotToken, string SigningSecret, string AppId, string TeamId, Guid OrganizationId)
{
    public bool Configured => BotToken.Length > 0 && SigningSecret.Length > 0 && AppId.Length > 0 && TeamId.Length > 0 && OrganizationId != Guid.Empty;
    public override string ToString() => "SlackSettings (credentials redacted)";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SlackIntegrationRequest(string ChannelId, bool Enabled);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SlackReviewerRequest(Guid UserId, string SlackUserId);
public sealed record SlackReviewerDto(Guid UserId, string Name, string SlackUserId);
public sealed record SlackIntegrationDto(bool Available, Guid OrganizationId, string? TeamId, string ChannelId, bool Enabled,
    IReadOnlyList<SlackReviewerDto> Reviewers, int FailedDeliveries);
public sealed record SlackClick(Guid ApprovalId, bool Approve, string TeamId, string AppId, string ChannelId, string MessageTs, string SlackUserId);
public sealed class SlackApiException(int retryAfterSeconds = 0) : AgentGate.Application.Errors.RequestException(502,
    "Slack rejected or could not receive the request. Check bot scopes, workspace, channel membership, and credentials.")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
public interface ISlackClient
{
    Task VerifyWorkspaceAsync(CancellationToken ct);
    Task VerifyReviewerAsync(string userId, CancellationToken ct);
    Task<string> SendAsync(string channelId, string? messageTs, ApprovalDetailDto detail, Guid deliveryId, CancellationToken ct);
    Task FeedbackAsync(string channelId, string userId, string text, CancellationToken ct);
}
public interface ISlackStore
{
    Task<SlackIntegrationDto> GetAsync(Guid organizationId, CancellationToken ct);
    Task ConfigureAsync(Guid organizationId, SlackIntegrationRequest request, CancellationToken ct);
    Task MapAsync(Guid organizationId, SlackReviewerRequest request, CancellationToken ct);
    Task UnmapAsync(Guid organizationId, Guid userId, CancellationToken ct);
    Task<string> HandleAsync(SlackClick click, CancellationToken ct, string? requestKey = null);
    Task DispatchFeedbackAsync(CancellationToken ct);
    Task DispatchAsync(CancellationToken ct);
}
