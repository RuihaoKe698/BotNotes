# BotNotes

WPF note app for Windows, C# with no XAML. Formerly NotchNotes for Windows, then DotNotes. The user speaks Chinese; user-visible text is Chinese.

## Build and test

- Solution: `BotNotes.sln`; app project `src/BotNotes/BotNotes.csproj` (SDK-style, target `net48`, WPF + WinForms), tests `tests/BotNotes.Tests/BotNotes.Tests.csproj`.
- Build: `build.cmd`, or `dotnet build BotNotes.sln -c Release`. `build.cmd` runs `dotnet build src\BotNotes\BotNotes.csproj -c Release`, then copies `src\BotNotes\bin\Release\net48\BotNotes.exe` to the repo root as `BotNotes.exe`; it does not copy when the build fails.
- Test: `dotnet test BotNotes.sln -c Release`.
- Output is `BotNotes.exe` in the app project folder (`src/BotNotes/bin/Release/net48/BotNotes.exe`). `BotNotes.ico` is both the win32 icon and an embedded manifest resource named exactly `BotNotes.ico` (the code calls `GetManifestResourceStream("BotNotes.ico")`).
- The app must run on the .NET Framework 4.8 built into Windows. Never require a new runtime for users. Reference assemblies come from `Microsoft.NETFramework.ReferenceAssemblies`.
- Source files keep their UTF-8 BOM.

## Rules

- Do not change user-visible text, data file names (`note.md`, `settings.ini`, `order.txt`, `shelf.txt`, the `archive` folder), the data folder name (`%LOCALAPPDATA%\BotNotes`), mutex names, or registry value names.
- Never write to the real data folder `%LOCALAPPDATA%\BotNotes` or the old ones (`DotNotes`, `NotchNotesWindows`) in tests. Point `MainWindow.LocalRoot` at a temp directory.
- Never touch the real autostart keys (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and `...\Explorer\StartupApproved\Run`) in tests. Use a throwaway key under `HKCU\Software\BotNotesTests\<guid>` and delete it afterwards.
- Do not start BotNotes on the user's desktop, do not send synthetic mouse or keyboard input, and do not kill a running BotNotes. Test in-process.
- When run from the Claude desktop app (an MSIX package), `%LOCALAPPDATA%` for child processes is redirected to `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\...`.

## Docs

- `DESIGN.md`: design decisions and the visual system.
- `使用说明.md`: user guide (Chinese), including how to build.
