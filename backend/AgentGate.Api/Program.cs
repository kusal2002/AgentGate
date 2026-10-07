using AgentGate.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using AgentGate.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAccountAuthentication(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AccountExceptionHandler>();
var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
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

// Preserve the existing prototype for local development only. It has no authentication
// or persistence and must not be exposed as a production authorization gateway.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/v1/actions/evaluate",
        (ActionRequest request) => Evaluate(request));
}

app.Run();

static IResult Evaluate(ActionRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Action)
        || request.Parameters is null
        || request.Parameters.AmountMinor is null
        || request.Parameters.AmountMinor <= 0
        || string.IsNullOrWhiteSpace(request.Parameters.Currency))
    {
        return Results.BadRequest(new
        {
            error = "Provide action, a positive amountMinor, and currency."
        });
    }

    // This first demo supports USD refunds only.
    if (request.Action != "refund"
        || request.Parameters.Currency != "USD")
    {
        return Results.Ok(new
        {
            decision = "DENY",
            reason = "No policy permits this action or currency."
        });
    }

    long amount = request.Parameters.AmountMinor.Value;

    var result = amount switch
    {
        <= 10_000 => (
            Decision: "ALLOW",
            ReviewerRole: (string?)null,
            Reason: "Refund is within the automatic approval limit."
        ),

        <= 100_000 => (
            Decision: "REVIEW",
            ReviewerRole: (string?)"support_manager",
            Reason: "Refund requires Support Manager approval."
        ),

        <= 500_000 => (
            Decision: "REVIEW",
            ReviewerRole: (string?)"finance_manager",
            Reason: "Refund requires Finance Manager approval."
        ),

        _ => (
            Decision: "DENY",
            ReviewerRole: (string?)null,
            Reason: "Refund exceeds the permitted limit."
        )
    };

    return Results.Ok(new
    {
        decision = result.Decision,
        reviewerRole = result.ReviewerRole,
        reason = result.Reason,
        policyVersion = "refund-demo-v1"
    });
}

public record ActionRequest(
    string? Action,
    ActionParameters? Parameters
);

public record ActionParameters(
    long? AmountMinor,
    string? Currency
);

public partial class Program;
