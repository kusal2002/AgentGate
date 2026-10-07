using AgentGate.Application.Slack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace AgentGate.Api.Slack;
// Resource filters run before MVC form value providers can consume the raw body.
public sealed class SlackSignatureFilter(SlackSettings settings) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request; var ct = context.HttpContext.RequestAborted;
        if (!settings.Configured) { context.Result = new StatusCodeResult(503); return; }
        if (request.ContentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) != true)
        { context.Result = new BadRequestResult(); return; }
        if (request.Headers["X-Slack-Request-Timestamp"].Count != 1 || request.Headers["X-Slack-Signature"].Count != 1)
        { context.Result = new UnauthorizedResult(); return; }
        request.EnableBuffering(); using var raw = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (raw.Length + read > 65536) { context.Result = new StatusCodeResult(413); return; }
            raw.Write(buffer, 0, read);
        }
        if (!SlackSecurity.Verify(settings.SigningSecret, request.Headers["X-Slack-Request-Timestamp"].ToString(),
            request.Headers["X-Slack-Signature"].ToString(), raw.ToArray(), DateTimeOffset.UtcNow))
        { context.Result = new UnauthorizedResult(); return; }
        request.Body.Position = 0; await next();
    }
}
