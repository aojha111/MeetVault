@echo off
setlocal
cd /d "c:\MyProjects\MeetVault"
echo === winget ===
where winget 2>nul || echo "winget NOT found"
echo === stored credentials (cmdkey) ===
cmdkey /list 2>nul | findstr /i "github" || echo "(no github credential entries in cmdkey)"
echo === non-interactive push dry-run auth probe ===
set GIT_TERMINAL_PROMPT=0
git push --dry-run origin master 2>&1 | findstr /i "denied\|forbidden\|authentication\|up-to-date\|Everything\|error:" || echo "(probe inconclusive)"

