using AgentGate.Application.Approvals;
using AgentGate.Application.Errors;
using AgentGate.Domain.Accounts;

namespace AgentGate.Tests;

public sealed class ApprovalRulesTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, "Owner", true)]
    [InlineData(OrganizationRole.Owner, "Admin", true)]
    [InlineData(OrganizationRole.Owner, "Reviewer", true)]
    [InlineData(OrganizationRole.Admin, "Owner", false)]
    [InlineData(OrganizationRole.Admin, "Admin", true)]
    [InlineData(OrganizationRole.Admin, "Reviewer", true)]
    [InlineData(OrganizationRole.Reviewer, "Owner", false)]
    [InlineData(OrganizationRole.Reviewer, "Admin", false)]
    [InlineData(OrganizationRole.Reviewer, "Reviewer", true)]
    [InlineData(OrganizationRole.Developer, "Reviewer", false)]
    [InlineData(OrganizationRole.Viewer, "Reviewer", false)]
    [InlineData(OrganizationRole.Owner, "invalid", false)]
    public void ReviewerHierarchyIsExplicit(OrganizationRole actual, string required, bool allowed) => Assert.Equal(allowed, ApprovalRules.CanReview(actual, required));
    [Fact]
    public void CommentsAreBoundedAndTrimmed()
    {
        Assert.Equal("Checked", ApprovalRules.ValidateComment("  Checked  "));
        Assert.Equal("", ApprovalRules.ValidateComment(""));
        Assert.Throws<RequestException>(() => ApprovalRules.ValidateComment(new string('x', 2001)));
        Assert.Throws<RequestException>(() => ApprovalRules.ValidateComment("bad\0comment"));
        Assert.Throws<RequestException>(() => ApprovalRules.ValidateComment(null));
    }
}
