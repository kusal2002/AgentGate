using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property<uint>("VersionStamp").IsRowVersion();
        builder.HasIndex(x => new { x.OrganizationId, x.Slug }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AgentApiKeyConfiguration : IEntityTypeConfiguration<AgentApiKey>
{
    public void Configure(EntityTypeBuilder<AgentApiKey> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.KeyPrefix).HasMaxLength(24).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.KeyPrefix).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.AgentId });
        builder.HasOne<Agent>().WithMany().HasForeignKey(x => new { x.AgentId, x.OrganizationId, x.Environment })
            .HasPrincipalKey(x => new { x.Id, x.OrganizationId, x.Environment }).OnDelete(DeleteBehavior.Restrict);
    }
}
