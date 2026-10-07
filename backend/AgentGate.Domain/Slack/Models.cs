namespace AgentGate.Domain.Slack;
public sealed class SlackIntegration
{
    public Guid OrganizationId { get; set; }
    public string ChannelId { get; set; } = "";
    public bool Enabled { get; set; }
}
public sealed class SlackReviewer
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string SlackUserId { get; set; } = "";
}
public sealed class SlackDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ApprovalId { get; set; }
    public string TeamId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string? MessageTs { get; set; }
    public string? SentStatus { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
}
public sealed class SlackFeedback
{
    public string RequestKey { get; set; } = "";
    public Guid OrganizationId { get; set; }
    public string ChannelId { get; set; } = "";
    public string SlackUserId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);
    public DateTimeOffset? SentAt { get; set; }
}
