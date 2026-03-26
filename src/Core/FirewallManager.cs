using SshEasyConfig.Platform;

namespace SshEasyConfig.Core;

public static class FirewallManager
{
    /// <summary>
    /// Checks whether port 22 is open in the platform's firewall.
    /// </summary>
    public static async Task<bool> IsPort22OpenAsync(IPlatform platform)
    {
        return platform.FirewallType switch
        {
            FirewallType.WindowsFirewall => await CheckWindowsFirewallAsync(platform),
            FirewallType.Ufw => await CheckUfwAsync(platform),
            FirewallType.Firewalld => await CheckFirewalldAsync(platform),
            FirewallType.Iptables => await CheckIptablesAsync(platform),
            FirewallType.Pf => true,
            FirewallType.None => true,
            _ => true
        };
    }

    /// <summary>
    /// Returns the command and arguments to open a port in the given firewall.
    /// </summary>
    public static (string Command, string Arguments) GetOpenPortCommand(FirewallType firewallType, int port)
    {
        return firewallType switch
        {
            FirewallType.WindowsFirewall => ("netsh",
                $"advfirewall firewall add rule name=\"OpenSSH-Server\" dir=in action=allow protocol=TCP localport={port}"),
            FirewallType.Ufw => ("sudo", $"ufw allow {port}/tcp"),
            FirewallType.Firewalld => ("sudo",
                "bash -c \"firewall-cmd --permanent --add-service=ssh && firewall-cmd --reload\""),
            FirewallType.Iptables => ("sudo",
                $"iptables -I INPUT -p tcp --dport {port} -j ACCEPT"),
            FirewallType.None => throw new InvalidOperationException(
                "No firewall detected. Cannot create firewall rule when no firewall is present."),
            _ => throw new InvalidOperationException($"Unsupported firewall type: {firewallType}")
        };
    }

    /// <summary>
    /// Opens port 22 in the platform's firewall.
    /// </summary>
    public static async Task OpenPort22Async(IPlatform platform)
    {
        var (command, arguments) = GetOpenPortCommand(platform.FirewallType, 22);
        await platform.RunCommandAsync(command, arguments);
    }

    private static async Task<bool> CheckWindowsFirewallAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("netsh",
            "advfirewall firewall show rule name=all dir=in");
        if (result.ExitCode != 0)
            return false;

        // Check for a rule that allows port 22
        var output = result.StdOut;
        var rules = output.Split("Rule Name:", StringSplitOptions.RemoveEmptyEntries);
        foreach (var rule in rules)
        {
            if (rule.Contains("22") && rule.Contains("Allow", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<bool> CheckUfwAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("sudo", "ufw status");
        if (result.ExitCode != 0)
            return false;

        var output = result.StdOut;

        // If ufw is inactive, ports are not filtered (open)
        if (output.Contains("inactive", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for port 22 ALLOW rule
        return output.Contains("22") &&
               output.Contains("ALLOW", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckFirewalldAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("sudo", "firewall-cmd --list-services");
        if (result.ExitCode != 0)
            return false;

        return result.StdOut.Contains("ssh", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CheckIptablesAsync(IPlatform platform)
    {
        var result = await platform.TryRunCommandAsync("sudo", "iptables -L INPUT -n");
        if (result.ExitCode != 0)
            return false;

        var output = result.StdOut;

        // Check for explicit port 22 rule
        if (output.Contains("dpt:22"))
            return true;

        // Check if default policy is ACCEPT
        if (output.Contains("policy ACCEPT"))
            return true;

        return false;
    }
}
