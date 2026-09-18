@echo off
REM ============================================================================
REM MeetVault standalone build (Windows x64)
REM Produces in dist\:
REM   portable\MeetVault\            self-contained folder layout (app + CLI)
REM   MeetVault-<version>-portable-win-x64.zip
REM   single-file\MeetVault.exe      single-file app (no .NET install needed)
REM   single-file\mvault.exe         single-file CLI
REM No AI model weights are ever bundled - they are downloaded + SHA-256
REM verified from inside the app (Model Manager).
REM ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0.."

set CONFIG=Release
set RID=win-x64
if "%MEETVAULT_VERSION%"=="" set MEETVAULT_VERSION=1.0.0

echo === Publishing self-contained portable layout (%CONFIG%, %RID%) ===
dotnet publish src\MeetVault.App -c %CONFIG% -r %RID% --self-contained true ^
  -p:PublishSingleFile=false -p:PublishTrimmed=false ^
  -o dist\portable\MeetVault || goto :fail
dotnet publish src\MeetVault.Cli -c %CONFIG% -r %RID% --self-contained true ^
  -p:PublishSingleFile=false -p:PublishTrimmed=false ^
  -o dist\portable\MeetVault || goto :fail

REM Portable layout marker: keeps data next to the exe even from Program Files.
type nul > dist\portable\MeetVault\.portable-root

echo === Zipping portable layout ===
if exist dist\MeetVault-%MEETVAULT_VERSION%-portable-win-x64.zip del dist\MeetVault-%MEETVAULT_VERSION%-portable-win-x64.zip
powershell -NoProfile -Command "Compress-Archive -Path 'dist\portable\MeetVault' -DestinationPath 'dist\MeetVault-%MEETVAULT_VERSION%-portable-win-x64.zip'" || goto :fail

echo === Publishing single-file EXEs ===
if exist dist\single-file rmdir /s /q dist\single-file
dotnet publish src\MeetVault.App -c %CONFIG% -r %RID% --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false -o dist\single-file || goto :fail
dotnet publish src\MeetVault.Cli -c %CONFIG% -r %RID% --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false -o dist\single-file-cli || goto :fail
move /y dist\single-file-cli\mvault.exe dist\single-file\mvault.exe >nul
rmdir /s /q dist\single-file-cli

echo.
echo === Done. Artifacts: ===
dir /b dist
echo   portable\MeetVault\MeetVault.exe      (self-contained folder app)
echo   portable\MeetVault\mvault.exe         (self-contained folder CLI)
echo   MeetVault-%MEETVAULT_VERSION%-portable-win-x64.zip
echo   single-file\MeetVault.exe, single-file\mvault.exe
goto :eof

:fail
echo Publish FAILED. & exit /b 1
