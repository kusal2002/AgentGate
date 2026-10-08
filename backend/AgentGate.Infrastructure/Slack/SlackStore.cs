using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Application.Slack;
using AgentGate.Domain.Accounts;
using AgentGate.Domain.Approvals;
using AgentGate.Domain.Slack;
using AgentGate.Infrastructure.Actions;
using AgentGate.Infrastructure.Approvals;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AgentGate.Application.Audit;
using AgentGate.Domain.Audit;
namespace AgentGate.Infrastructure.Slack;

public sealed class SlackStore(AgentGateDbContext db, SlackSettings settings, ISlackClient client, IApprovalStore approvals, IAuditWriter audit) : ISlackStore
{
    public async Task<SlackIntegrationDto> GetAsync(Guid org, CancellationToken ct)
    {
        var available = settings.Configured && org == settings.OrganizationId;
        var config = await db.SlackIntegrations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org, ct);
        var reviewers = await db.SlackReviewers.Where(x => x.OrganizationId == org)
            .Join(db.OrganizationUsers.Where(x => x.OrganizationId == org), x => x.UserId, x => x.UserId, (mapping, member) => mapping)
            .Join(db.Users, x => x.UserId, x => x.Id, (mapping, user) => new SlackReviewerDto(user.Id, user.Name, mapping.SlackUserId)).ToListAsync(ct);
        var failed = await db.SlackDeliveries.CountAsync(x => x.OrganizationId == org && x.LastError != null, ct);
        return new(available, org, available ? settings.TeamId : null, config?.ChannelId ?? "", config?.Enabled ?? false, reviewers, failed);
    }
    private void Available(Guid org)
    {
        if (!settings.Configured || org != settings.OrganizationId) throw new RequestException(409, "Slack is not configured for this organization on the server.");
    }
    public async Task ConfigureAsync(Guid org, SlackIntegrationRequest request, CancellationToken ct)
    {
        Available(org);
        if (!SlackSecurity.IsId(request.ChannelId, "CG")) throw new RequestException(400, "Enter a Slack channel ID beginning with C or G.");
        if (request.Enabled) await client.VerifyWorkspaceAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Organizations\" WHERE \"Id\" = {org} FOR NO KEY UPDATE", ct);
        var config = await db.SlackIntegrations.SingleOrDefaultAsync(x => x.OrganizationId == org, ct);
        if (config is null) { config = new() { OrganizationId = org }; db.SlackIntegrations.Add(config); }
        config.ChannelId = request.ChannelId; config.Enabled = request.Enabled;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task MapAsync(Guid org, SlackReviewerRequest request, CancellationToken ct)
    {
        Available(org);
        if (!SlackSecurity.IsId(request.SlackUserId, "UW")) throw new RequestException(400, "Enter a Slack member ID beginning with U or W.");
        if (!await db.OrganizationUsers.AnyAsync(x => x.OrganizationId == org && x.UserId == request.UserId, ct)) throw new RequestException(404, "Organization member not found.");
        await client.VerifyReviewerAsync(request.SlackUserId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Organizations\" WHERE \"Id\" = {org} FOR NO KEY UPDATE", ct);
        if (await db.SlackReviewers.AnyAsync(x => x.OrganizationId == org && x.SlackUserId == request.SlackUserId && x.UserId != request.UserId, ct))
            throw new RequestException(409, "This Slack member is already mapped to another account.");
        var mapping = await db.SlackReviewers.SingleOrDefaultAsync(x => x.OrganizationId == org && x.UserId == request.UserId, ct);
        if (mapping is null) { mapping = new() { OrganizationId = org, UserId = request.UserId }; db.SlackReviewers.Add(mapping); }
        mapping.SlackUserId = request.SlackUserId; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task UnmapAsync(Guid org, Guid userId, CancellationToken ct)
    {
        Available(org); await db.SlackReviewers.Where(x => x.OrganizationId == org && x.UserId == userId).ExecuteDeleteAsync(ct);
    }
    public async Task<string> HandleAsync(SlackClick click, CancellationToken ct, string? requestKey = null)
    {
        if (!settings.Configured || click.TeamId != settings.TeamId || click.AppId != settings.AppId)
            throw new RequestException(403, "This workspace or app is not connected to AgentGate.");
        var org = settings.OrganizationId;
        if (!await db.SlackIntegrations.AnyAsync(x => x.OrganizationId == org && x.Enabled, ct)) throw new RequestException(403, "Slack approval handling is disabled.");
        if (!await db.SlackDeliveries.AnyAsync(x => x.OrganizationId == org && x.ApprovalId == click.ApprovalId
            && x.TeamId == click.TeamId && x.ChannelId == click.ChannelId && x.MessageTs == click.MessageTs, ct))
            throw new RequestException(404, "This message is not a delivered AgentGate approval.");
        var mapping = await db.SlackReviewers.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.SlackUserId == click.SlackUserId, ct);
        string text;
        try
        {
            if (mapping is null) throw new RequestException(403, "Your Slack account is not mapped to an AgentGate member. Ask an organization administrator.");
            await approvals.ResolveAsync(org, mapping.UserId, click.ApprovalId, click.Approve, "Decision made through Slack.", ct, ApprovalDecisionSource.Slack);
            text = click.Approve ? "Approval recorded. AgentGate has not executed the action." : "Rejection recorded. The agent must not execute this action.";
        }
        catch (RequestException ex) when (ex.StatusCode is 403 or 409) { text = ex.Message; }
        // Slack ignores acknowledgment bodies for block_actions. Queue real ephemeral feedback through the fixed Web API.
        if (requestKey is { Length: 64 })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SlackFeedback" ("RequestKey", "OrganizationId", "ChannelId", "SlackUserId", "Text", "NextAttemptAt", "ExpiresAt")
                VALUES ({requestKey}, {org}, {click.ChannelId}, {click.SlackUserId}, {text}, now(), now() + interval '5 minutes')
                ON CONFLICT ("RequestKey") DO NOTHING
                """, ct);
        return text;
    }
    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!settings.Configured) return;
        var org = settings.OrganizationId;
        // Durable discovery catches requests created before startup/configuration; uniqueness makes repeated scans safe.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SlackDeliveries" ("Id", "OrganizationId", "ApprovalId", "TeamId", "ChannelId", "Attempts", "NextAttemptAt")
            SELECT gen_random_uuid(), a."OrganizationId", a."Id", {settings.TeamId}, i."ChannelId", 0, now()
            FROM "ApprovalRequests" a JOIN "SlackIntegrations" i ON a."OrganizationId" = i."OrganizationId"
            JOIN "Organizations" o ON o."Id" = a."OrganizationId"
            WHERE a."OrganizationId" = {org} AND i."Enabled" AND o."Status" = 'Active'
              AND a."Status" = 'Pending' AND a."ExpiresAt" > clock_timestamp()
              AND NOT EXISTS (SELECT 1 FROM "SlackDeliveries" d WHERE d."OrganizationId" = a."OrganizationId" AND d."ApprovalId" = a."Id")
            ORDER BY a."RequestedAt", a."Id" LIMIT 100
            ON CONFLICT ("OrganizationId", "ApprovalId") DO NOTHING
            """, ct);
        await approvals.ExpireDueAsync(org, ct);
        // One delivery lock per bounded network call; other workers skip it. Never hold an approval lock over HTTP.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.SlackDeliveries.FromSqlInterpolated($"""
            SELECT d.* FROM "SlackDeliveries" d JOIN "ApprovalRequests" a ON a."Id" = d."ApprovalId" AND a."OrganizationId" = d."OrganizationId"
            JOIN "SlackIntegrations" i ON i."OrganizationId" = d."OrganizationId"
            JOIN "Organizations" o ON o."Id" = d."OrganizationId"
            WHERE d."OrganizationId" = {org} AND d."TeamId" = {settings.TeamId} AND i."Enabled" AND o."Status" = 'Active'
              AND d."NextAttemptAt" <= clock_timestamp() AND (d."SentStatus" IS NULL OR d."SentStatus" <> lower(a."Status"))
            ORDER BY d."NextAttemptAt", d."Id" LIMIT 1 FOR UPDATE OF d SKIP LOCKED
            """).ToListAsync(ct);
        var delivery = rows.SingleOrDefault();
        if (delivery is null) { await transaction.CommitAsync(ct); return; }
        var approval = await db.ApprovalRequests.AsNoTracking().SingleAsync(x => x.Id == delivery.ApprovalId && x.OrganizationId == org, ct);
        if (delivery.MessageTs is null && approval.Status != ApprovalStatus.Pending)
        { delivery.SentStatus = approval.Status.ToString().ToLowerInvariant(); delivery.LastError = null; delivery.Attempts = 0; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return; }
        var action = await db.AgentActions.AsNoTracking().Include(x => x.Approval).SingleAsync(x => x.Id == approval.ActionId && x.OrganizationId == org, ct);
        var name = await db.Agents.Where(x => x.OrganizationId == org && x.Id == action.AgentId).Select(x => x.Name).SingleAsync(ct);
        var detail = new ApprovalDetailDto(ApprovalStore.Summary(approval, action, name), ActionStore.MapDetail(action, name), false, null, "", []);
        delivery.Attempts++;
        var retryAfter = 0;
        try
        {
            var initial = delivery.MessageTs is null;
            delivery.MessageTs = await client.SendAsync(delivery.ChannelId, delivery.MessageTs, detail, delivery.Id, ct);
            delivery.SentStatus = detail.Approval.Status; delivery.LastError = null; delivery.Attempts = 0;
            audit.Record(org, initial ? "approval.slack_sent" : "approval.slack_updated", AuditActorType.System, null,
                new { delivery.Id, delivery.ChannelId, status = detail.Approval.Status }, action.AgentId, action.Id, approval.Id);
        }
        catch (Exception ex) when (ex is RequestException or HttpRequestException or JsonException or TaskCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            retryAfter = ex is SlackApiException slack ? slack.RetryAfterSeconds : 0;
            delivery.LastError = "Slack delivery failed. Check credentials, bot scopes and channel membership.";
        }
        delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delivery.LastError is null ? 1 : Math.Max(retryAfter, Math.Min(300, 5 * Math.Pow(2, Math.Min(delivery.Attempts, 6)))));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task DispatchFeedbackAsync(CancellationToken ct)
    {
        if (!settings.Configured) return;
        var org = settings.OrganizationId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.SlackFeedback.FromSqlInterpolated($"""
            SELECT f.* FROM "SlackFeedback" f JOIN "SlackIntegrations" i ON i."OrganizationId" = f."OrganizationId"
            JOIN "Organizations" o ON o."Id" = f."OrganizationId"
            WHERE f."OrganizationId" = {org} AND i."Enabled" AND o."Status" = 'Active'
              AND f."SentAt" IS NULL AND f."ExpiresAt" > clock_timestamp() AND f."NextAttemptAt" <= clock_timestamp()
            ORDER BY f."NextAttemptAt" LIMIT 1 FOR UPDATE OF f SKIP LOCKED
            """).ToListAsync(ct);
        var feedback = rows.SingleOrDefault();
        if (feedback is not null)
        {
            try { await client.FeedbackAsync(feedback.ChannelId, feedback.SlackUserId, feedback.Text, ct); feedback.SentAt = DateTimeOffset.UtcNow; }
            catch (Exception ex) when (ex is RequestException or HttpRequestException or JsonException or TaskCanceledException)
            { ct.ThrowIfCancellationRequested(); feedback.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(ex is SlackApiException slack ? Math.Max(30, slack.RetryAfterSeconds) : 30); }
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
