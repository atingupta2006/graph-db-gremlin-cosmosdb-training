@echo off
setlocal

REM ============================================================
REM  github.bat — Push to GitHub with clean history
REM  1. Removes .gitignore'd files from tracking & full history
REM  2. Squashes all commits into a single fresh commit
REM  3. Force-pushes to origin/master
REM ============================================================

echo.
echo === Step 1: Remove .gitignore'd files from Git tracking ===
git rm -r --cached .
git add .
git commit -m "chore: remove ignored files from tracking"

echo.
echo === Step 2: Squash entire history into one commit ===
REM Create a new orphan branch (no parent commits)
git checkout --orphan temp_branch

REM Stage all currently-tracked files
git add .

REM Create a single commit with the current state
git commit -m "update"

REM Replace master with this clean branch
git branch -D master
git branch -m master

echo.
echo === Step 3: Force-push to GitHub ===
git push --force origin master

echo.
echo Done. GitHub now has a single commit with no ignored files in history.
endlocal
