# Rust+ Desktop — Avalonia migration

This is the independent cross-platform successor to Rust+ Desktop, under active migration.
It is a working preview, not a feature-complete replacement for the Windows app.
The destination is **every shipped feature with nearly the same design**. No reduced feature set or
general redesign is authorized. [The UI parity checklist](migration/reference/UI-CHECKLIST.md) tracks
reference screens/components; `migration/STATUS.md` distinguishes working behavior from pending ports.

## Build and run

Install the .NET 10 SDK. Run these commands **from this folder**:

```sh
dotnet build RustPlusDesk.CrossPlatform.sln --configuration Release
dotnet test tests/RustPlusDesk.CrossPlatform.Tests --configuration Release
dotnet run --project src/RustPlusDesk.Avalonia
```

The preview automatically tries the existing `RustPlusDesk/profiles.json` file in the operating system's
application-data directory. Use **Open profiles…** to choose a copied file from another machine.
Files are read in place. Browsing does not connect automatically or change the file.
The preview displays server names, hosts, and ports. It does not display or retain pairing tokens in UI models.

Select a server and press **Connect** to authenticate and display live population, queue, in-game time,
and day/night countdown. **Disconnect**, selecting another server, refreshing profiles, or closing the
window stops the session and polling. A dropped connection retries with the existing app's 2–60 second
exponential backoff. Re-pair and companion-login failures show distinct recovery instructions.

The companion route uses the existing `rustplusjs-config.json` next to the chosen profiles file,
when available, followed by the direct route. **Account → Sign in with Steam** opens the native web
authentication dialog and verifies the companion token before registering a new push device and saving
credentials beside the chosen profiles file. Canceling or failing sign-in preserves existing credentials.
Successful writes preserve unknown configuration fields and restrict the credential file to the current user.
Use **Start listening** in Account, then pair a server or smart device from Rust. The listener saves
pairings into the selected profiles file, preserves nested groups/settings, and refreshes the server list.
It keeps running after the account panel closes; **Stop listening**, a profile-source change, sign-in,
sign-out or app shutdown stops and awaits it. Temporary socket failures retry with 3–60 second backoff.
Recent pairing, alarm, team-chat and offline-death pushes appear in the account panel with their sent time.
This feed keeps the newest 100 entries in memory; the full notification center, sounds and durable inbox remain pending.

**Sign out this device** unregisters its push registration. **Sign out all devices** invalidates the saved
companion token across devices using it. Both require confirmation, stop owned sessions, preserve paired
servers and unknown configuration fields, and report remote/local partial completion. Startup push-health
checks and automatic registration repair remain pending. Live Steam/Facepunch/FCM acceptance still needs verification;
automated checks use local callbacks, synthetic credentials and fake transports.

Paired smart switches, nested groups, alarm state and storage contents load after connecting.
ON/OFF commands wait for a confirmed state; failed or missing devices do not invalidate the server login.
Storage uses the bundled Rust item names, including quantity, blueprint and upkeep information.
Automation, device editing, alarm audio and the rest of the original device tools remain pending.

Use **Edit…** for the selected server's name, host and companion port. The direct-address test uses its
existing credentials and checks paired devices without saving. **Save changes** preserves unknown JSON
fields, credentials, nested devices and other profiles, keeps an exact `.bak` beside the original file,
and atomically replaces that file. A changed source revision is rejected; refresh before editing again.
Backups contain the same sensitive pairing data as the original and should remain in its protected folder.
Keep other applications that write this file closed while saving; unrelated atomic-renaming writers cannot
participate in the editor's file lock. Deletion remains pending with its original cloud-cleanup behavior.
Incoming pairings share the editor's writer and create protected backups only when data changes. Server-ID
matches survive address changes. Legacy matches use direct device verification or the original unique-host
fallback; ambiguous/conflicting matches are reported and kept unchanged. Alarm/chat metadata can backfill
missing Facepunch server IDs without replacing pairing credentials or custom server descriptions.

## Structure

```text
src/
  RustPlusDesk.Core/             Portable domain logic and contracts; no UI dependencies
    Servers/                    Credential-free server summaries and catalog contract
    Connections/                Session contract, status snapshots and classified failures
    Devices/                    Portable definitions, snapshots and confirmed-control contract
    Time/                       Existing, tested server-clock calibration
    Accounts/                   Account status and sign-in/browser contracts
  RustPlusDesk.Infrastructure/   File-system and external-service implementations
    Profiles/                   Read-only legacy JSON adapter
                                Schema-preserving editor with backup/revision protection
    Connections/                Companion/direct routing, credential reads and Rust+ transport
    Items/                      Bundled item-name catalog
    Accounts/                   Companion verification, registration, push sessions and sign-out
    Storage/                    Shared private atomic writes with exact revision checks
  RustPlusDesk.Avalonia/         Desktop host and composition root
    Features/Servers/           Feature view and MVVM view model
    Features/Connections/       Owned session, cancellation, recovery, live clock and status view
    Features/Devices/           Groups, confirmed switch control, alarms and storage presentation
    Features/Accounts/          Account dialog, push feed, confirmation and owned cancellation
    Shared/Controls/            Reusable status panel
    Styles/                     Shared visual tokens and component styles
    Services/                   Native file-picker and authentication browser adapters
    Shell/                      Small application window
tests/
  RustPlusDesk.CrossPlatform.Tests/
migration/
  PROMPT.md                     Execution brief for continuing the migration
  STATUS.md                     Verified progress, remaining work, and acceptance gates
```

Dependencies point inward: Infrastructure → Core; the desktop composition root wires both into the UI.
View models use constructor-injected contracts and CommunityToolkit.Mvvm's observable base class and async commands.
Views use compiled bindings. Code-behind only initializes views; application composition is in `App`.
Use composition for shared behavior and inheritance for actual control/view-model relationships.
Do not rebuild the monolithic WPF window as a hierarchy of partial classes.

## Standalone upload

Copy this folder's source contents to the root of a new repository, including `.github`, `.gitignore`,
`nuget.config`, `Directory.Build.props`, `global.json`, `LICENSE`, and `NOTICE.md`.
Exclude `bin`, `obj`, `TestResults`, and `artifacts`. There are no project, asset, package-source, or build
references to the parent repository, and no private submodule or secrets are required for this preview.
Include `packages-local/`: it contains the same MIT-licensed RustPlusApi companion-socket package
used by the reference application, plus its license. The standalone NuGet configuration resolves it
locally. Runtime connections require the user's existing pairing credentials; builds and tests do not.

`.github/workflows/build.yml` is ready for that repository root. GitHub does **not** run it while this folder
is nested inside the existing repository. It builds, tests, and creates preview artifacts on Windows,
Linux, and macOS; it does not publish a release. Native installers, signing, and updating remain future work.

The project uses .NET 10 LTS, Avalonia 12.1.4, CommunityToolkit.Mvvm, and the repository's existing MSTest package versions.
Avalonia versions and common build settings are centralized in `Directory.Build.props`.
Windows uses **FluentAvaloniaUI 3.1.0**, with WinUI/Windows 11-style control templates, the system font
and system accent. This replaces Avalonia's older built-in Fluent appearance on Windows, per Jawad's
updated preference. Linux/macOS currently use the built-in Fluent base with separate platform palettes
and control proportions. All platforms follow system light/dark mode and keep native window decorations.
The Linux/macOS styles are a starting point, not native GTK/Qt/AppKit controls or verified native parity.
Native desktop palette/font/menu integration and accessibility acceptance remain required.

The native authentication browser uses `Avalonia.Controls.WebView` 12.1.1. Windows requires WebView2
and the app's embedded Windows compatibility manifest; removing it crashes native child-window creation.
Linux needs WebKitGTK 4.1 with GTK 3/libsoup 3; macOS uses WKWebView. Run the local browser check in
[`tools/native-login-smoke`](tools/native-login-smoke/README.md) on native desktop runners when changing hosting.

## Reference coverage

`migration/reference/manifest.json` snapshots 83 XAML candidates, their named elements and 963
action-bearing elements, plus hashes of 5,988 source candidates including embedded web/vendor sources.
It is source evidence, not a claim that each candidate is reachable or that code-only behavior has been fully mapped.
Every UI candidate starts required/pending; exclude one only with recorded reachability evidence.
The original palette and Fluent accent have been ported. Screen layouts and interactions still require comparison.

The optional reference-capture tool uses Python's standard library; Python is not required to build/run the app.
When the reference application is available, regenerate the initial source snapshot with:

```sh
python -m unittest discover -s tools -p test_capture_reference.py
python tools/capture_reference.py --source ../RustPlusDesktop --output migration/reference
```

Regeneration resets generated checklist statuses; keep verified migration decisions in `migration/STATUS.md`.

See [the migration prompt](migration/PROMPT.md) and [current status](migration/STATUS.md) before continuing.

## References

- [Avalonia WPF migration guide](https://docs.avaloniaui.net/docs/migration/wpf/)
- [Compiled bindings](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)
- [Native file dialogs](https://docs.avaloniaui.net/docs/services/file-dialogs)
- [Headless UI testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
