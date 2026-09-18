@echo off
rem Build script: produces a Release build and a portable package layout under dist\.
rem Model weights are intentionally NOT included; users install them via Model Manager.

setlocal
cd /d "%~dp0"

echo === Building solution (Release) ===
dotnet build MeetVault.slnx -c Release || goto :fail

echo === Running tests ===
dotnet test MeetVault.slnx -c Release --no-build || goto :fail

set DIST=dist\MeetVault-Portable
if exist dist rmdir /s /q dist
mkdir "%DIST%" || goto :fail

echo === Staging portable layout ===
xcopy src\MeetVault.App\bin\Release\net10.0-windows\* "%DIST%\" /e /i /q /y || goto :fail

rem Pre-create the portable data skeleton.
for %%D in (runtime models data config logs) do mkdir "%DIST%\%%D" 2>nul
for %%D in (whisper llm tts) do mkdir "%DIST%\models\%%D" 2>nul

rem Keep the CLI next to the app for headless use.
copy /y src\MeetVault.Cli\bin\Release\net10.0-windows\MeetVault.Cli.exe "%DIST%\" >nul
copy /y src\MeetVault.Cli\bin\Release\net10.0-windows\MeetVault.Cli.dll "%DIST%\" >nul
copy /y src\MeetVault.Cli\bin\Release\net10.0-windows\MeetVault.Cli.runtimeconfig.json "%DIST%\" >nul

echo === Package ready: %DIST% ===
echo Model packs are downloaded separately through Model Manager (or: MeetVault.Cli packs / install).
exit /b 0

:fail
echo Build or packaging FAILED.
exit /b 1
