using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ConfigCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string? subaction)
    {
        var action = subaction?.ToLowerInvariant();

        if (action is null)
        {
            var choices = new List<string> { "audit", "harden", "hosts", "fix" };
            action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .AddChoices(choices))
                .ToLowerInvariant();
        }

        return action switch
        {
            "audit" => await RunAuditAsync(platform),
            "harden" => await RunHardenAsync(platform),
            "hosts" => await RunHostsAsync(platform),
            "fix" => await RunFixAsync(platform),
            _ => InvalidAction(action)
        };
    }

    private static async Task<int> RunAuditAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]sshd_config Audit[/]").LeftJustified());
        AnsiConsole.WriteLine();

        if (!File.Exists(platform.SshdConfigPath))
        {
            AnsiConsole.MarkupLine("[yellow]sshd_config not found. Skipping audit.[/]");
            return 0;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(platform.SshdConfigPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to read sshd_config:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var findings = SshdConfigManager.Audit(content);
        var table = new Table();
        table.AddColumn("Status");
        table.AddColumn("Directive");
        table.AddColumn("Current");
        table.AddColumn("Recommended");
        table.AddColumn("Message");

        foreach (var finding in findings)
        {
            var statusIcon = finding.Severity switch
            {
                AuditSeverity.Ok => "[green]OK[/]",
                AuditSeverity.Warning => "[yellow]WARN[/]",
                AuditSeverity.Info => "[blue]INFO[/]",
                _ => "[grey]?[/]"
            };

            table.AddRow(
                statusIcon,
                Markup.Escape(finding.Key),
                Markup.Escape(finding.CurrentValue.Length > 0 ? finding.CurrentValue : "(not set)"),
                Markup.Escape(finding.RecommendedValue),
                Markup.Escape(finding.Message));
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<int> RunHardenAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]sshd_config Hardening[/]").LeftJustified());
        AnsiConsole.WriteLine();

        if (!File.Exists(platform.SshdConfigPath))
        {
            AnsiConsole.MarkupLine("[yellow]sshd_config not found. Cannot harden.[/]");
            return 1;
        }

        string content;
        try
        {
            content = await SshdConfigManager.BackupAndReadAsync(platform);
            AnsiConsole.MarkupLine("[green]Backup created.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to backup sshd_config:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var findings = SshdConfigManager.Audit(content);
        var fixableFindings = findings.Where(f => f.Severity == AuditSeverity.Warning || f.Severity == AuditSeverity.Info).ToList();

        if (fixableFindings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All checks passed. Nothing to harden.[/]");
            return 0;
        }

        var modified = false;
        foreach (var finding in fixableFindings)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(finding.Key)}:[/] {Markup.Escape(finding.Message)}");
            var apply = AnsiConsole.Prompt(
                new ConfirmationPrompt($"Set {finding.Key} to '{finding.RecommendedValue}'?")
                    { DefaultValue = true });

            if (apply)
            {
                content = SshdConfigManager.SetDirective(content, finding.Key, finding.RecommendedValue);
                modified = true;
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(finding.Key)} set to '{Markup.Escape(finding.RecommendedValue)}'.[/]");
            }
        }

        if (modified)
        {
            await SshdConfigManager.WriteAsync(platform, content);
            AnsiConsole.MarkupLine("[green]sshd_config updated.[/]");

            var restart = AnsiConsole.Prompt(
                new ConfirmationPrompt("Restart SSH service to apply changes?") { DefaultValue = false });

            if (restart)
            {
                try
                {
                    await platform.RestartSshServiceAsync();
                    AnsiConsole.MarkupLine("[green]SSH service restarted.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to restart SSH service:[/] {Markup.Escape(ex.Message)}");
                }
            }
        }

        return 0;
    }

    private static async Task<int> RunHostsAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Config Hosts[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var content = await SshConfigManager.ReadConfigAsync(platform);

        if (string.IsNullOrWhiteSpace(content))
        {
            AnsiConsole.MarkupLine("[yellow]No SSH config found or file is empty.[/]");
            return 0;
        }

        var hosts = SshConfigManager.ParseHosts(content);

        if (hosts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No host entries found in SSH config.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("Alias");
        table.AddColumn("HostName");
        table.AddColumn("User");
        table.AddColumn("Port");
        table.AddColumn("IdentityFile");
        table.AddColumn("ForwardAgent");

        foreach (var host in hosts)
        {
            table.AddRow(
                Markup.Escape(host.Alias),
                Markup.Escape(host.HostName),
                Markup.Escape(host.User ?? ""),
                host.Port.ToString(),
                Markup.Escape(host.IdentityFile ?? ""),
                host.ForwardAgent ? "yes" : "no");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    internal static async Task<int> RunFixAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Configuration Fix[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var fixes = 0;

        // Windows MS-linked account: migrate keys to administrators_authorized_keys
        if (OperatingSystem.IsWindows() && platform.Kind == PlatformKind.Windows)
        {
            var isMsAccount = WindowsAccountHelper.IsMicrosoftLinkedAccount();
            var isAdmin = platform.IsElevated;

            if (isMsAccount)
            {
                AnsiConsole.MarkupLine("[yellow]Microsoft-linked account detected.[/]");

                if (!isAdmin)
                {
                    AnsiConsole.MarkupLine("[red]Administrator privileges required for Windows SSH fixes.[/]");
                    AnsiConsole.MarkupLine("[grey]Re-run: ssh-easy-config config fix (as Administrator)[/]");
                    return 1;
                }

                // Check and migrate keys from authorized_keys to administrators_authorized_keys
                var userKeysPath = Path.Combine(platform.SshDirectoryPath, "authorized_keys");
                var adminKeysPath = WindowsAccountHelper.GetAdminAuthorizedKeysPath();

                if (File.Exists(userKeysPath))
                {
                    var userKeys = await File.ReadAllTextAsync(userKeysPath);
                    var userKeyLines = userKeys.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                        .ToList();

                    if (userKeyLines.Count > 0)
                    {
                        // Read existing admin keys
                        string adminContent;
                        if (File.Exists(adminKeysPath))
                        {
                            // Grant access first in case ACLs are locked down
                            try { await platform.RunCommandAsync("icacls", $"\"{adminKeysPath}\" /grant \"BUILTIN\\Administrators:(F)\""); }
                            catch { }
                            adminContent = await File.ReadAllTextAsync(adminKeysPath);
                        }
                        else
                        {
                            adminContent = "";
                        }

                        var keysAdded = 0;
                        foreach (var key in userKeyLines)
                        {
                            var before = adminContent;
                            adminContent = AuthorizedKeysManager.AddKey(adminContent, key.Trim());
                            if (adminContent != before) keysAdded++;
                        }

                        if (keysAdded > 0)
                        {
                            await WindowsAccountHelper.SetupAdminAuthorizedKeysAsync(platform, ""); // ensures file + ACLs
                            // Rewrite with all merged keys
                            await platform.RunCommandAsync("icacls", $"\"{adminKeysPath}\" /grant \"BUILTIN\\Administrators:(F)\"");
                            await File.WriteAllTextAsync(adminKeysPath, adminContent);
                            await platform.RunCommandAsync("icacls",
                                $"\"{adminKeysPath}\" /inheritance:r /grant \"SYSTEM:(F)\" /grant \"BUILTIN\\Administrators:(F)\"");

                            AnsiConsole.MarkupLine($"[green]Migrated {keysAdded} key(s) to administrators_authorized_keys.[/]");
                            fixes++;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[green]All keys already present in administrators_authorized_keys.[/]");
                        }
                    }
                }

                // Ensure Match block in sshd_config
                if (File.Exists(platform.SshdConfigPath))
                {
                    var sshdContent = await File.ReadAllTextAsync(platform.SshdConfigPath);
                    if (!WindowsAccountHelper.SshdConfigHasMatchBlock(sshdContent))
                    {
                        await WindowsAccountHelper.EnsureMatchBlockAsync(platform);
                        AnsiConsole.MarkupLine("[green]Added Match Group administrators block to sshd_config.[/]");
                        fixes++;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[green]Match block already present in sshd_config.[/]");
                    }
                }

                // Ensure PubkeyAuthentication is enabled
                if (File.Exists(platform.SshdConfigPath))
                {
                    var sshdContent = await File.ReadAllTextAsync(platform.SshdConfigPath);
                    if (!sshdContent.Contains("PubkeyAuthentication yes", StringComparison.OrdinalIgnoreCase))
                    {
                        sshdContent = SshdConfigManager.SetDirective(sshdContent, "PubkeyAuthentication", "yes");
                        await SshdConfigManager.WriteAsync(platform, sshdContent);
                        AnsiConsole.MarkupLine("[green]Enabled PubkeyAuthentication in sshd_config.[/]");
                        fixes++;
                    }
                }

                if (fixes > 0)
                {
                    AnsiConsole.MarkupLine("[grey]Restarting SSH service...[/]");
                    try
                    {
                        await platform.RestartSshServiceAsync();
                        AnsiConsole.MarkupLine("[green]SSH service restarted.[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Could not restart SSH service: {Markup.Escape(ex.Message)}[/]");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Local account (not MS-linked) — no Windows-specific fixes needed.[/]");
            }
        }

        // Cross-platform: check and fix file permissions
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Checking file permissions...[/]");

        var sshDir = platform.SshDirectoryPath;
        if (Directory.Exists(sshDir))
        {
            var dirOk = await platform.CheckFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
            if (!dirOk)
            {
                await platform.SetFilePermissionsAsync(sshDir, SshFileKind.SshDirectory);
                AnsiConsole.MarkupLine("[green]Fixed .ssh directory permissions.[/]");
                fixes++;
            }

            foreach (var keyFile in new[] { "id_ed25519", "id_rsa", "id_ecdsa" })
            {
                var keyPath = Path.Combine(sshDir, keyFile);
                if (!File.Exists(keyPath)) continue;
                var keyOk = await platform.CheckFilePermissionsAsync(keyPath, SshFileKind.PrivateKey);
                if (!keyOk)
                {
                    await platform.SetFilePermissionsAsync(keyPath, SshFileKind.PrivateKey);
                    AnsiConsole.MarkupLine($"[green]Fixed {keyFile} permissions.[/]");
                    fixes++;
                }
            }
        }

        AnsiConsole.WriteLine();
        if (fixes > 0)
            AnsiConsole.MarkupLine($"[green]Applied {fixes} fix(es).[/]");
        else
            AnsiConsole.MarkupLine("[green]No fixes needed — everything looks good.[/]");

        return 0;
    }

    private static int InvalidAction(string action)
    {
        AnsiConsole.MarkupLine($"[red]Unknown config action:[/] {Markup.Escape(action)}. Use: audit, harden, hosts");
        return 1;
    }
}
