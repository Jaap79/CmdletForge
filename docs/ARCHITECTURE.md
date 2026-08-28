# Architecture

## Components

- **WPF shell** — native window, themed title bar, menu, editor, problems list and terminal surface.
- **AvalonEdit** — text document, selection, line model and rendering.
- **System.Management.Automation parser** — local parse-only syntax diagnostics; no runspace is hosted in the UI process.
- **TerminalSession** — persistent redirected `pwsh.exe` process. Output and error streams are rendered separately.
- **ModuleService** — encoded PowerShell helper process for module discovery and explicit install/update actions.
- **AppUpdateService** — GitHub Releases API client, staged download and mandatory SHA-256 verification.
- **SettingsService** — atomic per-user JSON settings in `%APPDATA%\Cmdlet Forge`.

## Execution flow

Full scripts and selections are written as short-lived UTF-8 files under `%TEMP%\Cmdlet Forge`. The persistent PowerShell process invokes the file and deletes it in a `finally` block. Stopping execution kills the complete child process tree and starts a clean terminal.

This boundary protects editor availability from `exit`, crashes and hangs in ordinary scripts. It does not restrict filesystem, registry, network or tenant access granted to the user and imported modules.

## Module management

Discovery prefers `Find-PSResource`; install/update prefers `Install-PSResource` and `Update-PSResource`. PowerShellGet commands are a compatibility fallback. Operations use exact validated names, PSGallery and `CurrentUser` scope.

`ActiveDirectory` is intentionally excluded from the Gallery list because the supported Windows path is RSAT / Windows capabilities. Cmdlet Forge does not silently elevate to add capabilities.

## Updates

Release CI emits exactly:

- `CmdletForge-win-x64.exe`
- `CmdletForge-win-x64.exe.sha256`

The app checks the repository embedded at build time, compares versions, downloads both assets, verifies SHA-256 in constant time and stages the executable under `%LOCALAPPDATA%\Cmdlet Forge\Updates`. Replacement only occurs after the current process exits.

## Data locations

| Data | Location |
|---|---|
| Settings / recent paths | `%APPDATA%\Cmdlet Forge\settings.json` |
| Logs | `%LOCALAPPDATA%\Cmdlet Forge\Logs` |
| Staged updates | `%LOCALAPPDATA%\Cmdlet Forge\Updates` |
| Temporary run files | `%TEMP%\Cmdlet Forge` |

No credentials are persisted by the application.
