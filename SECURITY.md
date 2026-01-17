# Security & Transparency

## Malware Scanner False Positives

HyPrism may trigger warnings in some antivirus/malware scanners. This is a **false positive** common with unsigned applications. Here's why:

### Why Scanners Flag HyPrism

1. **Unsigned Binary**: The Windows executable is not code-signed (signing certificates cost $300+/year)
2. **Network Activity**: Downloads game files from GitHub, checks for updates, connects to news APIs
3. **Process Monitoring**: Checks if game is running (recently fixed to use Windows API instead of shell commands)
4. **UAC Detection**: Normal check for installation permissions

### Specific False Positives Explained

#### Triage.ge Analysis (Score 6/10)
- **"Checks whether UAC is enabled"**: Standard for apps that install files to AppData
- **"Network Share Discovery"**: Normal Windows API call to detect network configuration
- **EdgeWebView2 processes**: Wails framework uses Microsoft's WebView2 (same as Edge browser)
- **DNS queries**: github.com (updates/downloads), dns.google (connectivity check)

None of these behaviors are malicious.

### How to Verify Safety

1. **Source Code**: Fully open source at [github.com/yyyumeniku/HyPrism](https://github.com/yyyumeniku/HyPrism)
2. **Build Yourself**: See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions
3. **Network Traffic**: Only connects to:
   - `github.com` - Game files and updates
   - `hytale.com` - Official news feed
   - `curseforge.com` - Mod downloads
4. **Permissions**: Only accesses:
   - `AppData/Local/HyPrism` - Game installation
   - `AppData/Roaming/HyPrism.exe` - User settings (WebView2 cache)

### What HyPrism Does NOT Do

- ❌ No cryptocurrency mining
- ❌ No keylogging or data theft
- ❌ No remote code execution
- ❌ No system file modification
- ❌ No unauthorized network access
- ❌ No persistence mechanisms (registry edits, startup entries)

## Technical Details

### Network Connections
```
github.com:443          - Check for launcher updates & download game files
api.github.com:443      - GitHub API for release information
hytale.com:443          - Fetch official news
www.curseforge.com:443  - Mod database (CurseForge API)
```

### File System Access
```
%LOCALAPPDATA%\HyPrism\             - Game installation directory
%APPDATA%\HyPrism.exe\EBWebView\    - WebView2 browser cache
%TEMP%\                             - Temporary download files
```

### Process Behavior
- **Windows**: Uses `CreateToolhelp32Snapshot` API to check if game is running (no shell commands)
- **macOS/Linux**: Uses `pgrep` command to check running processes
- **Updates**: Downloads `version.json` and compares with current version

## Recent Security Improvements

### v1.0.8 (January 17, 2026)
- **Fixed Windows Terminal Popup**: Replaced `tasklist` command with native Windows API
  - Before: Spawned visible console windows every 2 seconds
  - After: In-process check using `CreateToolhelp32Snapshot`
- **Reduced Polling**: Increased intervals from 2s to 3s to reduce CPU usage

## Reporting Security Issues

If you find a legitimate security vulnerability:
1. **DO NOT** open a public issue
2. Email: [Create a private security advisory](https://github.com/yyyumeniku/HyPrism/security/advisories/new)
3. Include: Steps to reproduce, impact, and suggested fix

## Build Reproducibility

To verify the binary matches the source code:
```bash
git clone https://github.com/yyyumeniku/HyPrism
cd HyPrism
wails build
# Compare hash of build/bin/HyPrism.exe with released version
```

Note: Exact reproducibility is difficult due to build timestamps and Go compiler variations, but you can verify the code does what it claims.

## Why Trust HyPrism?

1. **Open Source**: All code is public and reviewable
2. **Active Development**: Regular commits and issue responses
3. **Community Feedback**: Positive Reddit reviews, active users
4. **No Obfuscation**: Code is readable, no packed binaries
5. **Educational Purpose**: Stated clearly in README

## Disclaimer

This project is for **educational purposes only**. It provides offline access to leaked game files. We do not condone piracy - please support Hypixel Studios by purchasing Hytale when it officially releases.
