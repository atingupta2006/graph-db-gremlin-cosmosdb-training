@echo off
REM Kill any running processes for the training/seed projects so you can rebuild.
REM Run from repo root or from project folder: project\kill-processes.bat

echo Stopping any running project processes...
taskkill /IM GremlinTraining.exe /F >nul 2>&1
taskkill /IM GremlinFluent.exe /F >nul 2>&1
taskkill /IM GremlinSeed.exe /F >nul 2>&1
taskkill /IM GremlinSeedContinuous.exe /F >nul 2>&1
echo Done. You can build again.
exit /b 0
