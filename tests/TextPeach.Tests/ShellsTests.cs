using TextPeach.Services;

namespace TextPeach.Tests;

public class ShellsTests
{
    private const string EtcShells = "# /etc/shells: valid login shells\n/bin/sh\n/bin/bash\n/usr/bin/bash\n/usr/bin/zsh\n/usr/sbin/nologin\n/usr/bin/fish\n";

    [Fact]
    public void UserShellFirstOnePerNameOnlyThoseInstalled()
    {
        var installed = new HashSet<string> { "/bin/sh", "/bin/bash", "/usr/bin/bash", "/usr/bin/zsh", "/usr/sbin/nologin" };
        var shells = Shells.FromEtcShells(EtcShells, "/usr/bin/zsh", installed.Contains);
        Assert.Equal(new[] { "zsh", "sh", "bash" }, shells.Select(s => s.Name));
        Assert.Equal("/bin/bash", shells.Single(s => s.Id == "bash").Path);
    }

    [Fact]
    public void NoEtcShellsStillHasTheUsersShell()
    {
        var shells = Shells.FromEtcShells("", "/bin/bash", _ => true);
        Assert.Equal("bash", Assert.Single(shells).Id);
    }
}
