# Changelog

## 0.3.0-beta.1 - 2026-08-31

- Added a parser-backed parameter dialog for scripts with a static `param(...)` block.
- Added optional controls for scalar, switch, boolean and array parameters; PowerShell performs the final parameter validation during execution.
- Parameter values are passed as JSON data to a fixed wrapper and splatted in a separate, non-elevated `pwsh.exe` process.
- SecureString and PSCredential inputs are deliberately unsupported in this beta; parameter values are never printed to terminal or log.
- Added `Ctrl+F5` and a toolbar action for parameterized execution; `Shift+F5` also stops this process tree.
- The Problems pane now combines live syntax diagnostics with PowerShell execution errors and keeps them separate from complete terminal output.

## 0.2.0-beta.1 - 2026-08-28

- Parser-based folding for multiline PowerShell brace blocks, collapsed by default.
- Fold markers, hidden-line summaries and global collapse/expand commands.
- Go-to, search and diagnostics reveal folded target text automatically.
- Added breathing room around editor line numbers.
- Fixed unthemed white module-row hover and selection states.

## 0.1.0 - 2026-08-28

- Initial native Windows PowerShell editor and viewer.
- Dark/light theming with three editor palettes.
- PowerShell parser diagnostics, search/replace and line/character navigation.
- Persistent isolated `pwsh.exe` terminal with theme-aware optional CRT scanlines.
- Explicit module, app and PowerShell update flows.
- Windows CI, CodeQL, dependency updates, portable release packaging and build attestation.
