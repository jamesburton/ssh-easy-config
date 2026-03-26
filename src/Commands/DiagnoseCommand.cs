using System.Text.Json;
using Spectre.Console;
using SshEasyConfig.Diagnostics;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class DiagnoseCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(IPlatform platform, string? host, bool json, bool verbose)
    {
        var runner = new DiagnosticRunner(platform);
        var results = await runner.RunAllAsync(host);

        if (json)
        {
            var jsonOutput = JsonSerializer.Serialize(results, JsonOptions);
            AnsiConsole.WriteLine(jsonOutput);
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
        }

        return results.Any(r => r.Status == CheckStatus.Fail) ? 1 : 0;
    }
}
