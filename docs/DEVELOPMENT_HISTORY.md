# Development History

Last consolidated: 2026-06-11

This document preserves the useful content that used to live in the temporary
ProjectBuilder refactor memo. Keep this file as the durable engineering record
for the Windows desktop Project Builder.

## Application Contracts

- Preserve mobile-compatible project JSON.
- Preserve the current QR payload contract used by the mobile apps.
- Preserve localization behavior.
- Preserve UI behavior unless a visible change is explicitly requested.
- Do not rename historical compatibility functions only for style. In
  particular, `RequireProjectJsonV2` still has a historical name even though the
  current schema version is newer.

## Project Shape

- WPF desktop application on .NET 8.
- Core model and QR payload logic live under `dotnet/src/PlcIoCheckerQr.Core`.
- WPF UI lives under `dotnet/src/PlcIoCheckerQr.Wpf`.
- Tests live under `dotnet/tests/PlcIoCheckerQr.Core.Tests`.

## Refactoring History

### Code-Behind Split

Completed in commit `9f421dd refactor project builder code-behind (#10)`.

Problem:

- `MainWindow.xaml.cs` had grown into a large code-behind file of roughly 2,600
  lines.
- The file mixed row models, grid setup, QR generation, JSON import/export,
  clipboard parsing, localization, and command handlers.

Completed work:

- Extracted clipboard import parsing into `ClipboardImport.cs`.
- Split WPF code-behind into partial files:
  - `MainWindow.Clipboard.cs`
  - `MainWindow.Grids.cs`
  - `MainWindow.JsonIo.cs`
  - `MainWindow.Localization.cs`
  - `MainWindow.Qr.cs`
  - `MainWindow.RowCommands.cs`
  - `MainWindow.Rows.cs`
- Added focused helper files:
  - `NumericParsing.cs`
  - `ProjectJsonReader.cs`
  - `UiValueMapping.cs`
- Reduced `MainWindow.xaml.cs` by about 2,302 deleted lines while adding focused
  files for the moved responsibilities.

Effect:

- The main window entry file is smaller and easier to review.
- Clipboard, QR, JSON, localization, grid setup, and row command behavior can be
  inspected independently.
- The split kept WPF behavior in partial class files instead of forcing a larger
  MVVM rewrite.

### ProjectFactory Tests

Completed work:

- Added `ProjectFactoryTests.cs`.
- Expanded `ProjectQrPayloadTests.cs` coverage around mobile-compatible project
  behavior.

Covered behavior includes:

- Address type detection.
- Data type guessing.
- Available data types by device kind.
- Vendor-aware trap conditions.
- Trap threshold requirements.
- `MakeProject` limits for mobile maximums.
- `Slugify`.
- Supported and rejected mobile device names.
- Sequential device block generation.
- JSON payload compatibility with Android enum names.
- Rejection of obsolete `PLCIOC2D` QR chunk text.

Effect:

- The most important non-UI behavior is protected outside the WPF code-behind.
- Future UI refactors can rely on the core tests to catch project-format
  regressions.

## Work Intentionally Not Done

- No MVVM rewrite.
- No QR format change.
- No project JSON format change.
- No localization redesign.
- No schema-version compatibility rename for `RequireProjectJsonV2`.
- No broad `dotnet format` enforcement was introduced in the refactor pass.

## Future Notes

- Keep pure project-format behavior in the Core project when possible.
- Keep WPF-specific behavior in partial files only when it genuinely needs UI
  controls.
- Add tests before changing QR chunk generation, project JSON serialization, or
  mobile maximum limits.
- If a future MVVM rewrite is considered, treat it as a separate feature-level
  migration with screenshot and JSON compatibility checks.
