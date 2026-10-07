using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgentGate.Api.Authentication;

// A cross-origin form cannot attach this header. Combined with JSON-only bodies,
// SameSite=Strict cookies, and no cross-origin CORS grants, it blocks cookie CSRF.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthClientAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Request.Headers["X-AgentGate-Client"] != "dashboard")
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Client header required." }) { StatusCode = 403 };
    }
}
