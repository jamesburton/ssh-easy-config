using System.CommandLine;

var rootCommand = new RootCommand("ssh-easy-config - Cross-platform SSH key management, sharing, and diagnostics");

var setupCommand = new Command("setup", "Generate SSH keys and configure SSH");
var shareCommand = new Command("share", "Share keys with another machine");
var receiveCommand = new Command("receive", "Listen for incoming key share");
var diagnoseCommand = new Command("diagnose", "Diagnose SSH connectivity");
var configCommand = new Command("config", "Manage ssh_config / sshd_config");

var hostArgument = new Argument<string?>("host");
hostArgument.Description = "The host to diagnose";
hostArgument.Arity = ArgumentArity.ZeroOrOne;
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
