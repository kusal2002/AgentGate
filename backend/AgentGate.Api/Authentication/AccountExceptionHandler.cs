using AgentGate.Application.Accounts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Authentication;

public sealed class AccountExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not AccountException account) return false;
        context.Response.StatusCode = account.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = account.StatusCode, Title = "Request could not be completed", Detail = account.Message }
        });
    }
}
