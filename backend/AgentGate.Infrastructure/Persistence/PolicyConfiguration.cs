using AgentGate.Domain.Accounts;
using AgentGate.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentGate.Infrastructure.Persistence;

public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConditionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ReviewerRole).HasMaxLength(20);
        builder.Property(x => x.SeedKey).HasMaxLength(50);
        builder.Property(x => x.VersionStamp).IsRowVersion();
        builder.HasIndex(x => new { x.OrganizationId, x.ActionType, x.Enabled });
        builder.HasIndex(x => new { x.OrganizationId, x.SeedKey }).IsUnique();
        builder.HasAlternateKey(x => new { x.Id, x.OrganizationId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
