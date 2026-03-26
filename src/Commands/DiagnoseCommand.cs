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
            // Serialize without FixAction (it's a delegate and can't be serialized)
            var serializable = results.Select(r => new
            {
                r.CheckName,
                Status = r.Status.ToString(),
                r.Message,
                r.FixSuggestion,
                r.AutoFixAvailable
            }).ToList();

            var jsonOutput = JsonSerializer.Serialize(serializable, JsonOptions);
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

            // Inline fix-it prompts
            var fixable = results.Where(r => r.FixAction != null && r.AutoFixAvailable &&
                                              r.Status is CheckStatus.Fail or CheckStatus.Warn).ToList();

            if (fixable.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]{fixable.Count} issue(s) can be fixed automatically.[/]");
                AnsiConsole.WriteLine();

                foreach (var result in fixable)
                {
                    var prompt = $"Fix: {result.FixSuggestion ?? result.CheckName}?";
                    var confirm = AnsiConsole.Prompt(
                        new ConfirmationPrompt(Markup.Escape(prompt)) { DefaultValue = true });

                    if (confirm)
                    {
                        try
                        {
                            await result.FixAction!();
                            AnsiConsole.MarkupLine($"[green]Fixed:[/] {Markup.Escape(result.CheckName)}");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to fix {Markup.Escape(result.CheckName)}:[/] {Markup.Escape(ex.Message)}");
                        }
                    }
                }
            }
        }

        return results.Any(r => r.Status == CheckStatus.Fail) ? 1 : 0;
    }
}
