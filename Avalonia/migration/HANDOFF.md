# Avalonia migration handoff — 2026-10-16

## Workspace and authorization

- Repository: `D:\Development\rustplus-desktop`, branch `beta`; baseline `e1bf6594`.
- All migration work is in `Avalonia/`. Do not edit the original WPF application or unrelated `.serena/`.
- Preserve every shipped feature and nearly the same design. Windows uses FluentAvaloniaUI 3.1.0;
  Avalonia is 12.1.4 on .NET 10. Linux/macOS native appearance remains unfinished.
- Read `PROMPT.md` and `STATUS.md` before continuing. The migration is a preview, not production ready.
- Latest verified source checkpoint: **`d4bba03a` on `beta`**. A following documentation-only commit
  records this handoff. Push target is `origin` (JawadYzbk/rustplus-desktop), never upstream `remote`.
  Check `git log`/remote state before continuation. Only `Avalonia/` is included; unrelated `.serena/`
  and root `rustplus.proto` stay untracked and unchanged.

## Resumed source checkpoints

| Commit | Verified source |
| --- | --- |
| `8e1df49b` | Pairing listener, private/revision-safe storage and confirmed sign-out; 139 tests |
| `9fd4ee6d` | Saved-account startup, delivery correlation and one-shot renewal; 171 tests |
| `a8cf2a48` | Local device aliases/groups/movement/order; 188 tests |
| `61d2eab5` | Offline recycler workspace, 776 inputs and 796 original icons; 195 tests |
| `d4bba03a` | Immediate socket-loss recovery and local alarm-title edits; 199 tests |

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

- Debug/Release builds: zero warnings/errors. `8e1df49` has 139 tests, `9fd4ee6` has 171,
  `a8cf2a4` has 188, `61d2eab` has 195. `d4bba03a` has 199 passing cases in the repository and isolation.
- Source-only external copy with separate package cache: 199 tests pass, zero warnings.
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

The next verified slice adds saved-account startup, successful-sign-in listener restart, random-nonce
push delivery checks, one-shot saved-token registration renewal and a retest. Provider/network errors
remain inconclusive and do not renew; tickets alone never prove arrival. Rejected sign-in prompts the
user without opening Steam automatically. Source changes/shutdown cancel and await checks and renewal.
Account content scrolls while its Close action stays visible. No real Steam or FCM acceptance is claimed.

The device organizer adds local aliases, wrapping a selected subtree in a new group, movement between
groups/root and sibling reordering. Device keys are positional and pinned to SourceRevision. It preserves
whole JSON nodes and empty groups, rejects stale writes, awaits cancellation and reloads/reconnects on close.
Keyboard buttons are implemented; drag gestures, empty-group cleanup, deletion dependencies, device
import/transfer and cloud synchronization are still pending. Do not label full device editing complete.

Recycler: offline reference catalog with 776 inputs/796 bundled icons, isolated calculation, quantities,
filters/paging, fill/clear, pointer/Shift shortcuts and wild/outpost yields/times. It is a workspace tab,
keeps quantities/filter state on close/reopen and leaves the server sidebar available. All required
icons decode in tests; there is no image downloader. See RECYCLER.md for source/hash evidence and gaps.
Original navigation integration, tutorials, localization and native visual acceptance remain required.

Socket disconnects now complete an owned session signal. The connection feature cancels/awaits its
watcher and clock/device/status work before reconnecting; pending reads and late old replies cannot
replace recovered state. The SDK event subscription is removed on disposal. Smart-alarm titles can
also be edited/cleared locally, with device-kind validation and revision-protected storage.

## Exact next steps

1. Validate real Steam/Facepunch return-URL callbacks on Windows. The native window crash is fixed;
   actual Steam authentication and FCM delivery have not been accepted. Do not claim complete sign-in parity.
2. Validate the implemented startup delivery self-test/one-shot registration repair with real FCM.
   Pairing, reconnect, ID persistence, direct re-pair probes and sign-out are implemented; do not redo them.
   Re-sign-in/renewal creates a new registration without deleting earlier ones. Add explicit ambiguous-match resolution.
3. Continue cloud identity/consent and device snapshot/sharing contracts before device import/transfer.
   Reference `Views/MainWindow/Devices/MainWindow.Devices.cs`, `Services/Data/DeviceDataModule.cs`,
   `Models/SharingModels.cs` and `DeviceImportWindow`: the existing Export button toggles consent-based
   cloud sync; Import uses team identities/cloud snapshots with local-overlay and wipe-aware fallback.
   Do not replace that shipped workflow with invented standalone JSON-file export/import.
   Continue drag gestures, deletion/hotkey/automation cleanup, server menus and the original sidebar/
   workspace layout. The full backlog is in STATUS.md/reference checklist.
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
no cloud task was configured. The resumed account/pairing slice is committed as `8e1df49`, startup/health
as `9fd4ee6`, local device organization as `a8cf2a4`, recycler as `61d2eab`, socket teardown/alarm-title
as `d4bba03a`. Inspect Git status/log for the documentation checkpoint. Latest checkpoint usage reading
was 95%; new feature implementation stopped while finishing verified saves. At 97%, stop
implementation, commit/push completed verified work to origin/beta and update this handoff. Leave
unrelated `.serena/` and `rustplus.proto` unchanged. The full production-readiness goal remains unfinished.
