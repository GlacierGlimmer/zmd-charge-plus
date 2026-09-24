@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo  Endfield Charge Plus v0.1.0 - x64 Release Test Build
echo ========================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Runtime win-x64
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" (
    echo x64 Release test build FAILED with exit code %EXITCODE%.
) else (
    echo x64 Release test build completed successfully.
    echo Output:
    echo   %~dp0dist\Portable\EndfieldChargePlus-v0.1.0-win-x64-portable.exe
)
pause
exit /b %EXITCODE%
