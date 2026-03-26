using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class WindowsAccountHelperTests
{
    [Fact]
    public void GetAdminAuthorizedKeysPath_ContainsSshAndAdminKeys()
    {
        var path = WindowsAccountHelper.GetAdminAuthorizedKeysPath();

        Assert.Contains("ssh", path);
        Assert.Contains("administrators_authorized_keys", path);
    }

    [Fact]
    public void GetMatchGroupBlock_ContainsMatchGroupAdministrators()
    {
        var block = WindowsAccountHelper.GetMatchGroupBlock();

        Assert.Contains("Match Group administrators", block);
    }

    [Fact]
    public void GetMatchGroupBlock_ContainsProgramDataPlaceholder()
    {
        var block = WindowsAccountHelper.GetMatchGroupBlock();

        Assert.Contains("__PROGRAMDATA__", block);
    }

    [Fact]
    public void SshdConfigHasMatchBlock_WhenBlockPresent_ReturnsTrue()
    {
        var content = """
            Port 22
            PermitRootLogin no
            Match Group administrators
                AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
            """;

        Assert.True(WindowsAccountHelper.SshdConfigHasMatchBlock(content));
    }

    [Fact]
    public void SshdConfigHasMatchBlock_WhenBlockAbsent_ReturnsFalse()
    {
        var content = """
            Port 22
            PermitRootLogin no
            PubkeyAuthentication yes
            """;

        Assert.False(WindowsAccountHelper.SshdConfigHasMatchBlock(content));
    }

    [Fact]
    public void SshdConfigHasMatchBlock_WhenOnlyPartialMatch_ReturnsFalse()
    {
        var content = """
            Port 22
            Match Group administrators
            """;

        // Has Match Group but not administrators_authorized_keys
        Assert.False(WindowsAccountHelper.SshdConfigHasMatchBlock(content));
    }

    [SkippableFact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    public void IsMicrosoftLinkedAccount_RunsWithoutError()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "This test requires Windows");

        // Should not throw - just verify it runs
        var result = WindowsAccountHelper.IsMicrosoftLinkedAccount();

        // Result is either true or false depending on the machine
        Assert.IsType<bool>(result);
    }
}
