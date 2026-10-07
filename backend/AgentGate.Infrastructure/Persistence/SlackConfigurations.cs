using AgentGate.Domain.Accounts;
using AgentGate.Domain.Approvals;
using AgentGate.Domain.Slack;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace AgentGate.Infrastructure.Persistence;
public sealed class SlackIntegrationConfiguration : IEntityTypeConfiguration<SlackIntegration>
{
    public void Configure(EntityTypeBuilder<SlackIntegration> b)
    {
        b.HasKey(x => x.OrganizationId); b.Property(x => x.ChannelId).HasMaxLength(40);
        b.HasOne<Organization>().WithOne().HasForeignKey<SlackIntegration>(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SlackReviewerConfiguration : IEntityTypeConfiguration<SlackReviewer>
{
    public void Configure(EntityTypeBuilder<SlackReviewer> b)
    {
        b.HasKey(x => new { x.OrganizationId, x.UserId }); b.Property(x => x.SlackUserId).HasMaxLength(40);
        b.HasIndex(x => new { x.OrganizationId, x.SlackUserId }).IsUnique();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SlackDeliveryConfiguration : IEntityTypeConfiguration<SlackDelivery>
{
    public void Configure(EntityTypeBuilder<SlackDelivery> b)
    {
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.OrganizationId, x.ApprovalId }).IsUnique();
        b.HasIndex(x => x.NextAttemptAt); b.Property(x => x.TeamId).HasMaxLength(40); b.Property(x => x.ChannelId).HasMaxLength(40);
        b.Property(x => x.MessageTs).HasMaxLength(40); b.Property(x => x.SentStatus).HasMaxLength(20); b.Property(x => x.LastError).HasMaxLength(100);
        b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => new { x.ApprovalId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SlackFeedbackConfiguration : IEntityTypeConfiguration<SlackFeedback>
{
    public void Configure(EntityTypeBuilder<SlackFeedback> b)
    {
        b.HasKey(x => x.RequestKey); b.Property(x => x.RequestKey).HasMaxLength(64); b.Property(x => x.ChannelId).HasMaxLength(40);
        b.Property(x => x.SlackUserId).HasMaxLength(40); b.Property(x => x.Text).HasMaxLength(500);
        b.HasIndex(x => x.NextAttemptAt); b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
