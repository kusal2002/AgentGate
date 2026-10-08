using System.Text.Json;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;
using AgentGate.Infrastructure.Persistence;

namespace AgentGate.Infrastructure.Audit;

public sealed class AuditWriter(AgentGateDbContext db, ISensitiveDataRedactor redactor, IAuditContext context) : IAuditWriter
{
    public AuditEvent Record(Guid organizationId, string eventType, AuditActorType actorType, Guid? actorId, object metadata,
        Guid? agentId = null, Guid? actionId = null, Guid? approvalId = null, DateTimeOffset? at = null, int order = 0)
    {
        var row = new AuditEvent { OrganizationId = organizationId, EventType = eventType, ActorType = actorType, ActorId = actorId,
            AgentId = agentId, ActionId = actionId, ApprovalRequestId = approvalId, CreatedAt = at ?? DateTimeOffset.UtcNow,
            MetadataJson = redactor.Redact(JsonSerializer.Serialize(metadata)), EventOrder = order,
            IPAddress = actorType is AuditActorType.Agent or AuditActorType.User or AuditActorType.Slack ? context.IPAddress : null };
        db.AuditEvents.Add(row);
        return row;
    }
    public void Management(Guid organizationId, string eventType, object metadata, Guid? agentId = null) =>
        Record(organizationId, eventType, context.UserId is null ? AuditActorType.System : AuditActorType.User, context.UserId, metadata, agentId);
}
