using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ReceiveCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string mode, string? input, string? host = null, string? code = null)
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

                string connectHost;
                int port;

                if (host is not null)
                {
                    // --host was provided, use it directly
                    connectHost = host;
                    port = 0; // will need to get from mDNS or prompt
                }
                else
                {
                    connectHost = null!;
                    port = 0;
                }

                // If no --host or we need a port, try mDNS
                if (host is null || port == 0)
                {
                    AnsiConsole.MarkupLine("[grey]Searching for nearby ssh-easy-config instances...[/]");
                    var services = await Discovery.BrowseAsync(TimeSpan.FromSeconds(3));

                    if (services.Count > 0)
                    {
                        // Deduplicate by instance name + port, merge addresses
                        var merged = services
                            .GroupBy(s => $"{s.InstanceName}:{s.Port}")
                            .Select(g =>
                            {
                                var first = g.First();
                                var allAddrs = g.SelectMany(s => s.Addresses).Distinct().ToList();
                                return first with { Addresses = allAddrs };
                            })
                            .ToList();

                        // Show clean selection: just hostname:port (instance name)
                        var choices = merged.Select(s => $"{s.HostName}:{s.Port} ({s.InstanceName})").ToList();
                        choices.Add("Enter manually");

                        var selection = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("Select a host:")
                                .AddChoices(choices));

                        if (selection == "Enter manually")
                        {
                            (connectHost, port) = PromptForHostPort();
                        }
                        else
                        {
                            var idx = choices.IndexOf(selection);
                            var service = merged[idx];
                            port = service.Port;

                            if (host is not null)
                            {
                                connectHost = host;
                            }
                            else if (service.Addresses.Count > 1)
                            {
                                // Multiple IPs — let user pick (filter out virtual adapter ranges)
                                var addrChoices = service.Addresses
                                    .OrderBy(a => IsLikelyVirtualAddress(a) ? 1 : 0)
                                    .ToList();
                                addrChoices.Add(service.HostName + " (hostname)");

                                connectHost = AnsiConsole.Prompt(
                                    new SelectionPrompt<string>()
                                        .Title("Multiple addresses found. Which to connect to?")
                                        .AddChoices(addrChoices));

                                if (connectHost.EndsWith(" (hostname)"))
                                    connectHost = service.HostName;
                            }
                            else if (service.Addresses.Count == 1)
                            {
                                connectHost = service.Addresses[0];
                            }
                            else
                            {
                                connectHost = service.HostName;
                            }
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]No instances found via mDNS.[/]");
                        if (host is not null)
                        {
                            connectHost = host;
                            port = AnsiConsole.Prompt(
                                new TextPrompt<int>("Enter port:").DefaultValue(12345));
                        }
                        else
                        {
                            (connectHost, port) = PromptForHostPort();
                        }
                    }
                }

                var pairingCode = code ?? AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter pairing code:"));

                AnsiConsole.MarkupLine($"[grey]Connecting to {Markup.Escape(connectHost)}:{port}...[/]");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                try
                {
                    var exchange = new NetworkExchange();
                    remoteBundle = await exchange.ConnectAndExchangeAsync(
                        connectHost, port, localBundle, pairingCode, cts.Token);
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

    /// <summary>
    /// Heuristic: addresses in these ranges are likely virtual adapters (WSL, Hyper-V, Docker).
    /// </summary>
    private static bool IsLikelyVirtualAddress(string addr)
    {
        return addr.StartsWith("172.") && int.TryParse(addr.Split('.')[1], out var second) && second >= 16 && second <= 31
            || addr.StartsWith("169.254.")
            || addr.StartsWith("fe80:")
            || addr.StartsWith("fd");
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
