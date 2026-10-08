using System.Text.Json;
using AgentGate.Domain.Audit;

namespace AgentGate.Application.Audit;

public interface IAuditContext
{
    Guid? UserId { get; }
    string? IPAddress { get; }
}
public interface ISensitiveDataRedactor { string Redact(string json); }
public interface IAuditWriter
{
    AuditEvent Record(Guid organizationId, string eventType, AuditActorType actorType, Guid? actorId, object metadata,
        Guid? agentId = null, Guid? actionId = null, Guid? approvalId = null, DateTimeOffset? at = null, int order = 0);
    void Management(Guid organizationId, string eventType, object metadata, Guid? agentId = null);
}
public sealed record AuditFilter(Guid? AgentId = null, Guid? ActionId = null, Guid? ApprovalRequestId = null,
    string? EventType = null, string? ActorType = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25,
    string? ActionType = null, string? Decision = null, Guid? ReviewerId = null, string? RiskLevel = null, string? Search = null);
public sealed record AuditEventDto(Guid Id, Guid OrganizationId, Guid? AgentId, Guid? ActionId, Guid? ApprovalRequestId,
    string EventType, string ActorType, Guid? ActorId, JsonElement Metadata, string? IPAddress, DateTimeOffset CreatedAt,
    string? AgentName = null, string? ActionType = null, string? ResourceType = null, string? ResourceId = null,
    string? Decision = null, string? RiskLevel = null, Guid? ReviewerId = null, string? ReviewerName = null, string? ActorName = null);
public sealed record AuditReviewerDto(Guid Id, string Name);
public sealed record AuditOptionsDto(IReadOnlyList<string> Actions, IReadOnlyList<AuditReviewerDto> Reviewers);
public sealed record AuditPageDto(IReadOnlyList<AuditEventDto> Items, int Total, int Page, int PageSize);
public interface IAuditStore
{
    Task<AuditPageDto> ListAsync(Guid org, AuditFilter filter, bool ascending, CancellationToken ct);
    Task<AuditEventDto?> GetAsync(Guid org, Guid id, CancellationToken ct);
    Task<bool> ActionExistsAsync(Guid org, Guid id, CancellationToken ct);
    Task<Guid?> ApprovalActionAsync(Guid org, Guid id, CancellationToken ct);
    Task<AuditOptionsDto> OptionsAsync(Guid org, CancellationToken ct);
}
