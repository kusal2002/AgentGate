using AgentGate.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using AgentGate.Api.Authentication;
using AgentGate.Application.Actions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAccountAuthentication(builder.Configuration);
builder.Services.AddAgentAuthentication();
builder.Services.AddScoped<ICurrentAgent, CurrentAgent>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AccountExceptionHandler>();
builder.Services.AddHostedService<AgentGate.Api.Approvals.ApprovalExpiryWorker>();
builder.Services.AddHostedService<AgentGate.Api.Slack.SlackWorker>();
builder.Services.AddScoped<AgentGate.Api.Slack.SlackSignatureFilter>();
var app = builder.Build();
// Validate configured fallback decisions before accepting requests.
app.Services.GetRequiredService<AgentGate.Application.Policies.PolicyDefaults>();
app.Services.GetRequiredService<AgentGate.Application.Approvals.ApprovalSettings>();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "AgentGate"
}));

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            service = "AgentGate",
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString().ToLowerInvariant()
            })
        }, context.RequestAborted)
});

app.Run();

public partial class Program;
