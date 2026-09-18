@echo off
setlocal enabledelayedexpansion
REM ============================================================================
REM gitpush.bat - stage, commit and push MeetVault to GitHub
REM   https://github.com/aojha111/MeetVault.git
REM
REM Usage:
REM   gitpush.bat                  commit all changes with a default message
REM   gitpush.bat "my message"     commit all changes with your message
REM
REM First run: initializes the repo (if needed), sets origin, and pushes master.
REM ============================================================================

cd /d "%~dp0"
set "REPO_URL=https://github.com/aojha111/MeetVault.git"
set "MSG=%~1"
if "%MSG%"=="" set "MSG=Update MeetVault"

REM ── Preflight ──────────────────────────────────────────────────────────────
where git >nul 2>nul || (echo ERROR: git not found in PATH. & exit /b 1)

if not exist .git (
  echo === Initializing git repository ===
  git init -b master || goto :fail
)
git remote get-url origin >nul 2>nul || git remote add origin "%REPO_URL%"

echo.
echo === Changes to be committed ===
git status --short
echo.

set "HAS_CHANGES="
for /f %%i in ('git status --porcelain 2^>nul ^| findstr /r "." ') do set "HAS_CHANGES=1"
if not defined HAS_CHANGES (
  echo Nothing to commit - working tree clean. Pushing any unpushed commits...
  git push -u origin master || goto :push_fail
  goto :done
)

git add -A
git commit -m "%MSG%" || goto :fail

echo.
echo === Pushing to %REPO_URL% ===
git push -u origin master || goto :push_fail

:done
echo.
echo === Done. Repository: https://github.com/aojha111/MeetVault ===
exit /b 0

:push_fail
echo.
echo ERROR: git push failed.
echo   - If the remote already has commits (e.g. a README created on GitHub), run:
echo       git pull origin master --rebase --allow-unrelated-histories
echo     then re-run gitpush.bat
echo   - If authentication is asked for, sign in once in the browser popup
echo     (Git Credential Manager), or run:  gh auth login
goto :fail

:fail
echo.
echo Push FAILED.
exit /b 1
