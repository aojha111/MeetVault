@echo off
REM ============================================================================
REM Builds the MeetVault installer EXE with Inno Setup 6.
REM Prerequisites: run scripts\publish.cmd first. Inno Setup 6 is located in
REM   the standard install paths or on PATH; set ISCC to its full path to
REM   override.
REM Output: dist\MeetVault-<version>-setup-win-x64.exe
REM ============================================================================
setlocal
cd /d "%~dp0.."

if "%MEETVAULT_VERSION%"=="" set MEETVAULT_VERSION=1.0.0

if not exist dist\portable\MeetVault\MeetVault.exe (
  echo dist\portable\MeetVault\ not found. Run scripts\publish.cmd first.
  exit /b 1
)

set ISCC_EXE=
for %%P in (
  "%ISCC%"
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
  "C:\Program Files\Inno Setup 6\ISCC.exe"
  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
) do (
  if not defined ISCC_EXE if exist %%P set ISCC_EXE=%%~P
)

if not defined ISCC_EXE (
  echo Inno Setup 6 not found. Installing per-user via winget...
  winget install --id JRSoftware.InnoSetup -e --scope user --accept-source-agreements --accept-package-agreements
  if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
  ) else (
    echo Could not locate ISCC.exe after install. Add Inno Setup 6 to PATH or set ISCC.
    exit /b 1
  )
)

echo === Compiling installer with "%ISCC_EXE%" ===
"%ISCC_EXE%" /DMEETVAULT_VERSION=%MEETVAULT_VERSION% scripts\installer.iss || exit /b 1

echo.
echo === Done: dist\MeetVault-%MEETVAULT_VERSION%-setup-win-x64.exe ===
