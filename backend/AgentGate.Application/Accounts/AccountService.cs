using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentGate.Domain.Accounts;

namespace AgentGate.Application.Accounts;

public sealed class AccountService(IAccountStore store, IPasswordService passwords, IAccessTokenIssuer tokens)
{
    public async Task<SessionResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (await store.FindUserAsync(email, ct) is not null) throw new AccountException(409, "This account cannot be registered. Try signing in.");
        var user = new User { Email = email, Name = CleanName(request.Name) };
        user.PasswordHash = passwords.Hash(user, request.Password);
        var org = NewOrganization(request.OrganizationName);
        var membership = new OrganizationUser { OrganizationId = org.Id, UserId = user.Id, Role = OrganizationRole.Owner };
        var (session, refresh) = NewSession(user.Id, org.Id);
        store.Add(user); store.Add(org); store.Add(membership); store.Add(session);
        await store.SaveAsync(ct); // One atomic commit for the account, organization, owner, and session.
        return Result(user, org, membership, session, refresh);
    }

    public async Task<SessionResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await store.FindUserAsync(NormalizeEmail(request.Email), ct);
        // Always use the same response for an unknown user, bad password, or locked account.
        if (user is null) { passwords.Verify(new User(), request.Password); throw Unauthorized(); }
        if (user.LockedUntil > DateTimeOffset.UtcNow) throw Unauthorized();
        if (!passwords.Verify(user, request.Password)) { await store.RecordFailureAsync(user.Id, ct); throw Unauthorized(); }
        var organizations = await store.GetOrganizationsAsync(user.Id, ct);
        var orgId = request.OrganizationId ?? organizations.FirstOrDefault()?.Id ?? throw Unauthorized();
        var membership = await store.GetMembershipAsync(orgId, user.Id, ct) ?? throw Unauthorized();
        var org = await ActiveOrganization(orgId, ct);
        user.FailedLoginAttempts = 0; user.LockedUntil = null;
        var (session, refresh) = NewSession(user.Id, org.Id);
        store.Add(session); await store.SaveAsync(ct);
        return Result(user, org, membership, session, refresh);
    }

    public async Task<SessionResult> RefreshAsync(string refresh, CancellationToken ct)
    {
        if (refresh.Length > 256) throw Unauthorized();
        var old = await store.FindSessionAsync(HashToken(refresh), ct);
        if (old is null || old.RevokedAt is not null || old.ExpiresAt <= DateTimeOffset.UtcNow) throw Unauthorized();
        var user = await store.GetUserAsync(old.UserId, ct) ?? throw Unauthorized();
        var membership = await store.GetMembershipAsync(old.OrganizationId, old.UserId, ct) ?? throw Unauthorized();
        var org = await ActiveOrganization(old.OrganizationId, ct);
        var (next, token) = NewSession(user.Id, org.Id);
        if (!await store.RotateSessionAsync(old.Id, next, ct)) throw Unauthorized();
        return Result(user, org, membership, next, token);
    }

    public async Task LogoutAsync(string? refresh, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refresh) || refresh.Length > 256) return;
        var session = await store.FindSessionAsync(HashToken(refresh), ct);
        if (session is not null) await store.RevokeSessionAsync(session.Id, ct);
    }

    public async Task<OrganizationRole?> ValidateSessionAsync(Guid userId, Guid orgId, Guid sessionId, CancellationToken ct)
    {
        var session = await store.GetSessionAsync(sessionId, ct);
        if (session is null || session.UserId != userId || session.OrganizationId != orgId || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var org = await store.GetOrganizationAsync(orgId, ct);
        if (org?.Status != OrganizationStatus.Active) return null;
        return (await store.GetMembershipAsync(orgId, userId, ct))?.Role;
    }

    public async Task<AccountDto> MeAsync(ICurrentAccount current, CancellationToken ct)
    {
        var user = await store.GetUserAsync(current.UserId, ct) ?? throw Unauthorized();
        var org = await ActiveOrganization(current.OrganizationId, ct);
        var member = await Membership(current, ct);
        return Account(user, org, member);
    }
    public Task<IReadOnlyList<OrganizationDto>> OrganizationsAsync(ICurrentAccount current, CancellationToken ct) => store.GetOrganizationsAsync(current.UserId, ct);
    public Task<IReadOnlyList<MemberDto>> MembersAsync(ICurrentAccount current, CancellationToken ct) => store.GetMembersAsync(current.OrganizationId, ct);

    public async Task<SessionResult> CreateOrganizationAsync(ICurrentAccount current, OrganizationRequest request, CancellationToken ct)
    {
        var user = await store.GetUserAsync(current.UserId, ct) ?? throw Unauthorized();
        var org = NewOrganization(request.Name);
        var member = new OrganizationUser { UserId = user.Id, OrganizationId = org.Id, Role = OrganizationRole.Owner };
        store.Add(org); store.Add(member); await store.SaveAsync(ct);
        return await SwitchAsync(current, new SwitchRequest(org.Id), ct);
    }

    public async Task<SessionResult> SwitchAsync(ICurrentAccount current, SwitchRequest request, CancellationToken ct)
    {
        var member = await store.GetMembershipAsync(request.OrganizationId, current.UserId, ct) ?? throw new AccountException(404, "Organization not found.");
        var org = await ActiveOrganization(member.OrganizationId, ct);
        var user = await store.GetUserAsync(current.UserId, ct) ?? throw Unauthorized();
        var (next, refresh) = NewSession(user.Id, org.Id);
        if (!await store.RotateSessionAsync(current.SessionId, next, ct)) throw Unauthorized();
        return Result(user, org, member, next, refresh);
    }

    public async Task RenameAsync(ICurrentAccount current, OrganizationRequest request, CancellationToken ct)
    {
        await RequireManager(current, ct);
        var org = await ActiveOrganization(current.OrganizationId, ct);
        org.Name = CleanName(request.Name); org.UpdatedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(ct);
    }

    public async Task AddMemberAsync(ICurrentAccount current, MemberRequest request, CancellationToken ct)
    {
        var actor = await RequireManager(current, ct);
        var role = ParseRole(request.Role);
        CheckRoleChange(actor.Role, role, null);
        var user = await store.FindUserAsync(NormalizeEmail(request.Email), ct) ?? throw new AccountException(404, "The user must register an account before being added.");
        if (await store.GetMembershipAsync(current.OrganizationId, user.Id, ct) is not null) throw new AccountException(409, "User is already a member.");
        store.Add(new OrganizationUser { OrganizationId = current.OrganizationId, UserId = user.Id, Role = role });
        await store.SaveAsync(ct);
    }

    public async Task ChangeRoleAsync(ICurrentAccount current, Guid memberId, RoleRequest request, CancellationToken ct)
    {
        var actor = await RequireManager(current, ct);
        var target = await store.GetMemberAsync(current.OrganizationId, memberId, ct) ?? throw new AccountException(404, "Member not found.");
        var role = ParseRole(request.Role);
        CheckRoleChange(actor.Role, role, target.Role);
        target.Role = role; await store.SaveAsync(ct);
    }

    public static void CheckRoleChange(OrganizationRole actor, OrganizationRole next, OrganizationRole? existing)
    {
        if (actor is not (OrganizationRole.Owner or OrganizationRole.Admin) || next == OrganizationRole.Owner || existing == OrganizationRole.Owner || (actor == OrganizationRole.Admin && (next == OrganizationRole.Admin || existing == OrganizationRole.Admin)))
            throw new AccountException(403, "You cannot assign or change this role.");
    }
    private async Task<OrganizationUser> Membership(ICurrentAccount current, CancellationToken ct) => await store.GetMembershipAsync(current.OrganizationId, current.UserId, ct) ?? throw Unauthorized();
    private async Task<OrganizationUser> RequireManager(ICurrentAccount current, CancellationToken ct)
    {
        var member = await Membership(current, ct);
        if (member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin)) throw new AccountException(403, "Owner or Admin role required.");
        return member;
    }
    private async Task<Organization> ActiveOrganization(Guid id, CancellationToken ct)
    {
        var org = await store.GetOrganizationAsync(id, ct);
        if (org?.Status != OrganizationStatus.Active) throw Unauthorized();
        return org;
    }
    private SessionResult Result(User user, Organization org, OrganizationUser member, AuthSession session, string refresh) => new(Account(user, org, member), tokens.Issue(session), refresh, session.ExpiresAt);
    private static AccountDto Account(User user, Organization org, OrganizationUser member) => new(new(user.Id, user.Email, user.Name), new(org.Id, org.Name, org.Slug, org.Status.ToString(), member.Role.ToString()));
    private static (AuthSession, string) NewSession(Guid user, Guid org)
    {
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        return (new AuthSession { UserId = user, OrganizationId = org, RefreshTokenHash = HashToken(refresh), ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) }, refresh);
    }
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static OrganizationRole ParseRole(string role) => Enum.TryParse<OrganizationRole>(role, false, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(role, out _) ? parsed : throw new AccountException(400, "Invalid organization role.");
    private static Organization NewOrganization(string name)
    {
        name = CleanName(name);
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return new Organization { Name = name, Slug = (slug.Length > 0 ? slug : "organization") + "-" + Guid.NewGuid().ToString("N")[..8] };
    }
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string CleanName(string name) => !string.IsNullOrWhiteSpace(name) ? name.Trim() : throw new AccountException(400, "Name is required.");
    private static AccountException Unauthorized() => new(401, "Invalid credentials or session.");
}
