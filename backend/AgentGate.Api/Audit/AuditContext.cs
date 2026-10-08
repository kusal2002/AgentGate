using System.Security.Claims;
using AgentGate.Application.Audit;

namespace AgentGate.Api.Audit;

public sealed class AuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("sub"), out var id) ? id : null;
    // Do not accept spoofable X-Forwarded-For headers. Behind a tunnel this is the proxy address.
    public string? IPAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
