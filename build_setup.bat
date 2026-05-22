@echo off
title Build Rust+ Desktop Installer
cd /d "%~dp0"

echo ==============================================
echo 1. Publishing application in Release configuration...
echo ==============================================
dotnet publish "%~dp0RustPlusDesktop\RustPlusDesk.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0RustPlusDesktop\bin\Installer\publish"
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Dotnet publish failed!
    echo ==============================================
    pause
    exit /b %errorlevel%
)

echo.
echo ==============================================
echo 2. Locating Inno Setup Compiler (ISCC)...
echo ==============================================

set "ISCC_PATH="

:: Check common installation paths
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 5\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 5\ISCC.exe"
)

:: If not found in standard paths, try checking PATH
if "%ISCC_PATH%"=="" (
    where iscc >nul 2>nul
    if %errorlevel% eq 0 (
        set "ISCC_PATH=iscc"
    )
)

if "%ISCC_PATH%"=="" (
    echo.
    echo [WARNING] Inno Setup Compiler (ISCC.exe) was not found on your system.
    echo.
    echo To build the setup installer (RustPlusDesk-Setup.exe):
    echo 1. Download and install Inno Setup 6 from:
    echo    https://jrsoftware.org/isdl.php
    echo 2. Once installed, run this build_setup.bat script again, OR:
    echo 3. Open "%~dp0RustPlusDesktop\Installer\Setup.iss" using the Inno Setup GUI and press F9.
    echo.
    echo NOTE: The compiled files are already ready in:
    echo       "%~dp0RustPlusDesktop\bin\Installer\publish\"
    echo ==============================================
    pause
    exit /b 0
)

echo Found ISCC at: "%ISCC_PATH%"
echo.
echo ==============================================
echo 3. Compiling Installer...
echo ==============================================
"%ISCC_PATH%" "%~dp0RustPlusDesktop\Installer\Setup.iss"
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Inno Setup compilation failed!
    echo ==============================================
    pause
    exit /b %errorlevel%
)

echo.
echo ==============================================
echo [SUCCESS] Installer generated successfully!
echo Location: "%~dp0RustPlusDesktop\bin\Installer\RustPlusDesk-Setup.exe"
echo ==============================================
pause
