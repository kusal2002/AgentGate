using AgentGate.Application.Accounts;
using AgentGate.Domain.Accounts;
using Microsoft.AspNetCore.Identity;

namespace AgentGate.Infrastructure.Accounts;

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> hasher = new();
    private readonly string dummyHash;
    public PasswordService() => dummyHash = hasher.HashPassword(new User(), Guid.NewGuid().ToString());
    public string Hash(User user, string password) => hasher.HashPassword(user, password);
    public bool Verify(User user, string password)
    {
        var result = hasher.VerifyHashedPassword(user, string.IsNullOrEmpty(user.PasswordHash) ? dummyHash : user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = Hash(user, password);
        return result != PasswordVerificationResult.Failed;
    }
}
