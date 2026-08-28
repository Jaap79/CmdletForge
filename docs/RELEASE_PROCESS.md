# Release process

Cmdlet Forge uses three ordered promotion lanes. Code moves forward; release branches do not receive direct feature work.

| Lane | Purpose | Distribution |
|---|---|---|
| `development` | Active integration | CI artifact only |
| `beta` | Stabilization and field testing | GitHub prerelease |
| `main` | Supported public version | Stable GitHub release and in-app updater |

## Development

1. Branch from `development`.
2. Open a focused pull request back to `development`.
3. Require CI and CodeQL to succeed before merge.
4. Treat uploaded workflow artifacts as disposable development builds.

Development commits are never tagged as releases.

## Beta promotion

1. Open a promotion pull request from `development` to `beta`.
2. Set the project version to `X.Y.Z-beta.N` and update `CHANGELOG.md`.
3. Re-run functional checks on Windows, including terminal execution, diagnostics, search/replace, module handling and both UI themes.
4. Merge the promotion and tag the exact `beta` HEAD as `vX.Y.Z-beta.N`.

The release workflow marks this as a GitHub prerelease. The stable in-app update check does not consume prereleases.

## Stable promotion

1. Open a promotion pull request from `beta` to `main`.
2. Remove the prerelease suffix so the project version is `X.Y.Z`; finalize `CHANGELOG.md`.
3. Confirm the release EXE and SHA-256 sidecar, and complete visual smoke tests on Windows.
4. Merge the promotion and tag the exact `main` HEAD as `vX.Y.Z`.

The tag workflow rebuilds and tests the application, creates the portable Windows x64 executable, generates its SHA-256 sidecar, emits a GitHub build attestation and creates the stable release.

## Hotfixes

Start a hotfix from `main`, validate it as normal, then merge it into `main`. Immediately forward-merge the same commit into `beta` and `development` so the lanes cannot diverge. Tag the patched `main` HEAD with the next stable patch version.

## Release invariants

- A tag must match the project `<Version>` exactly.
- Beta tags must point to the current `beta` HEAD.
- Stable tags must point to the current `main` HEAD.
- Every distributed EXE has a same-named `.sha256` asset.
- Internal Cubics documentation, credentials, customer scripts, logs and local build directories are never published.
