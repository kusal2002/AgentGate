using System.ComponentModel.DataAnnotations;
using AgentGate.Domain.Accounts;

namespace AgentGate.Application.Accounts;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, StringLength(100)] string Name,
    [Required, StringLength(100)] string OrganizationName);
public sealed record LoginRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password, Guid? OrganizationId = null);
public sealed record OrganizationRequest([Required, StringLength(100)] string Name);
public sealed record MemberRequest([Required, EmailAddress, StringLength(254)] string Email, [Required] string Role);
public sealed record RoleRequest([Required] string Role);
public sealed record SwitchRequest(Guid OrganizationId);
public sealed record UserDto(Guid Id, string Email, string Name);
public sealed record OrganizationDto(Guid Id, string Name, string Slug, string Status, string Role);
public sealed record MemberDto(Guid Id, Guid UserId, string Email, string Name, string Role);
public sealed record AccountDto(UserDto User, OrganizationDto Organization);
public sealed record TokenDto(string AccessToken, DateTimeOffset ExpiresAt);
public sealed record SessionResult(AccountDto Account, TokenDto Token, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public sealed class AccountException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public interface ICurrentAccount
{
    Guid UserId { get; }
    Guid OrganizationId { get; }
    Guid SessionId { get; }
}
public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}
public interface IAccessTokenIssuer { TokenDto Issue(AuthSession session); }
public interface IAccountStore
{
    Task<User?> FindUserAsync(string email, CancellationToken ct);
    Task<User?> GetUserAsync(Guid id, CancellationToken ct);
    Task<Organization?> GetOrganizationAsync(Guid id, CancellationToken ct);
    Task<OrganizationUser?> GetMembershipAsync(Guid orgId, Guid userId, CancellationToken ct);
    Task<OrganizationUser?> GetMemberAsync(Guid orgId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrganizationDto>> GetOrganizationsAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<MemberDto>> GetMembersAsync(Guid orgId, CancellationToken ct);
    Task<AuthSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<AuthSession?> FindSessionAsync(string hash, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken ct);
    Task<bool> RotateSessionAsync(Guid oldId, AuthSession next, CancellationToken ct);
    Task RevokeSessionAsync(Guid id, CancellationToken ct);
    Task RecordFailureAsync(Guid userId, CancellationToken ct);
}
