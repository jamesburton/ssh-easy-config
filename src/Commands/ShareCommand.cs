using System.Net;
using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class ShareCommand
{
    public static async Task<int> RunAsync(IPlatform platform, string mode, string? output, string? host = null)
    {
        const string keyName = "id_ed25519";

        if (!KeyManager.KeyExists(platform, keyName))
        {
            AnsiConsole.MarkupLine("[red]No SSH key found. Run 'setup' first.[/]");
            return 1;
        }

        var publicKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);

        // Determine the hostname to advertise
        var advertiseHost = host ?? ResolveAdvertiseHost();

        var bundle = new KeyBundle(
            publicKey.Trim(),
            advertiseHost,
            Environment.UserName,
            advertiseHost.ToLowerInvariant().Split('.')[0]);

        switch (mode.ToLowerInvariant())
        {
            case "clipboard":
            {
                var text = bundle.ToClipboardText();
                AnsiConsole.MarkupLine("[bold]Copy the text below and paste it on the receiving machine:[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine(text);
                return 0;
            }

            case "file":
            {
                var filePath = output ?? "key-bundle.sshec";
                bundle.SaveToFile(filePath);
                AnsiConsole.MarkupLine($"[green]Key bundle saved to:[/] {Markup.Escape(filePath)}");
                return 0;
            }

            default: // network
            {
                var pairingCode = PairingProtocol.GeneratePairingCode();

                AnsiConsole.Write(new Rule("[bold blue]Network Key Share[/]").LeftJustified());
                AnsiConsole.WriteLine();

                // Bind to all interfaces and get assigned port
                var exchange = new NetworkExchange(0);
                var listenPort = 0;

                using var cts = new CancellationTokenSource();
                KeyBundle? remoteBundle = null;

                var listenTask = Task.Run(async () =>
                {
                    // Get a port by briefly binding
                    var tempListener = new System.Net.Sockets.TcpListener(IPAddress.Any, 0);
                    tempListener.Start();
                    listenPort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
                    tempListener.Stop();

                    var ex = new NetworkExchange(listenPort);
                    remoteBundle = await ex.ListenAndExchangeAsync(
                        bundle,
                        pairingCode,
                        status => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(status)}[/]"),
                        cts.Token);
                });

                // Wait for port assignment
                await Task.Delay(200);

                AnsiConsole.MarkupLine($"[bold]Pairing code:[/] [yellow]{pairingCode}[/]");
                AnsiConsole.MarkupLine($"[bold]Hostname:[/] {Markup.Escape(advertiseHost)}");
                AnsiConsole.MarkupLine($"[bold]Port:[/] {listenPort}");

                // Show local IPs for manual connection
                var localAddrs = Discovery.GetLocalAddresses();
                if (localAddrs.Count > 0)
                    AnsiConsole.MarkupLine($"[bold]Local IPs:[/] {string.Join(", ", localAddrs)}");

                AnsiConsole.MarkupLine("[grey]Share the pairing code with the receiving machine.[/]");

                // Advertise via mDNS
                IDisposable? advertisement = null;
                try
                {
                    if (listenPort > 0)
                    {
                        var profile = Discovery.CreateServiceProfile(advertiseHost, listenPort);
                        advertisement = await Discovery.AdvertiseAsync(profile);
                    }
                }
                catch
                {
                    // mDNS advertising is optional
                }

                AnsiConsole.MarkupLine("[grey]Waiting for connection...[/]");

                try
                {
                    await listenTask;
                }
                finally
                {
                    advertisement?.Dispose();
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

                await PromptToAddKeyAndAlias(platform, remoteBundle);
                return 0;
            }
        }
    }

    /// <summary>
    /// Resolves which hostname to advertise. If running interactively,
    /// offers the user a choice of machine name and local IPs.
    /// </summary>
    private static string ResolveAdvertiseHost()
    {
        var options = new List<string> { Environment.MachineName };

        var localAddrs = Discovery.GetLocalAddresses();
        options.AddRange(localAddrs);

        if (options.Count == 1)
            return options[0];

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which hostname/IP should the other machine use to connect?")
                .AddChoices(options));
    }

    internal static async Task PromptToAddKeyAndAlias(IPlatform platform, KeyBundle remoteBundle)
    {
        var addToAuthorized = AnsiConsole.Prompt(
            new ConfirmationPrompt("Add their key to authorized_keys?") { DefaultValue = true });

        if (addToAuthorized)
        {
            var existing = await AuthorizedKeysManager.ReadAsync(platform);
            var updated = AuthorizedKeysManager.AddKey(existing, remoteBundle.PublicKey);
            await AuthorizedKeysManager.WriteAsync(platform, updated);
            AnsiConsole.MarkupLine("[green]Key added to authorized_keys.[/]");
        }

        var addAlias = AnsiConsole.Prompt(
            new ConfirmationPrompt("Add SSH config alias?") { DefaultValue = true });

        if (addAlias)
        {
            var alias = AnsiConsole.Prompt(
                new TextPrompt<string>("Alias:")
                    .DefaultValue(remoteBundle.SuggestedAlias ?? remoteBundle.Hostname.ToLowerInvariant()));

            var entry = new SshHostEntry(
                alias,
                remoteBundle.Hostname,
                remoteBundle.Username);

            var configContent = await SshConfigManager.ReadConfigAsync(platform);
            var updated = SshConfigManager.AddHost(configContent, entry);
            await SshConfigManager.WriteConfigAsync(platform, updated);
            AnsiConsole.MarkupLine($"[green]SSH alias '{Markup.Escape(alias)}' added.[/]");
        }
    }
}
