using System.CommandLine;
using Spectre.Console;
using SshEasyConfig.Commands;
using SshEasyConfig.Platform;

var platform = PlatformDetector.Detect();
var rootCommand = new RootCommand("ssh-easy-config - Cross-platform SSH key management, sharing, and diagnostics");

// setup
var setupCommand = new Command("setup", "Generate SSH keys and configure SSH");
setupCommand.SetAction(async (_, _) => await SetupCommand.RunAsync(platform));
rootCommand.Subcommands.Add(setupCommand);

// share — options: --mode (network|clipboard|file), --output
var shareCommand = new Command("share", "Share keys with another machine");
var shareModeOption = new Option<string>("--mode") { Description = "Transfer mode: network, clipboard, file" };
shareModeOption.DefaultValueFactory = _ => "network";
var shareOutputOption = new Option<string?>("--output") { Description = "Output file path (for file mode)" };
shareCommand.Options.Add(shareModeOption);
shareCommand.Options.Add(shareOutputOption);
shareCommand.SetAction(async (pr, _) =>
    await ShareCommand.RunAsync(platform, pr.GetValue(shareModeOption)!, pr.GetValue(shareOutputOption)));
rootCommand.Subcommands.Add(shareCommand);

// receive — options: --mode, --input
var receiveCommand = new Command("receive", "Listen for incoming key share");
var receiveModeOption = new Option<string>("--mode") { Description = "Transfer mode: network, clipboard, file" };
receiveModeOption.DefaultValueFactory = _ => "network";
var receiveInputOption = new Option<string?>("--input") { Description = "Input file path (for file mode)" };
receiveCommand.Options.Add(receiveModeOption);
receiveCommand.Options.Add(receiveInputOption);
receiveCommand.SetAction(async (pr, _) =>
    await ReceiveCommand.RunAsync(platform, pr.GetValue(receiveModeOption)!, pr.GetValue(receiveInputOption)));
rootCommand.Subcommands.Add(receiveCommand);

// diagnose — argument: host (optional), options: --json, --verbose
var diagnoseCommand = new Command("diagnose", "Diagnose SSH connectivity");
var hostArgument = new Argument<string?>("host") { Description = "The host to diagnose", Arity = ArgumentArity.ZeroOrOne };
var jsonOption = new Option<bool>("--json") { Description = "Output results as JSON" };
var verboseOption = new Option<bool>("--verbose") { Description = "Show all checks including skipped" };
diagnoseCommand.Arguments.Add(hostArgument);
diagnoseCommand.Options.Add(jsonOption);
diagnoseCommand.Options.Add(verboseOption);
diagnoseCommand.SetAction(async (pr, _) =>
    await DiagnoseCommand.RunAsync(platform, pr.GetValue(hostArgument), pr.GetValue(jsonOption), pr.GetValue(verboseOption)));
rootCommand.Subcommands.Add(diagnoseCommand);

// config — argument: action (optional: audit|harden|hosts)
var configCommand = new Command("config", "Manage ssh_config / sshd_config");
var configActionArg = new Argument<string?>("action") { Description = "Action: audit, harden, hosts", Arity = ArgumentArity.ZeroOrOne };
configCommand.Arguments.Add(configActionArg);
configCommand.SetAction(async (pr, _) =>
    await ConfigCommand.RunAsync(platform, pr.GetValue(configActionArg)));
rootCommand.Subcommands.Add(configCommand);

// root — interactive wizard (placeholder for Task 12)
rootCommand.SetAction(async (_, _) =>
{
    AnsiConsole.MarkupLine("[bold blue]SSH Easy Config[/]\n");
    var prompt = new SelectionPrompt<string>();
    prompt.Title = "What would you like to do?";
    prompt.AddChoices("Setup SSH keys", "Share keys", "Receive keys", "Diagnose connectivity", "Manage SSH config");
    var action = AnsiConsole.Prompt(prompt);
    return action switch
    {
        "Setup SSH keys" => await SetupCommand.RunAsync(platform),
        "Share keys" => await ShareCommand.RunAsync(platform, "network", null),
        "Receive keys" => await ReceiveCommand.RunAsync(platform, "network", null),
        "Diagnose connectivity" => await DiagnoseCommand.RunAsync(platform, null, false, false),
        "Manage SSH config" => await ConfigCommand.RunAsync(platform, null),
        _ => 0
    };
});

return await rootCommand.Parse(args).InvokeAsync();
