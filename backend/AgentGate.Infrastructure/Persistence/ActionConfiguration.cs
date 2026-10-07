using AgentGate.Domain.Actions;
using AgentGate.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentGate.Infrastructure.Persistence;

public sealed class ActionConfiguration : IEntityTypeConfiguration<AgentAction>
{
    public void Configure(EntityTypeBuilder<AgentAction> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActionType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ResourceId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ParametersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContextJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.MatchedPolicyName).HasMaxLength(100);
        builder.Property(x => x.ReviewerRole).HasMaxLength(20);
        builder.HasOne<AgentGate.Domain.Policies.Policy>().WithMany().HasForeignKey(x => new { x.MatchedPolicyId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.AgentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt, x.Id });
        builder.HasOne<Agent>().WithMany().HasForeignKey(x => new { x.AgentId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.Id, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
