# Source provenance

This folder contains a migration of Rust+ Desktop. Preserve the accompanying GNU GPL version 3 license
and upstream attribution when copying or distributing it.

- Original project: <https://github.com/Pronwan/rustplus-desktop>
- Working repository: <https://github.com/JawadYzbk/rustplus-desktop>
- Source baseline for this migration: `e1bf6594` on `beta` (2026-10-16).
- `src/RustPlusDesk.Core/Time/ServerClock.cs` is copied from `RustPlusDesktop/Services/ServerClock.cs`;
  its algorithm is unchanged, with a new namespace, public visibility, and a type summary.
- `tests/RustPlusDesk.CrossPlatform.Tests/ServerClockTests.cs` preserves the existing five clock tests
  with namespace/import changes.
- `src/RustPlusDesk.Avalonia/Assets/app.ico` is the supplied `RustPlusDesktop/Assets/rustplus-desktop-icon.ico`.
- Legacy profile field names/defaults and the default data directory follow the existing
  `Models/ServerProfile.cs`, `Services/Data/ProfileDataModule.cs`, and `Services/StorageService.cs`.
- Application palette values follow `App.xaml` and `Views/MainWindow/SharedResources.xaml`.
- Device kind aliases, nested switch groups and confirmation behavior follow `Models/SmartDevice.cs`
  and `Services/RustPlusClientReal.cs`. The storage adapter treats entity `not_found` independently of login denial.
- `src/RustPlusDesk.Infrastructure/Assets/rust-item-list.json` is copied unchanged from
  `RustPlusDesktop/Assets/Data/rust-item-list.json`; it is embedded for offline item names.
- Editable server fields, blank-name handling and the address/device probe follow
  `Views/Windows/EditServerConnectionDialog.xaml.cs`. Unknown fields are preserved by the new writer.
- Windows control theming uses the MIT-licensed `FluentAvaloniaUI` 3.1.0 by amwx;
  upstream: <https://github.com/amwx/FluentAvalonia>. See `licenses/FluentAvalonia.LICENSE`.
  Avalonia remains pinned to 12.1.4.
- Connection routing, denial classification, numeric credential parsing, and 2–60 second reconnect
  backoff follow `Services/RustPlusClientReal.cs` and `Views/MainWindow/Connection/MainWindow.Connection.Reset.cs`.
- The companion auth reader uses the existing `rustplusjs-config.json` / `rustplus_auth_token` storage
  convention without requiring or copying the proprietary authentication provider.
- New sign-in reimplements the existing companion HTTP protocol and configuration schema; it uses
  the public MIT-licensed `RustPlusApi.Fcm.Registration` 2.0.0-beta.9 for push credentials/registration,
  and MIT-licensed `Avalonia.Controls.WebView` 12.1.1 for native browser authentication. No private
  authentication-provider source is copied. The Windows compatibility declaration follows the
  official Avalonia desktop template: <https://github.com/AvaloniaUI/avalonia-dotnet-templates>.
- Push classification, sent-time normalization, pairing identity/device matching and re-pair verification
  follow `Services/NativeFcmListener.cs`, `MainWindow.xaml.cs` and `Services/RustPlusConnectionVerifier.cs`.
  They are reworked into a portable stream and shared schema-preserving storage. The same public MIT
  `RustPlusApi.Fcm` dependency supplies the MCS transport; no private authentication provider is required.
- Push self-test and one-shot renewal preserve the workflows in `Services/FcmSelfTestService.cs` and
  `Views/MainWindow/Connection/MainWindow.Connection.FcmHealth.cs`. Registration uses the public SDK
  with the saved companion token; no private repair-provider source is copied. Delivery is confirmed
  only by a correlated push arriving at the selected profile source's listener.
- Local alias/group organization follows `Models/SmartDevice.cs` and `Views/MainWindow/Devices/MainWindow.Devices.cs`.
  The portable writer moves original JSON nodes and pins positional keys to the source revision, retaining
  unknown settings. Keyboard-accessible buttons are implemented; the original drag/drop gestures,
  empty-group cleanup, deletion dependencies and cloud synchronization are not yet ported.
- `packages-local/RustPlusApi.2.0.0-beta.9.socket.1.nupkg` is copied unchanged from the reference
  repository's local feed. It is MIT-licensed (HandyS11 / Clergue Valentin); see `packages-local/RustPlusApi.LICENSE`.
  SHA-256: `DBEAE2BF1241D6F77C2EF10493FC274306393ED2910923C178DC0F60822343BF`.
- The recycler calculation/workflows follow `Views/MainWindow/Map/RecyclerOverlay.xaml(.cs)`.
  `Assets/Data/recycler-items.json` and `Assets/Data/Recycling-Data.json` are copied unchanged into
  Infrastructure. The 796 required item icons are copied unchanged from `Assets/icons/items` into the
  desktop host and keyed by short name. `migration/RECYCLER.md` records source hashes and coverage.
- `migration/reference/manifest.json` records hashes and UI metadata from the reference source tree;
  it includes checked-in/locally present source candidates and is not proof of feature reachability.

The original implementation stays in place during migration. When a feature is fully ported and the
old host is retired, remove its superseded implementation after the parity checks in `migration/PROMPT.md`.
