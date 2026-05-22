@echo off
title Build Rust+ Desktop
echo ==============================================
echo Building Rust+ Desktop...
echo ==============================================
dotnet build
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed!
    echo ==============================================
    pause
    exit /b %errorlevel%
)
echo.
echo [SUCCESS] Build completed successfully!
echo ==============================================
pause
