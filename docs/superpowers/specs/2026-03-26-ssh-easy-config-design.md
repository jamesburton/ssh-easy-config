# ssh-easy-config Design Specification

## Overview

A cross-platform .NET 10 CLI tool for configuring SSH access, sharing keys between machines, and diagnosing connectivity issues. Distributed as a NuGet tool package — users run via `dnx ssh-easy-config` (zero install) or `dotnet tool install -g ssh-easy-config`.

## Audience

Developers and sysadmins. Works for personal single-machine setup and scales to team provisioning across infrastructure.

## CLI Interface

### Subcommands

```
ssh-easy-config                     # Interactive wizard (no args)
ssh-easy-config setup               # Generate keys, configure SSH
ssh-easy-config share               # Share keys with another machine
ssh-easy-config receive             # Listen for incoming key share
ssh-easy-config diagnose [host]     # Diagnose SSH connectivity
ssh-easy-config config              # Manage ssh_config / sshd_config
```

### Interactive Wizard

When run with no arguments, launches a step-by-step guided wizard using Spectre.Console. Covers the same operations as the subcommands but walks the user through choices interactively.

## Module 1: Key Management

### Key Generation

- Ed25519 only, via `System.Security.Cryptography` APIs
- Keys stored in platform-standard locations:
  - Linux/macOS/WSL: `~/.ssh/`
  - Windows: `%USERPROFILE%\.ssh\`
- Wizard prompts for optional passphrase and key comment (defaults to `user@hostname`)
- Detects existing keys, asks whether to reuse or generate new

### authorized_keys Management

- Add/remove public keys with deduplication
- Preserves existing entries and comments
- Handles Windows OpenSSH `administrators_authorized_keys` for admin users

### Permission Enforcement

Validates and fixes file permissions (SSH is strict about these):

| Path | Linux/macOS/WSL | Windows |
|------|-----------------|---------|
| `~/.ssh/` | 700 | Equivalent ACL via `icacls` |
| Private keys | 600 | Equivalent ACL via `icacls` |
| `authorized_keys` | 600 | Equivalent ACL via `icacls` |
| `config` | 644 | Equivalent ACL via `icacls` |

## Module 2: SSH Configuration

### Client-side (`~/.ssh/config`)

- Create/update/remove Host alias entries
- Manages: `HostName`, `User`, `Port`, `IdentityFile`, `ForwardAgent`
- Parser preserves comments and formatting of existing config

### Server-side (`sshd_config`)

- Audit current settings and recommend changes
- Apply changes with user confirmation:
  - Disable `PasswordAuthentication`
  - Set `PubkeyAuthentication yes`
  - Configure `AllowUsers` / `AllowGroups`
  - Set `PermitRootLogin no` (or `prohibit-password`)
- Restart/reload SSH service after changes (platform-aware)
- Always creates a backup of the original config before modifying

## Module 3: Network Key Exchange

### Primary: Direct Network Transfer

Two-role protocol:

1. **Machine A** runs `ssh-easy-config share` — starts a temporary listener
2. **Machine B** runs `ssh-easy-config receive` — connects and exchanges keys

### Discovery

- **mDNS:** auto-advertises/discovers on LAN using service type `_ssh-easy._tcp` (via Makaretu.Dns or similar NuGet package)
- **Manual fallback:** user enters IP:port when mDNS fails or machines are cross-network

### Transfer Security

- Listener generates a short **pairing code** (6-digit numeric) displayed on screen
- Connecting machine prompts user to enter the pairing code
- Pairing code derives a shared key (HKDF) for encrypting the exchange over TLS
- Both machines display a **fingerprint confirmation** of exchanged keys for visual verification
- Listener shuts down after one successful exchange (no lingering open port)

### What Gets Exchanged

- Public keys (both directions for mutual setup)
- Hostnames and preferred SSH usernames
- Optional host alias suggestions for ssh_config

### Fallback: Clipboard (Option A)

```bash
ssh-easy-config share --mode clipboard
# Outputs base64-encoded block (public key + metadata)
# User pastes on the other machine:
ssh-easy-config receive --mode clipboard
```

### Fallback: File (Option B)

```bash
ssh-easy-config share --mode file --output ./my-key-bundle.sshec
# User transfers the .sshec file:
ssh-easy-config receive --mode file --input ./my-key-bundle.sshec
```

Both fallback modes use the same data format, just different transport.

## Module 4: Diagnostics

### `ssh-easy-config diagnose [host]`

Runs a layered check sequence, stopping to report at each failure:

**Layer 1 — Network Reachability:**
- DNS resolution (forward and reverse)
- TCP port connectivity (default 22 or custom from ssh_config)
- Firewall detection heuristics (connection refused vs. timeout)
- Windows Firewall rule checks

**Layer 2 — SSH Service Status:**
- sshd running on target (inferred from connection handshake)
- SSH protocol version banner check
- Local sshd service status (running and enabled)

**Layer 3 — Authentication Audit:**
- Key-based auth attempt and result interpretation
- Local key existence and match against remote authorized_keys
- Common key mismatches (wrong type, expired, revoked)

**Layer 4 — Configuration Audit:**
- Client-side: `~/.ssh/config` syntax validation, `IdentityFile` path checks, permission checks
- Server-side (if accessible): `sshd_config` review — pubkey auth enabled, password auth status, AllowUsers/AllowGroups
- File permission checks on both ends

**Layer 5 — WSL-Specific Checks:**
- SSH configured inside WSL, on Windows host, or both
- Port forwarding between Windows and WSL
- Interop path issues (`/mnt/c/Users/...` vs `C:\Users\...`)

### Guided Troubleshooting

- Each failed check produces a specific, actionable message
- Offers to auto-fix where safe (permissions, missing config lines) with user confirmation
- Step-by-step instructions for manual intervention (firewall, router config)

### Output Modes

- Interactive (default): colored output with fix-it prompts
- `--json`: machine-readable for scripting/CI
- `--verbose`: full trace of every check

## Platform Abstraction

### Platform Detection

Detects current environment once at startup, provides platform-specific implementations:

| Concern | Linux | macOS | Windows | WSL |
|---------|-------|-------|---------|-----|
| SSH paths | `~/.ssh/` | `~/.ssh/` | `%USERPROFILE%\.ssh\` | `~/.ssh/` |
| Service mgmt | `systemctl` | `launchctl` | `sc.exe` / `Get-Service` | `systemctl` or `service` |
| Permissions | `chmod` | `chmod` | `icacls` | `chmod` |
| sshd_config | `/etc/ssh/sshd_config` | `/etc/ssh/sshd_config` | `%ProgramData%\ssh\sshd_config` | `/etc/ssh/sshd_config` |
| Admin keys | `authorized_keys` | `authorized_keys` | `administrators_authorized_keys` | `authorized_keys` |
| Firewall | `iptables`/`ufw`/`firewalld` | `pfctl` | `netsh` | Inherits Windows firewall |

### WSL as First-Class Target

- Detection via `/proc/version` containing "Microsoft" or `WSL_DISTRO_NAME` env var
- Cross-boundary setup between Windows host and WSL instance
- WSL1 vs WSL2 network stack detection for port forwarding
- Path translation: `/mnt/c/Users/james/.ssh/` <-> `C:\Users\james\.ssh\`
- Dual config: when inside WSL, offers to configure both WSL-side and Windows-side SSH

### Abstraction Interface

Each module calls into `IPlatform` rather than hardcoding paths or commands. Platform detected once at startup and threaded through all modules.

## Project Structure

```
ssh-easy-config/
├── src/
│   ├── SshEasyConfig.csproj
│   ├── Program.cs                  # Entry point, CLI parsing, wizard router
│   ├── Commands/
│   │   ├── SetupCommand.cs
│   │   ├── ShareCommand.cs
│   │   ├── ReceiveCommand.cs
│   │   ├── DiagnoseCommand.cs
│   │   └── ConfigCommand.cs
│   ├── Core/
│   │   ├── KeyManager.cs
│   │   ├── ConfigManager.cs
│   │   ├── NetworkExchange.cs
│   │   ├── Discovery.cs
│   │   └── PairingProtocol.cs
│   ├── Diagnostics/
│   │   ├── DiagnosticRunner.cs
│   │   ├── NetworkCheck.cs
│   │   ├── SshServiceCheck.cs
│   │   ├── AuthCheck.cs
│   │   ├── ConfigCheck.cs
│   │   └── WslCheck.cs
│   └── Platform/
│       ├── IPlatform.cs
│       ├── PlatformDetector.cs
│       ├── LinuxPlatform.cs
│       ├── MacOsPlatform.cs
│       ├── WindowsPlatform.cs
│       └── WslPlatform.cs
├── tests/
│   └── SshEasyConfig.Tests/
│       └── SshEasyConfig.Tests.csproj
├── .gitignore
└── README.md
```

## NuGet Dependencies

- **System.CommandLine** — CLI parsing and subcommand routing
- **Spectre.Console** — Rich terminal UI for wizard, colored diagnostics output
- **Makaretu.Dns** (or similar) — mDNS discovery and advertisement
- **No SSH library** — uses .NET crypto APIs for Ed25519 key generation and file format writing; shells out to system `ssh` for connection testing in diagnostics and to system service managers (`systemctl`, `launchctl`, `sc.exe`) for sshd control

## Packaging

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>ssh-easy-config</ToolCommandName>
  <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-arm64;any</RuntimeIdentifiers>
  <PublishSelfContained>true</PublishSelfContained>
  <PublishTrimmed>true</PublishTrimmed>
</PropertyGroup>
```

- `any` RID provides framework-dependent fallback for platforms not explicitly targeted
- Self-contained + trimmed means `dnx ssh-easy-config` works without .NET installed

## Distribution

- `dotnet pack` produces NuGet package
- Primary: `dnx ssh-easy-config` (zero install, one-shot execution)
- Alternative: `dotnet tool install -g ssh-easy-config` (permanent installation)

## Decisions

- **Ed25519 only** — no RSA fallback, keeps the tool simple and secure
- **No centralized relay** — avoids security risks of stored keys and third-party services
- **Shell out for SSH connection testing and service management** — avoids bundling an SSH library; key generation and file I/O use .NET APIs directly
- **mDNS for discovery** — works on LAN without configuration, falls back to manual IP for cross-network
- **Pairing code for transfer security** — simple UX, no certificate infrastructure needed
- **Backup before modify** — always back up sshd_config before changes
- **WSL as distinct platform** — not treated as generic Linux, handles cross-boundary scenarios
