# ssh-easy-config

Cross-platform SSH key management, sharing, and diagnostics.

A .NET 10 CLI tool that handles the entire SSH setup lifecycle: generating keys, sharing them between machines, configuring client and server settings, and diagnosing connectivity problems. Works on Windows, Linux, macOS, and WSL.

## Quick Start

### Zero-install (requires .NET 10)

```bash
dnx ssh-easy-config
```

This downloads and runs the tool in a single command with no permanent installation.

### Global tool install

```bash
dotnet tool install -g ssh-easy-config
ssh-easy-config
```

## Features

- **Ed25519 key generation** -- generates keys using modern cryptography, stored in platform-standard locations
- **Three key exchange modes** -- share keys over the local network (mDNS + pairing code), via clipboard, or through a file
- **Interactive wizard** -- step-by-step guided setup when run with no arguments
- **5-layer connectivity diagnostics** -- pinpoints SSH problems from network reachability down to WSL-specific issues
- **SSH config management** -- create and manage host aliases, audit and harden sshd_config
- **Cross-platform** -- native support for Windows, Linux, macOS, and WSL (treated as a distinct platform, not generic Linux)
- **Permission enforcement** -- validates and fixes file permissions on keys and config files
- **No centralized relay** -- all key exchange happens directly between machines

## Commands

### Interactive Wizard

```bash
ssh-easy-config
```

Running with no arguments launches a guided wizard using Spectre.Console. It walks through key generation, sharing, configuration, and diagnostics interactively.

### setup

Generate SSH keys and configure SSH for the current machine.

```bash
ssh-easy-config setup
```

- Generates Ed25519 keys (or reuses existing ones)
- Prompts for optional passphrase and key comment (defaults to `user@hostname`)
- Sets correct file permissions on `~/.ssh/` and key files
- Handles Windows-specific `administrators_authorized_keys` for admin users

### share

Share your public key with another machine.

```bash
# Network mode (default) -- uses mDNS discovery and pairing code
ssh-easy-config share

# Clipboard mode -- outputs a base64-encoded block to paste on the other machine
ssh-easy-config share --mode clipboard

# File mode -- writes a .sshec bundle to disk
ssh-easy-config share --mode file --output ./my-key-bundle.sshec
```

**Options:**

| Option | Description |
|--------|-------------|
| `--mode` | Transfer mode: `network` (default), `clipboard`, or `file` |
| `--output` | Output file path (used with `--mode file`) |

### receive

Receive a key shared from another machine.

```bash
# Network mode (default) -- discovers the sender via mDNS
ssh-easy-config receive

# Clipboard mode -- prompts you to paste the base64 block
ssh-easy-config receive --mode clipboard

# File mode -- reads from a .sshec bundle
ssh-easy-config receive --mode file --input ./my-key-bundle.sshec
```

**Options:**

| Option | Description |
|--------|-------------|
| `--mode` | Transfer mode: `network` (default), `clipboard`, or `file` |
| `--input` | Input file path (used with `--mode file`) |

### diagnose

Diagnose SSH connectivity to a host.

```bash
# Diagnose a specific host
ssh-easy-config diagnose myserver

# JSON output for scripting or CI
ssh-easy-config diagnose myserver --json

# Show all checks including skipped ones
ssh-easy-config diagnose myserver --verbose

# General local SSH health check (no host)
ssh-easy-config diagnose
```

**Options:**

| Option | Description |
|--------|-------------|
| `host` | The host to diagnose (optional) |
| `--json` | Output results as JSON |
| `--verbose` | Show all checks including skipped |

### config

Manage SSH client and server configuration.

```bash
# Audit current sshd_config settings
ssh-easy-config config audit

# Apply security hardening to sshd_config
ssh-easy-config config harden

# List and manage host aliases in ~/.ssh/config
ssh-easy-config config hosts
```

**Actions:**

| Action | Description |
|--------|-------------|
| `audit` | Review sshd_config and recommend changes |
| `harden` | Apply security settings (disable password auth, enable pubkey auth, etc.) |
| `hosts` | Manage Host alias entries in `~/.ssh/config` |

Hardening applies these changes with user confirmation and always creates a backup first:

- Disable `PasswordAuthentication`
- Set `PubkeyAuthentication yes`
- Configure `AllowUsers` / `AllowGroups`
- Set `PermitRootLogin no` (or `prohibit-password`)

## Key Exchange

ssh-easy-config supports three modes for transferring public keys between machines. All three exchange the same data (public keys, hostnames, preferred usernames, and optional host alias suggestions) -- only the transport differs.

### Network Mode (default)

The primary mode. Two machines on the same network exchange keys directly:

1. **Machine A** runs `ssh-easy-config share` -- starts a temporary listener and displays a 6-digit pairing code
2. **Machine B** runs `ssh-easy-config receive` -- discovers Machine A via mDNS (`_ssh-easy._tcp` service) and prompts for the pairing code
3. The pairing code derives a shared encryption key (via HKDF) to secure the transfer
4. Both machines display a fingerprint confirmation of the exchanged keys for visual verification
5. The listener shuts down after one successful exchange

If mDNS discovery fails (different subnets, mDNS blocked), the user can fall back to entering an IP address and port manually.

### Clipboard Mode

For machines that cannot reach each other over the network:

```bash
# On the sending machine
ssh-easy-config share --mode clipboard
# Copy the base64-encoded output

# On the receiving machine
ssh-easy-config receive --mode clipboard
# Paste the block when prompted
```

### File Mode

For transferring via USB drive, shared folder, or any other file transport:

```bash
# On the sending machine
ssh-easy-config share --mode file --output ./keys.sshec

# Transfer the .sshec file to the other machine, then:
ssh-easy-config receive --mode file --input ./keys.sshec
```

## Platform Support

ssh-easy-config detects the current platform at startup and adapts its behavior accordingly.

| Concern | Linux | macOS | Windows | WSL |
|---------|-------|-------|---------|-----|
| SSH paths | `~/.ssh/` | `~/.ssh/` | `%USERPROFILE%\.ssh\` | `~/.ssh/` |
| Service management | `systemctl` | `launchctl` | `sc.exe` / `Get-Service` | `systemctl` or `service` |
| Permissions | `chmod` | `chmod` | `icacls` | `chmod` |
| sshd_config location | `/etc/ssh/sshd_config` | `/etc/ssh/sshd_config` | `%ProgramData%\ssh\sshd_config` | `/etc/ssh/sshd_config` |
| Admin authorized keys | `authorized_keys` | `authorized_keys` | `administrators_authorized_keys` | `authorized_keys` |
| Firewall | `iptables`/`ufw`/`firewalld` | `pfctl` | `netsh` | Inherits Windows firewall |

### WSL as a First-Class Target

WSL is treated as a distinct platform, not generic Linux:

- Detected via `/proc/version` containing "Microsoft" or the `WSL_DISTRO_NAME` environment variable
- Supports cross-boundary setup between the Windows host and WSL instances
- Detects WSL1 vs WSL2 for correct network stack handling and port forwarding
- Translates paths between WSL (`/mnt/c/Users/james/.ssh/`) and Windows (`C:\Users\james\.ssh\`)
- When running inside WSL, offers to configure both the WSL-side and Windows-side SSH

## Diagnostics

The `diagnose` command runs a 5-layer check sequence. Each layer builds on the previous one, stopping to report at the first failure with an actionable message and an offer to auto-fix where safe.

### Layer 1 -- Network Reachability

- DNS resolution (forward and reverse)
- TCP port connectivity (default 22, or custom port from ssh_config)
- Firewall detection heuristics (connection refused vs. timeout)
- Windows Firewall rule checks

### Layer 2 -- SSH Service Status

- Whether sshd is running on the target (inferred from connection handshake)
- SSH protocol version banner check
- Local sshd service status (running and enabled)

### Layer 3 -- Authentication Audit

- Key-based authentication attempt and result interpretation
- Local key existence and match against remote authorized_keys
- Detection of common key mismatches (wrong type, expired, revoked)

### Layer 4 -- Configuration Audit

- Client-side: `~/.ssh/config` syntax validation, `IdentityFile` path checks, permission checks
- Server-side (if accessible): `sshd_config` review -- pubkey auth enabled, password auth status, AllowUsers/AllowGroups
- File permission checks on both ends

### Layer 5 -- WSL-Specific Checks

- Whether SSH is configured inside WSL, on the Windows host, or both
- Port forwarding between Windows and WSL
- Interop path issues (`/mnt/c/Users/...` vs `C:\Users\...`)

### Output Modes

- **Interactive** (default): colored terminal output with fix-it prompts
- **JSON** (`--json`): machine-readable output for scripting and CI pipelines
- **Verbose** (`--verbose`): full trace of every check, including those that were skipped

## Building from Source

Requires .NET 10 SDK.

```bash
# Build
dotnet build src/SshEasyConfig.csproj

# Run tests
dotnet test tests/SshEasyConfig.Tests/SshEasyConfig.Tests.csproj

# Create NuGet package
dotnet pack src/SshEasyConfig.csproj
```

## Dependencies

- [System.CommandLine](https://github.com/dotnet/command-line-api) -- CLI parsing and subcommand routing
- [Spectre.Console](https://spectreconsole.net/) -- rich terminal UI for the wizard and colored diagnostics
- [Makaretu.Dns.Multicast](https://github.com/richardschneider/net-mdns) -- mDNS discovery and advertisement for network key exchange
- [NSec.Cryptography](https://nsec.rocks/) -- cryptographic operations

No SSH library is bundled. Key generation uses .NET cryptography APIs directly. Connection testing in diagnostics and service management shell out to the system's `ssh`, `systemctl`, `launchctl`, `sc.exe`, etc.

## License

MIT
