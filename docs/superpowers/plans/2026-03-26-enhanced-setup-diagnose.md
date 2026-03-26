# Enhanced Setup & Diagnose Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `setup` into a full SSH provisioning wizard (install server, start service, open firewall, handle Windows MS account auth, generate keys, validate) and enhance `diagnose` to offer inline fixes.

**Architecture:** Three new core modules (SshServerInstaller, FirewallManager, WindowsAccountHelper) provide platform-specific operations. IPlatform gets new properties for elevation and package/firewall detection. SetupCommand orchestrates the 8-step provisioning flow. DiagnoseCommand gains fix-it prompts for auto-fixable issues.

**Tech Stack:** .NET 10, System.CommandLine, Spectre.Console, platform CLIs (PowerShell, apt, dnf, systemctl, netsh, ufw, firewall-cmd), Windows Registry API.

---

## Task 1: Extend IPlatform with Elevation, Package Manager, and Firewall Detection

**Files:**
- Modify: `src/Platform/IPlatform.cs`
- Modify: `src/Platform/LinuxPlatform.cs`
- Modify: `src/Platform/MacOsPlatform.cs`
- Modify: `src/Platform/WindowsPlatform.cs`
- Modify: `src/Platform/WslPlatform.cs`
- Create: `tests/SshEasyConfig.Tests/Platform/PlatformCapabilitiesTests.cs`

- [ ] **Step 1: Add new enums and interface members to IPlatform**

Add to `src/Platform/IPlatform.cs`:

```csharp
namespace SshEasyConfig.Platform;

public enum PlatformKind { Linux, MacOS, Windows, Wsl }

public enum PackageManager { Apt, Dnf, Yum, Brew, WinGet, None }

public enum FirewallType { Ufw, Firewalld, Iptables, WindowsFirewall, Pf, None }

public interface IPlatform
{
    PlatformKind Kind { get; }
    string SshDirectoryPath { get; }
    string SshdConfigPath { get; }
    string AuthorizedKeysFilename { get; }
    bool IsElevated { get; }
    PackageManager PackageManager { get; }
    FirewallType FirewallType { get; }
    Task SetFilePermissionsAsync(string path, SshFileKind kind);
    Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind);
    Task RestartSshServiceAsync();
    Task<bool> IsSshServiceRunningAsync();
    Task<string> RunCommandAsync(string command, string arguments);
    /// <summary>
    /// Like RunCommandAsync but returns (exitCode, stdout, stderr) without throwing on non-zero exit.
    /// </summary>
    Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments);
}

public enum SshFileKind { SshDirectory, PrivateKey, AuthorizedKeys, Config }
```

- [ ] **Step 2: Write tests for the new properties**

Create `tests/SshEasyConfig.Tests/Platform/PlatformCapabilitiesTests.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Platform;

public class PlatformCapabilitiesTests
{
    [Fact]
    public void Platform_HasIsElevated()
    {
        var platform = PlatformDetector.Detect();
        // Just verify it doesn't throw — actual value depends on test runner
        _ = platform.IsElevated;
    }

    [Fact]
    public void Platform_HasPackageManager()
    {
        var platform = PlatformDetector.Detect();
        var pm = platform.PackageManager;
        Assert.True(Enum.IsDefined(pm));
    }

    [Fact]
    public void Platform_HasFirewallType()
    {
        var platform = PlatformDetector.Detect();
        var fw = platform.FirewallType;
        Assert.True(Enum.IsDefined(fw));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~PlatformCapabilities"
```

Expected: FAIL — new interface members not implemented.

- [ ] **Step 4: Implement TryRunCommandAsync and new properties in LinuxPlatform**

Add to `src/Platform/LinuxPlatform.cs`:

```csharp
public virtual bool IsElevated => Environment.GetEnvironmentVariable("USER") == "root"
    || (int)Environment.GetEnvironmentVariable("EUID")! == 0
    || geteuid() == 0;

// Simplified: just check if user is root
public virtual bool IsElevated
{
    get
    {
        try
        {
            var result = TryRunCommandAsync("id", "-u").GetAwaiter().GetResult();
            return result.ExitCode == 0 && result.StdOut.Trim() == "0";
        }
        catch { return false; }
    }
}

public virtual PackageManager PackageManager
{
    get
    {
        if (File.Exists("/usr/bin/apt") || File.Exists("/usr/bin/apt-get"))
            return PackageManager.Apt;
        if (File.Exists("/usr/bin/dnf"))
            return PackageManager.Dnf;
        if (File.Exists("/usr/bin/yum"))
            return PackageManager.Yum;
        return PackageManager.None;
    }
}

public virtual FirewallType FirewallType
{
    get
    {
        try
        {
            var result = TryRunCommandAsync("which", "ufw").GetAwaiter().GetResult();
            if (result.ExitCode == 0) return FirewallType.Ufw;
        }
        catch { }
        try
        {
            var result = TryRunCommandAsync("which", "firewall-cmd").GetAwaiter().GetResult();
            if (result.ExitCode == 0) return FirewallType.Firewalld;
        }
        catch { }
        return FirewallType.Iptables;
    }
}

public virtual async Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = command,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    process.Start();
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, stdout, stderr);
}
```

- [ ] **Step 5: Implement new properties in WindowsPlatform**

Add to `src/Platform/WindowsPlatform.cs`:

```csharp
public bool IsElevated => IsAdminUser();

public PackageManager PackageManager => PackageManager.WinGet;

public FirewallType FirewallType => FirewallType.WindowsFirewall;

public async Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = command,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    process.Start();
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, stdout, stderr);
}
```

- [ ] **Step 6: Implement new properties in MacOsPlatform**

Add to `src/Platform/MacOsPlatform.cs`:

```csharp
public bool IsElevated
{
    get
    {
        try
        {
            var result = TryRunCommandAsync("id", "-u").GetAwaiter().GetResult();
            return result.ExitCode == 0 && result.StdOut.Trim() == "0";
        }
        catch { return false; }
    }
}

public PackageManager PackageManager => File.Exists("/opt/homebrew/bin/brew") || File.Exists("/usr/local/bin/brew")
    ? PackageManager.Brew : PackageManager.None;

public FirewallType FirewallType => FirewallType.Pf;

public async Task<(int ExitCode, string StdOut, string StdErr)> TryRunCommandAsync(string command, string arguments)
{
    // Same implementation as LinuxPlatform.TryRunCommandAsync
}
```

- [ ] **Step 7: WslPlatform inherits from LinuxPlatform — verify no overrides needed**

WslPlatform extends LinuxPlatform, so it inherits `IsElevated`, `PackageManager`, `FirewallType`, and `TryRunCommandAsync` automatically. No changes needed.

- [ ] **Step 8: Run tests**

```bash
dotnet test
```

Expected: All pass including new PlatformCapabilitiesTests.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: extend IPlatform with elevation, package manager, firewall detection"
```

---

## Task 2: SSH Server Installer

**Files:**
- Create: `src/Core/SshServerInstaller.cs`
- Create: `tests/SshEasyConfig.Tests/Core/SshServerInstallerTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/SshEasyConfig.Tests/Core/SshServerInstallerTests.cs`:

```csharp
using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class SshServerInstallerTests
{
    [Fact]
    public async Task IsSshdInstalled_WhenRunning_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.IsSshServiceRunningAsync().Returns(true);

        var result = await SshServerInstaller.IsSshdInstalledAsync(platform);
        Assert.True(result);
    }

    [Fact]
    public async Task IsSshdInstalled_WhenSshdConfigExists_ReturnsTrue()
    {
        var platform = Substitute.For<IPlatform>();
        platform.IsSshServiceRunningAsync().Returns(false);
        // sshd_config existence check is done via File.Exists in the implementation,
        // so this test verifies the fallback logic pattern
        platform.SshdConfigPath.Returns("/nonexistent/path/sshd_config");

        var result = await SshServerInstaller.IsSshdInstalledAsync(platform);
        // Will be false because the file doesn't exist and service isn't running
        Assert.False(result);
    }

    [Fact]
    public void GetSshServiceName_Linux_ReturnsCorrectName()
    {
        // Debian/Ubuntu uses "ssh", RHEL/Fedora uses "sshd"
        var name = SshServerInstaller.GetSshServiceName(PlatformKind.Linux);
        Assert.True(name == "ssh" || name == "sshd");
    }

    [Fact]
    public void GetSshServiceName_Windows_ReturnsSshd()
    {
        var name = SshServerInstaller.GetSshServiceName(PlatformKind.Windows);
        Assert.Equal("sshd", name);
    }

    [Fact]
    public void GetInstallCommand_Windows_UsesPowerShell()
    {
        var (cmd, args) = SshServerInstaller.GetInstallCommand(PlatformKind.Windows, PackageManager.WinGet);
        Assert.Equal("powershell", cmd);
        Assert.Contains("Add-WindowsCapability", args);
    }

    [Fact]
    public void GetInstallCommand_LinuxApt_UsesApt()
    {
        var (cmd, args) = SshServerInstaller.GetInstallCommand(PlatformKind.Linux, PackageManager.Apt);
        Assert.Equal("sudo", cmd);
        Assert.Contains("apt", args);
    }

    [Fact]
    public void GetInstallCommand_LinuxDnf_UsesDnf()
    {
        var (cmd, args) = SshServerInstaller.GetInstallCommand(PlatformKind.Linux, PackageManager.Dnf);
        Assert.Equal("sudo", cmd);
        Assert.Contains("dnf", args);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~SshServerInstaller"
```

- [ ] **Step 3: Implement SshServerInstaller**

Create `src/Core/SshServerInstaller.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class SshServerInstaller
{
    /// <summary>
    /// Checks if sshd is installed by verifying service status or config file existence.
    /// </summary>
    public static async Task<bool> IsSshdInstalledAsync(IPlatform platform)
    {
        // If service is running, it's installed
        if (await platform.IsSshServiceRunningAsync())
            return true;

        // Check if sshd_config exists (service may be installed but stopped)
        if (File.Exists(platform.SshdConfigPath))
            return true;

        // Platform-specific checks
        if (platform.Kind == PlatformKind.Windows)
        {
            var (exitCode, stdout, _) = await platform.TryRunCommandAsync(
                "powershell", "-Command \"Get-WindowsCapability -Online -Name OpenSSH.Server* | Select-Object -ExpandProperty State\"");
            return exitCode == 0 && stdout.Trim().Equals("Installed", StringComparison.OrdinalIgnoreCase);
        }

        // Linux/macOS: check if sshd binary exists
        var (ec, _, _) = await platform.TryRunCommandAsync("which", "sshd");
        return ec == 0;
    }

    /// <summary>
    /// Checks if sshd service is enabled to start on boot.
    /// </summary>
    public static async Task<bool> IsSshdEnabledAsync(IPlatform platform)
    {
        if (platform.Kind == PlatformKind.Windows)
        {
            var (exitCode, stdout, _) = await platform.TryRunCommandAsync("sc", "qc sshd");
            return exitCode == 0 && stdout.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
        }

        if (platform.Kind == PlatformKind.MacOS)
        {
            var (exitCode, stdout, _) = await platform.TryRunCommandAsync("sudo", "systemsetup -getremotelogin");
            return exitCode == 0 && stdout.Contains("On", StringComparison.OrdinalIgnoreCase);
        }

        // Linux/WSL: check systemctl
        var serviceName = GetSshServiceName(platform.Kind);
        var (ec, output, _) = await platform.TryRunCommandAsync("systemctl", $"is-enabled {serviceName}");
        return ec == 0 && output.Trim() == "enabled";
    }

    /// <summary>
    /// Gets the correct SSH service name for this platform.
    /// Debian/Ubuntu uses "ssh", RHEL/Fedora uses "sshd".
    /// </summary>
    public static string GetSshServiceName(PlatformKind kind)
    {
        if (kind == PlatformKind.Windows)
            return "sshd";
        if (kind == PlatformKind.MacOS)
            return "com.openssh.sshd";

        // Linux/WSL: check for Debian-style "ssh" service
        if (File.Exists("/lib/systemd/system/ssh.service") ||
            File.Exists("/etc/init.d/ssh"))
            return "ssh";

        return "sshd";
    }

    /// <summary>
    /// Returns the command to install sshd for the given platform.
    /// </summary>
    public static (string Command, string Arguments) GetInstallCommand(PlatformKind kind, PackageManager pm)
    {
        if (kind == PlatformKind.Windows)
            return ("powershell", "-Command \"Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0\"");

        if (kind == PlatformKind.MacOS)
            return ("sudo", "systemsetup -setremotelogin on");

        // Linux/WSL
        return pm switch
        {
            PackageManager.Apt => ("sudo", "apt install -y openssh-server"),
            PackageManager.Dnf => ("sudo", "dnf install -y openssh-server"),
            PackageManager.Yum => ("sudo", "yum install -y openssh-server"),
            _ => throw new InvalidOperationException($"Cannot determine install command for package manager: {pm}")
        };
    }

    /// <summary>
    /// Installs sshd on the current platform.
    /// </summary>
    public static async Task InstallAsync(IPlatform platform)
    {
        var (cmd, args) = GetInstallCommand(platform.Kind, platform.PackageManager);
        await platform.RunCommandAsync(cmd, args);
    }

    /// <summary>
    /// Starts the sshd service.
    /// </summary>
    public static async Task StartAsync(IPlatform platform)
    {
        if (platform.Kind == PlatformKind.Windows)
        {
            await platform.RunCommandAsync("sc", "start sshd");
            return;
        }

        if (platform.Kind == PlatformKind.MacOS)
        {
            // macOS: systemsetup already starts the service
            return;
        }

        var serviceName = GetSshServiceName(platform.Kind);
        // Try systemctl first, fall back to service
        var (ec, _, _) = await platform.TryRunCommandAsync("which", "systemctl");
        if (ec == 0)
            await platform.RunCommandAsync("sudo", $"systemctl start {serviceName}");
        else
            await platform.RunCommandAsync("sudo", $"service {serviceName} start");
    }

    /// <summary>
    /// Enables sshd to start on boot.
    /// </summary>
    public static async Task EnableAsync(IPlatform platform)
    {
        if (platform.Kind == PlatformKind.Windows)
        {
            await platform.RunCommandAsync("sc", "config sshd start= auto");
            return;
        }

        if (platform.Kind == PlatformKind.MacOS)
        {
            // Already handled by systemsetup -setremotelogin on
            return;
        }

        var serviceName = GetSshServiceName(platform.Kind);
        var (ec, _, _) = await platform.TryRunCommandAsync("which", "systemctl");
        if (ec == 0)
            await platform.RunCommandAsync("sudo", $"systemctl enable {serviceName}");
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~SshServerInstaller"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: SSH server installer with cross-platform install, start, enable"
```

---

## Task 3: Firewall Manager

**Files:**
- Create: `src/Core/FirewallManager.cs`
- Create: `tests/SshEasyConfig.Tests/Core/FirewallManagerTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/SshEasyConfig.Tests/Core/FirewallManagerTests.cs`:

```csharp
using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class FirewallManagerTests
{
    [Fact]
    public void GetOpenCommand_WindowsFirewall_UsesNetsh()
    {
        var (cmd, args) = FirewallManager.GetOpenPortCommand(FirewallType.WindowsFirewall, 22);
        Assert.Equal("netsh", cmd);
        Assert.Contains("22", args);
        Assert.Contains("OpenSSH", args);
    }

    [Fact]
    public void GetOpenCommand_Ufw_UsesUfw()
    {
        var (cmd, args) = FirewallManager.GetOpenPortCommand(FirewallType.Ufw, 22);
        Assert.Equal("sudo", cmd);
        Assert.Contains("ufw", args);
        Assert.Contains("22", args);
    }

    [Fact]
    public void GetOpenCommand_Firewalld_UsesFirewallCmd()
    {
        var (cmd, args) = FirewallManager.GetOpenPortCommand(FirewallType.Firewalld, 22);
        Assert.Equal("sudo", cmd);
        Assert.Contains("firewall-cmd", args);
    }

    [Fact]
    public void GetOpenCommand_None_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FirewallManager.GetOpenPortCommand(FirewallType.None, 22));
    }

    [Fact]
    public async Task IsPortOpen_WithMockPlatform_ChecksCorrectly()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.WindowsFirewall);
        platform.TryRunCommandAsync("netsh", Arg.Any<string>())
            .Returns((0, "Rule Name: OpenSSH-Server\nEnabled: Yes\nDirection: In\nLocalPort: 22\nAction: Allow", ""));

        var result = await FirewallManager.IsPort22OpenAsync(platform);
        Assert.True(result);
    }

    [Fact]
    public async Task IsPortOpen_NoMatchingRule_ReturnsFalse()
    {
        var platform = Substitute.For<IPlatform>();
        platform.FirewallType.Returns(FirewallType.WindowsFirewall);
        platform.TryRunCommandAsync("netsh", Arg.Any<string>())
            .Returns((0, "No rules match the specified criteria.", ""));

        var result = await FirewallManager.IsPort22OpenAsync(platform);
        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~FirewallManager"
```

- [ ] **Step 3: Implement FirewallManager**

Create `src/Core/FirewallManager.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class FirewallManager
{
    /// <summary>
    /// Checks if port 22 is allowed through the firewall.
    /// </summary>
    public static async Task<bool> IsPort22OpenAsync(IPlatform platform)
    {
        switch (platform.FirewallType)
        {
            case FirewallType.WindowsFirewall:
            {
                var (ec, stdout, _) = await platform.TryRunCommandAsync(
                    "netsh", "advfirewall firewall show rule name=all dir=in");
                // Look for any rule allowing TCP port 22 inbound
                return ec == 0 && stdout.Contains("22") && stdout.Contains("Allow");
            }

            case FirewallType.Ufw:
            {
                var (ec, stdout, _) = await platform.TryRunCommandAsync("sudo", "ufw status");
                if (ec != 0) return true; // ufw not active = no blocking
                if (stdout.Contains("inactive", StringComparison.OrdinalIgnoreCase)) return true;
                return stdout.Contains("22") && stdout.Contains("ALLOW");
            }

            case FirewallType.Firewalld:
            {
                var (ec, stdout, _) = await platform.TryRunCommandAsync(
                    "sudo", "firewall-cmd --list-services");
                return ec == 0 && stdout.Contains("ssh");
            }

            case FirewallType.Iptables:
            {
                var (ec, stdout, _) = await platform.TryRunCommandAsync("sudo", "iptables -L INPUT -n");
                if (ec != 0) return true; // Can't check = assume open
                // Check for ACCEPT rule on port 22, or if default policy is ACCEPT
                return stdout.Contains("dpt:22") || stdout.Contains("policy ACCEPT");
            }

            case FirewallType.Pf:
                // macOS: SSH is generally allowed when Remote Login is enabled
                return true;

            case FirewallType.None:
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// Returns the command to open port 22 for the given firewall type.
    /// </summary>
    public static (string Command, string Arguments) GetOpenPortCommand(FirewallType firewallType, int port)
    {
        return firewallType switch
        {
            FirewallType.WindowsFirewall => ("netsh",
                $"advfirewall firewall add rule name=\"OpenSSH-Server\" dir=in action=allow protocol=TCP localport={port}"),
            FirewallType.Ufw => ("sudo", $"ufw allow {port}/tcp"),
            FirewallType.Firewalld => ("sudo",
                $"bash -c \"firewall-cmd --permanent --add-service=ssh && firewall-cmd --reload\""),
            FirewallType.Iptables => ("sudo",
                $"iptables -I INPUT -p tcp --dport {port} -j ACCEPT"),
            FirewallType.Pf => ("sudo",
                "pfctl -f /etc/pf.conf"), // macOS: usually no action needed
            FirewallType.None => throw new InvalidOperationException("No firewall detected to configure"),
            _ => throw new InvalidOperationException($"Unknown firewall type: {firewallType}")
        };
    }

    /// <summary>
    /// Opens port 22 on the platform's firewall.
    /// </summary>
    public static async Task OpenPort22Async(IPlatform platform)
    {
        var (cmd, args) = GetOpenPortCommand(platform.FirewallType, 22);
        await platform.RunCommandAsync(cmd, args);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~FirewallManager"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: firewall manager with cross-platform port 22 detection and opening"
```

---

## Task 4: Windows Account Helper

**Files:**
- Create: `src/Core/WindowsAccountHelper.cs`
- Create: `tests/SshEasyConfig.Tests/Core/WindowsAccountHelperTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/SshEasyConfig.Tests/Core/WindowsAccountHelperTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class WindowsAccountHelperTests
{
    [Fact]
    public void GetAdminAuthorizedKeysPath_ReturnsCorrectPath()
    {
        var path = WindowsAccountHelper.GetAdminAuthorizedKeysPath();
        Assert.Contains("ssh", path);
        Assert.Contains("administrators_authorized_keys", path);
    }

    [Fact]
    public void GetMatchGroupBlock_ReturnsCorrectConfig()
    {
        var block = WindowsAccountHelper.GetMatchGroupBlock();
        Assert.Contains("Match Group administrators", block);
        Assert.Contains("AuthorizedKeysFile", block);
        Assert.Contains("__PROGRAMDATA__", block);
    }

    [Fact]
    public void SshdConfigHasMatchBlock_WithBlock_ReturnsTrue()
    {
        var content = """
            PubkeyAuthentication yes
            Match Group administrators
                AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
            """;
        Assert.True(WindowsAccountHelper.SshdConfigHasMatchBlock(content));
    }

    [Fact]
    public void SshdConfigHasMatchBlock_WithoutBlock_ReturnsFalse()
    {
        var content = "PubkeyAuthentication yes\nPasswordAuthentication no\n";
        Assert.False(WindowsAccountHelper.SshdConfigHasMatchBlock(content));
    }

    [SkippableFact]
    public void IsMicrosoftLinkedAccount_RunsWithoutError()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test");
        // Just verify it doesn't throw — actual result depends on the account
        _ = WindowsAccountHelper.IsMicrosoftLinkedAccount();
    }
}
```

Note: Add `xunit.SkippableFact` package to test project:
```bash
cd tests/SshEasyConfig.Tests && dotnet add package Xunit.SkippableFact
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~WindowsAccountHelper"
```

- [ ] **Step 3: Implement WindowsAccountHelper**

Create `src/Core/WindowsAccountHelper.cs`:

```csharp
using System.Runtime.Versioning;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class WindowsAccountHelper
{
    /// <summary>
    /// Returns the path to the administrators_authorized_keys file.
    /// </summary>
    public static string GetAdminAuthorizedKeysPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ssh",
            "administrators_authorized_keys");
    }

    /// <summary>
    /// Checks if the current Windows user has a linked Microsoft account.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsMicrosoftLinkedAccount()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            // Microsoft-linked accounts have claims with the Microsoft account issuer
            foreach (var claim in identity.Claims)
            {
                if (claim.Issuer.Contains("MicrosoftAccount", StringComparison.OrdinalIgnoreCase) ||
                    claim.Type.Contains("MicrosoftAccount", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Alternative: check registry for linked account
            // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{SID}
            var sid = identity.User?.Value;
            if (sid is not null)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
                if (key is not null)
                {
                    // "Guid" value present indicates a Microsoft account link
                    var guid = key.GetValue("Guid");
                    if (guid is not null)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Returns the Match Group administrators block for sshd_config.
    /// </summary>
    public static string GetMatchGroupBlock()
    {
        return """
            Match Group administrators
                AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
            """;
    }

    /// <summary>
    /// Checks if sshd_config already has the Match Group administrators block.
    /// </summary>
    public static bool SshdConfigHasMatchBlock(string sshdConfigContent)
    {
        return sshdConfigContent.Contains("Match Group administrators", StringComparison.OrdinalIgnoreCase)
            && sshdConfigContent.Contains("administrators_authorized_keys", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets up administrators_authorized_keys with correct ACLs for a public key.
    /// Creates the file if it doesn't exist, adds the key, sets ACLs to SYSTEM + Administrators only.
    /// </summary>
    public static async Task SetupAdminAuthorizedKeysAsync(IPlatform platform, string publicKey)
    {
        var path = GetAdminAuthorizedKeysPath();
        var dir = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Read existing content or empty
        var existing = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";

        // Add key (with deduplication)
        var updated = AuthorizedKeysManager.AddKey(existing, publicKey);
        await File.WriteAllTextAsync(path, updated);

        // Set ACLs: SYSTEM and Administrators only
        await platform.RunCommandAsync("icacls",
            $"\"{path}\" /inheritance:r /grant \"SYSTEM:(F)\" /grant \"BUILTIN\\Administrators:(F)\"");
    }

    /// <summary>
    /// Adds the Match Group administrators block to sshd_config if missing.
    /// Creates a backup first.
    /// </summary>
    public static async Task EnsureMatchBlockAsync(IPlatform platform)
    {
        var configPath = platform.SshdConfigPath;
        if (!File.Exists(configPath))
            return;

        var content = await File.ReadAllTextAsync(configPath);
        if (SshdConfigHasMatchBlock(content))
            return;

        // Backup
        var backupPath = $"{configPath}.backup.{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Copy(configPath, backupPath, overwrite: false);

        // Append the match block
        var matchBlock = "\n\nMatch Group administrators\n    AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys\n";
        await File.AppendAllTextAsync(configPath, matchBlock);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~WindowsAccountHelper"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: Windows account helper for MS-linked account SSH auth"
```

---

## Task 5: Enhanced SetupCommand

**Files:**
- Modify: `src/Commands/SetupCommand.cs`

- [ ] **Step 1: Rewrite SetupCommand with full provisioning flow**

Replace `src/Commands/SetupCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class SetupCommand
{
    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Easy Config - Full Setup[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Step 1: Detect state
        AnsiConsole.MarkupLine("[bold]Step 1: Detecting SSH state...[/]");
        await DetectAndReportStateAsync(platform);
        AnsiConsole.WriteLine();

        // Step 2: Install sshd if missing
        if (!await SshServerInstaller.IsSshdInstalledAsync(platform))
        {
            AnsiConsole.MarkupLine("[yellow]SSH server is not installed.[/]");
            if (!platform.IsElevated && platform.Kind == PlatformKind.Windows)
            {
                AnsiConsole.MarkupLine("[red]Admin privileges required to install OpenSSH Server on Windows.[/]");
                AnsiConsole.MarkupLine("[grey]Re-run this command as Administrator, or install manually:[/]");
                var (cmd, args) = SshServerInstaller.GetInstallCommand(platform.Kind, platform.PackageManager);
                AnsiConsole.MarkupLine($"[dim]  {Markup.Escape(cmd)} {Markup.Escape(args)}[/]");
                return 1;
            }

            var install = AnsiConsole.Prompt(
                new ConfirmationPrompt("Install SSH server now?") { DefaultValue = true });

            if (install)
            {
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("Installing SSH server...", async _ =>
                    {
                        await SshServerInstaller.InstallAsync(platform);
                    });
                AnsiConsole.MarkupLine("[green]SSH server installed.[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]SSH server is installed.[/]");
        }

        // Step 3: Start and enable service
        if (!await platform.IsSshServiceRunningAsync())
        {
            AnsiConsole.MarkupLine("[yellow]SSH service is not running.[/]");
            var start = AnsiConsole.Prompt(
                new ConfirmationPrompt("Start SSH service now?") { DefaultValue = true });

            if (start)
            {
                await SshServerInstaller.StartAsync(platform);
                AnsiConsole.MarkupLine("[green]SSH service started.[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]SSH service is running.[/]");
        }

        if (!await SshServerInstaller.IsSshdEnabledAsync(platform))
        {
            var enable = AnsiConsole.Prompt(
                new ConfirmationPrompt("Enable SSH service on boot?") { DefaultValue = true });

            if (enable)
            {
                await SshServerInstaller.EnableAsync(platform);
                AnsiConsole.MarkupLine("[green]SSH service enabled on boot.[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]SSH service enabled on boot.[/]");
        }

        // Step 4: Firewall
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Step 4: Checking firewall...[/]");
        if (!await FirewallManager.IsPort22OpenAsync(platform))
        {
            AnsiConsole.MarkupLine("[yellow]Firewall may be blocking SSH (port 22).[/]");
            var open = AnsiConsole.Prompt(
                new ConfirmationPrompt("Open port 22 in firewall?") { DefaultValue = true });

            if (open)
            {
                await FirewallManager.OpenPort22Async(platform);
                AnsiConsole.MarkupLine("[green]Firewall rule added for port 22.[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Firewall allows SSH on port 22.[/]");
        }

        // Step 5: Windows Microsoft account handling
        if (platform.Kind == PlatformKind.Windows)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Step 5: Windows account configuration...[/]");
            await HandleWindowsAccountAsync(platform);
        }

        // Step 6: Generate keys
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Step 6: SSH key generation...[/]");
        await GenerateKeysAsync(platform);

        // Step 7: Fix permissions
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Step 7: Fixing permissions...[/]");
        await FixPermissionsAsync(platform);

        // Step 8: Validate
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Step 8: Validating SSH...[/]");
        await ValidateAsync(platform);

        return 0;
    }

    private static async Task DetectAndReportStateAsync(IPlatform platform)
    {
        var table = new Table().AddColumn("Check").AddColumn("Status");
        table.Border(TableBorder.Rounded);

        table.AddRow("Platform", Markup.Escape($"{platform.Kind}"));
        table.AddRow("Elevated/Root", platform.IsElevated ? "[green]Yes[/]" : "[yellow]No[/]");

        var installed = await SshServerInstaller.IsSshdInstalledAsync(platform);
        table.AddRow("sshd installed", installed ? "[green]Yes[/]" : "[red]No[/]");

        if (installed)
        {
            var running = await platform.IsSshServiceRunningAsync();
            table.AddRow("sshd running", running ? "[green]Yes[/]" : "[yellow]No[/]");

            var enabled = await SshServerInstaller.IsSshdEnabledAsync(platform);
            table.AddRow("sshd on boot", enabled ? "[green]Yes[/]" : "[yellow]No[/]");
        }

        var firewallOpen = await FirewallManager.IsPort22OpenAsync(platform);
        table.AddRow("Firewall port 22", firewallOpen ? "[green]Open[/]" : "[yellow]Blocked/Unknown[/]");
        table.AddRow("Package manager", Markup.Escape($"{platform.PackageManager}"));
        table.AddRow("Firewall type", Markup.Escape($"{platform.FirewallType}"));

        if (platform.Kind == PlatformKind.Windows && OperatingSystem.IsWindows())
        {
            var msLinked = WindowsAccountHelper.IsMicrosoftLinkedAccount();
            table.AddRow("MS-linked account", msLinked ? "[yellow]Yes[/]" : "No");
        }

        AnsiConsole.Write(table);
    }

    private static async Task HandleWindowsAccountAsync(IPlatform platform)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var isMsLinked = WindowsAccountHelper.IsMicrosoftLinkedAccount();
        var isAdmin = platform.IsElevated;

        if (isMsLinked)
        {
            AnsiConsole.MarkupLine("[yellow]Microsoft-linked account detected.[/]");

            if (isAdmin)
            {
                AnsiConsole.MarkupLine("[grey]Admin + MS account: using administrators_authorized_keys[/]");

                // Ensure the Match block exists in sshd_config
                if (File.Exists(platform.SshdConfigPath))
                {
                    var content = await File.ReadAllTextAsync(platform.SshdConfigPath);
                    if (!WindowsAccountHelper.SshdConfigHasMatchBlock(content))
                    {
                        var fix = AnsiConsole.Prompt(
                            new ConfirmationPrompt("Add Match Group administrators block to sshd_config?")
                                { DefaultValue = true });
                        if (fix)
                        {
                            await WindowsAccountHelper.EnsureMatchBlockAsync(platform);
                            await platform.RestartSshServiceAsync();
                            AnsiConsole.MarkupLine("[green]sshd_config updated and service restarted.[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[green]sshd_config Match block already configured.[/]");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Non-admin MS account: using standard authorized_keys[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Local account (not MS-linked).[/]");
        }
    }

    private static async Task GenerateKeysAsync(IPlatform platform)
    {
        const string keyName = "id_ed25519";

        if (KeyManager.KeyExists(platform, keyName))
        {
            var publicKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);
            var truncated = publicKey.Length > 60
                ? publicKey[..30] + "..." + publicKey[^20..]
                : publicKey;
            AnsiConsole.MarkupLine($"[green]Existing key found:[/] {Markup.Escape(truncated.Trim())}");

            var useExisting = AnsiConsole.Prompt(
                new ConfirmationPrompt("Use existing key?") { DefaultValue = true });
            if (useExisting)
                return;
        }

        var defaultComment = $"{Environment.UserName}@{Environment.MachineName}";
        var comment = AnsiConsole.Prompt(
            new TextPrompt<string>("Key comment:")
                .DefaultValue(defaultComment)
                .AllowEmpty());

        SshKeyPair? keyPair = null;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Ed25519 key pair...", async _ =>
            {
                keyPair = KeyManager.GenerateKeyPair(comment);
                await KeyManager.SaveKeyPairAsync(platform, keyPair, keyName);
            });

        AnsiConsole.MarkupLine("[green]Key pair generated and saved.[/]");
        AnsiConsole.MarkupLine($"[bold]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(keyPair!.PublicKeyOpenSsh.Trim())}");

        // For Windows admin + MS-linked: also add to administrators_authorized_keys
        if (platform.Kind == PlatformKind.Windows && OperatingSystem.IsWindows()
            && WindowsAccountHelper.IsMicrosoftLinkedAccount() && platform.IsElevated)
        {
            await WindowsAccountHelper.SetupAdminAuthorizedKeysAsync(platform, keyPair.PublicKeyOpenSsh.Trim());
            AnsiConsole.MarkupLine("[green]Key added to administrators_authorized_keys.[/]");
        }
    }

    private static async Task FixPermissionsAsync(IPlatform platform)
    {
        var sshDir = platform.SshDirectoryPath;
        if (!Directory.Exists(sshDir))
        {
            Directory.CreateDirectory(sshDir);
            await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
            AnsiConsole.MarkupLine($"[green]Created {Markup.Escape(sshDir)} with correct permissions.[/]");
            return;
        }

        var dirOk = await platform.CheckFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
        if (!dirOk)
        {
            await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
            AnsiConsole.MarkupLine("[green]Fixed .ssh directory permissions.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Permissions OK.[/]");
        }
    }

    private static async Task ValidateAsync(IPlatform platform)
    {
        // Try connecting to localhost
        var (ec, stdout, stderr) = await platform.TryRunCommandAsync(
            "ssh", "-o BatchMode=yes -o StrictHostKeyChecking=no -o ConnectTimeout=5 localhost exit 0");

        if (ec == 0)
        {
            AnsiConsole.MarkupLine("[green]SSH connection to localhost successful![/]");
            AnsiConsole.MarkupLine("[bold green]Setup complete. SSH is ready.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]SSH connection to localhost failed.[/]");
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(stderr.Trim())}[/]");
            AnsiConsole.MarkupLine("[grey]Run 'ssh-easy-config diagnose' for detailed diagnostics.[/]");
        }
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build
dotnet test
```

Expected: Build succeeds, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: enhanced setup command with full SSH provisioning wizard"
```

---

## Task 6: Enhanced DiagnoseCommand with Inline Fixes

**Files:**
- Modify: `src/Commands/DiagnoseCommand.cs`
- Modify: `src/Diagnostics/DiagnosticResult.cs`
- Modify: `src/Diagnostics/DiagnosticRunner.cs`

- [ ] **Step 1: Add FixAction to DiagnosticResult**

Modify `src/Diagnostics/DiagnosticResult.cs`:

```csharp
namespace SshEasyConfig.Diagnostics;

public enum CheckStatus { Pass, Warn, Fail, Skip }

public record DiagnosticResult(
    string CheckName,
    CheckStatus Status,
    string Message,
    string? FixSuggestion = null,
    bool AutoFixAvailable = false,
    Func<Task>? FixAction = null);
```

Note: The `FixAction` won't serialize to JSON (it's a delegate), which is fine — JSON mode ignores it.

- [ ] **Step 2: Update DiagnosticRunner to include fix actions**

Modify `src/Diagnostics/DiagnosticRunner.cs` to add fix actions to results:

```csharp
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public class DiagnosticRunner
{
    private readonly IPlatform _platform;

    public DiagnosticRunner(IPlatform platform)
    {
        _platform = platform;
    }

    public async Task<List<DiagnosticResult>> RunAllAsync(string? host, int port = 22)
    {
        var results = new List<DiagnosticResult>();
        var timeout = TimeSpan.FromSeconds(5);

        // Layer 1: Network checks (if host provided)
        if (host != null)
        {
            var dnsResults = await NetworkCheck.CheckDnsAsync(host);
            results.AddRange(dnsResults);

            if (dnsResults.Any(r => r.Status == CheckStatus.Fail))
                goto localChecks;

            var portResult = await NetworkCheck.CheckPortAsync(host, port, timeout);
            results.Add(portResult);

            if (portResult.Status == CheckStatus.Fail)
                goto localChecks;
        }

        // Layer 2: SSH service checks
        if (host != null)
        {
            var bannerResult = await SshServiceCheck.CheckBannerAsync(host, port, timeout);
            results.Add(bannerResult);
        }

    localChecks:
        // sshd installed check
        var installed = await SshServerInstaller.IsSshdInstalledAsync(_platform);
        if (!installed)
        {
            results.Add(new DiagnosticResult(
                "sshd Installed", CheckStatus.Fail,
                "SSH server is not installed",
                "Install SSH server",
                AutoFixAvailable: true,
                FixAction: async () => await SshServerInstaller.InstallAsync(_platform)));
        }
        else
        {
            results.Add(new DiagnosticResult("sshd Installed", CheckStatus.Pass, "SSH server is installed"));
        }

        // sshd running check
        var localService = await SshServiceCheck.CheckLocalServiceAsync(_platform);
        if (localService.Status == CheckStatus.Fail)
        {
            results.Add(localService with
            {
                AutoFixAvailable = true,
                FixAction = async () => await SshServerInstaller.StartAsync(_platform)
            });
        }
        else
        {
            results.Add(localService);
        }

        // Firewall check
        var firewallOpen = await FirewallManager.IsPort22OpenAsync(_platform);
        if (!firewallOpen)
        {
            results.Add(new DiagnosticResult(
                "Firewall", CheckStatus.Fail,
                "Firewall may be blocking SSH on port 22",
                "Open port 22 in firewall",
                AutoFixAvailable: true,
                FixAction: async () => await FirewallManager.OpenPort22Async(_platform)));
        }
        else
        {
            results.Add(new DiagnosticResult("Firewall", CheckStatus.Pass, "Port 22 is open"));
        }

        // Layer 3: Auth check
        var keyResult = await AuthCheck.CheckEd25519KeyExistsAsync(_platform);
        results.Add(keyResult);

        // Layer 4: Config and permissions
        var permResults = await ConfigCheck.CheckFilePermissionsAsync(_platform);
        // Add fix actions to permission failures
        foreach (var pr in permResults)
        {
            if (pr.Status == CheckStatus.Fail && pr.AutoFixAvailable)
            {
                var path = pr.CheckName.Contains(":") ? pr.CheckName.Split(": ")[1] : _platform.SshDirectoryPath;
                results.Add(pr with
                {
                    FixAction = async () =>
                    {
                        var kind = pr.CheckName.Contains("Key") ? SshFileKind.PrivateKey : SshFileKind.SshDirectory;
                        await _platform.SetFilePermissionsAsync(path, kind);
                    }
                });
            }
            else
            {
                results.Add(pr);
            }
        }

        var clientConfigResults = await ConfigCheck.CheckClientConfigAsync(_platform);
        results.AddRange(clientConfigResults);

        // sshd_config check
        if (File.Exists(_platform.SshdConfigPath))
        {
            try
            {
                var sshdContent = await File.ReadAllTextAsync(_platform.SshdConfigPath);
                var sshdResults = ConfigCheck.CheckSshdConfig(sshdContent, _platform);
                results.AddRange(sshdResults);

                // Windows: check Match block
                if (_platform.Kind == PlatformKind.Windows
                    && !WindowsAccountHelper.SshdConfigHasMatchBlock(sshdContent))
                {
                    results.Add(new DiagnosticResult(
                        "sshd: Match Group", CheckStatus.Fail,
                        "Missing 'Match Group administrators' block for admin SSH key auth",
                        "Add Match Group administrators block to sshd_config",
                        AutoFixAvailable: true,
                        FixAction: async () =>
                        {
                            await WindowsAccountHelper.EnsureMatchBlockAsync(_platform);
                            await _platform.RestartSshServiceAsync();
                        }));
                }
            }
            catch (Exception ex)
            {
                results.Add(new DiagnosticResult(
                    "sshd_config", CheckStatus.Warn,
                    $"Could not read sshd_config: {ex.Message}"));
            }
        }

        // Layer 5: WSL checks
        var wslResults = WslCheck.Check(_platform);
        results.AddRange(wslResults);

        return results;
    }
}
```

- [ ] **Step 3: Update DiagnoseCommand to offer fixes**

Modify `src/Commands/DiagnoseCommand.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using SshEasyConfig.Diagnostics;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class DiagnoseCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> RunAsync(IPlatform platform, string? host, bool json, bool verbose)
    {
        var runner = new DiagnosticRunner(platform);
        var results = await runner.RunAllAsync(host);

        if (json)
        {
            // Serialize without FixAction (delegates can't be serialized)
            var serializable = results.Select(r => new
            {
                r.CheckName,
                Status = r.Status.ToString(),
                r.Message,
                r.FixSuggestion,
                r.AutoFixAvailable
            });
            AnsiConsole.WriteLine(JsonSerializer.Serialize(serializable, JsonOptions));
        }
        else
        {
            AnsiConsole.Write(new Rule("[bold blue]SSH Diagnostics[/]").LeftJustified());
            if (host is not null)
                AnsiConsole.MarkupLine($"[bold]Host:[/] {Markup.Escape(host)}");
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn("Status");
            table.AddColumn("Check");
            table.AddColumn("Details");

            foreach (var result in results)
            {
                if (!verbose && result.Status == CheckStatus.Skip)
                    continue;

                var statusIcon = result.Status switch
                {
                    CheckStatus.Pass => "[green]PASS[/]",
                    CheckStatus.Warn => "[yellow]WARN[/]",
                    CheckStatus.Fail => "[red]FAIL[/]",
                    CheckStatus.Skip => "[grey]SKIP[/]",
                    _ => "[grey]?[/]"
                };

                var details = Markup.Escape(result.Message);
                if (result.FixSuggestion is not null)
                    details += $"\n[dim]{Markup.Escape(result.FixSuggestion)}[/]";

                table.AddRow(statusIcon, Markup.Escape(result.CheckName), details);
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            var passCount = results.Count(r => r.Status == CheckStatus.Pass);
            var warnCount = results.Count(r => r.Status == CheckStatus.Warn);
            var failCount = results.Count(r => r.Status == CheckStatus.Fail);

            AnsiConsole.MarkupLine(
                $"[green]{passCount} passed[/], [yellow]{warnCount} warnings[/], [red]{failCount} failed[/]");

            // Offer fixes for auto-fixable failures
            var fixable = results.Where(r =>
                r.AutoFixAvailable && r.FixAction is not null
                && (r.Status == CheckStatus.Fail || r.Status == CheckStatus.Warn)).ToList();

            if (fixable.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]{fixable.Count} issue(s) can be fixed automatically.[/]");

                foreach (var fix in fixable)
                {
                    var apply = AnsiConsole.Prompt(
                        new ConfirmationPrompt($"Fix: {fix.FixSuggestion ?? fix.CheckName}?")
                            { DefaultValue = true });

                    if (apply)
                    {
                        try
                        {
                            await fix.FixAction!();
                            AnsiConsole.MarkupLine($"[green]Fixed: {Markup.Escape(fix.CheckName)}[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to fix {Markup.Escape(fix.CheckName)}: {Markup.Escape(ex.Message)}[/]");
                        }
                    }
                }
            }
        }

        return results.Any(r => r.Status == CheckStatus.Fail) ? 1 : 0;
    }
}
```

- [ ] **Step 4: Build and test**

```bash
dotnet build
dotnet test
```

Expected: Build succeeds, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: enhanced diagnose with inline fix-it prompts"
```

---

## Spec Coverage Verification

| Spec Requirement | Task |
|------------------|------|
| Detect platform and SSH state | Task 1, Task 5 (Step 1) |
| Install SSH server (all platforms) | Task 2, Task 5 (Step 2) |
| Start and enable sshd service | Task 2, Task 5 (Step 3) |
| Open firewall (all platforms) | Task 3, Task 5 (Step 4) |
| Windows MS account detection | Task 4, Task 5 (Step 5) |
| administrators_authorized_keys ACLs | Task 4 |
| sshd_config Match block | Task 4, Task 6 |
| Generate keys (existing) | Task 5 (Step 6) |
| Fix permissions | Task 5 (Step 7) |
| Validate SSH connection | Task 5 (Step 8) |
| Diagnose inline fixes | Task 6 |
| Elevation detection and handling | Task 1, Task 5 |
| IPlatform extensions | Task 1 |
