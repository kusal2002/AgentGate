using AgentGate.Application.Accounts;
using AgentGate.Domain.Accounts;

namespace AgentGate.Tests;

public sealed class RoleRulesTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Admin, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Viewer, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Viewer, OrganizationRole.Admin, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Admin, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Reviewer, OrganizationRole.Developer, true)]
    [InlineData(OrganizationRole.Developer, OrganizationRole.Reviewer, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Reviewer, OrganizationRole.Viewer, OrganizationRole.Developer, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Developer, OrganizationRole.Reviewer, false)]
    public void RoleChangesProtectOwnershipAndPrivilege(OrganizationRole actor, OrganizationRole next, OrganizationRole existing, bool allowed)
    {
        var exception = Record.Exception(() => AccountService.CheckRoleChange(actor, next, existing));
        if (allowed) Assert.Null(exception);
        else Assert.Equal(403, Assert.IsType<AccountException>(exception).StatusCode);
    }
}
