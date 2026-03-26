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
            action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .AddChoices("audit", "harden", "hosts"))
                .ToLowerInvariant();
        }

        return action switch
        {
            "audit" => await RunAuditAsync(platform),
            "harden" => await RunHardenAsync(platform),
            "hosts" => await RunHostsAsync(platform),
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

    private static int InvalidAction(string action)
    {
        AnsiConsole.MarkupLine($"[red]Unknown config action:[/] {Markup.Escape(action)}. Use: audit, harden, hosts");
        return 1;
    }
}
