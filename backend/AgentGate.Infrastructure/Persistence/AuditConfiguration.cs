using AgentGate.Domain.Audit;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AuditConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.IPAddress).HasMaxLength(45);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Agent>().WithMany().HasForeignKey(x => new { x.AgentId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentAction>().WithMany().HasForeignKey(x => new { x.ActionId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => new { x.ApprovalRequestId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt, x.EventOrder, x.Id });
        builder.HasIndex(x => new { x.OrganizationId, x.ActionId, x.CreatedAt, x.EventOrder, x.Id });
        builder.HasIndex(x => new { x.OrganizationId, x.AgentId });
        builder.HasIndex(x => new { x.OrganizationId, x.EventType });
    }
}
