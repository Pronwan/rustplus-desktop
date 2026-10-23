# Rust+ Desktop: clean Avalonia migration execution brief

Act as the senior .NET/Avalonia engineer responsible for delivering a maintainable cross-platform
successor to Rust+ Desktop. Implement and verify the migration; do not stop at architecture advice.

## Objective

Migrate the application from WPF/Windows-specific UI to Avalonia for Windows, macOS, and Linux.
Preserve **every shipped feature** and keep the design **as close to the existing application as possible**.
Do not substitute a reduced product, omit secondary tools/dialogs, or redesign the information architecture.
Preserve real product behavior, existing user data, Rust+ pairing, and cloud integrations. Replace the
large, tangled XAML/code-behind structure with cohesive feature modules, focused view models,
reusable components, and explicit platform boundaries. Remove code only after proving it obsolete.

## Workspace and delivery

- Work on `beta` in the existing repository, per Jawad's explicit instruction. Do not switch branches.
- Keep **all migration changes inside `Avalonia/`**. Treat the existing application as reference code.
- Reuse or copy proven source, tests, branding, and assets from the existing repository; preserve
  licensing and provenance. Never require `../RustPlusDesktop`, private credentials, or a private
  submodule to build the portable preview.
- Keep this folder suitable for moving to a new repository root. Its own solution, package sources,
  build properties, SDK selection, tests, documentation, and workflow must travel with it.
- Never upload or publish without an explicit request. Do not mark unfinished features complete.

## Architectural constraints

The user-supplied layout in `reference/layout-reference.png` is the visual arrangement to preserve.
Follow `LAYOUT.md`: narrow left rail, server/connection/devices panel, large map workspace, bottom
activity panel and thin status bar. Keep it responsive from the minimum window size through maximized
and wide displays. Retain this arrangement while porting the remaining tools into the new native theme.

1. Core contains domain models, business rules, and contracts. No UI, dispatcher, image-control,
   operating-system, storage, or SDK types may leak into it.
2. Infrastructure implements file storage, Rust+ transport, cloud access, authentication integration,
   and operating-system adapters. Add abstractions at real boundaries, with visible ownership and lifetime.
3. The Avalonia host owns startup, dependency wiring, windows, styles, and feature presentation.
   Organize features around product responsibilities: servers/pairing, connections, map, devices,
   chat/team, cameras, automation/timers, settings, and supporting tools.
4. Use MVVM, constructor injection, typed bindings, async commands, cancellation, and observable state.
   Business workflows must be testable without a window. Create an Application project only when
   use cases justify a separate layer; do not add empty layers or generic repository/service wrappers.
5. Use shared controls, styles, templates, and resource tokens for repeated UI. Create focused
   subclasses only where there is a real behavioral/type relationship. Prefer composition over
   decorative inheritance. Do not split a giant window into dozens of coupled partial files.
6. Keep views small enough to understand as one screen or component. Code-behind is for view/native
   interaction and lifecycle, not networking, persistence, or business rules.
7. Use the .NET 10 LTS baseline and pinned compatible Avalonia packages. Reuse established libraries
   and platform facilities; justify each new dependency by actual functionality.
8. Follow Jawad's updated appearance requirement: **Windows 11 Fluent on Windows**, and the current
   operating system's design conventions elsewhere. Windows uses **FluentAvaloniaUI 3.1.0** (WinUI-style
   control templates) on Avalonia 12.1.4, replacing the older built-in Fluent look. Linux/macOS currently
   use the built-in Fluent base with separate platform styles; their native GTK/Qt/AppKit appearance is
   still pending and must not be claimed complete. Follow system light/dark mode, system fonts, native
   window decoration/menu conventions, shared semantic tokens and original branding/layout. Keep platform
   styling separate from feature views. Do not hand-build standard control templates when a maintained
   compatible theme provides them. Verify each platform on its native runner.

## Migration method

Read `STATUS.md` first. Establish the baseline and map the next feature before editing it:

- Follow repository graph instructions for symbols, callers, dependencies, and coverage. Inspect
  actual source when coverage is incomplete or stale. An absent graph edge is not evidence of dead code.
- Inventory user-visible behavior, commands, models, XAML bindings/resources, persistence schemas,
  background tasks, cancellation, error paths, and tests in the chosen feature.
- Identify Windows-specific code: WPF/WinForms, Win32 interop, WebView2, protected storage, audio/speech,
  screen capture/overlays, tray/hotkeys, helper processes, and update/installer scripts.
- Port a complete bounded behavior, then validate it against the reference application. Keep a clear
  feature-parity table and exact remaining gaps. A nonfunctional button or placeholder is not a port.
- Treat all shipped workflows, context menus, shortcuts, settings, overlays, windows, calculators,
  AI/support/community/admin tools, integrations, tutorials, and embedded applications as required.
  Inventory source candidates first; prove runtime/build reachability before excluding any candidate.
- Preserve navigation order, panel arrangement, density, typography, spacing, palette, icons, labels,
  states, dialogs, and interaction patterns. Reuse existing design tokens and assets. Compare renders
  at the same viewport/DPI to captured reference screens. Platform-required differences must be documented.
- Keep unresolved parity items required and pending. A platform limitation must be solved or explicitly
  escalated to Jawad; it never authorizes silently removing a shipped capability.
- Retain JSON names/defaults and unknown persisted data when implementing writers. Back up and write
  atomically. Keep initial readers read-only until a tested migration/writer exists. Never silently
  replace an unreadable profile with an empty saved file.
- Keep credential-bearing transport/storage models away from UI serialization, telemetry, errors,
  screenshots, and test fixtures. Surface useful safe messages without echoing input secrets.
- For genuinely platform-specific capabilities, implement and test the relevant adapter or expose a
  clear unsupported state. Never claim portability because compilation alone succeeds.

## Cleanup rules

For every deletion, inspect callers, exports, bindings, resource URIs, event wiring, reflection,
serialization contracts, helper-process entry points, and package/content/build references.
Remove the associated dependencies/resources only when no remaining feature needs them.
Record the evidence and verification in `STATUS.md`. Keep working functionality until its replacement
is verified. Do not bulk-delete files using a zero-call-count list or copy the entire old UI tree.

## Acceptance and verification

- Build this folder in isolation, outside the parent checkout, with no sibling references or secrets.
- Keep the original app unchanged while this independent migration is being built.
- Run portable unit tests plus meaningful state/command/error tests for each migrated behavior.
- Compile typed XAML bindings; use headless rendering to check real control trees, data templates,
  shared controls, and minimum window sizes. Also test keyboard use, focus, and accessible names.
- Build and test on all three native operating systems. Separately validate runtime integration,
  packaging, signing, update behavior, and user-data continuity before calling a platform supported.
- Maintain the workflow under this folder so it works when promoted to a repository root.
- Report exactly what works, what was tested, what was removed, limitations, and the next concrete slice.

## Finish line

The migration is complete only when **every shipped feature** meets documented behavioral and close visual parity, original data remains
usable, Windows/macOS/Linux runtime and distribution checks pass, and obsolete WPF/platform code
and unused dependencies have been retired. A shell or successful cross-target publish alone is not completion.
