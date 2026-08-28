# Cmdlet Forge

Cmdlet Forge is a compact native PowerShell workbench for Windows. It combines a fast AvalonEdit-based editor with parser-backed syntax checks, a persistent `pwsh.exe` terminal, explicit module management and verifiable self-updates.

[![CI](https://github.com/Jaap79/CmdletForge/actions/workflows/ci.yml/badge.svg)](https://github.com/Jaap79/CmdletForge/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Jaap79/CmdletForge/actions/workflows/codeql.yml/badge.svg)](https://github.com/Jaap79/CmdletForge/actions/workflows/codeql.yml)
[![Latest release](https://img.shields.io/github/v/release/Jaap79/CmdletForge)](https://github.com/Jaap79/CmdletForge/releases/latest)

![Cmdlet Forge icon](assets/cmdletforge.png)

## Highlights

- Native WPF application; no Electron or browser runtime.
- PowerShell editor for `.ps1`, `.psm1` and `.psd1` files.
- Parser-backed syntax diagnostics with exact line, column and selection offsets.
- Dark and light mode with Forge, Oceanic and High Contrast editor palettes.
- Literal, whole-word, case-sensitive and regex search/replace.
- Direct line/character navigation.
- Persistent `pwsh.exe` terminal with captured output, error stream and optional CRT scanlines.
- Selection or full-script execution in an isolated PowerShell process.
- Per-module install/update flow via PSResourceGet, with a PowerShellGet fallback.
- App updates from GitHub Releases only when the EXE has a matching SHA-256 sidecar.
- PowerShell updates delegated to Windows Package Manager (`winget`).
- Portable, self-contained, single-file Windows x64 release.

## Requirements

- Windows 10 or Windows 11 x64.
- PowerShell 7 (`pwsh.exe`) for terminal and execution features.
- Internet access only for module discovery/installation and update checks.

The portable release includes its own .NET runtime. The executable is not Authenticode-signed unless a release maintainer adds signing; verify the published SHA-256 and GitHub build attestation before use.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+O` / `Ctrl+S` | Open / save |
| `Ctrl+F` / `Ctrl+H` | Find / replace |
| `Ctrl+G` | Focus line and character navigation |
| `F3` / `Shift+F3` | Next / previous match |
| `Ctrl+Enter` | Run selection, or the full document when nothing is selected |
| `F5` | Run document |
| `Shift+F5` | Stop the active PowerShell process and restart the terminal |
| `Ctrl++` / `Ctrl+-` | Increase / decrease editor font size |

## Build

Install the .NET 10 SDK, then run:

```powershell
dotnet restore .\CmdletForge.slnx
.\scripts\build.ps1
.\scripts\package.ps1
```

To enable app updates in a build, embed the GitHub repository slug:

```powershell
.\scripts\package.ps1 -UpdateRepository 'owner/repository'
```

Outputs are written to `artifacts/publish/`.

## Release channels

- `development` receives normal feature and fix pull requests. CI artifacts from this branch are development builds, not releases.
- `beta` receives reviewed promotions from `development`. Tags such as `v0.2.0-beta.1` create an explicit GitHub prerelease.
- `main` receives tested promotions from `beta`. Tags such as `v0.2.0` create the stable public release consumed by the in-app updater.

See [docs/RELEASE_PROCESS.md](docs/RELEASE_PROCESS.md) for the promotion and versioning rules.

## Security model

Scripts do not execute inside the WPF process. Cmdlet Forge starts a separate, non-elevated `pwsh.exe`; stopping execution terminates that process tree. This is crash isolation, not a security sandbox: a script still has the permissions of the current Windows user.

Module names are constrained before they reach PowerShell, installations target `CurrentUser`, and every install/update requires a user confirmation. PSGallery content remains third-party code; review publisher and source before installation.

See [SECURITY.md](SECURITY.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for boundaries and implementation details.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
