# Avalonia migration handoff — 2026-10-16

## Workspace and authorization

- Repository: `D:\Development\rustplus-desktop`, branch `beta`; baseline `e1bf6594`.
- All migration work is in `Avalonia/`. Do not edit the original WPF application or unrelated `.serena/`.
- Preserve every shipped feature and nearly the same design. Windows uses FluentAvaloniaUI 3.1.0;
  Avalonia is 12.1.4 on .NET 10. Linux/macOS native appearance remains unfinished.
- Read `PROMPT.md` and `STATUS.md` before continuing. The migration is a preview, not production ready.
- Verified preview and crash fix are committed in `1cb470b` on `beta`. A following documentation
  commit records this handoff. Push target is `origin` (JawadYzbk/rustplus-desktop), never upstream `remote`.
  Check `git log`/remote state before cloud continuation. Only `Avalonia/` is included; `.serena/` stays untracked.

## Latest resolved bug

Clicking Account → Sign in crashed in native Windows child-window creation. Windows Application
event 1026 showed `InvalidOperationException: Unable to create child window for native control host`.
The executable lacked a supported-OS compatibility manifest. Added `src/RustPlusDesk.Avalonia/app.manifest`
and `ApplicationManifest` to its project. Debug, Release and self-contained Windows executables were
checked for the embedded compatibility declaration. Do not remove it or replace window hosting blindly.

`tools/native-login-smoke/` exercises the actual production browser adapter with a local HTTP redirect,
then cancellation. Without the manifest it reproduced the exact crash; with it both checks passed.
WebView2 logged a Chrome class-unregister warning at shutdown, but the smoke exited 0 and did not crash.
The checks never used personal credentials or contacted Steam/Facepunch.

## Verified current source

- Debug/Release builds: zero warnings/errors. All 139 MSTest tests pass after local auto-resume.
- Source-only external copy with separate package cache: 139 tests pass, zero warnings.
- Self-contained Release publishes on Windows: win-x64, linux-x64, osx-arm64 succeed.
- Native Windows browser callback and cancellation smoke passes. Native Linux/macOS executables untested.
- Two Python reference-capture tests passed in the earlier slice; its code has not changed since.
- External copy location is recorded locally in ignored `artifacts/isolation-path.txt`; its source copy
  includes current account/listener source; final documentation can be newer than that copy.
- Rebuilt app: `src/RustPlusDesk.Avalonia/bin/Release/net10.0/RustPlusDesk.Avalonia.exe`.
- Self-contained Windows app: `artifacts/publish/win-x64/RustPlusDesk.Avalonia.exe`.

## Working preview

Saved-server browsing/editing with revision protection/backups, socket/direct connections and reconnect,
live server clock/status, confirmed device/group switches, alarm/storage state and subscriptions;
account modal with safe status, native browser sign-in, companion verification, FCM registration,
schema-preserving private credential writes, cancellation and failed-save registration rollback.
The resumed slice adds the FCM listener and recent push feed, server/device pairing and verified legacy
re-pair, metadata backfill, shared private atomic profile writes, and confirmed current/global sign-out.
Persistent IDs are scoped to the current push device and bounded to 1,000 entries; the in-memory feed
keeps 100 entries. Sign-in/out and source changes await the listener and clear the previous account feed.
Unknown JSON is preserved; tokens are excluded from presentation and logs.

## Exact next steps

1. Validate real Steam/Facepunch return-URL callbacks on Windows. The native window crash is fixed;
   actual Steam authentication and FCM delivery have not been accepted. Do not claim complete sign-in parity.
2. Continue original startup push-health/self-test and automatic registration repair. Pairing, reconnect,
   ID persistence, direct re-pair probes and sign-out are now implemented; do not redo them. Re-sign-in
   creates a new registration without deleting earlier ones. Add explicit ambiguous-match resolution later.
3. Continue device editing/import/transfer/automation, original server menus/cloud-aware deletion,
   and the original sidebar/workspace layout. The full required backlog is in STATUS.md/reference checklist.
4. Run native Windows/Linux/macOS acceptance, including permissions, browser prerequisites and appearance.
5. Keep source upload-ready without parent-repo/private-submodule references. Exclude bin/obj/artifacts.

## Continuation commands (run from Avalonia)

```text
rtk proxy dotnet build RustPlusDesk.CrossPlatform.sln -c Release
rtk proxy dotnet test RustPlusDesk.CrossPlatform.sln -c Release
rtk proxy dotnet run --project tools/native-login-smoke/Smoke.csproj -c Release
```

Use graph discovery first (project `D-Development-rustplus-desktop`, Tier 2), then coverage checks.
Recent auth files had unavailable/stale graph coverage; exact source and runnable checks were used.
Local SDK reference `D:\Development\RustPlusApi` is unindexed. No exhaustive dead-code audit is claimed.

The one-time local automation `resume-avalonia-migration-after-usage-reset` ran at 17:25 Beirut on
2026-10-16. Usage reset to 0% and implementation resumed. The PC must remain awake with Codex open;
no cloud task was configured. Current source changes after `3c36abf` are the resumed account/pairing
slice; inspect Git status/log for its checkpoint commit. Most recent usage check: 35%; at 97%, stop
implementation, commit/push completed verified work to origin/beta and update this handoff. Leave
unrelated `.serena/` and `rustplus.proto` unchanged. The full production-readiness goal remains unfinished.
