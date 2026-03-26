using Spectre.Console;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class WizardCommand
{
    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Easy Config[/]").RuleStyle("blue"));
        AnsiConsole.WriteLine();

        var prompt = new SelectionPrompt<string>()
            .Title("What would you like to do?")
            .AddChoices(
                "Generate SSH keys (setup)",
                "Share keys with another machine",
                "Receive keys from another machine",
                "Diagnose SSH connectivity",
                "Manage SSH configuration",
                "Exit");

        var choice = AnsiConsole.Prompt(prompt);

        return choice switch
        {
            "Generate SSH keys (setup)" => await SetupCommand.RunAsync(platform),
            "Share keys with another machine" => await HandleShareAsync(platform),
            "Receive keys from another machine" => await HandleReceiveAsync(platform),
            "Diagnose SSH connectivity" => await HandleDiagnoseAsync(platform),
            "Manage SSH configuration" => await ConfigCommand.RunAsync(platform, null),
            "Exit" => 0,
            _ => 0
        };
    }

    private static async Task<int> HandleShareAsync(IPlatform platform)
    {
        var modePrompt = new SelectionPrompt<string>()
            .Title("How would you like to share your keys?")
            .AddChoices(
                "Direct network (recommended)",
                "Copy to clipboard",
                "Save to file");

        var modeChoice = AnsiConsole.Prompt(modePrompt);

        var (mode, output) = modeChoice switch
        {
            "Direct network (recommended)" => ("network", (string?)null),
            "Copy to clipboard" => ("clipboard", (string?)null),
            "Save to file" => ("file", PromptForPath("Enter output file path", "ssh-keys-export.json")),
            _ => ("network", (string?)null)
        };

        return await ShareCommand.RunAsync(platform, mode, output);
    }

    private static async Task<int> HandleReceiveAsync(IPlatform platform)
    {
        var modePrompt = new SelectionPrompt<string>()
            .Title("How would you like to receive keys?")
            .AddChoices(
                "Direct network (recommended)",
                "Paste from clipboard",
                "Load from file");

        var modeChoice = AnsiConsole.Prompt(modePrompt);

        var (mode, input) = modeChoice switch
        {
            "Direct network (recommended)" => ("network", (string?)null),
            "Paste from clipboard" => ("clipboard", (string?)null),
            "Load from file" => ("file", PromptForPath("Enter input file path", "ssh-keys-export.json")),
            _ => ("network", (string?)null)
        };

        return await ReceiveCommand.RunAsync(platform, mode, input);
    }

    private static async Task<int> HandleDiagnoseAsync(IPlatform platform)
    {
        var targetPrompt = new SelectionPrompt<string>()
            .Title("What would you like to diagnose?")
            .AddChoices(
                "Local configuration",
                "Remote host");

        var targetChoice = AnsiConsole.Prompt(targetPrompt);

        string? host = null;
        if (targetChoice == "Remote host")
        {
            host = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter the hostname or IP address:")
                    .PromptStyle(new Style(Color.Green)));
        }

        return await DiagnoseCommand.RunAsync(platform, host, false, false);
    }

    private static string PromptForPath(string message, string defaultValue)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>(message)
                .DefaultValue(defaultValue)
                .PromptStyle(new Style(Color.Green)));
    }
}
