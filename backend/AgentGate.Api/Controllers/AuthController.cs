using AgentGate.Api.Authentication;
using AgentGate.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentGate.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(AccountService accounts, ICurrentAccount current, IWebHostEnvironment environment) : ControllerBase
{
    private const string CookieName = "agentgate.refresh";
    [HttpPost("register"), AllowAnonymous, AuthClient, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) => Session(await accounts.RegisterAsync(request, ct));
    [HttpPost("login"), AllowAnonymous, AuthClient, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) => Session(await accounts.LoginAsync(request, ct));
    [HttpPost("refresh"), AllowAnonymous, AuthClient, EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(CancellationToken ct) => Session(await accounts.RefreshAsync(Request.Cookies[CookieName] ?? "", ct));
    [HttpPost("logout"), AllowAnonymous, AuthClient]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await accounts.LogoutAsync(Request.Cookies[CookieName], ct);
        Response.Cookies.Delete(CookieName, CookieOptions());
        return NoContent();
    }
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await accounts.MeAsync(current, ct));
    [HttpPost("switch-organization"), Authorize, AuthClient]
    public async Task<IActionResult> Switch(SwitchRequest request, CancellationToken ct) => Session(await accounts.SwitchAsync(current, request, ct));
    [HttpPost("organizations"), Authorize, AuthClient]
    public async Task<IActionResult> CreateOrganization(OrganizationRequest request, CancellationToken ct) => Session(await accounts.CreateOrganizationAsync(current, request, ct));
    private IActionResult Session(SessionResult result)
    {
        var options = CookieOptions(); options.Expires = result.RefreshExpiresAt;
        Response.Cookies.Append(CookieName, result.RefreshToken, options);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { result.Token.AccessToken, result.Token.ExpiresAt, result.Account.User, result.Account.Organization });
    }
    private CookieOptions CookieOptions() => new() { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = "/api/auth", IsEssential = true };
}
