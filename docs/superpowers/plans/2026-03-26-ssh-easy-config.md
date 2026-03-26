# ssh-easy-config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cross-platform .NET 10 CLI tool for SSH key management, key sharing between machines, and connectivity diagnostics, distributed via NuGet for zero-install usage with `dnx`.

**Architecture:** Layered design with platform abstraction at the bottom, core modules (key management, config management, network exchange, diagnostics) in the middle, and CLI commands + interactive wizard at the top. Each module depends on `IPlatform` for OS-specific behavior.

**Tech Stack:** .NET 10, System.CommandLine 2.0.5, Spectre.Console 0.54.0, Makaretu.Dns.Multicast, xUnit + NSubstitute for testing.

---

## Task 1: Project Scaffolding

**Files:**
- Create: `src/SshEasyConfig.csproj`
- Create: `src/Program.cs`
- Create: `tests/SshEasyConfig.Tests/SshEasyConfig.Tests.csproj`
- Create: `ssh-easy-config.sln`

- [ ] **Step 1: Create the solution and projects**

```bash
cd /c/Development/ssh-easy-config
dotnet new sln -n ssh-easy-config
mkdir -p src
cd src
dotnet new console -n SshEasyConfig --framework net10.0
cd ..
mkdir -p tests/SshEasyConfig.Tests
cd tests/SshEasyConfig.Tests
dotnet new xunit -n SshEasyConfig.Tests --framework net10.0
cd /c/Development/ssh-easy-config
dotnet sln add src/SshEasyConfig.csproj
dotnet sln add tests/SshEasyConfig.Tests/SshEasyConfig.Tests.csproj
cd tests/SshEasyConfig.Tests
dotnet add reference ../../src/SshEasyConfig.csproj
```

- [ ] **Step 2: Configure the main project for tool packaging**

Replace `src/SshEasyConfig.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>SshEasyConfig</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>ssh-easy-config</ToolCommandName>
    <PackageId>ssh-easy-config</PackageId>
    <PackageVersion>0.1.0</PackageVersion>
    <Authors>ssh-easy-config contributors</Authors>
    <Description>Cross-platform SSH key management, sharing, and diagnostics</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.5" />
    <PackageReference Include="Spectre.Console" Version="0.54.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Add NSubstitute to the test project**

```bash
cd /c/Development/ssh-easy-config/tests/SshEasyConfig.Tests
dotnet add package NSubstitute
```

- [ ] **Step 4: Write a minimal Program.cs with root command skeleton**

Replace `src/Program.cs` with:

```csharp
using System.CommandLine;

var rootCommand = new RootCommand("ssh-easy-config - Cross-platform SSH key management, sharing, and diagnostics");

var setupCommand = new Command("setup", "Generate SSH keys and configure SSH");
var shareCommand = new Command("share", "Share keys with another machine");
var receiveCommand = new Command("receive", "Listen for incoming key share");
var diagnoseCommand = new Command("diagnose", "Diagnose SSH connectivity");
var configCommand = new Command("config", "Manage ssh_config / sshd_config");

var hostArgument = new Argument<string?>("host", () => null, "The host to diagnose");
diagnoseCommand.Arguments.Add(hostArgument);

rootCommand.Subcommands.Add(setupCommand);
rootCommand.Subcommands.Add(shareCommand);
rootCommand.Subcommands.Add(receiveCommand);
rootCommand.Subcommands.Add(diagnoseCommand);
rootCommand.Subcommands.Add(configCommand);

setupCommand.SetAction(parseResult =>
{
    Console.WriteLine("Setup not yet implemented.");
    return 0;
});

shareCommand.SetAction(parseResult =>
{
    Console.WriteLine("Share not yet implemented.");
    return 0;
});

receiveCommand.SetAction(parseResult =>
{
    Console.WriteLine("Receive not yet implemented.");
    return 0;
});

diagnoseCommand.SetAction(parseResult =>
{
    var host = parseResult.GetValue(hostArgument);
    Console.WriteLine(host is null ? "Diagnose (local) not yet implemented." : $"Diagnose {host} not yet implemented.");
    return 0;
});

configCommand.SetAction(parseResult =>
{
    Console.WriteLine("Config not yet implemented.");
    return 0;
});

rootCommand.SetAction(parseResult =>
{
    Console.WriteLine("Interactive wizard not yet implemented. Use a subcommand or --help.");
    return 0;
});

return rootCommand.Parse(args).Invoke();
```

- [ ] **Step 5: Verify build and run**

```bash
cd /c/Development/ssh-easy-config
dotnet build
dotnet run --project src -- --help
dotnet run --project src -- setup
dotnet run --project src -- diagnose example.com
dotnet test
```

Expected: Build succeeds. `--help` shows all subcommands. `setup` prints stub message. `diagnose example.com` prints "Diagnose example.com not yet implemented." Tests pass (default template test).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: project scaffolding with CLI command skeleton"
```

---

## Task 2: Platform Abstraction

**Files:**
- Create: `src/Platform/IPlatform.cs`
- Create: `src/Platform/PlatformDetector.cs`
- Create: `src/Platform/LinuxPlatform.cs`
- Create: `src/Platform/MacOsPlatform.cs`
- Create: `src/Platform/WindowsPlatform.cs`
- Create: `src/Platform/WslPlatform.cs`
- Create: `tests/SshEasyConfig.Tests/Platform/PlatformDetectorTests.cs`

- [ ] **Step 1: Write the IPlatform interface**

Create `src/Platform/IPlatform.cs`:

```csharp
namespace SshEasyConfig.Platform;

public enum PlatformKind
{
    Linux,
    MacOS,
    Windows,
    Wsl
}

public interface IPlatform
{
    PlatformKind Kind { get; }
    string SshDirectoryPath { get; }
    string SshdConfigPath { get; }
    string AuthorizedKeysFilename { get; }
    Task SetFilePermissionsAsync(string path, SshFileKind kind);
    Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind);
    Task RestartSshServiceAsync();
    Task<bool> IsSshServiceRunningAsync();
    Task<string> RunCommandAsync(string command, string arguments);
}

public enum SshFileKind
{
    SshDirectory,    // 700
    PrivateKey,      // 600
    AuthorizedKeys,  // 600
    Config           // 644
}
```

- [ ] **Step 2: Write PlatformDetector with tests**

Create `tests/SshEasyConfig.Tests/Platform/PlatformDetectorTests.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Platform;

public class PlatformDetectorTests
{
    [Fact]
    public void Detect_ReturnsNonNull()
    {
        var platform = PlatformDetector.Detect();
        Assert.NotNull(platform);
    }

    [Fact]
    public void Detect_ReturnsPlatformMatchingOs()
    {
        var platform = PlatformDetector.Detect();

        if (OperatingSystem.IsWindows())
        {
            // Could be Windows or WSL detection test environment
            Assert.True(
                platform.Kind == PlatformKind.Windows || platform.Kind == PlatformKind.Wsl);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(PlatformKind.MacOS, platform.Kind);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.True(
                platform.Kind == PlatformKind.Linux || platform.Kind == PlatformKind.Wsl);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test
```

Expected: FAIL — `PlatformDetector` does not exist.

- [ ] **Step 4: Implement PlatformDetector**

Create `src/Platform/PlatformDetector.cs`:

```csharp
namespace SshEasyConfig.Platform;

public static class PlatformDetector
{
    public static IPlatform Detect()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();

        if (OperatingSystem.IsMacOS())
            return new MacOsPlatform();

        if (OperatingSystem.IsLinux())
        {
            if (IsWsl())
                return new WslPlatform();
            return new LinuxPlatform();
        }

        throw new PlatformNotSupportedException(
            $"Unsupported operating system: {Environment.OSVersion}");
    }

    private static bool IsWsl()
    {
        if (Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null)
            return true;

        try
        {
            if (File.Exists("/proc/version"))
            {
                var version = File.ReadAllText("/proc/version");
                return version.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
                    || version.Contains("WSL", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore read errors
        }

        return false;
    }
}
```

- [ ] **Step 5: Implement LinuxPlatform**

Create `src/Platform/LinuxPlatform.cs`:

```csharp
using System.Diagnostics;

namespace SshEasyConfig.Platform;

public class LinuxPlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.Linux;

    public string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public string SshdConfigPath => "/etc/ssh/sshd_config";

    public string AuthorizedKeysFilename => "authorized_keys";

    public async Task SetFilePermissionsAsync(string path, SshFileKind kind)
    {
        var mode = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await RunCommandAsync("chmod", $"{mode} \"{path}\"");
    }

    public async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
    {
        var expected = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var result = await RunCommandAsync("stat", $"-c %a \"{path}\"");
        return result.Trim() == expected;
    }

    public async Task RestartSshServiceAsync()
    {
        await RunCommandAsync("sudo", "systemctl restart sshd");
    }

    public async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("systemctl", "is-active sshd");
            return result.Trim() == "active";
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RunCommandAsync(string command, string arguments)
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
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
```

- [ ] **Step 6: Implement MacOsPlatform**

Create `src/Platform/MacOsPlatform.cs`:

```csharp
using System.Diagnostics;

namespace SshEasyConfig.Platform;

public class MacOsPlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.MacOS;

    public string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public string SshdConfigPath => "/etc/ssh/sshd_config";

    public string AuthorizedKeysFilename => "authorized_keys";

    public async Task SetFilePermissionsAsync(string path, SshFileKind kind)
    {
        var mode = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await RunCommandAsync("chmod", $"{mode} \"{path}\"");
    }

    public async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
    {
        var expected = kind switch
        {
            SshFileKind.SshDirectory => "700",
            SshFileKind.PrivateKey => "600",
            SshFileKind.AuthorizedKeys => "600",
            SshFileKind.Config => "644",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var result = await RunCommandAsync("stat", $"-f %Lp \"{path}\"");
        return result.Trim() == expected;
    }

    public async Task RestartSshServiceAsync()
    {
        await RunCommandAsync("sudo", "launchctl kickstart -k system/com.openssh.sshd");
    }

    public async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("launchctl", "list com.openssh.sshd");
            return result.Contains("com.openssh.sshd");
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RunCommandAsync(string command, string arguments)
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
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
```

- [ ] **Step 7: Implement WindowsPlatform**

Create `src/Platform/WindowsPlatform.cs`:

```csharp
using System.Diagnostics;

namespace SshEasyConfig.Platform;

public class WindowsPlatform : IPlatform
{
    public PlatformKind Kind => PlatformKind.Windows;

    public string SshDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public string SshdConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ssh", "sshd_config");

    public string AuthorizedKeysFilename => IsAdminUser()
        ? "administrators_authorized_keys"
        : "authorized_keys";

    public async Task SetFilePermissionsAsync(string path, SshFileKind kind)
    {
        // Remove inheritance, then grant only the current user access
        var username = Environment.UserName;
        await RunCommandAsync("icacls", $"\"{path}\" /inheritance:r /grant:r \"{username}:(F)\"");

        if (kind == SshFileKind.SshDirectory)
        {
            // Directories also need (OI)(CI) for inheritance to contents
            await RunCommandAsync("icacls", $"\"{path}\" /grant:r \"{username}:(OI)(CI)(F)\"");
        }
    }

    public async Task<bool> CheckFilePermissionsAsync(string path, SshFileKind kind)
    {
        var result = await RunCommandAsync("icacls", $"\"{path}\"");
        var username = Environment.UserName;
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Should only have the current user with full control, no other entries
        int aclEntries = 0;
        foreach (var line in lines)
        {
            if (line.Contains(username) && line.Contains("(F)"))
                continue; // Expected
            if (line.Trim().StartsWith(path, StringComparison.OrdinalIgnoreCase))
                continue; // Path echo
            if (string.IsNullOrWhiteSpace(line) || line.Contains("Successfully processed"))
                continue;
            aclEntries++; // Unexpected entry
        }

        return aclEntries == 0;
    }

    public async Task RestartSshServiceAsync()
    {
        await RunCommandAsync("net", "stop sshd");
        await RunCommandAsync("net", "start sshd");
    }

    public async Task<bool> IsSshServiceRunningAsync()
    {
        try
        {
            var result = await RunCommandAsync("sc", "query sshd");
            return result.Contains("RUNNING");
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RunCommandAsync(string command, string arguments)
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
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static bool IsAdminUser()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
```

- [ ] **Step 8: Implement WslPlatform**

Create `src/Platform/WslPlatform.cs`:

```csharp
namespace SshEasyConfig.Platform;

public class WslPlatform : LinuxPlatform
{
    public new PlatformKind Kind => PlatformKind.Wsl;

    /// <summary>
    /// Returns true if running under WSL2 (which has its own network stack).
    /// WSL1 shares the Windows network stack.
    /// </summary>
    public bool IsWsl2 { get; }

    /// <summary>
    /// The Windows-side SSH directory path, accessible from WSL via /mnt/c/Users/...
    /// </summary>
    public string? WindowsSshDirectoryPath { get; }

    public WslPlatform()
    {
        IsWsl2 = DetectWsl2();
        WindowsSshDirectoryPath = DetectWindowsSshPath();
    }

    /// <summary>
    /// Translates a WSL path to a Windows path.
    /// e.g., /mnt/c/Users/james/.ssh -> C:\Users\james\.ssh
    /// </summary>
    public static string? ToWindowsPath(string wslPath)
    {
        if (!wslPath.StartsWith("/mnt/"))
            return null;

        // /mnt/c/Users/james -> C:\Users\james
        var parts = wslPath.Substring(5); // Remove "/mnt/"
        if (parts.Length < 1) return null;

        var driveLetter = char.ToUpper(parts[0]);
        var rest = parts.Length > 1 ? parts.Substring(1).Replace('/', '\\') : "\\";
        return $"{driveLetter}:{rest}";
    }

    /// <summary>
    /// Translates a Windows path to a WSL path.
    /// e.g., C:\Users\james\.ssh -> /mnt/c/Users/james/.ssh
    /// </summary>
    public static string? ToWslPath(string windowsPath)
    {
        if (windowsPath.Length < 2 || windowsPath[1] != ':')
            return null;

        var driveLetter = char.ToLower(windowsPath[0]);
        var rest = windowsPath.Substring(2).Replace('\\', '/');
        return $"/mnt/{driveLetter}{rest}";
    }

    private static bool DetectWsl2()
    {
        try
        {
            if (File.Exists("/proc/version"))
            {
                var version = File.ReadAllText("/proc/version");
                // WSL2 kernels contain "microsoft-standard-WSL2"
                return version.Contains("WSL2", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    private static string? DetectWindowsSshPath()
    {
        // Find the Windows user profile via /mnt/c/Users/<username>
        try
        {
            var windowsUser = Environment.GetEnvironmentVariable("WSLENV") is not null
                ? null // WSLENV exists but doesn't give us the Windows username directly
                : null;

            // Try to find it from cmd.exe
            var homeDir = "/mnt/c/Users";
            if (Directory.Exists(homeDir))
            {
                // Use wslpath or cmd.exe to get Windows USERPROFILE
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c echo %USERPROFILE%",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.Start();
                var winProfile = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(winProfile))
                {
                    var wslProfile = ToWslPath(winProfile);
                    if (wslProfile is not null)
                        return Path.Combine(wslProfile, ".ssh");
                }
            }
        }
        catch { }
        return null;
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: platform abstraction layer with Linux, macOS, Windows, WSL support"
```

---

## Task 3: Ed25519 Key Generation

**Files:**
- Create: `src/Core/KeyManager.cs`
- Create: `src/Core/OpenSshKeyFormat.cs`
- Create: `tests/SshEasyConfig.Tests/Core/OpenSshKeyFormatTests.cs`
- Create: `tests/SshEasyConfig.Tests/Core/KeyManagerTests.cs`

- [ ] **Step 1: Write tests for OpenSSH public key format**

Create `tests/SshEasyConfig.Tests/Core/OpenSshKeyFormatTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class OpenSshKeyFormatTests
{
    [Fact]
    public void FormatPublicKey_StartsWithKeyType()
    {
        var (publicKey, _) = TestKeyPair();
        var formatted = OpenSshKeyFormat.FormatPublicKey(publicKey, "test@host");
        Assert.StartsWith("ssh-ed25519 ", formatted);
    }

    [Fact]
    public void FormatPublicKey_EndsWithComment()
    {
        var (publicKey, _) = TestKeyPair();
        var formatted = OpenSshKeyFormat.FormatPublicKey(publicKey, "user@machine");
        Assert.EndsWith(" user@machine", formatted);
    }

    [Fact]
    public void FormatPublicKey_Base64MiddleSection()
    {
        var (publicKey, _) = TestKeyPair();
        var formatted = OpenSshKeyFormat.FormatPublicKey(publicKey, "test");
        var parts = formatted.Split(' ');
        Assert.Equal(3, parts.Length);
        // Should be valid base64
        var decoded = Convert.FromBase64String(parts[1]);
        Assert.True(decoded.Length > 0);
    }

    [Fact]
    public void FormatPrivateKey_HasCorrectPemBoundaries()
    {
        var (publicKey, privateKey) = TestKeyPair();
        var formatted = OpenSshKeyFormat.FormatPrivateKey(publicKey, privateKey, "test");
        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", formatted);
        Assert.Contains("-----END OPENSSH PRIVATE KEY-----", formatted);
    }

    [Fact]
    public void FormatPublicKey_RoundTrips_WireFormat()
    {
        var (publicKey, _) = TestKeyPair();
        var formatted = OpenSshKeyFormat.FormatPublicKey(publicKey, "test");
        var base64 = formatted.Split(' ')[1];
        var wireBytes = Convert.FromBase64String(base64);

        // Parse wire format: read key type string, then raw key
        using var ms = new MemoryStream(wireBytes);
        using var reader = new BinaryReader(ms);
        var typeLen = ReadUInt32BigEndian(reader);
        var typeBytes = reader.ReadBytes((int)typeLen);
        Assert.Equal("ssh-ed25519", System.Text.Encoding.UTF8.GetString(typeBytes));

        var keyLen = ReadUInt32BigEndian(reader);
        Assert.Equal(32u, keyLen);
        var rawKey = reader.ReadBytes(32);
        Assert.Equal(publicKey, rawKey);
    }

    private static (byte[] publicKey, byte[] privateKey) TestKeyPair()
    {
        using var ed = System.Security.Cryptography.Ed25519.Create();
        // Export raw 32-byte keys
        byte[] pub = new byte[32];
        byte[] priv = new byte[32];
        ed.TryExportEd25519PublicKey(pub, out _);
        ed.TryExportEd25519PrivateKey(priv, out _);
        return (pub, priv);
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test
```

Expected: FAIL — `OpenSshKeyFormat` does not exist.

- [ ] **Step 3: Implement OpenSshKeyFormat**

Create `src/Core/OpenSshKeyFormat.cs`:

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SshEasyConfig.Core;

/// <summary>
/// Formats Ed25519 keys in OpenSSH file formats.
/// All wire-format integers are big-endian per the SSH spec.
/// </summary>
public static class OpenSshKeyFormat
{
    private const string KeyType = "ssh-ed25519";
    private const string PemHeader = "-----BEGIN OPENSSH PRIVATE KEY-----";
    private const string PemFooter = "-----END OPENSSH PRIVATE KEY-----";
    private static readonly byte[] AuthMagic = "openssh-key-v1\0"u8.ToArray();

    /// <summary>
    /// Formats a 32-byte Ed25519 public key as an OpenSSH public key line.
    /// Output: "ssh-ed25519 AAAA... comment"
    /// </summary>
    public static string FormatPublicKey(byte[] rawPublicKey, string comment)
    {
        var wireBytes = BuildPublicKeyWireFormat(rawPublicKey);
        var base64 = Convert.ToBase64String(wireBytes);
        return $"{KeyType} {base64} {comment}";
    }

    /// <summary>
    /// Formats Ed25519 keys as an OpenSSH private key PEM block (unencrypted).
    /// </summary>
    public static string FormatPrivateKey(byte[] rawPublicKey, byte[] rawPrivateKey, string comment)
    {
        using var ms = new MemoryStream();

        // Auth magic
        ms.Write(AuthMagic);

        // Cipher: none (unencrypted)
        WriteWireString(ms, "none"u8);

        // KDF: none
        WriteWireString(ms, "none"u8);

        // KDF options: empty
        WriteWireUInt32(ms, 0);

        // Number of keys: 1
        WriteWireUInt32(ms, 1);

        // Public key block
        var pubWire = BuildPublicKeyWireFormat(rawPublicKey);
        WriteWireBytes(ms, pubWire);

        // Private key section
        var privSection = BuildPrivateKeySection(rawPublicKey, rawPrivateKey, comment);
        WriteWireBytes(ms, privSection);

        var allBytes = ms.ToArray();
        var base64 = Convert.ToBase64String(allBytes);

        var sb = new StringBuilder();
        sb.AppendLine(PemHeader);
        for (int i = 0; i < base64.Length; i += 70)
        {
            int len = Math.Min(70, base64.Length - i);
            sb.AppendLine(base64.Substring(i, len));
        }
        sb.Append(PemFooter);
        sb.AppendLine();
        return sb.ToString();
    }

    private static byte[] BuildPublicKeyWireFormat(byte[] rawPublicKey)
    {
        using var ms = new MemoryStream();
        WriteWireString(ms, Encoding.UTF8.GetBytes(KeyType));
        WriteWireBytes(ms, rawPublicKey);
        return ms.ToArray();
    }

    private static byte[] BuildPrivateKeySection(byte[] rawPublicKey, byte[] rawPrivateKey, string comment)
    {
        using var ms = new MemoryStream();

        // Two identical check integers (random, for integrity verification)
        var checkBytes = new byte[4];
        RandomNumberGenerator.Fill(checkBytes);
        ms.Write(checkBytes);
        ms.Write(checkBytes);

        // Key type
        WriteWireString(ms, Encoding.UTF8.GetBytes(KeyType));

        // Public key (32 bytes)
        WriteWireBytes(ms, rawPublicKey);

        // Private key: 64 bytes = 32-byte seed + 32-byte public key (OpenSSH convention)
        var expandedPrivate = new byte[64];
        rawPrivateKey.CopyTo(expandedPrivate, 0);
        rawPublicKey.CopyTo(expandedPrivate, 32);
        WriteWireBytes(ms, expandedPrivate);

        // Comment
        WriteWireString(ms, Encoding.UTF8.GetBytes(comment));

        // Padding: bytes 1, 2, 3, ... up to block alignment (block size = 8 for "none" cipher)
        const int blockSize = 8;
        int paddingNeeded = blockSize - ((int)ms.Length % blockSize);
        if (paddingNeeded == blockSize) paddingNeeded = 0;
        for (byte i = 1; i <= paddingNeeded; i++)
            ms.WriteByte(i);

        return ms.ToArray();
    }

    private static void WriteWireUInt32(Stream ms, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteWireString(Stream ms, ReadOnlySpan<byte> data)
    {
        WriteWireUInt32(ms, (uint)data.Length);
        ms.Write(data);
    }

    private static void WriteWireBytes(Stream ms, byte[] data)
    {
        WriteWireUInt32(ms, (uint)data.Length);
        ms.Write(data);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~OpenSshKeyFormat"
```

Expected: All pass.

- [ ] **Step 5: Write KeyManager tests**

Create `tests/SshEasyConfig.Tests/Core/KeyManagerTests.cs`:

```csharp
using NSubstitute;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Core;

public class KeyManagerTests
{
    [Fact]
    public void GenerateKeyPair_ReturnsValidKeys()
    {
        var result = KeyManager.GenerateKeyPair("test@host");
        Assert.NotNull(result.PublicKeyOpenSsh);
        Assert.NotNull(result.PrivateKeyPem);
        Assert.StartsWith("ssh-ed25519 ", result.PublicKeyOpenSsh);
        Assert.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", result.PrivateKeyPem);
        Assert.EndsWith(" test@host", result.PublicKeyOpenSsh);
    }

    [Fact]
    public void GenerateKeyPair_ProducesDifferentKeysEachCall()
    {
        var a = KeyManager.GenerateKeyPair("test");
        var b = KeyManager.GenerateKeyPair("test");
        Assert.NotEqual(a.PublicKeyOpenSsh, b.PublicKeyOpenSsh);
    }

    [Fact]
    public async Task SaveKeyPair_WritesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sshtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var platform = Substitute.For<IPlatform>();
            platform.SshDirectoryPath.Returns(tempDir);

            var keyPair = KeyManager.GenerateKeyPair("test");
            await KeyManager.SaveKeyPairAsync(platform, keyPair, "id_ed25519");

            var pubPath = Path.Combine(tempDir, "id_ed25519.pub");
            var privPath = Path.Combine(tempDir, "id_ed25519");

            Assert.True(File.Exists(pubPath));
            Assert.True(File.Exists(privPath));
            Assert.StartsWith("ssh-ed25519", await File.ReadAllTextAsync(pubPath));
            Assert.Contains("BEGIN OPENSSH PRIVATE KEY", await File.ReadAllTextAsync(privPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~KeyManager"
```

Expected: FAIL — `KeyManager` does not exist.

- [ ] **Step 7: Implement KeyManager**

Create `src/Core/KeyManager.cs`:

```csharp
using System.Security.Cryptography;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public record SshKeyPair(
    string PublicKeyOpenSsh,
    string PrivateKeyPem,
    byte[] RawPublicKey);

public static class KeyManager
{
    public static SshKeyPair GenerateKeyPair(string comment)
    {
        using var ed = Ed25519.Create();
        var rawPublic = new byte[32];
        var rawPrivate = new byte[32];
        ed.TryExportEd25519PublicKey(rawPublic, out _);
        ed.TryExportEd25519PrivateKey(rawPrivate, out _);

        var publicKeyLine = OpenSshKeyFormat.FormatPublicKey(rawPublic, comment);
        var privateKeyPem = OpenSshKeyFormat.FormatPrivateKey(rawPublic, rawPrivate, comment);

        // Zero out raw private key material
        CryptographicOperations.ZeroMemory(rawPrivate);

        return new SshKeyPair(publicKeyLine, privateKeyPem, rawPublic);
    }

    public static async Task SaveKeyPairAsync(IPlatform platform, SshKeyPair keyPair, string keyName)
    {
        var sshDir = platform.SshDirectoryPath;
        if (!Directory.Exists(sshDir))
        {
            Directory.CreateDirectory(sshDir);
            await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
        }

        var privatePath = Path.Combine(sshDir, keyName);
        var publicPath = Path.Combine(sshDir, $"{keyName}.pub");

        await File.WriteAllTextAsync(privatePath, keyPair.PrivateKeyPem);
        await File.WriteAllTextAsync(publicPath, keyPair.PublicKeyOpenSsh + "\n");

        await platform.SetFilePermissionsAsync(privatePath, SshFileKind.PrivateKey);
    }

    public static bool KeyExists(IPlatform platform, string keyName)
    {
        var privatePath = Path.Combine(platform.SshDirectoryPath, keyName);
        return File.Exists(privatePath);
    }

    public static async Task<string?> ReadPublicKeyAsync(IPlatform platform, string keyName)
    {
        var publicPath = Path.Combine(platform.SshDirectoryPath, $"{keyName}.pub");
        if (!File.Exists(publicPath))
            return null;
        return (await File.ReadAllTextAsync(publicPath)).Trim();
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~KeyManager"
```

Expected: All pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: Ed25519 key generation with OpenSSH format export"
```

---

## Task 4: authorized_keys Management

**Files:**
- Create: `src/Core/AuthorizedKeysManager.cs`
- Create: `tests/SshEasyConfig.Tests/Core/AuthorizedKeysManagerTests.cs`

- [ ] **Step 1: Write tests for authorized_keys operations**

Create `tests/SshEasyConfig.Tests/Core/AuthorizedKeysManagerTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class AuthorizedKeysManagerTests
{
    [Fact]
    public void AddKey_ToEmpty_AddsLine()
    {
        var content = "";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA test@host");
        Assert.Equal("ssh-ed25519 AAAA test@host\n", result);
    }

    [Fact]
    public void AddKey_PreservesExistingEntries()
    {
        var content = "ssh-rsa BBBB existing@host\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA new@host");
        Assert.Contains("ssh-rsa BBBB existing@host", result);
        Assert.Contains("ssh-ed25519 AAAA new@host", result);
    }

    [Fact]
    public void AddKey_DeduplicatesSameKey()
    {
        var content = "ssh-ed25519 AAAA test@host\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA test@host");
        // Should appear exactly once
        var occurrences = result.Split('\n').Count(l => l.Trim() == "ssh-ed25519 AAAA test@host");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void AddKey_DeduplicatesByKeyData_IgnoringComment()
    {
        var content = "ssh-ed25519 AAAA oldcomment\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA newcomment");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        // Keeps the existing entry (does not replace)
        Assert.Contains("oldcomment", lines[0]);
    }

    [Fact]
    public void AddKey_PreservesComments()
    {
        var content = "# My important comment\nssh-rsa BBBB existing\n";
        var result = AuthorizedKeysManager.AddKey(content, "ssh-ed25519 AAAA new");
        Assert.Contains("# My important comment", result);
    }

    [Fact]
    public void RemoveKey_RemovesMatchingEntry()
    {
        var content = "ssh-ed25519 AAAA test@host\nssh-rsa BBBB other@host\n";
        var result = AuthorizedKeysManager.RemoveKey(content, "ssh-ed25519 AAAA");
        Assert.DoesNotContain("ssh-ed25519 AAAA", result);
        Assert.Contains("ssh-rsa BBBB other@host", result);
    }

    [Fact]
    public void RemoveKey_PreservesOtherEntries()
    {
        var content = "# comment\nssh-ed25519 AAAA remove\nssh-rsa BBBB keep\n";
        var result = AuthorizedKeysManager.RemoveKey(content, "ssh-ed25519 AAAA");
        Assert.Contains("# comment", result);
        Assert.Contains("ssh-rsa BBBB keep", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~AuthorizedKeys"
```

Expected: FAIL — `AuthorizedKeysManager` does not exist.

- [ ] **Step 3: Implement AuthorizedKeysManager**

Create `src/Core/AuthorizedKeysManager.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class AuthorizedKeysManager
{
    /// <summary>
    /// Adds a public key line to authorized_keys content, deduplicating by key data.
    /// </summary>
    public static string AddKey(string existingContent, string publicKeyLine)
    {
        var newKeyData = ExtractKeyData(publicKeyLine);
        if (newKeyData is null)
            throw new ArgumentException("Invalid public key line", nameof(publicKeyLine));

        var lines = existingContent.Split('\n').ToList();

        // Check for duplicate (match on key type + key data, ignore comment)
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var existingKeyData = ExtractKeyData(line);
            if (existingKeyData == newKeyData)
                return NormalizeContent(lines);
        }

        // Remove trailing empty lines, add the key, add final newline
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        lines.Add(publicKeyLine);
        return NormalizeContent(lines);
    }

    /// <summary>
    /// Removes entries matching the given key type + key data prefix.
    /// </summary>
    public static string RemoveKey(string existingContent, string keyTypeAndData)
    {
        var lines = existingContent.Split('\n').ToList();
        lines.RemoveAll(line =>
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                return false;
            return line.TrimStart().StartsWith(keyTypeAndData, StringComparison.Ordinal);
        });
        return NormalizeContent(lines);
    }

    /// <summary>
    /// Gets the full path to the authorized_keys file for the current platform.
    /// </summary>
    public static string GetPath(IPlatform platform)
    {
        return Path.Combine(platform.SshDirectoryPath, platform.AuthorizedKeysFilename);
    }

    /// <summary>
    /// Reads the current authorized_keys content, or empty string if file doesn't exist.
    /// </summary>
    public static async Task<string> ReadAsync(IPlatform platform)
    {
        var path = GetPath(platform);
        if (!File.Exists(path))
            return "";
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Writes authorized_keys content and sets correct permissions.
    /// </summary>
    public static async Task WriteAsync(IPlatform platform, string content)
    {
        var path = GetPath(platform);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            await platform.SetFilePermissionsAsync(dir, SshFileKind.SshDirectory);
        }

        await File.WriteAllTextAsync(path, content);
        await platform.SetFilePermissionsAsync(path, SshFileKind.AuthorizedKeys);
    }

    /// <summary>
    /// Extracts "keytype keydata" (without comment) from a public key line.
    /// </summary>
    private static string? ExtractKeyData(string line)
    {
        var trimmed = line.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return $"{parts[0]} {parts[1]}";
    }

    private static string NormalizeContent(List<string> lines)
    {
        // Remove trailing empty lines
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0) return "";
        return string.Join('\n', lines) + "\n";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~AuthorizedKeys"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: authorized_keys management with deduplication"
```

---

## Task 5: SSH Config Parser/Writer

**Files:**
- Create: `src/Core/SshConfigManager.cs`
- Create: `tests/SshEasyConfig.Tests/Core/SshConfigManagerTests.cs`

- [ ] **Step 1: Write tests for ssh_config parsing and writing**

Create `tests/SshEasyConfig.Tests/Core/SshConfigManagerTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class SshConfigManagerTests
{
    [Fact]
    public void ParseHosts_EmptyContent_ReturnsEmpty()
    {
        var hosts = SshConfigManager.ParseHosts("");
        Assert.Empty(hosts);
    }

    [Fact]
    public void ParseHosts_SingleHost_ReturnsEntry()
    {
        var content = """
            Host myserver
                HostName 192.168.1.100
                User admin
                Port 22
                IdentityFile ~/.ssh/id_ed25519
            """;
        var hosts = SshConfigManager.ParseHosts(content);
        Assert.Single(hosts);
        Assert.Equal("myserver", hosts[0].Alias);
        Assert.Equal("192.168.1.100", hosts[0].HostName);
        Assert.Equal("admin", hosts[0].User);
        Assert.Equal(22, hosts[0].Port);
    }

    [Fact]
    public void ParseHosts_MultipleHosts()
    {
        var content = """
            Host server1
                HostName 10.0.0.1
                User alice

            Host server2
                HostName 10.0.0.2
                User bob
            """;
        var hosts = SshConfigManager.ParseHosts(content);
        Assert.Equal(2, hosts.Count);
        Assert.Equal("server1", hosts[0].Alias);
        Assert.Equal("server2", hosts[1].Alias);
    }

    [Fact]
    public void AddHost_ToEmptyConfig()
    {
        var entry = new SshHostEntry("myserver", "192.168.1.100", "admin", 22, "~/.ssh/id_ed25519");
        var result = SshConfigManager.AddHost("", entry);
        Assert.Contains("Host myserver", result);
        Assert.Contains("HostName 192.168.1.100", result);
        Assert.Contains("User admin", result);
        Assert.Contains("IdentityFile ~/.ssh/id_ed25519", result);
    }

    [Fact]
    public void AddHost_PreservesExistingContent()
    {
        var existing = """
            # Global settings
            Host *
                ServerAliveInterval 60

            Host existing
                HostName 10.0.0.1
            """;
        var entry = new SshHostEntry("newhost", "10.0.0.2", "root", 2222, null);
        var result = SshConfigManager.AddHost(existing, entry);
        Assert.Contains("# Global settings", result);
        Assert.Contains("Host existing", result);
        Assert.Contains("Host newhost", result);
        Assert.Contains("Port 2222", result);
    }

    [Fact]
    public void RemoveHost_RemovesEntireBlock()
    {
        var content = """
            Host keep
                HostName 10.0.0.1

            Host remove
                HostName 10.0.0.2
                User admin

            Host alsokeep
                HostName 10.0.0.3
            """;
        var result = SshConfigManager.RemoveHost(content, "remove");
        Assert.DoesNotContain("Host remove", result);
        Assert.DoesNotContain("10.0.0.2", result);
        Assert.Contains("Host keep", result);
        Assert.Contains("Host alsokeep", result);
    }

    [Fact]
    public void AddHost_DoesNotAddDefaultPort()
    {
        var entry = new SshHostEntry("myserver", "10.0.0.1", "admin", 22, null);
        var result = SshConfigManager.AddHost("", entry);
        Assert.DoesNotContain("Port", result);
    }

    [Fact]
    public void AddHost_IncludesNonDefaultPort()
    {
        var entry = new SshHostEntry("myserver", "10.0.0.1", "admin", 2222, null);
        var result = SshConfigManager.AddHost("", entry);
        Assert.Contains("Port 2222", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~SshConfigManager"
```

Expected: FAIL.

- [ ] **Step 3: Implement SshConfigManager**

Create `src/Core/SshConfigManager.cs`:

```csharp
using System.Text;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public record SshHostEntry(
    string Alias,
    string HostName,
    string? User,
    int Port = 22,
    string? IdentityFile = null,
    bool ForwardAgent = false);

public static class SshConfigManager
{
    public static List<SshHostEntry> ParseHosts(string content)
    {
        var hosts = new List<SshHostEntry>();
        string? currentAlias = null;
        string? hostName = null;
        string? user = null;
        int port = 22;
        string? identityFile = null;
        bool forwardAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                // Blank line or comment might end a block contextually,
                // but SSH config doesn't require blank lines between blocks.
                continue;
            }

            var (key, value) = ParseDirective(line);
            if (key is null) continue;

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous host if any
                if (currentAlias is not null && hostName is not null)
                {
                    hosts.Add(new SshHostEntry(currentAlias, hostName, user, port, identityFile, forwardAgent));
                }
                currentAlias = value;
                hostName = null;
                user = null;
                port = 22;
                identityFile = null;
                forwardAgent = false;
            }
            else if (key.Equals("HostName", StringComparison.OrdinalIgnoreCase))
                hostName = value;
            else if (key.Equals("User", StringComparison.OrdinalIgnoreCase))
                user = value;
            else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
                port = p;
            else if (key.Equals("IdentityFile", StringComparison.OrdinalIgnoreCase))
                identityFile = value;
            else if (key.Equals("ForwardAgent", StringComparison.OrdinalIgnoreCase))
                forwardAgent = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        // Save last host
        if (currentAlias is not null && hostName is not null)
        {
            hosts.Add(new SshHostEntry(currentAlias, hostName, user, port, identityFile, forwardAgent));
        }

        return hosts;
    }

    public static string AddHost(string existingContent, SshHostEntry entry)
    {
        var sb = new StringBuilder(existingContent.TrimEnd());
        if (sb.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine($"Host {entry.Alias}");
        sb.AppendLine($"    HostName {entry.HostName}");

        if (entry.User is not null)
            sb.AppendLine($"    User {entry.User}");
        if (entry.Port != 22)
            sb.AppendLine($"    Port {entry.Port}");
        if (entry.IdentityFile is not null)
            sb.AppendLine($"    IdentityFile {entry.IdentityFile}");
        if (entry.ForwardAgent)
            sb.AppendLine("    ForwardAgent yes");

        return sb.ToString();
    }

    public static string RemoveHost(string content, string alias)
    {
        var lines = content.Split('\n').ToList();
        int blockStart = -1;
        int blockEnd = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var (key, value) = ParseDirective(lines[i].Trim());
            if (key?.Equals("Host", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (value == alias)
                {
                    blockStart = i;
                    // Find end of block: next Host line or end of file
                    blockEnd = lines.Count;
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        var (k2, _) = ParseDirective(lines[j].Trim());
                        if (k2?.Equals("Host", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            blockEnd = j;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        if (blockStart == -1)
            return content;

        // Remove the block and any preceding blank lines
        lines.RemoveRange(blockStart, blockEnd - blockStart);

        // Clean up double blank lines at the removal point
        while (blockStart > 0 && blockStart < lines.Count
            && string.IsNullOrWhiteSpace(lines[blockStart - 1])
            && string.IsNullOrWhiteSpace(lines[blockStart]))
        {
            lines.RemoveAt(blockStart);
        }

        return string.Join('\n', lines);
    }

    public static string GetConfigPath(IPlatform platform)
    {
        return Path.Combine(platform.SshDirectoryPath, "config");
    }

    public static async Task<string> ReadConfigAsync(IPlatform platform)
    {
        var path = GetConfigPath(platform);
        if (!File.Exists(path))
            return "";
        return await File.ReadAllTextAsync(path);
    }

    public static async Task WriteConfigAsync(IPlatform platform, string content)
    {
        var path = GetConfigPath(platform);
        await File.WriteAllTextAsync(path, content);
        await platform.SetFilePermissionsAsync(path, SshFileKind.Config);
    }

    private static (string? key, string? value) ParseDirective(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            return (null, null);

        // SSH config uses "Key Value" or "Key=Value" syntax
        var eqIndex = line.IndexOf('=');
        var spIndex = line.IndexOf(' ');

        int splitAt;
        if (eqIndex >= 0 && (spIndex < 0 || eqIndex < spIndex))
            splitAt = eqIndex;
        else if (spIndex >= 0)
            splitAt = spIndex;
        else
            return (line, null);

        var key = line[..splitAt].Trim();
        var value = line[(splitAt + 1)..].Trim();
        return (key, value);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~SshConfigManager"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: SSH config parser/writer with host alias management"
```

---

## Task 6: sshd_config Auditing and Modification

**Files:**
- Create: `src/Core/SshdConfigManager.cs`
- Create: `tests/SshEasyConfig.Tests/Core/SshdConfigManagerTests.cs`

- [ ] **Step 1: Write tests for sshd_config auditing**

Create `tests/SshEasyConfig.Tests/Core/SshdConfigManagerTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class SshdConfigManagerTests
{
    [Fact]
    public void Audit_DetectsPasswordAuthEnabled()
    {
        var content = """
            PasswordAuthentication yes
            PubkeyAuthentication yes
            """;
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PasswordAuthentication" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Audit_DetectsMissingPubkeyAuth()
    {
        var content = """
            PasswordAuthentication no
            """;
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PubkeyAuthentication");
    }

    [Fact]
    public void Audit_DetectsRootLoginPermitted()
    {
        var content = """
            PermitRootLogin yes
            PasswordAuthentication no
            PubkeyAuthentication yes
            """;
        var findings = SshdConfigManager.Audit(content);
        Assert.Contains(findings, f => f.Key == "PermitRootLogin" && f.Severity == AuditSeverity.Warning);
    }

    [Fact]
    public void Audit_PassesSecureConfig()
    {
        var content = """
            PasswordAuthentication no
            PubkeyAuthentication yes
            PermitRootLogin no
            """;
        var findings = SshdConfigManager.Audit(content);
        Assert.All(findings, f => Assert.Equal(AuditSeverity.Ok, f.Severity));
    }

    [Fact]
    public void SetDirective_UpdatesExistingValue()
    {
        var content = "PasswordAuthentication yes\nPubkeyAuthentication no\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("PasswordAuthentication no", result);
        Assert.DoesNotContain("PasswordAuthentication yes", result);
    }

    [Fact]
    public void SetDirective_AddsNewDirective()
    {
        var content = "PubkeyAuthentication yes\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("PasswordAuthentication no", result);
        Assert.Contains("PubkeyAuthentication yes", result);
    }

    [Fact]
    public void SetDirective_PreservesComments()
    {
        var content = "# Important comment\nPasswordAuthentication yes\n";
        var result = SshdConfigManager.SetDirective(content, "PasswordAuthentication", "no");
        Assert.Contains("# Important comment", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~SshdConfig"
```

Expected: FAIL.

- [ ] **Step 3: Implement SshdConfigManager**

Create `src/Core/SshdConfigManager.cs`:

```csharp
using System.Text;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public enum AuditSeverity { Ok, Warning, Info }

public record AuditFinding(string Key, AuditSeverity Severity, string CurrentValue, string RecommendedValue, string Message);

public static class SshdConfigManager
{
    private static readonly (string Key, string Recommended, string BadValue, string Message)[] Rules =
    [
        ("PasswordAuthentication", "no", "yes", "Password authentication is enabled. Disable it for key-only access."),
        ("PubkeyAuthentication", "yes", "no", "Public key authentication is disabled. Enable it to allow key-based login."),
        ("PermitRootLogin", "no", "yes", "Root login is permitted. Disable it or set to 'prohibit-password'."),
    ];

    public static List<AuditFinding> Audit(string content)
    {
        var directives = ParseDirectives(content);
        var findings = new List<AuditFinding>();

        foreach (var (key, recommended, badValue, message) in Rules)
        {
            if (directives.TryGetValue(key, out var actual))
            {
                if (actual.Equals(badValue, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AuditFinding(key, AuditSeverity.Warning, actual, recommended, message));
                }
                else
                {
                    findings.Add(new AuditFinding(key, AuditSeverity.Ok, actual, recommended, $"{key} is set to '{actual}'."));
                }
            }
            else
            {
                // Missing directive — flag if it should be explicitly set
                findings.Add(new AuditFinding(key, AuditSeverity.Info, "(not set)", recommended,
                    $"{key} is not explicitly set. Recommend setting to '{recommended}'."));
            }
        }

        return findings;
    }

    public static string SetDirective(string content, string key, string value)
    {
        var lines = content.Split('\n').ToList();
        bool found = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#')) continue;

            var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key} {value}";
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Add at end, before any trailing whitespace
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);
            lines.Add($"{key} {value}");
        }

        return string.Join('\n', lines) + "\n";
    }

    public static async Task<string> BackupAndReadAsync(IPlatform platform)
    {
        var path = platform.SshdConfigPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"sshd_config not found at {path}");

        // Create timestamped backup
        var backupPath = $"{path}.backup.{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Copy(path, backupPath, overwrite: false);

        return await File.ReadAllTextAsync(path);
    }

    public static async Task WriteAsync(IPlatform platform, string content)
    {
        await File.WriteAllTextAsync(platform.SshdConfigPath, content);
    }

    private static Dictionary<string, string> ParseDirectives(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                result[parts[0]] = parts[1];
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~SshdConfig"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: sshd_config auditing and directive management"
```

---

## Task 7: Key Exchange Data Format and Fallback Modes

**Files:**
- Create: `src/Core/KeyBundle.cs`
- Create: `tests/SshEasyConfig.Tests/Core/KeyBundleTests.cs`

- [ ] **Step 1: Write tests for the key bundle serialization**

Create `tests/SshEasyConfig.Tests/Core/KeyBundleTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class KeyBundleTests
{
    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var bundle = new KeyBundle(
            PublicKey: "ssh-ed25519 AAAA test@host",
            Hostname: "mypc",
            Username: "james",
            SuggestedAlias: "mypc-james");

        var json = bundle.ToJson();
        var parsed = KeyBundle.FromJson(json);

        Assert.Equal(bundle.PublicKey, parsed.PublicKey);
        Assert.Equal(bundle.Hostname, parsed.Hostname);
        Assert.Equal(bundle.Username, parsed.Username);
        Assert.Equal(bundle.SuggestedAlias, parsed.SuggestedAlias);
    }

    [Fact]
    public void ToClipboardText_ProducesBase64Block()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var text = bundle.ToClipboardText();
        Assert.StartsWith("--- BEGIN SSH-EASY-CONFIG ---", text);
        Assert.Contains("--- END SSH-EASY-CONFIG ---", text);
    }

    [Fact]
    public void FromClipboardText_RoundTrips()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var text = bundle.ToClipboardText();
        var parsed = KeyBundle.FromClipboardText(text);
        Assert.Equal(bundle.PublicKey, parsed.PublicKey);
        Assert.Equal(bundle.Hostname, parsed.Hostname);
    }

    [Fact]
    public void SaveToFile_And_LoadFromFile_RoundTrips()
    {
        var bundle = new KeyBundle("ssh-ed25519 AAAA test", "host", "user", "alias");
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.sshec");

        try
        {
            bundle.SaveToFile(path);
            Assert.True(File.Exists(path));
            var loaded = KeyBundle.LoadFromFile(path);
            Assert.Equal(bundle.PublicKey, loaded.PublicKey);
            Assert.Equal(bundle.Hostname, loaded.Hostname);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~KeyBundle"
```

Expected: FAIL.

- [ ] **Step 3: Implement KeyBundle**

Create `src/Core/KeyBundle.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace SshEasyConfig.Core;

/// <summary>
/// The data exchanged between machines during key sharing.
/// Same format used by all transfer modes (network, clipboard, file).
/// </summary>
public record KeyBundle(
    string PublicKey,
    string Hostname,
    string Username,
    string? SuggestedAlias)
{
    private const string ClipboardHeader = "--- BEGIN SSH-EASY-CONFIG ---";
    private const string ClipboardFooter = "--- END SSH-EASY-CONFIG ---";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static KeyBundle FromJson(string json) =>
        JsonSerializer.Deserialize<KeyBundle>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize key bundle");

    public string ToClipboardText()
    {
        var json = ToJson();
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var sb = new StringBuilder();
        sb.AppendLine(ClipboardHeader);
        for (int i = 0; i < base64.Length; i += 76)
        {
            int len = Math.Min(76, base64.Length - i);
            sb.AppendLine(base64.Substring(i, len));
        }
        sb.Append(ClipboardFooter);
        return sb.ToString();
    }

    public static KeyBundle FromClipboardText(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l != ClipboardHeader && l != ClipboardFooter && !string.IsNullOrEmpty(l))
            .ToArray();

        var base64 = string.Join("", lines);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return FromJson(json);
    }

    public void SaveToFile(string path)
    {
        File.WriteAllText(path, ToJson());
    }

    public static KeyBundle LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return FromJson(json);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~KeyBundle"
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: key bundle data format with clipboard and file serialization"
```

---

## Task 8: Network Exchange with Pairing Protocol

**Files:**
- Create: `src/Core/PairingProtocol.cs`
- Create: `src/Core/NetworkExchange.cs`
- Create: `tests/SshEasyConfig.Tests/Core/PairingProtocolTests.cs`

- [ ] **Step 1: Write tests for pairing code generation and key derivation**

Create `tests/SshEasyConfig.Tests/Core/PairingProtocolTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class PairingProtocolTests
{
    [Fact]
    public void GeneratePairingCode_Returns6Digits()
    {
        var code = PairingProtocol.GeneratePairingCode();
        Assert.Equal(6, code.Length);
        Assert.True(int.TryParse(code, out var n));
        Assert.InRange(n, 100000, 999999);
    }

    [Fact]
    public void GeneratePairingCode_ProducesDifferentCodes()
    {
        var codes = Enumerable.Range(0, 10).Select(_ => PairingProtocol.GeneratePairingCode()).ToHashSet();
        Assert.True(codes.Count > 1, "Should generate different codes");
    }

    [Fact]
    public void DeriveKey_SameInputs_ProduceSameKey()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt123");
        var key2 = PairingProtocol.DeriveKey("123456", "salt123");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentCodes_ProduceDifferentKeys()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt123");
        var key2 = PairingProtocol.DeriveKey("654321", "salt123");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_Returns32Bytes()
    {
        var key = PairingProtocol.DeriveKey("123456", "salt");
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var key = PairingProtocol.DeriveKey("123456", "salt");
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, SSH!");
        var (ciphertext, nonce) = PairingProtocol.Encrypt(key, plaintext);
        var decrypted = PairingProtocol.Decrypt(key, ciphertext, nonce);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var key1 = PairingProtocol.DeriveKey("123456", "salt");
        var key2 = PairingProtocol.DeriveKey("000000", "salt");
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var (ciphertext, nonce) = PairingProtocol.Encrypt(key1, plaintext);
        Assert.ThrowsAny<Exception>(() => PairingProtocol.Decrypt(key2, ciphertext, nonce));
    }

    [Fact]
    public void ComputeFingerprint_DeterministicForSameKey()
    {
        var fp1 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test");
        var fp2 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test");
        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_DifferentForDifferentKeys()
    {
        var fp1 = PairingProtocol.ComputeFingerprint("ssh-ed25519 AAAA test1");
        var fp2 = PairingProtocol.ComputeFingerprint("ssh-ed25519 BBBB test2");
        Assert.NotEqual(fp1, fp2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~PairingProtocol"
```

Expected: FAIL.

- [ ] **Step 3: Implement PairingProtocol**

Create `src/Core/PairingProtocol.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SshEasyConfig.Core;

/// <summary>
/// Handles pairing code generation, key derivation, encryption,
/// and fingerprint display for the network key exchange.
/// </summary>
public static class PairingProtocol
{
    public static string GeneratePairingCode()
    {
        // Generate a 6-digit code (100000-999999)
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    /// <summary>
    /// Derives a 32-byte encryption key from a pairing code and salt using HKDF.
    /// </summary>
    public static byte[] DeriveKey(string pairingCode, string salt)
    {
        var ikm = Encoding.UTF8.GetBytes(pairingCode);
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var info = Encoding.UTF8.GetBytes("ssh-easy-config-pairing-v1");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, saltBytes, info);
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with the derived key.
    /// Returns (ciphertext with tag appended, nonce).
    /// </summary>
    public static (byte[] ciphertext, byte[] nonce) Encrypt(byte[] key, byte[] plaintext)
    {
        using var aes = new AesGcm(key, tagSize: 16);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Append tag to ciphertext for simplicity
        var result = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(result, 0);
        tag.CopyTo(result, ciphertext.Length);

        return (result, nonce);
    }

    /// <summary>
    /// Decrypts data encrypted with Encrypt(). Expects ciphertext with tag appended.
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] ciphertextWithTag, byte[] nonce)
    {
        using var aes = new AesGcm(key, tagSize: 16);

        var ciphertext = ciphertextWithTag[..^16];
        var tag = ciphertextWithTag[^16..];
        var plaintext = new byte[ciphertext.Length];

        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>
    /// Computes a human-readable fingerprint for visual verification.
    /// Format: "SHA256:xxxx:xxxx:xxxx:xxxx" (first 8 bytes of SHA256 hash, hex).
    /// </summary>
    public static string ComputeFingerprint(string publicKeyLine)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKeyLine));
        var hex = Convert.ToHexString(hash[..8]).ToLower();
        // Format as colon-separated pairs
        var parts = Enumerable.Range(0, hex.Length / 4)
            .Select(i => hex.Substring(i * 4, 4));
        return $"SHA256:{string.Join(':', parts)}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~PairingProtocol"
```

Expected: All pass.

- [ ] **Step 5: Implement NetworkExchange (listener and connector)**

Create `src/Core/NetworkExchange.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SshEasyConfig.Core;

/// <summary>
/// Handles direct network key exchange between two machines.
/// One side runs as a listener (share), the other as a connector (receive).
/// </summary>
public class NetworkExchange
{
    public int Port { get; }

    public NetworkExchange(int port = 0)
    {
        Port = port;
    }

    /// <summary>
    /// Starts a listener that waits for a single connection, performs the pairing
    /// handshake, exchanges key bundles, and shuts down.
    /// </summary>
    public async Task<KeyBundle?> ListenAndExchangeAsync(
        KeyBundle localBundle,
        string pairingCode,
        Action<string> onStatus,
        CancellationToken ct = default)
    {
        using var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        onStatus($"Listening on port {actualPort}");

        try
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            onStatus("Connection received. Performing handshake...");
            return await PerformExchangeAsync(client.GetStream(), localBundle, pairingCode, isListener: true);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Connects to a listener, performs the pairing handshake, and exchanges key bundles.
    /// </summary>
    public async Task<KeyBundle?> ConnectAndExchangeAsync(
        string host,
        int port,
        KeyBundle localBundle,
        string pairingCode,
        CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        return await PerformExchangeAsync(client.GetStream(), localBundle, pairingCode, isListener: false);
    }

    /// <summary>
    /// Gets the actual port the listener bound to (useful when Port=0 for auto-assignment).
    /// </summary>
    public int GetListenerPort(TcpListener listener)
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task<KeyBundle?> PerformExchangeAsync(
        NetworkStream stream,
        KeyBundle localBundle,
        string pairingCode,
        bool isListener)
    {
        // 1. Exchange salt (listener sends, connector reads)
        string salt;
        if (isListener)
        {
            salt = Guid.NewGuid().ToString("N");
            await SendFrameAsync(stream, Encoding.UTF8.GetBytes(salt));
        }
        else
        {
            var saltBytes = await ReadFrameAsync(stream);
            salt = Encoding.UTF8.GetString(saltBytes);
        }

        // 2. Derive encryption key from pairing code + salt
        var key = PairingProtocol.DeriveKey(pairingCode, salt);

        // 3. Encrypt and send our bundle
        var localJson = Encoding.UTF8.GetBytes(localBundle.ToJson());
        var (encrypted, nonce) = PairingProtocol.Encrypt(key, localJson);

        // Send nonce + encrypted data
        await SendFrameAsync(stream, nonce);
        await SendFrameAsync(stream, encrypted);

        // 4. Receive and decrypt their bundle
        var remoteNonce = await ReadFrameAsync(stream);
        var remoteCiphertext = await ReadFrameAsync(stream);

        try
        {
            var remoteJson = PairingProtocol.Decrypt(key, remoteCiphertext, remoteNonce);
            return KeyBundle.FromJson(Encoding.UTF8.GetString(remoteJson));
        }
        catch (CryptographicException)
        {
            // Wrong pairing code
            return null;
        }
    }

    private static async Task SendFrameAsync(NetworkStream stream, byte[] data)
    {
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length);
        await stream.WriteAsync(lenBuf);
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream)
    {
        var lenBuf = new byte[4];
        await stream.ReadExactlyAsync(lenBuf);
        var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);

        if (len < 0 || len > 1024 * 1024) // 1MB sanity limit
            throw new InvalidOperationException($"Invalid frame length: {len}");

        var data = new byte[len];
        await stream.ReadExactlyAsync(data);
        return data;
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: network key exchange with pairing code security"
```

---

## Task 9: mDNS Discovery

**Files:**
- Modify: `src/SshEasyConfig.csproj` (add Makaretu.Dns.Multicast)
- Create: `src/Core/Discovery.cs`
- Create: `tests/SshEasyConfig.Tests/Core/DiscoveryTests.cs`

- [ ] **Step 1: Add the mDNS NuGet package**

```bash
cd /c/Development/ssh-easy-config/src
dotnet add package Makaretu.Dns.Multicast
```

- [ ] **Step 2: Write a basic test for Discovery**

Create `tests/SshEasyConfig.Tests/Core/DiscoveryTests.cs`:

```csharp
using SshEasyConfig.Core;

namespace SshEasyConfig.Tests.Core;

public class DiscoveryTests
{
    [Fact]
    public void ServiceName_IsCorrect()
    {
        Assert.Equal("_ssh-easy._tcp.local", Discovery.ServiceType);
    }

    [Fact]
    public void CreateServiceProfile_ContainsPort()
    {
        var profile = Discovery.CreateServiceProfile("myhost", 12345);
        Assert.Equal("myhost", profile.HostName);
        Assert.Equal(12345, profile.Port);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~Discovery"
```

Expected: FAIL.

- [ ] **Step 4: Implement Discovery**

Create `src/Core/Discovery.cs`:

```csharp
using Makaretu.Dns;

namespace SshEasyConfig.Core;

/// <summary>
/// mDNS-based discovery for finding ssh-easy-config instances on the local network.
/// </summary>
public static class Discovery
{
    public const string ServiceType = "_ssh-easy._tcp.local";
    private const string ServiceName = "_ssh-easy._tcp";

    public record ServiceProfile(string HostName, int Port, string InstanceName);

    public static ServiceProfile CreateServiceProfile(string hostName, int port)
    {
        var instanceName = $"ssh-easy-config on {hostName}";
        return new ServiceProfile(hostName, port, instanceName);
    }

    /// <summary>
    /// Advertises this machine's ssh-easy-config listener via mDNS.
    /// Returns an IDisposable that stops advertising when disposed.
    /// </summary>
    public static async Task<IDisposable> AdvertiseAsync(ServiceProfile profile)
    {
        var mdns = new MulticastService();
        var sd = new ServiceDiscovery(mdns);

        var serviceProfile = new Makaretu.Dns.ServiceProfile(
            profile.InstanceName,
            ServiceName,
            (ushort)profile.Port);

        sd.Advertise(serviceProfile);
        mdns.Start();

        // Return a disposable that stops the mDNS service
        return new AdvertisementHandle(mdns, sd);
    }

    /// <summary>
    /// Browses for ssh-easy-config instances on the local network.
    /// Returns discovered instances as they are found.
    /// </summary>
    public static async Task<List<ServiceProfile>> BrowseAsync(TimeSpan timeout)
    {
        var results = new List<ServiceProfile>();
        using var mdns = new MulticastService();
        var sd = new ServiceDiscovery(mdns);
        using var cts = new CancellationTokenSource(timeout);

        sd.ServiceInstanceDiscovered += (_, args) =>
        {
            var msg = args.Message;
            string? hostName = null;
            int port = 0;

            foreach (var record in msg.AdditionalRecords)
            {
                if (record is SRVRecord srv)
                {
                    hostName = srv.Target.ToString();
                    port = srv.Port;
                }
            }

            if (hostName is not null && port > 0)
            {
                results.Add(new ServiceProfile(hostName, port, args.ServiceInstanceName.ToString()));
            }
        };

        mdns.Start();
        sd.QueryServiceInstances(ServiceName);

        try { await Task.Delay(timeout, cts.Token); }
        catch (OperationCanceledException) { }

        return results;
    }

    private sealed class AdvertisementHandle(MulticastService mdns, ServiceDiscovery sd) : IDisposable
    {
        public void Dispose()
        {
            sd.Dispose();
            mdns.Stop();
            mdns.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~Discovery"
```

Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: mDNS discovery for LAN-based machine finding"
```

---

## Task 10: Diagnostic Checks

**Files:**
- Create: `src/Diagnostics/DiagnosticResult.cs`
- Create: `src/Diagnostics/NetworkCheck.cs`
- Create: `src/Diagnostics/SshServiceCheck.cs`
- Create: `src/Diagnostics/AuthCheck.cs`
- Create: `src/Diagnostics/ConfigCheck.cs`
- Create: `src/Diagnostics/WslCheck.cs`
- Create: `src/Diagnostics/DiagnosticRunner.cs`
- Create: `tests/SshEasyConfig.Tests/Diagnostics/NetworkCheckTests.cs`
- Create: `tests/SshEasyConfig.Tests/Diagnostics/ConfigCheckTests.cs`

- [ ] **Step 1: Define the diagnostic result model**

Create `src/Diagnostics/DiagnosticResult.cs`:

```csharp
namespace SshEasyConfig.Diagnostics;

public enum CheckStatus { Pass, Warn, Fail, Skip }

public record DiagnosticResult(
    string CheckName,
    CheckStatus Status,
    string Message,
    string? FixSuggestion = null,
    bool AutoFixAvailable = false);
```

- [ ] **Step 2: Write tests for NetworkCheck**

Create `tests/SshEasyConfig.Tests/Diagnostics/NetworkCheckTests.cs`:

```csharp
using SshEasyConfig.Diagnostics;

namespace SshEasyConfig.Tests.Diagnostics;

public class NetworkCheckTests
{
    [Fact]
    public async Task CheckDns_Localhost_Resolves()
    {
        var results = await NetworkCheck.CheckDnsAsync("localhost");
        Assert.Contains(results, r => r.CheckName == "DNS Resolution" && r.Status == CheckStatus.Pass);
    }

    [Fact]
    public async Task CheckDns_InvalidHost_Fails()
    {
        var results = await NetworkCheck.CheckDnsAsync("this-host-does-not-exist-12345.invalid");
        Assert.Contains(results, r => r.CheckName == "DNS Resolution" && r.Status == CheckStatus.Fail);
    }

    [Fact]
    public async Task CheckPort_ClosedPort_Fails()
    {
        // Port 1 is almost certainly closed
        var result = await NetworkCheck.CheckPortAsync("127.0.0.1", 1, TimeSpan.FromSeconds(1));
        Assert.Equal(CheckStatus.Fail, result.Status);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~NetworkCheck"
```

Expected: FAIL.

- [ ] **Step 4: Implement NetworkCheck**

Create `src/Diagnostics/NetworkCheck.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace SshEasyConfig.Diagnostics;

public static class NetworkCheck
{
    public static async Task<List<DiagnosticResult>> CheckDnsAsync(string host)
    {
        var results = new List<DiagnosticResult>();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length > 0)
            {
                var addrList = string.Join(", ", addresses.Select(a => a.ToString()));
                results.Add(new DiagnosticResult(
                    "DNS Resolution", CheckStatus.Pass,
                    $"{host} resolves to {addrList}"));
            }
            else
            {
                results.Add(new DiagnosticResult(
                    "DNS Resolution", CheckStatus.Fail,
                    $"{host} resolved but returned no addresses",
                    "Check DNS configuration or use an IP address directly"));
            }
        }
        catch (SocketException ex)
        {
            results.Add(new DiagnosticResult(
                "DNS Resolution", CheckStatus.Fail,
                $"Cannot resolve {host}: {ex.Message}",
                "Verify hostname is correct, check DNS settings, or use IP address"));
        }

        return results;
    }

    public static async Task<DiagnosticResult> CheckPortAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(host, port, cts.Token);
            return new DiagnosticResult(
                "TCP Port", CheckStatus.Pass,
                $"Port {port} on {host} is reachable");
        }
        catch (OperationCanceledException)
        {
            return new DiagnosticResult(
                "TCP Port", CheckStatus.Fail,
                $"Connection to {host}:{port} timed out (possible firewall block)",
                $"Check firewall rules allow inbound connections on port {port}",
                AutoFixAvailable: false);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new DiagnosticResult(
                "TCP Port", CheckStatus.Fail,
                $"Connection to {host}:{port} refused (SSH service may not be running)",
                "Ensure the SSH server (sshd) is installed and running");
        }
        catch (SocketException ex)
        {
            return new DiagnosticResult(
                "TCP Port", CheckStatus.Fail,
                $"Cannot connect to {host}:{port}: {ex.Message}",
                "Check network connectivity and firewall rules");
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~NetworkCheck"
```

Expected: All pass.

- [ ] **Step 6: Implement SshServiceCheck**

Create `src/Diagnostics/SshServiceCheck.cs`:

```csharp
using System.Net.Sockets;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class SshServiceCheck
{
    public static async Task<DiagnosticResult> CheckBannerAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(host, port, cts.Token);

            using var stream = client.GetStream();
            stream.ReadTimeout = (int)timeout.TotalMilliseconds;

            var buffer = new byte[256];
            var bytesRead = await stream.ReadAsync(buffer, cts.Token);
            var banner = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            if (banner.StartsWith("SSH-"))
            {
                return new DiagnosticResult(
                    "SSH Banner", CheckStatus.Pass,
                    $"SSH service responded: {banner}");
            }

            return new DiagnosticResult(
                "SSH Banner", CheckStatus.Warn,
                $"Service on port {port} responded but is not SSH: {banner}");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult(
                "SSH Banner", CheckStatus.Fail,
                $"Could not read SSH banner: {ex.Message}");
        }
    }

    public static async Task<DiagnosticResult> CheckLocalServiceAsync(IPlatform platform)
    {
        var running = await platform.IsSshServiceRunningAsync();
        return running
            ? new DiagnosticResult("Local sshd", CheckStatus.Pass, "SSH service is running")
            : new DiagnosticResult("Local sshd", CheckStatus.Fail,
                "SSH service is not running",
                "Start the SSH service on this machine",
                AutoFixAvailable: true);
    }
}
```

- [ ] **Step 7: Implement ConfigCheck**

Create `src/Diagnostics/ConfigCheck.cs`:

```csharp
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class ConfigCheck
{
    public static async Task<List<DiagnosticResult>> CheckClientConfigAsync(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();
        var configPath = SshConfigManager.GetConfigPath(platform);

        if (!File.Exists(configPath))
        {
            results.Add(new DiagnosticResult("Client Config", CheckStatus.Pass,
                "No ~/.ssh/config file found (this is OK for basic usage)"));
            return results;
        }

        // Check permissions
        var permsOk = await platform.CheckFilePermissionsAsync(configPath, SshFileKind.Config);
        results.Add(permsOk
            ? new DiagnosticResult("Config Permissions", CheckStatus.Pass,
                $"Permissions on {configPath} are correct")
            : new DiagnosticResult("Config Permissions", CheckStatus.Warn,
                $"Permissions on {configPath} may be too open",
                "Fix file permissions",
                AutoFixAvailable: true));

        // Basic syntax check
        try
        {
            var content = await File.ReadAllTextAsync(configPath);
            SshConfigManager.ParseHosts(content);
            results.Add(new DiagnosticResult("Config Syntax", CheckStatus.Pass,
                "Config file parses without errors"));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticResult("Config Syntax", CheckStatus.Fail,
                $"Config file has errors: {ex.Message}"));
        }

        return results;
    }

    public static List<DiagnosticResult> CheckSshdConfig(string sshdContent, IPlatform platform)
    {
        var results = new List<DiagnosticResult>();
        var findings = SshdConfigManager.Audit(sshdContent);

        foreach (var f in findings)
        {
            var status = f.Severity switch
            {
                AuditSeverity.Ok => CheckStatus.Pass,
                AuditSeverity.Warning => CheckStatus.Warn,
                AuditSeverity.Info => CheckStatus.Warn,
                _ => CheckStatus.Warn
            };
            results.Add(new DiagnosticResult(
                $"sshd: {f.Key}", status, f.Message,
                f.Severity != AuditSeverity.Ok ? $"Set '{f.Key} {f.RecommendedValue}' in sshd_config" : null,
                AutoFixAvailable: f.Severity != AuditSeverity.Ok));
        }

        return results;
    }

    public static async Task<List<DiagnosticResult>> CheckFilePermissionsAsync(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();
        var sshDir = platform.SshDirectoryPath;

        if (!Directory.Exists(sshDir))
        {
            results.Add(new DiagnosticResult("SSH Directory", CheckStatus.Warn,
                $"SSH directory {sshDir} does not exist",
                "Run 'ssh-easy-config setup' to create it"));
            return results;
        }

        // Check .ssh directory permissions
        var dirOk = await platform.CheckFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
        results.Add(dirOk
            ? new DiagnosticResult("SSH Dir Permissions", CheckStatus.Pass, $"{sshDir} permissions are correct")
            : new DiagnosticResult("SSH Dir Permissions", CheckStatus.Fail,
                $"{sshDir} has incorrect permissions (should be 700/owner-only)",
                "Fix directory permissions",
                AutoFixAvailable: true));

        // Check private key permissions
        foreach (var keyFile in Directory.GetFiles(sshDir, "id_*").Where(f => !f.EndsWith(".pub")))
        {
            var keyOk = await platform.CheckFilePermissionsAsync(keyFile, SshFileKind.PrivateKey);
            var name = Path.GetFileName(keyFile);
            results.Add(keyOk
                ? new DiagnosticResult($"Key Permissions: {name}", CheckStatus.Pass, $"{name} permissions are correct")
                : new DiagnosticResult($"Key Permissions: {name}", CheckStatus.Fail,
                    $"{name} has incorrect permissions (should be 600/owner-only)",
                    "Fix key file permissions",
                    AutoFixAvailable: true));
        }

        return results;
    }
}

```

- [ ] **Step 8: Implement WslCheck**

Create `src/Diagnostics/WslCheck.cs`:

```csharp
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

public static class WslCheck
{
    public static List<DiagnosticResult> Check(IPlatform platform)
    {
        var results = new List<DiagnosticResult>();

        if (platform is not WslPlatform wsl)
        {
            results.Add(new DiagnosticResult("WSL", CheckStatus.Skip, "Not running in WSL"));
            return results;
        }

        // WSL version detection
        results.Add(new DiagnosticResult(
            "WSL Version", CheckStatus.Pass,
            wsl.IsWsl2 ? "Running under WSL2 (separate network stack)" : "Running under WSL1 (shared network stack)"));

        // Windows SSH directory detection
        if (wsl.WindowsSshDirectoryPath is not null)
        {
            var winSshExists = Directory.Exists(wsl.WindowsSshDirectoryPath);
            results.Add(winSshExists
                ? new DiagnosticResult("Windows .ssh", CheckStatus.Pass,
                    $"Windows SSH directory found at {wsl.WindowsSshDirectoryPath}")
                : new DiagnosticResult("Windows .ssh", CheckStatus.Warn,
                    $"Windows SSH directory not found at {wsl.WindowsSshDirectoryPath}",
                    "Windows-side SSH may not be configured"));
        }
        else
        {
            results.Add(new DiagnosticResult("Windows .ssh", CheckStatus.Warn,
                "Could not detect Windows user profile path from WSL"));
        }

        // WSL2 port forwarding note
        if (wsl.IsWsl2)
        {
            results.Add(new DiagnosticResult("WSL2 Networking", CheckStatus.Warn,
                "WSL2 uses a virtual network. SSH from Windows to WSL2 requires port forwarding or localhost access.",
                "Use 'localhost' from Windows to reach WSL2 SSH, or configure port forwarding"));
        }

        return results;
    }
}
```

- [ ] **Step 9: Implement DiagnosticRunner**

Create `src/Diagnostics/DiagnosticRunner.cs`:

```csharp
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Diagnostics;

/// <summary>
/// Orchestrates all diagnostic checks in layered order.
/// </summary>
public class DiagnosticRunner(IPlatform platform)
{
    public async Task<List<DiagnosticResult>> RunAllAsync(string? host, int port = 22)
    {
        var results = new List<DiagnosticResult>();

        // Layer 1: Network
        if (host is not null)
        {
            var dnsResults = await NetworkCheck.CheckDnsAsync(host);
            results.AddRange(dnsResults);

            if (dnsResults.Any(r => r.Status == CheckStatus.Fail))
                return results; // Stop if DNS fails

            var portResult = await NetworkCheck.CheckPortAsync(host, port, TimeSpan.FromSeconds(5));
            results.Add(portResult);

            if (portResult.Status == CheckStatus.Fail)
                return results; // Stop if port unreachable
        }

        // Layer 2: SSH Service
        if (host is not null)
        {
            var banner = await SshServiceCheck.CheckBannerAsync(host, port, TimeSpan.FromSeconds(5));
            results.Add(banner);
        }

        var localService = await SshServiceCheck.CheckLocalServiceAsync(platform);
        results.Add(localService);

        // Layer 3: Auth (check local key exists)
        var sshDir = platform.SshDirectoryPath;
        var keyPath = Path.Combine(sshDir, "id_ed25519.pub");
        if (File.Exists(keyPath))
        {
            results.Add(new DiagnosticResult("Local Key", CheckStatus.Pass,
                $"Ed25519 public key found at {keyPath}"));
        }
        else
        {
            results.Add(new DiagnosticResult("Local Key", CheckStatus.Warn,
                "No Ed25519 key found",
                "Run 'ssh-easy-config setup' to generate one"));
        }

        // Layer 4: Configuration
        results.AddRange(await ConfigCheck.CheckFilePermissionsAsync(platform));
        results.AddRange(await ConfigCheck.CheckClientConfigAsync(platform));

        if (File.Exists(platform.SshdConfigPath))
        {
            var sshdContent = await File.ReadAllTextAsync(platform.SshdConfigPath);
            results.AddRange(ConfigCheck.CheckSshdConfig(sshdContent, platform));
        }

        // Layer 5: WSL
        results.AddRange(WslCheck.Check(platform));

        return results;
    }
}
```

- [ ] **Step 10: Write a test for ConfigCheck**

Create `tests/SshEasyConfig.Tests/Diagnostics/ConfigCheckTests.cs`:

```csharp
using SshEasyConfig.Core;
using SshEasyConfig.Diagnostics;
using NSubstitute;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Tests.Diagnostics;

public class ConfigCheckTests
{
    [Fact]
    public void CheckSshdConfig_SecureConfig_AllPass()
    {
        var content = """
            PasswordAuthentication no
            PubkeyAuthentication yes
            PermitRootLogin no
            """;
        var platform = Substitute.For<IPlatform>();
        var results = ConfigCheck.CheckSshdConfig(content, platform);
        Assert.All(results, r => Assert.Equal(CheckStatus.Pass, r.Status));
    }

    [Fact]
    public void CheckSshdConfig_InsecureConfig_HasWarnings()
    {
        var content = """
            PasswordAuthentication yes
            PermitRootLogin yes
            """;
        var platform = Substitute.For<IPlatform>();
        var results = ConfigCheck.CheckSshdConfig(content, platform);
        Assert.Contains(results, r => r.Status == CheckStatus.Warn);
    }
}
```

- [ ] **Step 11: Run all tests**

```bash
dotnet test
```

Expected: All pass.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: diagnostic checks (network, SSH service, auth, config, WSL)"
```

---

## Task 11: CLI Command Wiring

**Files:**
- Create: `src/Commands/SetupCommand.cs`
- Create: `src/Commands/ShareCommand.cs`
- Create: `src/Commands/ReceiveCommand.cs`
- Create: `src/Commands/DiagnoseCommand.cs`
- Create: `src/Commands/ConfigCommand.cs`
- Modify: `src/Program.cs`

- [ ] **Step 1: Implement SetupCommand**

Create `src/Commands/SetupCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class SetupCommand
{
    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.MarkupLine("[bold blue]SSH Easy Config - Setup[/]\n");

        var keyName = "id_ed25519";

        // Check for existing key
        if (KeyManager.KeyExists(platform, keyName))
        {
            var existing = await KeyManager.ReadPublicKeyAsync(platform, keyName);
            AnsiConsole.MarkupLine($"[yellow]Existing Ed25519 key found:[/] {existing?[..50]}...");

            var reuse = AnsiConsole.Prompt(
                new ConfirmationPrompt("Use existing key?") { DefaultValue = true });

            if (reuse)
            {
                AnsiConsole.MarkupLine("[green]Using existing key.[/]");
                return 0;
            }
        }

        // Generate new key
        var comment = AnsiConsole.Prompt(
            new TextPrompt<string>("Key comment:")
                .DefaultValue($"{Environment.UserName}@{Environment.MachineName}"));

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Generating Ed25519 key...", _ =>
            {
                var keyPair = KeyManager.GenerateKeyPair(comment);
                KeyManager.SaveKeyPairAsync(platform, keyPair, keyName).GetAwaiter().GetResult();
            });

        var pubKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);
        AnsiConsole.MarkupLine($"\n[green]Key generated successfully![/]");
        AnsiConsole.MarkupLine($"[dim]Public key:[/] {pubKey}");
        AnsiConsole.MarkupLine($"[dim]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(pubKey!)}");

        return 0;
    }
}
```

- [ ] **Step 2: Implement ShareCommand**

Create `src/Commands/ShareCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ShareCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string mode, string? output)
    {
        var pubKey = await KeyManager.ReadPublicKeyAsync(platform, "id_ed25519");
        if (pubKey is null)
        {
            AnsiConsole.MarkupLine("[red]No Ed25519 key found. Run 'ssh-easy-config setup' first.[/]");
            return 1;
        }

        var bundle = new KeyBundle(
            PublicKey: pubKey,
            Hostname: Environment.MachineName,
            Username: Environment.UserName,
            SuggestedAlias: $"{Environment.MachineName.ToLower()}-{Environment.UserName.ToLower()}");

        switch (mode)
        {
            case "clipboard":
                var clipText = bundle.ToClipboardText();
                AnsiConsole.MarkupLine("[bold blue]Copy the block below and paste it on the other machine:[/]\n");
                AnsiConsole.WriteLine(clipText);
                return 0;

            case "file":
                var filePath = output ?? "key-bundle.sshec";
                bundle.SaveToFile(filePath);
                AnsiConsole.MarkupLine($"[green]Key bundle saved to {filePath}[/]");
                return 0;

            case "network":
            default:
                return await RunNetworkShareAsync(platform, bundle);
        }
    }

    private static async Task<int> RunNetworkShareAsync(IPlatform platform, KeyBundle bundle)
    {
        var exchange = new NetworkExchange();
        var pairingCode = PairingProtocol.GeneratePairingCode();

        AnsiConsole.MarkupLine("[bold blue]SSH Easy Config - Share Keys[/]\n");
        AnsiConsole.MarkupLine($"[bold yellow]Pairing code: {pairingCode}[/]");
        AnsiConsole.MarkupLine("[dim]Enter this code on the receiving machine.[/]\n");

        // Try mDNS discovery
        IDisposable? advertisement = null;
        int port = 0;

        try
        {
            var remoteBundle = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Waiting for connection...", async ctx =>
                {
                    // Start listener
                    var result = await exchange.ListenAndExchangeAsync(
                        bundle,
                        pairingCode,
                        status => ctx.Status(status));
                    return result;
                });

            if (remoteBundle is null)
            {
                AnsiConsole.MarkupLine("[red]Key exchange failed. Wrong pairing code?[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"\n[green]Received key from {remoteBundle.Hostname}![/]");
            AnsiConsole.MarkupLine($"[dim]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(remoteBundle.PublicKey)}");

            // Add to authorized_keys
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt("Add this key to authorized_keys?") { DefaultValue = true });

            if (confirm)
            {
                var existing = await AuthorizedKeysManager.ReadAsync(platform);
                var updated = AuthorizedKeysManager.AddKey(existing, remoteBundle.PublicKey);
                await AuthorizedKeysManager.WriteAsync(platform, updated);
                AnsiConsole.MarkupLine("[green]Key added to authorized_keys.[/]");
            }

            // Suggest ssh_config alias
            if (remoteBundle.SuggestedAlias is not null)
            {
                var addAlias = AnsiConsole.Prompt(
                    new ConfirmationPrompt($"Add SSH config alias '{remoteBundle.SuggestedAlias}'?") { DefaultValue = true });

                if (addAlias)
                {
                    var configContent = await SshConfigManager.ReadConfigAsync(platform);
                    var entry = new SshHostEntry(
                        remoteBundle.SuggestedAlias,
                        remoteBundle.Hostname,
                        remoteBundle.Username);
                    var updated = SshConfigManager.AddHost(configContent, entry);
                    await SshConfigManager.WriteConfigAsync(platform, updated);
                    AnsiConsole.MarkupLine($"[green]Added host alias '{remoteBundle.SuggestedAlias}' to SSH config.[/]");
                }
            }

            return 0;
        }
        finally
        {
            advertisement?.Dispose();
        }
    }
}
```

- [ ] **Step 3: Implement ReceiveCommand**

Create `src/Commands/ReceiveCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ReceiveCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string mode, string? input)
    {
        var pubKey = await KeyManager.ReadPublicKeyAsync(platform, "id_ed25519");
        if (pubKey is null)
        {
            AnsiConsole.MarkupLine("[red]No Ed25519 key found. Run 'ssh-easy-config setup' first.[/]");
            return 1;
        }

        var localBundle = new KeyBundle(
            PublicKey: pubKey,
            Hostname: Environment.MachineName,
            Username: Environment.UserName,
            SuggestedAlias: $"{Environment.MachineName.ToLower()}-{Environment.UserName.ToLower()}");

        switch (mode)
        {
            case "clipboard":
                return await RunClipboardReceiveAsync(platform, localBundle);

            case "file":
                var filePath = input ?? "key-bundle.sshec";
                return await RunFileReceiveAsync(platform, filePath);

            case "network":
            default:
                return await RunNetworkReceiveAsync(platform, localBundle);
        }
    }

    private static async Task<int> RunClipboardReceiveAsync(IPlatform platform, KeyBundle localBundle)
    {
        AnsiConsole.MarkupLine("[bold blue]Paste the key bundle from the other machine:[/]");
        var lines = new List<string>();
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            lines.Add(line);
            if (line.Contains("--- END SSH-EASY-CONFIG ---"))
                break;
        }

        var text = string.Join('\n', lines);
        var remoteBundle = KeyBundle.FromClipboardText(text);
        return await ApplyRemoteBundleAsync(platform, remoteBundle);
    }

    private static async Task<int> RunFileReceiveAsync(IPlatform platform, string path)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]File not found: {path}[/]");
            return 1;
        }

        var remoteBundle = KeyBundle.LoadFromFile(path);
        return await ApplyRemoteBundleAsync(platform, remoteBundle);
    }

    private static async Task<int> RunNetworkReceiveAsync(IPlatform platform, KeyBundle localBundle)
    {
        AnsiConsole.MarkupLine("[bold blue]SSH Easy Config - Receive Keys[/]\n");

        // Try mDNS discovery first
        AnsiConsole.MarkupLine("[dim]Searching for ssh-easy-config instances on local network...[/]");
        var discovered = await Discovery.BrowseAsync(TimeSpan.FromSeconds(3));

        string host;
        int port;

        if (discovered.Count > 0)
        {
            var choices = discovered.Select(d => $"{d.HostName}:{d.Port} ({d.InstanceName})").ToList();
            choices.Add("Enter manually");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Found instances:")
                    .AddChoices(choices));

            if (choice == "Enter manually")
            {
                (host, port) = PromptForHostPort();
            }
            else
            {
                var idx = choices.IndexOf(choice);
                host = discovered[idx].HostName;
                port = discovered[idx].Port;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No instances found on local network.[/]");
            (host, port) = PromptForHostPort();
        }

        var pairingCode = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter pairing code:"));

        var exchange = new NetworkExchange();
        var remoteBundle = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Connecting...", async _ =>
            {
                return await exchange.ConnectAndExchangeAsync(host, port, localBundle, pairingCode);
            });

        if (remoteBundle is null)
        {
            AnsiConsole.MarkupLine("[red]Key exchange failed. Wrong pairing code?[/]");
            return 1;
        }

        return await ApplyRemoteBundleAsync(platform, remoteBundle);
    }

    private static async Task<int> ApplyRemoteBundleAsync(IPlatform platform, KeyBundle remoteBundle)
    {
        AnsiConsole.MarkupLine($"\n[green]Received key from {remoteBundle.Hostname}![/]");
        AnsiConsole.MarkupLine($"[dim]User:[/] {remoteBundle.Username}");
        AnsiConsole.MarkupLine($"[dim]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(remoteBundle.PublicKey)}");

        var addKey = AnsiConsole.Prompt(
            new ConfirmationPrompt("Add this key to authorized_keys?") { DefaultValue = true });

        if (addKey)
        {
            var existing = await AuthorizedKeysManager.ReadAsync(platform);
            var updated = AuthorizedKeysManager.AddKey(existing, remoteBundle.PublicKey);
            await AuthorizedKeysManager.WriteAsync(platform, updated);
            AnsiConsole.MarkupLine("[green]Key added to authorized_keys.[/]");
        }

        if (remoteBundle.SuggestedAlias is not null)
        {
            var addAlias = AnsiConsole.Prompt(
                new ConfirmationPrompt($"Add SSH config alias '{remoteBundle.SuggestedAlias}'?") { DefaultValue = true });

            if (addAlias)
            {
                var config = await SshConfigManager.ReadConfigAsync(platform);
                var entry = new SshHostEntry(remoteBundle.SuggestedAlias, remoteBundle.Hostname, remoteBundle.Username);
                var updated = SshConfigManager.AddHost(config, entry);
                await SshConfigManager.WriteConfigAsync(platform, updated);
                AnsiConsole.MarkupLine($"[green]Added host alias '{remoteBundle.SuggestedAlias}'.[/]");
            }
        }

        return 0;
    }

    private static (string host, int port) PromptForHostPort()
    {
        var host = AnsiConsole.Prompt(new TextPrompt<string>("Host/IP:"));
        var port = AnsiConsole.Prompt(
            new TextPrompt<int>("Port:").DefaultValue(9022));
        return (host, port);
    }
}
```

- [ ] **Step 4: Implement DiagnoseCommand**

Create `src/Commands/DiagnoseCommand.cs`:

```csharp
using System.Text.Json;
using Spectre.Console;
using SshEasyConfig.Diagnostics;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class DiagnoseCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string? host, bool json, bool verbose)
    {
        var runner = new DiagnosticRunner(platform);
        var results = await runner.RunAllAsync(host);

        if (json)
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(results, jsonOptions));
            return results.Any(r => r.Status == CheckStatus.Fail) ? 1 : 0;
        }

        // Interactive output
        var title = host is not null
            ? $"[bold blue]Diagnosing SSH connectivity to {host}[/]"
            : "[bold blue]Diagnosing local SSH configuration[/]";
        AnsiConsole.MarkupLine($"\n{title}\n");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Status")
            .AddColumn("Check")
            .AddColumn("Details");

        foreach (var result in results)
        {
            if (!verbose && result.Status == CheckStatus.Skip)
                continue;

            var statusIcon = result.Status switch
            {
                CheckStatus.Pass => "[green]PASS[/]",
                CheckStatus.Warn => "[yellow]WARN[/]",
                CheckStatus.Fail => "[red]FAIL[/]",
                CheckStatus.Skip => "[dim]SKIP[/]",
                _ => "[dim]???[/]"
            };

            var details = result.Message;
            if (result.FixSuggestion is not null)
                details += $"\n[dim]Fix: {result.FixSuggestion}[/]";

            table.AddRow(statusIcon, Markup.Escape(result.CheckName), details);
        }

        AnsiConsole.Write(table);

        // Summary
        var fails = results.Count(r => r.Status == CheckStatus.Fail);
        var warns = results.Count(r => r.Status == CheckStatus.Warn);

        AnsiConsole.WriteLine();
        if (fails == 0 && warns == 0)
            AnsiConsole.MarkupLine("[green]All checks passed![/]");
        else if (fails == 0)
            AnsiConsole.MarkupLine($"[yellow]{warns} warning(s), no failures.[/]");
        else
            AnsiConsole.MarkupLine($"[red]{fails} failure(s), {warns} warning(s).[/]");

        return fails > 0 ? 1 : 0;
    }
}
```

- [ ] **Step 5: Implement ConfigCommand**

Create `src/Commands/ConfigCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ConfigCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string? subaction)
    {
        switch (subaction)
        {
            case "audit":
                return await AuditAsync(platform);
            case "harden":
                return await HardenAsync(platform);
            case "hosts":
                return await ListHostsAsync(platform);
            default:
                return await InteractiveAsync(platform);
        }
    }

    private static async Task<int> AuditAsync(IPlatform platform)
    {
        if (!File.Exists(platform.SshdConfigPath))
        {
            AnsiConsole.MarkupLine($"[yellow]sshd_config not found at {platform.SshdConfigPath}[/]");
            return 1;
        }

        var content = await File.ReadAllTextAsync(platform.SshdConfigPath);
        var findings = SshdConfigManager.Audit(content);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Setting")
            .AddColumn("Current")
            .AddColumn("Recommended")
            .AddColumn("Status");

        foreach (var f in findings)
        {
            var status = f.Severity switch
            {
                AuditSeverity.Ok => "[green]OK[/]",
                AuditSeverity.Warning => "[yellow]WARNING[/]",
                AuditSeverity.Info => "[blue]INFO[/]",
                _ => "[dim]?[/]"
            };
            table.AddRow(f.Key, f.CurrentValue, f.RecommendedValue, status);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> HardenAsync(IPlatform platform)
    {
        if (!File.Exists(platform.SshdConfigPath))
        {
            AnsiConsole.MarkupLine($"[red]sshd_config not found at {platform.SshdConfigPath}[/]");
            return 1;
        }

        var content = await SshdConfigManager.BackupAndReadAsync(platform);
        AnsiConsole.MarkupLine("[dim]Backup created.[/]");

        var findings = SshdConfigManager.Audit(content);
        var needsFix = findings.Where(f => f.Severity != AuditSeverity.Ok).ToList();

        if (needsFix.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]sshd_config is already secure.[/]");
            return 0;
        }

        foreach (var f in needsFix)
        {
            var apply = AnsiConsole.Prompt(
                new ConfirmationPrompt($"Set {f.Key} to '{f.RecommendedValue}'? ({f.Message})")
                    { DefaultValue = true });

            if (apply)
            {
                content = SshdConfigManager.SetDirective(content, f.Key, f.RecommendedValue);
                AnsiConsole.MarkupLine($"[green]Set {f.Key} = {f.RecommendedValue}[/]");
            }
        }

        await SshdConfigManager.WriteAsync(platform, content);

        var restart = AnsiConsole.Prompt(
            new ConfirmationPrompt("Restart SSH service to apply changes?") { DefaultValue = true });

        if (restart)
        {
            await platform.RestartSshServiceAsync();
            AnsiConsole.MarkupLine("[green]SSH service restarted.[/]");
        }

        return 0;
    }

    private static async Task<int> ListHostsAsync(IPlatform platform)
    {
        var content = await SshConfigManager.ReadConfigAsync(platform);
        var hosts = SshConfigManager.ParseHosts(content);

        if (hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No host entries found in SSH config.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Alias")
            .AddColumn("HostName")
            .AddColumn("User")
            .AddColumn("Port");

        foreach (var h in hosts)
        {
            table.AddRow(
                h.Alias,
                h.HostName,
                h.User ?? "[dim]-[/]",
                h.Port == 22 ? "[dim]22[/]" : h.Port.ToString());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> InteractiveAsync(IPlatform platform)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Audit sshd_config", "Harden sshd_config", "List SSH hosts", "Cancel"));

        return action switch
        {
            "Audit sshd_config" => await AuditAsync(platform),
            "Harden sshd_config" => await HardenAsync(platform),
            "List SSH hosts" => await ListHostsAsync(platform),
            _ => 0
        };
    }
}
```

- [ ] **Step 6: Wire up Program.cs with all commands**

Replace `src/Program.cs` with:

```csharp
using System.CommandLine;
using SshEasyConfig.Commands;
using SshEasyConfig.Platform;

var platform = PlatformDetector.Detect();
var rootCommand = new RootCommand("ssh-easy-config - Cross-platform SSH key management, sharing, and diagnostics");

// --- setup ---
var setupCommand = new Command("setup", "Generate SSH keys and configure SSH");
setupCommand.SetAction(async _ => await SetupCommand.RunAsync(platform));
rootCommand.Subcommands.Add(setupCommand);

// --- share ---
var shareCommand = new Command("share", "Share keys with another machine");
var shareModeOption = new Option<string>("--mode", () => "network", "Transfer mode: network, clipboard, file");
var shareOutputOption = new Option<string?>("--output", "Output file path (for file mode)");
shareCommand.Options.Add(shareModeOption);
shareCommand.Options.Add(shareOutputOption);
shareCommand.SetAction(async parseResult =>
{
    var mode = parseResult.GetValue(shareModeOption)!;
    var output = parseResult.GetValue(shareOutputOption);
    return await ShareCommand.RunAsync(platform, mode, output);
});
rootCommand.Subcommands.Add(shareCommand);

// --- receive ---
var receiveCommand = new Command("receive", "Listen for incoming key share");
var receiveModeOption = new Option<string>("--mode", () => "network", "Transfer mode: network, clipboard, file");
var receiveInputOption = new Option<string?>("--input", "Input file path (for file mode)");
receiveCommand.Options.Add(receiveModeOption);
receiveCommand.Options.Add(receiveInputOption);
receiveCommand.SetAction(async parseResult =>
{
    var mode = parseResult.GetValue(receiveModeOption)!;
    var input = parseResult.GetValue(receiveInputOption);
    return await ReceiveCommand.RunAsync(platform, mode, input);
});
rootCommand.Subcommands.Add(receiveCommand);

// --- diagnose ---
var diagnoseCommand = new Command("diagnose", "Diagnose SSH connectivity");
var hostArgument = new Argument<string?>("host", () => null, "The host to diagnose");
var jsonOption = new Option<bool>("--json", "Output results as JSON");
var verboseOption = new Option<bool>("--verbose", "Show all checks including skipped");
diagnoseCommand.Arguments.Add(hostArgument);
diagnoseCommand.Options.Add(jsonOption);
diagnoseCommand.Options.Add(verboseOption);
diagnoseCommand.SetAction(async parseResult =>
{
    var host = parseResult.GetValue(hostArgument);
    var json = parseResult.GetValue(jsonOption);
    var verbose = parseResult.GetValue(verboseOption);
    return await DiagnoseCommand.RunAsync(platform, host, json, verbose);
});
rootCommand.Subcommands.Add(diagnoseCommand);

// --- config ---
var configCommand = new Command("config", "Manage ssh_config / sshd_config");
var configActionArg = new Argument<string?>("action", () => null, "Action: audit, harden, hosts");
configCommand.Arguments.Add(configActionArg);
configCommand.SetAction(async parseResult =>
{
    var action = parseResult.GetValue(configActionArg);
    return await ConfigCommand.RunAsync(platform, action);
});
rootCommand.Subcommands.Add(configCommand);

// --- root (interactive wizard) ---
rootCommand.SetAction(async _ =>
{
    // TODO: Task 12 will implement the full wizard
    Spectre.Console.AnsiConsole.MarkupLine("[bold blue]SSH Easy Config[/]\n");
    var action = Spectre.Console.AnsiConsole.Prompt(
        new Spectre.Console.SelectionPrompt<string>()
            .Title("What would you like to do?")
            .AddChoices("Setup SSH keys", "Share keys with another machine", "Receive keys", "Diagnose connectivity", "Manage SSH config"));

    return action switch
    {
        "Setup SSH keys" => await SetupCommand.RunAsync(platform),
        "Share keys with another machine" => await ShareCommand.RunAsync(platform, "network", null),
        "Receive keys" => await ReceiveCommand.RunAsync(platform, "network", null),
        "Diagnose connectivity" => await DiagnoseCommand.RunAsync(platform, null, false, false),
        "Manage SSH config" => await ConfigCommand.RunAsync(platform, null),
        _ => 0
    };
});

return rootCommand.Parse(args).Invoke();
```

- [ ] **Step 7: Build and verify**

```bash
dotnet build
dotnet run --project src -- --help
dotnet run --project src -- setup --help
dotnet run --project src -- diagnose --help
dotnet run --project src -- share --help
dotnet test
```

Expected: Build succeeds. Help text shows all commands with options. Tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: wire up all CLI commands with Spectre.Console UI"
```

---

## Task 12: Interactive Wizard

**Files:**
- Create: `src/Commands/WizardCommand.cs`
- Modify: `src/Program.cs` (update root action)

- [ ] **Step 1: Implement the interactive wizard**

Create `src/Commands/WizardCommand.cs`:

```csharp
using Spectre.Console;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class WizardCommand
{
    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Easy Config[/]").RuleStyle("blue"));
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .HighlightStyle(new Style(Color.Blue))
                .AddChoices(
                    "Generate SSH keys (setup)",
                    "Share keys with another machine",
                    "Receive keys from another machine",
                    "Diagnose SSH connectivity",
                    "Manage SSH configuration",
                    "Exit"));

        return action switch
        {
            "Generate SSH keys (setup)" => await SetupCommand.RunAsync(platform),
            "Share keys with another machine" => await ShareWizardAsync(platform),
            "Receive keys from another machine" => await ReceiveWizardAsync(platform),
            "Diagnose SSH connectivity" => await DiagnoseWizardAsync(platform),
            "Manage SSH configuration" => await ConfigCommand.RunAsync(platform, null),
            _ => 0
        };
    }

    private static async Task<int> ShareWizardAsync(IPlatform platform)
    {
        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to share?")
                .AddChoices(
                    "Direct network connection (recommended)",
                    "Copy to clipboard",
                    "Save to file"));

        return mode switch
        {
            "Direct network connection (recommended)" =>
                await ShareCommand.RunAsync(platform, "network", null),
            "Copy to clipboard" =>
                await ShareCommand.RunAsync(platform, "clipboard", null),
            "Save to file" =>
            {
                var path = AnsiConsole.Prompt(
                    new TextPrompt<string>("Save to:")
                        .DefaultValue("key-bundle.sshec"));
                return await ShareCommand.RunAsync(platform, "file", path);
            },
            _ => 0
        };
    }

    private static async Task<int> ReceiveWizardAsync(IPlatform platform)
    {
        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How are you receiving keys?")
                .AddChoices(
                    "Direct network connection (recommended)",
                    "Paste from clipboard",
                    "Load from file"));

        return mode switch
        {
            "Direct network connection (recommended)" =>
                await ReceiveCommand.RunAsync(platform, "network", null),
            "Paste from clipboard" =>
                await ReceiveCommand.RunAsync(platform, "clipboard", null),
            "Load from file" =>
            {
                var path = AnsiConsole.Prompt(
                    new TextPrompt<string>("File path:")
                        .DefaultValue("key-bundle.sshec"));
                return await ReceiveCommand.RunAsync(platform, "file", path);
            },
            _ => 0
        };
    }

    private static async Task<int> DiagnoseWizardAsync(IPlatform platform)
    {
        var target = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to diagnose?")
                .AddChoices("Local SSH configuration", "Connection to a remote host"));

        if (target == "Local SSH configuration")
            return await DiagnoseCommand.RunAsync(platform, null, false, false);

        var host = AnsiConsole.Prompt(new TextPrompt<string>("Hostname or IP:"));
        return await DiagnoseCommand.RunAsync(platform, host, false, false);
    }
}
```

- [ ] **Step 2: Update Program.cs root action**

In `src/Program.cs`, replace the root command's `SetAction` block:

```csharp
// --- root (interactive wizard) ---
rootCommand.SetAction(async _ => await WizardCommand.RunAsync(platform));
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build
dotnet run --project src -- --help
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: interactive wizard for guided SSH configuration"
```

---

## Task 13: Packaging and Final Verification

**Files:**
- Modify: `src/SshEasyConfig.csproj` (verify packaging config)
- Modify: `.gitignore`

- [ ] **Step 1: Update .gitignore for build artifacts**

Append to `.gitignore`:

```
bin/
obj/
*.nupkg
.vs/
*.user
```

- [ ] **Step 2: Verify dotnet pack produces a valid package**

```bash
cd /c/Development/ssh-easy-config
dotnet pack src/SshEasyConfig.csproj -o ./artifacts
```

Expected: Package `ssh-easy-config.0.1.0.nupkg` created in `./artifacts/`.

- [ ] **Step 3: Test the tool via dotnet tool install from local path**

```bash
dotnet tool install --global --add-source ./artifacts ssh-easy-config
ssh-easy-config --help
ssh-easy-config diagnose --json
dotnet tool uninstall --global ssh-easy-config
```

Expected: Installs, shows help, runs diagnose, uninstalls cleanly.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test --verbosity normal
```

Expected: All tests pass.

- [ ] **Step 5: Clean up artifacts and commit**

```bash
rm -rf artifacts
git add -A
git commit -m "chore: update gitignore, verify packaging"
```

---

## Spec Coverage Verification

| Spec Requirement | Task |
|------------------|------|
| CLI subcommands (setup, share, receive, diagnose, config) | Task 1, 11 |
| Interactive wizard | Task 12 |
| Ed25519 key generation | Task 3 |
| authorized_keys management | Task 4 |
| Permission enforcement | Task 2 (platform layer) |
| Client ssh_config management | Task 5 |
| Server sshd_config audit/harden | Task 6 |
| Direct network key exchange | Task 8 |
| Pairing code security | Task 8 |
| mDNS discovery | Task 9 |
| Clipboard fallback | Task 7, 11 |
| File fallback | Task 7, 11 |
| Diagnostic layers 1-5 | Task 10 |
| Guided troubleshooting | Task 10, 11 |
| JSON output mode | Task 11 |
| Platform abstraction | Task 2 |
| WSL support | Task 2, 10 |
| NuGet packaging | Task 13 |
