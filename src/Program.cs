using System.CommandLine;
using Spectre.Console;
using SshEasyConfig.Commands;
using SshEasyConfig.Platform;

var platform = PlatformDetector.Detect();
var rootCommand = new RootCommand("ssh-easy-config - Cross-platform SSH key management, sharing, and diagnostics");

// setup — options: --verbose
var setupCommand = new Command("setup", "Generate SSH keys and configure SSH");
var setupVerboseOption = new Option<bool>("--verbose") { Description = "Show diagnostic details during setup" };
setupCommand.Options.Add(setupVerboseOption);
setupCommand.SetAction(async (pr, _) => await SetupCommand.RunAsync(platform, pr.GetValue(setupVerboseOption)));
rootCommand.Subcommands.Add(setupCommand);

// share — options: --mode (network|clipboard|file), --output, --host
var shareCommand = new Command("share", "Share keys with another machine");
var shareModeOption = new Option<string>("--mode") { Description = "Transfer mode: network, clipboard, file" };
shareModeOption.DefaultValueFactory = _ => "network";
var shareOutputOption = new Option<string?>("--output") { Description = "Output file path (for file mode)" };
var shareHostOption = new Option<string?>("--host") { Description = "Hostname/IP to advertise (e.g. mypc.tail12345.ts.net)" };
shareCommand.Options.Add(shareModeOption);
shareCommand.Options.Add(shareOutputOption);
shareCommand.Options.Add(shareHostOption);
shareCommand.SetAction(async (pr, _) =>
    await ShareCommand.RunAsync(platform, pr.GetValue(shareModeOption)!, pr.GetValue(shareOutputOption), pr.GetValue(shareHostOption)));
rootCommand.Subcommands.Add(shareCommand);

// receive — options: --mode, --input, --host
var receiveCommand = new Command("receive", "Listen for incoming key share");
var receiveModeOption = new Option<string>("--mode") { Description = "Transfer mode: network, clipboard, file" };
receiveModeOption.DefaultValueFactory = _ => "network";
var receiveInputOption = new Option<string?>("--input") { Description = "Input file path (for file mode)" };
var receiveHostOption = new Option<string?>("--host") { Description = "Hostname/IP to connect to (e.g. mypc.tail12345.ts.net)" };
receiveCommand.Options.Add(receiveModeOption);
receiveCommand.Options.Add(receiveInputOption);
receiveCommand.Options.Add(receiveHostOption);
receiveCommand.SetAction(async (pr, _) =>
    await ReceiveCommand.RunAsync(platform, pr.GetValue(receiveModeOption)!, pr.GetValue(receiveInputOption), pr.GetValue(receiveHostOption)));
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

rootCommand.SetAction(async _ => await WizardCommand.RunAsync(platform));

return await rootCommand.Parse(args).InvokeAsync();
