@echo off
setlocal enabledelayedexpansion
REM ============================================================================
REM MeetVault one-shot publisher  (https://github.com/aojha111/MeetVault.git)
REM
REM   1. Runs the test suite (aborts on failure)
REM   2. Builds all artifacts: portable folder + zip, single-file EXEs, installer
REM   3. Commits source and pushes to GitHub
REM   4. Creates the v<version> GitHub release with the EXEs attached
REM
REM Usage:
REM   publish.bat                 version defaults to 1.0.0
REM   publish.bat 1.2.0           publish as v1.2.0
REM
REM One-time setup (first run only): gh asks you to sign in via browser.
REM ============================================================================

cd /d "%~dp0"
set "REPO_URL=https://github.com/aojha111/MeetVault.git"
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"
set "TAG=v%VERSION%"
set "MEETVAULT_VERSION=%VERSION%"

echo ============================================================
echo  MeetVault publisher  -  version %VERSION%  tag %TAG%
echo ============================================================

REM ── Preflight ──────────────────────────────────────────────────────────────
where git >nul 2>nul || (echo ERROR: git not found in PATH. & exit /b 1)
where dotnet >nul 2>nul || (echo ERROR: .NET SDK not found in PATH. & exit /b 1)

if not exist .git (
  echo === Initializing git repository ===
  git init -b master || goto :fail
)
git remote get-url origin >nul 2>nul || git remote add origin "%REPO_URL%"

echo.
echo === [1/4] Running tests ===
dotnet test MeetVault.slnx -c Release || goto :fail

echo.
echo === [2/4] Building artifacts ===
call scripts\publish.cmd %VERSION% || goto :fail
call scripts\installer.cmd || goto :fail

set "ASSETS="
if exist "dist\MeetVault-%VERSION%-portable-win-x64.zip" set ASSETS=%ASSETS% dist\MeetVault-%VERSION%-portable-win-x64.zip
if exist "dist\MeetVault-%VERSION%-setup-win-x64.exe" set ASSETS=%ASSETS% dist\MeetVault-%VERSION%-setup-win-x64.exe
if exist "dist\single-file\MeetVault.exe" set ASSETS=%ASSETS% dist\single-file\MeetVault.exe
if exist "dist\single-file\mvault.exe" set ASSETS=%ASSETS% dist\single-file\mvault.exe
if not defined ASSETS (echo ERROR: no artifacts found in dist\. & goto :fail)

echo.
echo === [3/4] Committing and pushing source ===
set "HAS_CHANGES="
for /f %%i in ('git status --porcelain 2^>nul ^| findstr /r "." ') do set "HAS_CHANGES=1"
if defined HAS_CHANGES (
  git add -A
  git commit -m "Release %TAG%" || goto :fail
) else (
  echo Nothing to commit - source already up to date.
)

git rev-parse --verify HEAD >nul 2>nul || (echo ERROR: no commits to push. & goto :fail)
git tag -f %TAG% >nul 2>nul
git push -u origin master || goto :push_fail
git push origin %TAG% || echo WARNING: tag push failed - release will attach to an existing %TAG% or fail later.

echo.
echo === [4/4] Creating GitHub release %TAG% ===
where gh >nul 2>nul && (set "GH=gh") || (call :ensure_gh || goto :fail)
if not defined GH set "GH=%LOCALAPPDATA%\Programs\gh.exe"

"%GH%" auth status >nul 2>nul
if errorlevel 1 (
  echo.
  echo gh needs a one-time login. Running:  gh auth login
  echo    ^(choose: GitHub.com ^> HTTPS ^> Login with a web browser^)
  echo.
  "%GH%" auth login || goto :fail
)

"%GH%" release delete %TAG% --yes --cleanup-tag >nul 2>nul
"%GH%" release create %TAG% %ASSETS% ^
  --title "MeetVault %TAG%" ^
  --notes "Self-contained Windows x64 build - no .NET runtime needed. AI model weights are NOT bundled: the app downloads and SHA-256-verifies the packs you choose on first run (all CPU-only, fits 16 GB RAM). setup = per-user installer; portable zip = unzip and run MeetVault.exe; single-file = standalone EXEs." || goto :fail

echo.
echo ============================================================
echo  Published %TAG%
echo  https://github.com/aojha111/MeetVault/releases/tag/%TAG%
echo ============================================================
exit /b 0

:ensure_gh
if exist "%LOCALAPPDATA%\Programs\gh.exe" (
  set "GH=%LOCALAPPDATA%\Programs\gh.exe"
  exit /b 0
)
echo gh CLI not found - downloading standalone gh.exe per-user...
powershell -NoProfile -Command ^
  "$ErrorActionPreference='Stop';" ^
  "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;" ^
  "$rel=Invoke-RestMethod -Uri 'https://api.github.com/repos/cli/cli/releases/latest';" ^
  "$asset=$rel.assets | Where-Object { $_.name -like 'gh_*_windows_amd64.zip' } | Select-Object -First 1;" ^
  "$zip=Join-Path $env:TEMP 'gh-cli.zip';" ^
  "Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip;" ^
  "Expand-Archive $zip (Join-Path $env:TEMP 'gh-cli') -Force;" ^
  "$exe=Get-ChildItem (Join-Path $env:TEMP 'gh-cli') -Recurse -Filter gh.exe | Select-Object -First 1;" ^
  "New-Item -ItemType Directory -Force (Join-Path $env:LOCALAPPDATA 'Programs') | Out-Null;" ^
  "Copy-Item $exe.FullName (Join-Path $env:LOCALAPPDATA 'Programs\gh.exe') -Force" || (
    echo ERROR: could not download gh. Install it from https://cli.github.com and re-run.
    exit /b 1
  )
set "GH=%LOCALAPPDATA%\Programs\gh.exe"
exit /b 0

:push_fail
echo.
echo ERROR: git push failed.
echo   - If the remote already has commits (e.g. a README created on GitHub), run:
echo       git pull origin master --rebase --allow-unrelated-histories
echo     then re-run publish.bat %VERSION%
echo   - If authentication is asked for, run:  git config --global credential.helper manager
echo     and sign in once, or use:  gh auth login
goto :fail

:fail
echo.
echo Publish FAILED.
exit /b 1
