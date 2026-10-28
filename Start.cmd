@echo off
if exist "%~dp0publish\releases\TimeManagementMaster-1.1.0-win-x64\TimeManagementMaster.exe" (
    start "" "%~dp0publish\releases\TimeManagementMaster-1.1.0-win-x64\TimeManagementMaster.exe"
    exit /b
)
if exist "%~dp0publish\TimeManagementMaster\TimeManagementMaster.exe" (
    start "" "%~dp0publish\TimeManagementMaster\TimeManagementMaster.exe"
    exit /b
)
echo Release build not found. Run build-release.ps1 first.
pause
exit /b 1
