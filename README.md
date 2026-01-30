<p align="left">
  <img src="assets/Hyprism.svg" alt="HyPrism Logo" width="64" height="64" />
</p>

# HyPrism

[![Downloads](https://img.shields.io/github/downloads/yyyumeniku/HyPrism/total?style=for-the-badge&logo=github&label=Downloads&color=207e5c)](https://github.com/yyyumeniku/HyPrism/releases)
[![Website](https://img.shields.io/badge/Website-hyprism-207e5c?style=for-the-badge&logo=website)](https://yyyumeniku.github.io/hyprism-site/)
[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-Support-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/yyyumeniku)
[![Discord](https://img.shields.io/badge/Website-discord-207e5c?style=for-the-badge&logo=discord)](https://discord.gg/ekZqTtynjp)


A multiplatform Hytale launcher with mod manager and more!

<img width="3084" height="1964" alt="Screenshot 2026-01-17 at 22 29 55@2x" src="https://github.com/user-attachments/assets/0a27bc91-d6d5-4148-ae3b-f9e6c36cd6db" />

## Installation
Downloads are available in [releases](https://github.com/yyyumeniku/HyPrism/releases)

### Linux Users
HyPrism is available in two variants for Linux:

- **Modern** (webkit2gtk-4.1): For Ubuntu 22.04+, Debian 11+, Fedora 36+, and other modern distributions
- **Legacy** (webkit2gtk-4.0): For Ubuntu 18.04/20.04, Debian 10, CentOS 7/8, and other older distributions

If you're unsure which version to use, try the modern version first. If it fails to start with library errors, use the legacy version.

## Build instructions
**Backend**: 
- Default/Modern build: `dotnet build`
- Legacy build: `dotnet build /p:PhotinoVersion=legacy`
- Run: `dotnet run`

**Frontend**: 
- Build: `npm run build` (in the `frontend` directory)

## Credits
Sanasol for maintaining and creating the auth server (https://github.com/sanasol/hytale-auth-server)
