using System.Security.Claims;
using AgentGate.Application.Actions;
using AgentGate.Application.Errors;

namespace AgentGate.Api.Authentication;

public sealed class CurrentAgent(IHttpContextAccessor accessor) : ICurrentAgent
{
    public Guid OrganizationId => Read("org");
    public Guid AgentId => Read("agent_id");
    public string Environment => Principal.FindFirstValue("environment") ?? throw new RequestException(401, "Agent authentication required.");
    private ClaimsPrincipal Principal => accessor.HttpContext?.User is { } user && user.Identity?.AuthenticationType == AgentApiKeyAuthentication.Scheme
        ? user : throw new RequestException(401, "Agent authentication required.");
    private Guid Read(string claim) => Guid.TryParse(Principal.FindFirstValue(claim), out var id) ? id : throw new RequestException(401, "Agent authentication required.");
}
