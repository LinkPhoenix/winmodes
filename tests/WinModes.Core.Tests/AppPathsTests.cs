using System.Security.Principal;
using WinModes.Core.Engine;

namespace WinModes.Core.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData(WellKnownSidType.BuiltinAdministratorsSid, true)]
    [InlineData(WellKnownSidType.LocalSystemSid, true)]
    [InlineData(WellKnownSidType.BuiltinUsersSid, false)]
    [InlineData(WellKnownSidType.WorldSid, false)]
    [InlineData(WellKnownSidType.AuthenticatedUserSid, false)]
    public void IsTrustedOwner_AcceptsOnlyAdministratorsAndSystem(WellKnownSidType kind, bool expected) =>
        Assert.Equal(expected, AppPaths.IsTrustedOwner(new SecurityIdentifier(kind, null)));

    [Fact]
    public void IsTrustedOwner_RefusesAnOrdinaryUserAndAMissingOwner()
    {
        Assert.False(AppPaths.IsTrustedOwner(WindowsIdentity.GetCurrent().User));
        Assert.False(AppPaths.IsTrustedOwner(null));
    }
}
