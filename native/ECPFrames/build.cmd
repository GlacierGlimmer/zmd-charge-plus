@echo off
setlocal
set "ECP_ARCH=%~2"
if not defined ECP_ARCH set "ECP_ARCH=x64"
if /i "%ECP_ARCH%"=="arm64" (
  set "ECP_VCVARS=x64_arm64"
) else if /i "%ECP_ARCH%"=="x64" (
  set "ECP_VCVARS=x64"
) else (
  echo Supported collector architectures: x64, arm64
  exit /b 2
)
if "%~1"=="" (
  py -3 "%~dp0prepare.py"
) else (
  "%~1" "%~dp0prepare.py"
)
if errorlevel 1 exit /b 1
set "ECP_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "tokens=*" %%i in ('"%ECP_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath') do set "ECP_VS=%%i"
if not defined ECP_VS exit /b 2
call "%ECP_VS%\VC\Auxiliary\Build\vcvarsall.bat" %ECP_VCVARS% >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0build"
cl /nologo /Brepro /std:c++20 /O2 /MT /DNDEBUG /DNOMINMAX /DUNICODE /D_UNICODE /EHsc ECPFrames.cpp vendor\PresentData\*.cpp /Fe:ECPFrames-%ECP_ARCH%.exe /link /Brepro advapi32.lib tdh.lib user32.lib
exit /b %errorlevel%
