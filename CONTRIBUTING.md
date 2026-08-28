# Contributing

1. Create a focused branch from `development` and keep unrelated changes out of the pull request.
2. Run `./scripts/build.ps1` on Windows with the .NET 10 SDK.
3. Exercise dark and light mode, an open menu, search/replace, diagnostics and terminal output.
4. Do not commit customer scripts, credentials, build artifacts, logs or Cubics internal documentation.
5. Document security-boundary changes in `SECURITY.md` and `docs/ARCHITECTURE.md`.

Normal changes merge into `development`. Promote `development` to `beta` only through a reviewed pull request, and promote `beta` to `main` the same way. Do not merge feature branches directly into `beta` or `main`.

Release tags must match the `<Version>` in `src/CmdletForge/CmdletForge.csproj` exactly. Beta tags use `vX.Y.Z-beta.N`; stable tags use `vX.Y.Z`. See [docs/RELEASE_PROCESS.md](docs/RELEASE_PROCESS.md).
