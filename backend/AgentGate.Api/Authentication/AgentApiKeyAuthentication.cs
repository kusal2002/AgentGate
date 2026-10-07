using System.Security.Claims;
using System.Text.Encodings.Web;
using AgentGate.Application.Agents;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgentGate.Api.Authentication;

public static class AgentApiKeyAuthentication
{
    public const string Scheme = "AgentApiKey";
    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AgentApiKeyHandler>(Scheme, _ => { });
        services.AddAuthorization(options => options.AddPolicy("AgentIdentity", policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("actor", "agent")));
        return services;
    }
}
public sealed class AgentApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IAgentKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        if (header.Length > 256) return AuthenticateResult.Fail("Invalid agent credentials.");
        var identity = await authenticator.AuthenticateAsync(header[7..], Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("Invalid agent credentials.");
        var claims = new ClaimsIdentity([new("actor", "agent"), new("agent_id", identity.AgentId.ToString()), new("org", identity.OrganizationId.ToString()), new("key_id", identity.ApiKeyId.ToString()), new("environment", identity.Environment), new("version", identity.Version), new(ClaimTypes.Name, identity.Name)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name));
    }
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401; Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(statusCode: 401, title: "Agent API key required", detail: "Provide a valid, active agent API key.").ExecuteAsync(Context);
    }
}
