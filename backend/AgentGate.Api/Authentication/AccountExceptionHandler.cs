using AgentGate.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AgentGate.Api.Authentication;

public sealed class AccountExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, detail) = exception switch
        {
            RequestException request => (request.StatusCode, request.Message),
            BadHttpRequestException request => (request.StatusCode, request.StatusCode == 413 ? "Request body exceeds the permitted size." : "Invalid HTTP request."),
            _ => (0, "")
        };
        if (status == 0) return false;
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = "Request could not be completed", Detail = detail }
        });
    }
}
