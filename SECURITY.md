# Security policy

## Supported versions

Only the latest published release receives security fixes during the initial development phase.

## Reporting a vulnerability

Use GitHub private vulnerability reporting when enabled for the repository. Do not include credentials, customer data or production scripts in a public issue.

## Trust boundaries

- Cmdlet Forge is an editor and launcher, not a PowerShell sandbox.
- Scripts and terminal commands run with the current user's token in a separate `pwsh.exe` process.
- Module packages come from PSGallery. A GUI confirmation is not a publisher or code-quality guarantee.
- App auto-install refuses releases without the exact `CmdletForge-win-x64.exe.sha256` asset or with a mismatching SHA-256.
- SHA-256 proves file integrity against the release metadata, not publisher identity. Prefer GitHub build attestations and Authenticode signing where available.
- Settings contain presentation preferences and recent file paths only. Cmdlet Forge does not store passwords, access tokens or API keys.

## Defensive defaults

- Non-elevated application manifest (`asInvoker`).
- No background update checks.
- No automatic module or PowerShell updates.
- Module name validation before command construction.
- Per-user module installation by default.
- Global exception logging under `%LOCALAPPDATA%\Cmdlet Forge\Logs` without script contents.
