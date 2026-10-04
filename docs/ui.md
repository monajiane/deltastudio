# WinUI 3 app

`src/DeltaStudio.App` — MVVM (CommunityToolkit.Mvvm), generic host DI (`AddDeltaStudioEngine` +
UI services), no engineering logic in code-behind. The window composes four surfaces over the
engine: **Project tree · Ladder editor · Online monitor · Output/Diagnostics/Listings** with a
menu/toolbar routing to the same commands the MCP tools run.

## Ladder editor (real, not a mock)

Each rung renders from the **semantic IR**: contact chips (`─├┤─` NO / `─┤/├─` NC / edge-triggered),
coil chips (`─( )─`/`(S)`/`(R)`), instruction blocks with operands, and parallel groups as
vertically stacked branch strips — a `DataTemplateSelector` over `ContactViewModel/CoilViewModel/
BlockViewModel/ParallelViewModel`, which `RungViewModel` rebuilds from `SeriesNetwork`/
`ParallelNetwork` nodes after every mutation. Below each chip strip: the IL preview produced by
the *same* `IlFormatter` the compiler uses (WYSIWYG by construction: the UI never re-parses, it
formats). Editing = selecting a rung + toolbar (+NO/+NC/+UP/+OUT/SET/RST, add/move/delete rung);
every edit goes through `Workspace.Mutate` → validator gates it. Undo/redo are IR snapshots.

## Theming & localization

- **Dark/Light** via `ThemeService` → `ElementTheme` on the root (follows Windows by default).
- **English / فارسی** via `LocalizationService` swapping merged `Resources/Strings.{en,fa}.xaml`;
  the shell flips to **RTL** (`FlowDirection.RightToLeft`) in Persian.
- The **ladder surface is pinned LTR** even in Persian UI (`LadderEditorView.xaml` sets
  `FlowDirection=LeftToRight`) — ladder reading order is an engineering convention; only chrome
  mirrors. Monospace chips keep Latin mnemonics/device names in both languages.

## Status & limits (honest list)

See [feature-status.md](feature-status.md) "WinUI 3 app" table. Key admits: drag-and-drop element
placement is NOT_IMPLEMENTED (toolbar editing is PARTIAL replacement); parallel-branch insertion
is only in the AI/MCP path today; live monitor refreshes the watch list but does not yet recolor
individual chips; **the app was compiled-by-shim on Linux** — full XAML compile + interactive smoke
still need a Windows run (`dotnet build DeltaStudio.sln` on Windows, then F5 `DeltaStudio.App`).

## Building on non-Windows hosts

`Directory.Build.props/.targets` in the project turn it into an empty classlib on Linux/macOS so
CI/solution builds stay green (WinUI's `XamlCompiler.exe` is a Windows-only toolchain piece).
On Windows the same project builds the full app (`WindowsPackageType=None`, unpackaged).

## Prerequisites for the Windows build

The full app build needs the **.NET 8 SDK** plus the **Windows 10 SDK** (the `net8.0-windows10.0.19041.0`
target framework and the XAML markup compiler `XamlCompiler.exe` both resolve Windows SDK reference
assemblies under `C:\Program Files (x86)\Windows Kits\10`). Without the Windows SDK the App project
fails at `MarkupCompilePass1` with `XamlCompiler.exe exited with code 1`; every other project and all
tests still build and pass, so the SDK is only needed when you actually want the GUI binary.

