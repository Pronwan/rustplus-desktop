@echo off
setlocal
title RustPlus Desktop - Build
pushd "%~dp0.." || exit /b 1

if not exist ".env" (
    echo Copy .env.example to .env and set your development values first.
    goto failed
)

REM Incremental: the csproj builds Genetics Lab (npm ci + vite) only when its sources changed,
REM and regenerates secrets / re-publishes MapParser only when their inputs changed.
REM   (default)  build only the app project (skips the test project)
REM   --all       build the whole solution
REM   --clean     force a full, non-incremental rebuild
set "BUILD_TARGET=src\RustPlusDesktop\RustPlusDesk.csproj"
set "BUILD_ARGS=--configuration Debug --nologo -v:minimal -m"
:parse
if "%~1"=="" goto run
if /i "%~1"=="--all" set "BUILD_TARGET=RustPlusDesk.sln"
if /i "%~1"=="--clean" set "BUILD_ARGS=%BUILD_ARGS% --no-incremental"
shift
goto parse

:run
echo Building RustPlus Desktop...
call dotnet build "%BUILD_TARGET%" %BUILD_ARGS%
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
