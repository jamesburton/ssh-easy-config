using Spectre.Console;
using SshEasyConfig.Core;
using SshEasyConfig.Platform;

namespace SshEasyConfig.Commands;

public static class SetupCommand
{
    public static async Task<int> RunAsync(IPlatform platform)
    {
        AnsiConsole.Write(new Rule("[bold blue]SSH Easy Config - Setup[/]").LeftJustified());
        AnsiConsole.WriteLine();

        const string keyName = "id_ed25519";

        if (KeyManager.KeyExists(platform, keyName))
        {
            var publicKey = await KeyManager.ReadPublicKeyAsync(platform, keyName);
            var truncated = publicKey.Length > 60
                ? publicKey[..30] + "..." + publicKey[^20..]
                : publicKey;

            AnsiConsole.MarkupLine($"[green]Existing key found:[/] {Markup.Escape(truncated.Trim())}");
            AnsiConsole.WriteLine();

            var useExisting = AnsiConsole.Prompt(
                new ConfirmationPrompt("Use existing key?") { DefaultValue = true });

            if (useExisting)
            {
                AnsiConsole.MarkupLine("[green]Using existing key.[/]");
                return 0;
            }
        }

        var defaultComment = $"{Environment.UserName}@{Environment.MachineName}";
        var comment = AnsiConsole.Prompt(
            new TextPrompt<string>("Key comment:")
                .DefaultValue(defaultComment)
                .AllowEmpty());

        SshKeyPair? keyPair = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating Ed25519 key pair...", async _ =>
            {
                keyPair = KeyManager.GenerateKeyPair(comment);
                await KeyManager.SaveKeyPairAsync(platform, keyPair, keyName);
            });

        AnsiConsole.MarkupLine("[green]Key pair generated and saved.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Public key:[/] {Markup.Escape(keyPair!.PublicKeyOpenSsh.Trim())}");
        AnsiConsole.MarkupLine($"[bold]Fingerprint:[/] {PairingProtocol.ComputeFingerprint(keyPair.PublicKeyOpenSsh.Trim())}");

        return 0;
    }
}
