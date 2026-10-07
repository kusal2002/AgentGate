using Microsoft.EntityFrameworkCore;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Agents;
using AgentGate.Domain.Actions;
using AgentGate.Domain.Policies;
using AgentGate.Domain.Approvals;

namespace AgentGate.Infrastructure.Persistence;

public sealed class AgentGateDbContext(DbContextOptions<AgentGateDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentApiKey> AgentApiKeys => Set<AgentApiKey>();
    public DbSet<AgentAction> AgentActions => Set<AgentAction>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<AgentGate.Domain.Slack.SlackIntegration> SlackIntegrations => Set<AgentGate.Domain.Slack.SlackIntegration>();
    public DbSet<AgentGate.Domain.Slack.SlackReviewer> SlackReviewers => Set<AgentGate.Domain.Slack.SlackReviewer>();
    public DbSet<AgentGate.Domain.Slack.SlackDelivery> SlackDeliveries => Set<AgentGate.Domain.Slack.SlackDelivery>();
    public DbSet<AgentGate.Domain.Slack.SlackFeedback> SlackFeedback => Set<AgentGate.Domain.Slack.SlackFeedback>();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardDecisions();
        return base.SaveChangesAsync(cancellationToken);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardDecisions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardDecisions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void GuardDecisions()
    {
        if (ChangeTracker.Entries<ApprovalDecision>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Approval decisions are append-only.");
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgentGateDbContext).Assembly);
    }
}
