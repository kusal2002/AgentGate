using AgentGate.Domain.Accounts;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentGate.Infrastructure.Persistence;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.OrganizationId });
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ReviewerRole).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReviewerComment).HasMaxLength(2000).IsRequired();
        builder.HasOne<AgentAction>().WithOne(x => x.Approval).HasForeignKey<ApprovalRequest>(x => new { x.ActionId, x.OrganizationId })
            .HasPrincipalKey<AgentAction>(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.RequestedAt, x.Id });
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
    }
}
public sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.ApprovalRequestId }).IsUnique();
        builder.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => new { x.ApprovalRequestId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
