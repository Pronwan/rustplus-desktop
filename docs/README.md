# Repository layout

| Folder | Purpose |
| --- | --- |
| `RustPlusDesktop/` | Windows WPF app, runtime assets, installer and supporting services. |
| `RustPlusDesktop/Features/GeneticsLab/` | Genetics Lab web app. Vite copies `public/`, including the OCR files in `public/tesseract/`, into `dist/`. |
| `RustPlusDesktop.Tests/` | Windows app tests. |
| `MapParser/` | Local proprietary map parser workspace used by the 3D map build. |
| `rustplus-rust/` | Separate local Rust migration repository. |
| `Submodules/` | Private authentication dependency. |
| `packages-local/` | Local NuGet package required by the app. |
| `scripts/` and `.github/` | Versioning, verification and release automation. |
| `docs/media/` | Repository artwork, branding and video thumbnails. |
| `docs/releases/` | Archived release announcements. |

Build instructions are in [BUILDING.md](../BUILDING.md). Build outputs, local
releases, editor settings and agent caches are not application source. Preserve
local work and separate repositories when cleaning these folders.

The Avalonia cross-platform migration lives on `codex/avalonia-desktop`, including
its tests, tools and migration references. Switch to that branch to work on it;
the `beta` branch contains the Windows app.
