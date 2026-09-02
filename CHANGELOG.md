# Changelog

## 0.99.0 - 2026-09-02

- Added native ANSI/VT rendering for 16-color, 256-color and truecolor PowerShell output.
- Added terminal formatting for bold, dim, italic, underline, inverse and strikethrough text, with readable dark/light palettes.
- Fixed Unicode commands and output in the persistent PowerShell session through UTF-8 streams and encoding-safe command transport.
- Preserved active terminal styles across output records and reapplied the correct palette after a runtime theme change.
- Removed ANSI and other terminal control sequences from Problems messages while retaining the readable error text.

## 0.3.0 - 2026-09-01

- Promoted parser-backed parameterized execution and combined syntax/runtime Problems reporting from beta.
- Added a live, resizable script inspector with unique function/filter/workflow definitions and click-to-line navigation.
- Added document metadata for save state, line/character/byte counts, encoding and SHA-256 of the saved or current content.
- Added persistent visibility and width settings for the script inspector.
- Added application-owned scrollbars that remain readable and interactive in dark and light mode across the editor and side panels.
- Replaced the mixed-theme native Save As surface with a compact themed file dialog, including folder navigation, file-type filtering, new-folder creation and explicit overwrite confirmation.
- Shared one PowerShell parser result between diagnostics, folding and inspection during live editing.

## 0.3.0-beta.1 - 2026-08-31

- Added a parser-backed parameter dialog for scripts with a static `param(...)` block.
- Added optional controls for scalar, switch, boolean and array parameters; PowerShell performs the final parameter validation during execution.
- Parameter values are passed as JSON data to a fixed wrapper and splatted in a separate, non-elevated `pwsh.exe` process.
- SecureString and PSCredential inputs are deliberately unsupported in this beta; parameter values are never printed to terminal or log.
- Added `Ctrl+F5` and a toolbar action for parameterized execution; `Shift+F5` also stops this process tree.
- The Problems pane now combines live syntax diagnostics with PowerShell execution errors and keeps them separate from complete terminal output.

## 0.2.0 - 2026-08-28

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
