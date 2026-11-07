@echo off
cd /d "%~dp0"
dotnet build src\BotNotes\BotNotes.csproj -c Release
if errorlevel 1 (
  echo Build failed. BotNotes.exe was not copied.
  pause
  exit /b 1
)
copy /y "src\BotNotes\bin\Release\net48\BotNotes.exe" "BotNotes.exe" >nul
if errorlevel 1 (
  echo Copy failed. BotNotes.exe was not copied.
  pause
  exit /b 1
)
echo Built and copied BotNotes.exe to the repository root.
pause
