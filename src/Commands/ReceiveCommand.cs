using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ReceiveCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string mode, string? input)
    {
        const string keyName = "id_ed25519";

        if (!KeyManager.KeyExists(platform, keyName))
        {
            AnsiConsole.MarkupLine("[red]No SSH key found. Run 'setup' first.[/]");
            return 1;
        }

        var publicKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);
        var localBundle = new KeyBundle(
            publicKey.Trim(),
            Environment.MachineName,
            Environment.UserName,
            Environment.MachineName.ToLowerInvariant());

        KeyBundle? remoteBundle = null;

        switch (mode.ToLowerInvariant())
        {
            case "clipboard":
            {
                AnsiConsole.MarkupLine("[bold]Paste the key bundle below (ends with '--- END SSH-EASY-CONFIG ---'):[/]");
                var lines = new List<string>();
                while (true)
                {
                    var line = Console.ReadLine();
                    if (line is null)
                        break;
                    lines.Add(line);
                    if (line.Trim() == KeyBundle.ClipboardFooter)
                        break;
                }

                try
                {
                    remoteBundle = KeyBundle.FromClipboardText(string.Join('\n', lines));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to parse key bundle:[/] {Markup.Escape(ex.Message)}");
                    return 1;
                }

                break;
            }

            case "file":
            {
                var filePath = input ?? "key-bundle.sshec";
                if (!File.Exists(filePath))
                {
                    AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(filePath)}");
                    return 1;
                }

                try
                {
                    remoteBundle = KeyBundle.LoadFromFile(filePath);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to load key bundle:[/] {Markup.Escape(ex.Message)}");
                    return 1;
                }

                break;
            }

            default: // network
            {
                AnsiConsole.Write(new Rule("[bold blue]Network Key Receive[/]").LeftJustified());
                AnsiConsole.WriteLine();

                // Browse for mDNS services
                string host;
                int port;

                AnsiConsole.MarkupLine("[grey]Searching for nearby ssh-easy-config instances...[/]");
                var services = await Discovery.BrowseAsync(TimeSpan.FromSeconds(3));

                if (services.Count > 0)
                {
                    var choices = services.Select(s => $"{s.HostName}:{s.Port} ({s.InstanceName})").ToList();
                    choices.Add("Enter manually");

                    var selection = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Select a host:")
                            .AddChoices(choices));

                    if (selection == "Enter manually")
                    {
                        (host, port) = PromptForHostPort();
                    }
                    else
                    {
                        var idx = choices.IndexOf(selection);
                        host = services[idx].HostName;
                        port = services[idx].Port;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]No instances found via mDNS.[/]");
                    (host, port) = PromptForHostPort();
                }

                var pairingCode = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter pairing code:"));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                try
                {
                    var exchange = new NetworkExchange();
                    remoteBundle = await exchange.ConnectAndExchangeAsync(
                        host, port, localBundle, pairingCode, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    AnsiConsole.MarkupLine("[red]Connection timed out.[/]");
                    return 1;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Connection failed:[/] {Markup.Escape(ex.Message)}");
                    return 1;
                }

                break;
            }
        }

        if (remoteBundle is null)
        {
            AnsiConsole.MarkupLine("[red]Key exchange failed (pairing code mismatch or connection error).[/]");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Received key from:[/] {Markup.Escape(remoteBundle.Username)}@{Markup.Escape(remoteBundle.Hostname)}");
        AnsiConsole.MarkupLine($"[bold]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(remoteBundle.PublicKey)}");
        AnsiConsole.WriteLine();

        await ShareCommand.PromptToAddKeyAndAlias(platform, remoteBundle);
        return 0;
    }

    private static (string host, int port) PromptForHostPort()
    {
        var hostPort = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter host:port:"));

        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 12345;
        return (host, port);
    }
}
