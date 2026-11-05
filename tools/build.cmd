@echo off
setlocal
title RustPlus Desktop - Build
pushd "%~dp0.." || exit /b 1

if not exist ".env" (
    echo Copy .env.example to .env and set your development values first.
    goto failed
)

if not exist "src\RustPlusDesktop\Features\GeneticsLab\node_modules" (
    echo Installing Genetics Lab dependencies...
    call npm --prefix "src\RustPlusDesktop\Features\GeneticsLab" ci
    if errorlevel 1 goto failed
)

echo Building Genetics Lab...
call npm --prefix "src\RustPlusDesktop\Features\GeneticsLab" run build
if errorlevel 1 goto failed

echo Building RustPlus Desktop...
call dotnet build "RustPlusDesk.sln" --configuration Debug --nologo
if errorlevel 1 goto failed

echo.
echo Build complete. Application:
echo "%CD%\src\RustPlusDesktop\bin\Debug\net8.0-windows10.0.17763.0\RustPlusDesk.exe"
popd
pause
exit /b 0

:failed
echo.
echo Build failed. Review the error above.
popd
pause
exit /b 1
