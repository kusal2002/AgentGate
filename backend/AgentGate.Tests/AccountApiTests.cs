using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentGate.Domain.Accounts;
using AgentGate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using AgentGate.Api.Authentication;

namespace AgentGate.Tests;

public sealed class AccountApiTests(AccountApiFactory factory) : IClassFixture<AccountApiFactory>
{
    private const string Password = "test-password-with-16-characters";
    private async Task<(HttpClient Client, JsonElement Session, string Email)> Register()
    {
        var client = factory.NewClient();
        var email = $"test-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password, name = "Test User", organizationName = "Test Organization" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        Assert.Contains("httponly", response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant());
        Assert.Contains("samesite=strict", response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return (client, session, email);
    }
    [Fact]
    public async Task RegistrationValidatesAndStoresOnlyPasswordHash()
    {
        var (client, session, email) = await Register();
        Assert.Equal("Owner", session.GetProperty("organization").GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = Password, name = "User", organizationName = "Org" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = "bad-email", password = "short", name = "User", organizationName = "Org" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        Assert.NotEqual(Password, (await db.Users.SingleAsync(x => x.Email == email)).PasswordHash);
        Assert.All(await db.AuthSessions.Where(x => x.UserId == session.GetProperty("user").GetProperty("id").GetGuid()).ToListAsync(), x => Assert.Equal(64, x.RefreshTokenHash.Length));
    }
    [Fact]
    public async Task RefreshRotatesAtomicallyAndLogoutRevokesAccess()
    {
        var (client, original, _) = await Register();
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var next = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", next.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }
    [Fact]
    public async Task ConcurrentRefreshAllowsOneWinnerAndRejectsReplay()
    {
        var (client, _, _) = await Register();
        // Capture a single current refresh cookie without sharing rotating cookie containers.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("user").GetProperty("email").GetString(), password = Password });
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        async Task<HttpResponseMessage> Refresh()
        {
            var separate = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
            separate.DefaultRequestHeaders.Add("Cookie", cookie);
            separate.DefaultRequestHeaders.Add("X-AgentGate-Client", "dashboard");
            return await separate.PostAsync("/api/auth/refresh", null);
        }
        var results = await Task.WhenAll(Refresh(), Refresh());
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh()).StatusCode);
    }
    [Fact]
    public async Task TenantIsolationBlocksForeignMembershipsAndOrganizationSwitch()
    {
        var (a, aSession, _) = await Register();
        var (b, bSession, _) = await Register();
        var organizations = await a.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Single(organizations.EnumerateArray());
        Assert.Equal(aSession.GetProperty("organization").GetProperty("id").GetGuid(), organizations[0].GetProperty("id").GetGuid());
        var foreignMembers = await b.GetFromJsonAsync<JsonElement>("/api/organizations/current/members");
        var foreignId = foreignMembers[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await a.PatchAsJsonAsync($"/api/organizations/current/members/{foreignId}", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = bSession.GetProperty("organization").GetProperty("id").GetGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PatchAsJsonAsync($"/api/organizations/current/members/{foreignId}", new { role = "Viewer" })).StatusCode);
    }
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Developer", false)]
    [InlineData("Reviewer", false)]
    [InlineData("Viewer", false)]
    public async Task CurrentDatabaseRolesEnforceOrganizationManagement(string role, bool canManage)
    {
        var (owner, ownerSession, _) = await Register();
        var (member, _, email) = await Register();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/organizations/current/members", new { email, role })).StatusCode);
        var switchResponse = await member.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = ownerSession.GetProperty("organization").GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);
        var switched = await switchResponse.Content.ReadFromJsonAsync<JsonElement>();
        member.DefaultRequestHeaders.Authorization = new("Bearer", switched.GetProperty("accessToken").GetString());
        Assert.Equal(canManage ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, (await member.PatchAsJsonAsync("/api/organizations/current", new { name = "Renamed" })).StatusCode);
        var members = await owner.GetFromJsonAsync<JsonElement>("/api/organizations/current/members");
        var membership = members.EnumerateArray().Single(x => x.GetProperty("email").GetString() == email);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/organizations/current/members/{membership.GetProperty("id").GetGuid()}", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PatchAsJsonAsync("/api/organizations/current", new { name = "Unauthorized rename" })).StatusCode);
    }
    [Fact]
    public async Task InvalidCredentialsLockoutCsrfAndTamperingAreRejected()
    {
        var (client, session, email) = await Register();
        var noHeader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await noHeader.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noHeader.GetAsync("/api/organizations/current")).StatusCode);
        noHeader.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString() + "tampered");
        Assert.Equal(HttpStatusCode.Unauthorized, (await noHeader.GetAsync("/api/auth/me")).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
    }
    [Fact]
    public async Task ExpiredSessionsAndSuspendedOrganizationsCannotAccessOrRefresh()
    {
        var (client, session, _) = await Register();
        var orgId = session.GetProperty("organization").GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentGateDbContext>();
        await db.Organizations.Where(x => x.Id == orgId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, OrganizationStatus.Suspended));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        await db.Organizations.Where(x => x.Id == orgId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, OrganizationStatus.Active));
        await db.AuthSessions.Where(x => x.OrganizationId == orgId).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }
    [Fact]
    public async Task JwtExpiryIssuerAndAudienceAreValidated()
    {
        var (client, session, _) = await Register();
        var settings = factory.Services.GetRequiredService<JwtSettings>();
        var original = new JwtSecurityTokenHandler().ReadJwtToken(session.GetProperty("accessToken").GetString());
        foreach (var variant in new[] { "expired", "issuer", "audience" })
        {
            var jwt = new JwtSecurityToken(variant == "issuer" ? "foreign-issuer" : settings.Issuer,
                variant == "audience" ? "foreign-audience" : settings.Audience, original.Claims.Where(claim => claim.Type is not ("exp" or "nbf" or "iat" or "iss" or "aud")),
                DateTime.UtcNow.AddHours(-1), variant == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(15),
                new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
    }
    [Fact]
    public async Task CreateOrganizationAndSwitchRetainSeparateMemberships()
    {
        var (client, first, _) = await Register();
        var created = await client.PostAsJsonAsync("/api/auth/organizations", new { name = "Second Workspace" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var next = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(first.GetProperty("organization").GetProperty("id").GetGuid(), next.GetProperty("organization").GetProperty("id").GetGuid());
        Assert.Equal("Owner", next.GetProperty("organization").GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", next.GetProperty("accessToken").GetString());
        var organizations = await client.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Equal(2, organizations.GetArrayLength());
        var switched = await client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = first.GetProperty("organization").GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
    }
    [Fact]
    public async Task AuthenticationRateLimitReturns429()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:PermitLimit"] = "2" })));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgentGate-Client", "dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }
}
