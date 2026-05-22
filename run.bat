@echo off
title Run Rust+ Desktop
echo ==============================================
echo Running Rust+ Desktop...
echo ==============================================
dotnet run --project RustPlusDesktop\RustPlusDesk.csproj
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Failed to run the project. Make sure it builds correctly first.
    echo ==============================================
    pause
    exit /b %errorlevel%
)
