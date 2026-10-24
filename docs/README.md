# Repository layout

```text
src/RustPlusDesktop/          Windows app, runtime assets and Genetics Lab
tests/RustPlusDesktop.Tests/  Windows app tests
tools/build/                 Build support and secret generation
tools/release/               Versioning and release notes
tools/agents/                Agent skill installation manifest
tools/dev.cmd                Local Hot Reload launcher
tools/build.cmd              Double-click frontend and Windows build
vendor/nuget/                Committed local NuGet packages
vendor/RustPlus.Auth/         Private authentication submodule (Git reference only)
vendor/MapParser/             Ignored private map parser checkout
docs/development/            Build and contributor instructions
docs/releases/               Changelog and release announcements
docs/media/                  Repository artwork and video thumbnails
artifacts/                   Ignored local release output
experiments/                 Ignored independent migration repositories
.cache/                      Ignored generated analysis data
.github/                     CI workflows and repository automation
```

Keep application source in `src`, automated tests in `tests`, and developer-only
utilities in `tools`. The solution, README, license, automatic build/package
configuration, localization configuration and agent entry points stay at the
root so their tools can discover them.

Start with [building locally](development/BUILDING.md),
[release automation](development/RELEASING.md), or the
[changelog](releases/CHANGELOG.md).

## Private dependencies

`vendor/MapParser/` is a separate private checkout and must remain ignored.
`vendor/RustPlus.Auth/` is recorded only as a submodule commit; its source must
never be added to this repository. Initialize it with `git submodule update
--init --recursive` when authorized to access the private repository. Never
force-add either dependency's files or generated `ObfuscatedSecrets.cs`.

The local NuGet package in `vendor/nuget/` is an intentional dependency. Local
secrets remain in the ignored root `.env`; copy `.env.example` when setting up.

## Other migrations

The Avalonia migration, including its unfinished work, is preserved on
`codex/avalonia-desktop`. The `beta` branch contains the Windows app.
`experiments/rustplus-rust/` is a separate local repository, ignored by this one.
