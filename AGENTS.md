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

- Do not change user-visible text, the app data files (`settings.ini`, `order.txt`, `folds.txt`, `shelf.txt`（旧版文件暂存，现已不用，保留不删）, `history\`, `logs\crash.log`, `backup-迁移前-*`), the data folder name (`%LOCALAPPDATA%\BotNotes`), mutex names, or registry value names. Notes live as real files in the save location (default `文档\BotNotes`); the data folder contains only the app's own files.
- Never write to the real data folder `%LOCALAPPDATA%\BotNotes` or the old ones (`DotNotes`, `NotchNotesWindows`) in tests. Point `MainWindow.LocalRoot` at a temp directory.
- Never write to the real “文档” folder in tests. Point `MainWindow.DocumentsRoot` at a temp directory so the default save location is temporary.
- Never touch the real autostart keys (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and `...\Explorer\StartupApproved\Run`) in tests. Use a throwaway key under `HKCU\Software\BotNotesTests\<guid>` and delete it afterwards.
- Do not start BotNotes on the user's desktop, do not send synthetic mouse or keyboard input, and do not kill a running BotNotes. Test in-process.
- When run from the Claude desktop app (an MSIX package), `%LOCALAPPDATA%` for child processes is redirected to `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\...`.

## Docs

- `DESIGN.md`: design decisions and the visual system.
- `使用说明.md`: user guide (Chinese), including how to build.
