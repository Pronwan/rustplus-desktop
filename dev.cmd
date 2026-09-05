@echo off
REM ============================================================================
REM  Fast dev loop: Hot Reload. Edits to C# method bodies and XAML apply to the
REM  RUNNING app in milliseconds -- no rebuild, no waiting, no exe file locks.
REM
REM  Just run:  dev.cmd
REM
REM  While it's running:
REM    - Save a .cs / .xaml file  -> change is hot-applied instantly.
REM    - "Rude edits" (new fields, changed signatures, new types) can't be
REM      patched live -> the app auto-restarts (fast) instead of prompting.
REM    - Press Ctrl+R in this window to force a manual restart.
REM    - Press Ctrl+C to stop.
REM ============================================================================

REM Auto-restart on rude edits instead of asking.
set DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1

dotnet watch --project "%~dp0RustPlusDesktop\RustPlusDesk.csproj" run --configuration Debug
