using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AgentGate.Application.Accounts;
using AgentGate.Domain.Accounts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AgentGate.Api.Authentication;

public static class AuthSetup
{
    public static IServiceCollection AddAccountAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var secret = config["JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Configure JWT_SECRET with at least 32 random bytes in the environment or local .env.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        services.AddSingleton(new JwtSettings(key, config["JWT_ISSUER"] ?? "AgentGate", config["JWT_AUDIENCE"] ?? "AgentGate.Dashboard"));
        services.AddScoped<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAccount, CurrentAccount>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                ValidateIssuer = true, ValidIssuer = config["JWT_ISSUER"] ?? "AgentGate",
                ValidateAudience = true, ValidAudience = config["JWT_AUDIENCE"] ?? "AgentGate.Dashboard",
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(15), ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                RoleClaimType = ClaimTypes.Role, NameClaimType = "sub"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal!;
                    if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId) ||
                        !Guid.TryParse(principal.FindFirstValue("org"), out var orgId) ||
                        !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)) { context.Fail("Invalid session."); return; }
                    var account = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
                    var role = await account.ValidateSessionAsync(userId, orgId, sessionId, context.HttpContext.RequestAborted);
                    if (role is null) { context.Fail("Session is no longer valid."); return; }
                    var identity = (ClaimsIdentity)principal.Identity!;
                    foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray()) identity.RemoveClaim(claim);
                    identity.AddClaim(new Claim(ClaimTypes.Role, role.ToString()!));
                }
            };
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy("ManageOrganization", policy => policy.RequireRole("Owner", "Admin"));
            options.AddPolicy("ManageAgents", policy => policy.RequireRole("Owner", "Admin", "Developer"));
            options.AddPolicy("ReviewActions", policy => policy.RequireRole("Owner", "Admin", "Reviewer"));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("Auth:PermitLimit", 30), Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.AddPolicy("agent", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("AgentAuth:PermitLimit", 120), Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        return services;
    }
}
public sealed record JwtSettings(SecurityKey Key, string Issuer, string Audience);
public sealed class AccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    public TokenDto Issue(AuthSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(15);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new("sub", session.UserId.ToString()), new("org", session.OrganizationId.ToString()), new("sid", session.Id.ToString()), new("jti", Guid.NewGuid().ToString())],
            now.UtcDateTime, expiry.UtcDateTime, new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }
}
public sealed class CurrentAccount(IHttpContextAccessor accessor) : ICurrentAccount
{
    public Guid UserId => Read("sub");
    public Guid OrganizationId => Read("org");
    public Guid SessionId => Read("sid");
    private Guid Read(string claim) => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(claim), out var id) ? id : throw new AccountException(401, "Authentication required.");
}
