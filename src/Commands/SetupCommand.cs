using System.Diagnostics;
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class SetupCommand
{
    /// <summary>
    /// Relaunches the current process as Administrator on Windows.
    /// Uses cmd /k to keep the window open so the user can see output.
    /// Returns true if relaunch was initiated (caller should exit).
    /// </summary>
    private static bool TryRelaunchElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            // Reconstruct the command line for the elevated process.
            // For .NET tools installed globally, the tool name is on PATH.
            // For local dev, use dotnet run. We use cmd /k to keep the window open.
            var toolCommand = "ssh-easy-config setup";

            // Check if we're running as a global tool (exe exists on PATH)
            var exePath = Environment.ProcessPath;
            if (exePath is not null && exePath.EndsWith("ssh-easy-config.exe", StringComparison.OrdinalIgnoreCase))
            {
                toolCommand = $"\"{exePath}\" setup";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {toolCommand}",
                UseShellExecute = true,
                Verb = "runas" // triggers UAC prompt
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            // User declined UAC or other failure
            return false;
        }
    }

    /// <summary>
    /// Prompts user to restart as Administrator. Returns true if relaunched.
    /// </summary>
    private static bool PromptAndRelaunchElevated()
    {
        var relaunch = AnsiConsole.Prompt(
            new ConfirmationPrompt("Restart as Administrator?") { DefaultValue = true });

        if (!relaunch)
        {
            AnsiConsole.MarkupLine("[yellow]Continuing without elevation. Some steps may fail.[/]");
            return false;
        }

        AnsiConsole.MarkupLine("[grey]Launching elevated process...[/]");
        if (TryRelaunchElevated())
        {
            AnsiConsole.MarkupLine("[green]Elevated process started. This window can be closed.[/]");
            return true;
        }

        AnsiConsole.MarkupLine("[red]Failed to relaunch as Administrator (UAC declined or error).[/]");
        return false;
    }

    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Easy Config - Setup Wizard[/]").LeftJustified());
        AnsiConsole.WriteLine();

        // Early elevation check on Windows — many steps require admin
        if (platform.Kind == PlatformKind.Windows && !platform.IsElevated)
        {
            AnsiConsole.MarkupLine("[yellow]Some setup steps require Administrator privileges (installing SSH server, configuring firewall, modifying sshd_config).[/]");
            if (PromptAndRelaunchElevated())
                return 0;
        }

        // ── Step 1: Detect state ──────────────────────────────────────────
        AnsiConsole.Write(new Rule("[bold]Step 1: Detecting system state[/]").LeftJustified());
        AnsiConsole.WriteLine();

        bool sshdInstalled = false;
        bool sshdRunning = false;
        bool sshdEnabled = false;
        bool firewallOpen = false;
        bool isMsAccount = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Scanning system...", async _ =>
            {
                sshdInstalled = await SshServerInstaller.IsSshdInstalledAsync(platform);
                sshdRunning = await platform.IsSshServiceRunningAsync();
                sshdEnabled = sshdInstalled && await SshServerInstaller.IsSshdEnabledAsync(platform);
                firewallOpen = await FirewallManager.IsPort22OpenAsync(platform);
                if (OperatingSystem.IsWindows())
                    isMsAccount = WindowsAccountHelper.IsMicrosoftLinkedAccount();
            });

        var stateTable = new Table();
        stateTable.AddColumn("Property");
        stateTable.AddColumn("Value");
        stateTable.AddRow("Platform", Markup.Escape(platform.Kind.ToString()));
        stateTable.AddRow("Elevated / Root", platform.IsElevated ? "[green]Yes[/]" : "[yellow]No[/]");
        stateTable.AddRow("sshd Installed", sshdInstalled ? "[green]Yes[/]" : "[red]No[/]");
        stateTable.AddRow("sshd Running", sshdRunning ? "[green]Yes[/]" : "[red]No[/]");
        stateTable.AddRow("sshd Enabled on Boot", sshdEnabled ? "[green]Yes[/]" : "[yellow]No[/]");
        stateTable.AddRow("Firewall Port 22", firewallOpen ? "[green]Open[/]" : "[red]Blocked[/]");
        stateTable.AddRow("Package Manager", Markup.Escape(platform.PackageManager.ToString()));
        stateTable.AddRow("Firewall Type", Markup.Escape(platform.FirewallType.ToString()));
        if (OperatingSystem.IsWindows())
            stateTable.AddRow("MS-linked Account", isMsAccount ? "[yellow]Yes[/]" : "No");

        AnsiConsole.Write(stateTable);
        AnsiConsole.WriteLine();

        // ── Step 2: Install sshd if missing ───────────────────────────────
        if (!sshdInstalled)
        {
            AnsiConsole.Write(new Rule("[bold]Step 2: Install SSH Server[/]").LeftJustified());
            AnsiConsole.WriteLine();

            if (platform.Kind == PlatformKind.Windows && !platform.IsElevated)
            {
                AnsiConsole.MarkupLine("[red]Administrator privileges are required to install OpenSSH Server on Windows.[/]");
                if (PromptAndRelaunchElevated())
                    return 0; // Relaunched — exit this instance
                return 1;
            }

            var installCmd = SshServerInstaller.GetInstallCommand(platform.Kind, platform.PackageManager);
            AnsiConsole.MarkupLine($"[dim]Command: {Markup.Escape(installCmd.Command)} {Markup.Escape(installCmd.Arguments)}[/]");

            var installConfirm = AnsiConsole.Prompt(
                new ConfirmationPrompt("Install SSH server now?") { DefaultValue = true });

            if (installConfirm)
            {
                try
                {
                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("Installing SSH server...", async _ =>
                        {
                            await SshServerInstaller.InstallAsync(platform);
                        });

                    sshdInstalled = true;
                    AnsiConsole.MarkupLine("[green]SSH server installed successfully.[/]");
                }
                catch (Exception ex) when (NeedsElevation(ex))
                {
                    AnsiConsole.MarkupLine("[red]Installation failed — elevation required.[/]");
                    if (PromptAndRelaunchElevated())
                        return 0;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Skipping SSH server installation.[/]");
            }

            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Step 2: sshd already installed — skipping.[/]");
        }

        // ── Step 3: Start and enable sshd ─────────────────────────────────
        if (sshdInstalled)
        {
            // Refresh running status after potential install
            sshdRunning = await platform.IsSshServiceRunningAsync();

            if (!sshdRunning)
            {
                AnsiConsole.Write(new Rule("[bold]Step 3: Start SSH Service[/]").LeftJustified());
                AnsiConsole.WriteLine();

                var startConfirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("SSH service is not running. Start it now?") { DefaultValue = true });

                if (startConfirm)
                {
                    try
                    {
                        await AnsiConsole.Status()
                            .Spinner(Spinner.Known.Dots)
                            .StartAsync("Starting SSH service...", async _ =>
                            {
                                await SshServerInstaller.StartAsync(platform);
                            });

                        sshdRunning = true;
                        AnsiConsole.MarkupLine("[green]SSH service started.[/]");
                    }
                    catch (Exception ex) when (NeedsElevation(ex))
                    {
                        AnsiConsole.MarkupLine("[red]Starting service failed — elevation required.[/]");
                        if (PromptAndRelaunchElevated())
                            return 0;
                    }
                }

                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Step 3: sshd already running — skipping.[/]");
            }

            // Enable on boot
            sshdEnabled = await SshServerInstaller.IsSshdEnabledAsync(platform);
            if (!sshdEnabled)
            {
                var enableConfirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("SSH service is not enabled on boot. Enable it?") { DefaultValue = true });

                if (enableConfirm)
                {
                    try
                    {
                        await AnsiConsole.Status()
                            .Spinner(Spinner.Known.Dots)
                            .StartAsync("Enabling SSH service on boot...", async _ =>
                            {
                                await SshServerInstaller.EnableAsync(platform);
                            });

                        AnsiConsole.MarkupLine("[green]SSH service enabled on boot.[/]");
                    }
                    catch (Exception ex) when (NeedsElevation(ex))
                    {
                        AnsiConsole.MarkupLine("[red]Enabling service failed — elevation required.[/]");
                        if (PromptAndRelaunchElevated())
                            return 0;
                    }
                }

                AnsiConsole.WriteLine();
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Step 3: sshd not installed — skipping start/enable.[/]");
        }

        // ── Step 4: Open firewall ─────────────────────────────────────────
        if (!firewallOpen && platform.FirewallType != FirewallType.None)
        {
            AnsiConsole.Write(new Rule("[bold]Step 4: Firewall Configuration[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var fwConfirm = AnsiConsole.Prompt(
                new ConfirmationPrompt("Port 22 appears blocked. Open it in the firewall?") { DefaultValue = true });

            if (fwConfirm)
            {
                try
                {
                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("Opening port 22...", async _ =>
                        {
                            await FirewallManager.OpenPort22Async(platform);
                        });

                    firewallOpen = true;
                    AnsiConsole.MarkupLine("[green]Firewall rule added for port 22.[/]");
                }
                catch (Exception ex) when (NeedsElevation(ex))
                {
                    AnsiConsole.MarkupLine("[red]Firewall configuration failed — elevation required.[/]");
                    if (PromptAndRelaunchElevated())
                        return 0;
                }
            }

            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Step 4: Firewall already open or no firewall — skipping.[/]");
        }

        // ── Step 5: Windows MS account handling ───────────────────────────
        if (OperatingSystem.IsWindows() && platform.Kind == PlatformKind.Windows && isMsAccount)
        {
            AnsiConsole.Write(new Rule("[bold]Step 5: Windows Microsoft Account Configuration[/]").LeftJustified());
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[yellow]Microsoft-linked account detected.[/]");
            AnsiConsole.MarkupLine("Windows OpenSSH requires a Match block in sshd_config for admin accounts.");

            bool hasMatchBlock = false;
            if (File.Exists(platform.SshdConfigPath))
            {
                var sshdContent = await File.ReadAllTextAsync(platform.SshdConfigPath);
                hasMatchBlock = WindowsAccountHelper.SshdConfigHasMatchBlock(sshdContent);
            }

            if (!hasMatchBlock)
            {
                AnsiConsole.MarkupLine("[yellow]Match block for administrators is missing from sshd_config.[/]");

                var matchConfirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("Add Match block and restart sshd?") { DefaultValue = true });

                if (matchConfirm)
                {
                    await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("Updating sshd_config...", async _ =>
                        {
                            await WindowsAccountHelper.EnsureMatchBlockAsync(platform);
                            await platform.RestartSshServiceAsync();
                        });

                    AnsiConsole.MarkupLine("[green]Match block added and sshd restarted.[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Match block already present in sshd_config.[/]");
            }

            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Step 5: Windows MS account config — not applicable.[/]");
        }

        // ── Step 6: Generate keys ─────────────────────────────────────────
        AnsiConsole.Write(new Rule("[bold]Step 6: SSH Key Generation[/]").LeftJustified());
        AnsiConsole.WriteLine();

        const string keyName = "id_ed25519";
        SshKeyPair? keyPair = null;
        string? publicKey = null;

        if (KeyManager.KeyExists(platform, keyName))
        {
            publicKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);
            var truncated = publicKey.Length > 60
                ? publicKey[..30] + "..." + publicKey[^20..]
                : publicKey;

            AnsiConsole.MarkupLine($"[green]Existing key found:[/] {Markup.Escape(truncated.Trim())}");
            AnsiConsole.WriteLine();

            var useExisting = AnsiConsole.Prompt(
                new ConfirmationPrompt("Use existing key?") { DefaultValue = true });

            if (useExisting)
            {
                AnsiConsole.MarkupLine("[green]Using existing key.[/]");
            }
            else
            {
                keyPair = await GenerateNewKeyAsync(platform, keyName);
                publicKey = keyPair.PublicKeyOpenSsh;
            }
        }
        else
        {
            keyPair = await GenerateNewKeyAsync(platform, keyName);
            publicKey = keyPair.PublicKeyOpenSsh;
        }

        // Windows admin + MS account: also set up admin authorized_keys
        if (OperatingSystem.IsWindows() && platform.IsElevated && isMsAccount && publicKey != null)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Setting up admin authorized_keys...", async _ =>
                {
                    await WindowsAccountHelper.SetupAdminAuthorizedKeysAsync(platform, publicKey.Trim());
                });

            AnsiConsole.MarkupLine("[green]Admin authorized_keys configured.[/]");
        }

        AnsiConsole.WriteLine();

        // ── Step 7: Fix permissions ───────────────────────────────────────
        AnsiConsole.Write(new Rule("[bold]Step 7: File Permissions[/]").LeftJustified());
        AnsiConsole.WriteLine();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking and fixing permissions...", async _ =>
            {
                var sshDir = platform.SshDirectoryPath;

                if (Directory.Exists(sshDir))
                {
                    var dirOk = await platform.CheckFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
                    if (!dirOk)
                        await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);

                    var privateKeyPath = Path.Combine(sshDir, keyName);
                    if (File.Exists(privateKeyPath))
                    {
                        var keyOk = await platform.CheckFilePermissionsAsync(privateKeyPath, SshFileKind.PrivateKey);
                        if (!keyOk)
                            await platform.SetFilePermissionsAsync(privateKeyPath, SshFileKind.PrivateKey);
                    }

                    var authKeysPath = Path.Combine(sshDir, platform.AuthorizedKeysFilename);
                    if (File.Exists(authKeysPath))
                    {
                        var akOk = await platform.CheckFilePermissionsAsync(authKeysPath, SshFileKind.AuthorizedKeys);
                        if (!akOk)
                            await platform.SetFilePermissionsAsync(authKeysPath, SshFileKind.AuthorizedKeys);
                    }
                }
            });

        AnsiConsole.MarkupLine("[green]Permissions verified and fixed.[/]");
        AnsiConsole.WriteLine();

        // ── Step 8: Validate ──────────────────────────────────────────────
        AnsiConsole.Write(new Rule("[bold]Step 8: Validation[/]").LeftJustified());
        AnsiConsole.WriteLine();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Testing SSH connection to localhost...", async _ =>
            {
                var result = await platform.TryRunCommandAsync("ssh",
                    "-o BatchMode=yes -o StrictHostKeyChecking=no -o ConnectTimeout=5 localhost exit 0");

                if (result.ExitCode == 0)
                {
                    AnsiConsole.MarkupLine("[green]SSH connection to localhost succeeded![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]SSH connection to localhost failed.[/]");
                    AnsiConsole.MarkupLine("[dim]This may be expected if authorized_keys is not yet configured.[/]");
                    AnsiConsole.MarkupLine("Run [bold]ssh-easy-config diagnose[/] for detailed troubleshooting.");
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold green]Setup Complete[/]").LeftJustified());
        return 0;
    }

    /// <summary>
    /// Heuristic to detect if an exception was caused by lack of elevation.
    /// </summary>
    private static bool NeedsElevation(Exception ex)
    {
        var msg = ex.Message + (ex.InnerException?.Message ?? "");
        return msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("elevated", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("administrator", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("requires elevation", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SshKeyPair> GenerateNewKeyAsync(IPlatform platform, string keyName)
    {
        var defaultComment = $"{Environment.UserName}@{Environment.MachineName}";
        var comment = AnsiConsole.Prompt(
            new TextPrompt<string>("Key comment:")
                .DefaultValue(defaultComment)
                .AllowEmpty());

        SshKeyPair? keyPair = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Ed25519 key pair...", async _ =>
            {
                keyPair = KeyManager.GenerateKeyPair(comment);
                await KeyManager.SaveKeyPairAsync(platform, keyPair, keyName);
            });

        AnsiConsole.MarkupLine("[green]Key pair generated and saved.[/]");
        AnsiConsole.MarkupLine($"[bold]Public key:[/] {Markup.Escape(keyPair!.PublicKeyOpenSsh.Trim())}");
        AnsiConsole.MarkupLine($"[bold]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(keyPair.PublicKeyOpenSsh.Trim())}");

        return keyPair;
    }
}
