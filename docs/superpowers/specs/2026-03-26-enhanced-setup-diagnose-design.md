# Enhanced Setup & Diagnose Design Specification

## Overview

Enhance `ssh-easy-config setup` from key-generation-only to a full SSH provisioning wizard that installs the SSH server, starts the service, opens the firewall, handles Windows Microsoft account auth quirks, generates keys, fixes permissions, and validates the result. Enhance `diagnose` to offer inline fixes for issues it detects.

## Setup Command Flow

### Step 1: Detect Platform and SSH State

Detect and report:
- OS, version, WSL detection (existing platform abstraction)
- Is sshd installed?
- Is sshd running?
- Is sshd enabled on boot?
- Is firewall blocking port 22?
- Current user account type (Windows: local vs MS-linked, admin vs standard)
- Is the process running elevated/root?

### Step 2: Install SSH Server (if missing)

Each platform has a different installation method. Prompt user for confirmation before installing.

| Platform | Detection | Install Command |
|----------|-----------|-----------------|
| Windows | `Get-WindowsCapability -Online -Name OpenSSH.Server*` | `Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0` |
| Linux (apt) | `which sshd` or `dpkg -l openssh-server` | `sudo apt install -y openssh-server` |
| Linux (dnf) | `which sshd` or `rpm -q openssh-server` | `sudo dnf install -y openssh-server` |
| macOS | Built-in, check Remote Login status | `sudo systemsetup -setremotelogin on` |
| WSL | Same as Linux (detect apt vs dnf) | Same as Linux |

**Elevation handling:** On Windows, installing OpenSSH requires admin privileges. If not elevated:
- Detect via `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`
- Inform user and offer to re-launch elevated, or provide manual commands

### Step 3: Start and Enable SSH Service

| Platform | Start | Enable on Boot |
|----------|-------|----------------|
| Windows | `sc start sshd` | `sc config sshd start= auto` |
| Linux (systemd) | `sudo systemctl start sshd` (or `ssh` on Debian/Ubuntu) | `sudo systemctl enable sshd` |
| Linux (no systemd) | `sudo service ssh start` | Manual/distro-specific |
| macOS | Handled by `systemsetup -setremotelogin on` | Same |
| WSL (systemd) | `sudo systemctl start ssh` | `sudo systemctl enable ssh` |
| WSL (no systemd) | `sudo service ssh start` | Note: won't persist across WSL restarts without extra config |

**Service name detection:** On Debian/Ubuntu the service is named `ssh`, on RHEL/Fedora it's `sshd`. Detect by checking which service file exists.

### Step 4: Open Firewall

| Platform | Detection | Open Command |
|----------|-----------|--------------|
| Windows | `netsh advfirewall firewall show rule name=all dir=in` filtered for port 22 | `netsh advfirewall firewall add rule name="OpenSSH-Server" dir=in action=allow protocol=TCP localport=22` |
| Linux (ufw) | `sudo ufw status` | `sudo ufw allow 22/tcp` |
| Linux (firewalld) | `sudo firewall-cmd --list-services` | `sudo firewall-cmd --permanent --add-service=ssh && sudo firewall-cmd --reload` |
| Linux (iptables) | `sudo iptables -L -n` checking for port 22 | `sudo iptables -I INPUT -p tcp --dport 22 -j ACCEPT` |
| macOS | Firewall generally allows SSH when Remote Login is on | Verify; no action usually needed |
| WSL | Inherits Windows firewall; no separate config | Ensure Windows firewall rule exists |

**Firewall detection order on Linux:** Check ufw first, then firewalld, then fall back to iptables. Only configure the one that's active.

### Step 5: Windows Microsoft Account Authentication

**Problem:** When a Windows user signs in with a Microsoft account, their SSH authentication has specific requirements:
- Admin users must use `%ProgramData%\ssh\administrators_authorized_keys` instead of `%USERPROFILE%\.ssh\authorized_keys`
- This file must be owned by SYSTEM and the Administrators group, NOT the individual user
- `sshd_config` must have the `Match Group administrators` block configured correctly

**Detection:**
- Check registry at `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{current-SID}` for linked Microsoft account presence
- Check if user is in the Administrators group

**Fix (for admin + MS-linked account):**
1. Ensure `%ProgramData%\ssh\administrators_authorized_keys` exists
2. Copy the user's public key into it (if not already present)
3. Set ACLs: `icacls administrators_authorized_keys /inheritance:r /grant "SYSTEM:(F)" /grant "BUILTIN\Administrators:(F)"`
4. Verify `sshd_config` contains the correct Match block:
   ```
   Match Group administrators
       AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
   ```
5. If the Match block is missing or incorrect, add/fix it (with backup)

**For non-admin MS-linked accounts:** Standard `authorized_keys` in user profile should work, but verify ACLs are correct.

### Step 6: Generate Keys (existing)

Existing Ed25519 key generation flow:
- Check for existing key, prompt to reuse
- Generate new key with comment
- Save and set permissions

### Step 7: Fix Permissions (existing, enhanced)

Existing permission enforcement, plus:
- Windows `administrators_authorized_keys` ACL fix (from Step 5)
- Verify `%ProgramData%\ssh\` directory permissions

### Step 8: Validate

- Attempt SSH connection to localhost: `ssh -o BatchMode=yes -o StrictHostKeyChecking=no localhost exit 0`
- Report success or remaining issues
- If validation fails, run diagnose checks to identify what's still wrong

## Diagnose Enhancement

When `diagnose` finds a fixable issue, offer to fix inline rather than just reporting:

| Issue Detected | Current Behavior | New Behavior |
|----------------|-----------------|--------------|
| sshd not installed | Report as FAIL | "Install now? [Y/n]" → run install |
| sshd not running | Report as FAIL | "Start now? [Y/n]" → start service |
| Firewall blocking | Report as FAIL | "Open port 22? [Y/n]" → configure firewall |
| Wrong permissions | Report as WARN | "Fix permissions? [Y/n]" → fix |
| Wrong authorized_keys ACLs (Windows) | Report as FAIL | "Fix ACLs? [Y/n]" → fix |
| Missing Match block (Windows) | Report as FAIL | "Add to sshd_config? [Y/n]" → add |

In `--json` mode, fixes are not offered (non-interactive). The fix suggestions are included in the JSON output as `autoFixAvailable: true`.

## Implementation Scope

### New Files
- `src/Core/SshServerInstaller.cs` — platform-specific sshd installation, start, enable
- `src/Core/FirewallManager.cs` — platform-specific firewall detection and configuration
- `src/Core/WindowsAccountHelper.cs` — MS account detection, administrators_authorized_keys management

### Modified Files
- `src/Commands/SetupCommand.cs` — expanded to full provisioning flow
- `src/Commands/DiagnoseCommand.cs` — add fix-it prompts
- `src/Diagnostics/DiagnosticRunner.cs` — return fixable results
- `src/Platform/IPlatform.cs` — add `IsElevated` property, `GetPackageManager()`, `GetFirewallType()`
- `src/Platform/WindowsPlatform.cs` — elevation detection, MS account detection
- `src/Platform/LinuxPlatform.cs` — package manager detection, firewall type detection

### Not In Scope (this iteration)
- SELinux context management
- WSL2 port forwarding automation
- Custom SSH port configuration
- Multi-user provisioning
