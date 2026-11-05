# Building Rust+ Desktop Locally

If you are a developer looking to compile and build **Rust+ Desktop** from the source code, you'll need to set up a few things first. Some sensitive files and proprietary assets are intentionally ignored via `.gitignore`.

## Prerequisites
- **IDE:** Visual Studio 2022 (recommended) or JetBrains Rider
- **Framework:** .NET 8 SDK with the Windows Desktop Development workload
- **Node.js:** Required to build the GeneticsLab frontend; it is not shipped with the app.

---

## 1. Generated Secrets (`ObfuscatedSecrets.cs`)

The build generates `src/RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs`
from the ignored repository-root `.env` using
`tools/build/obfuscate-secrets.ps1`. Do not create a hand-written replacement
or commit the generated file. Obfuscation is not encryption; use development
credentials and keep production credentials out of source control.

## 2. Environment Variables (`.env`)
The app expects an environment file in the root directory.
Copy `.env.example` to `.env` beside `RustPlusDesk.sln` and use development credentials.

## 3. The 3D Map Parser
Rust+ Desktop v7.0.0 introduced an interactive 2D/3D Map engine. **Please note that the 3D Map Parser module is proprietary intellectual property and is NOT open-source.** It is integrated into Rust+ Desktop under a specific license and is therefore entirely excluded from this repository.

**What this means for local builds:**
- You do **not** need the `vendor/MapParser` folder to successfully compile and run the application.
- The build will succeed without it.
- However, the 3D Map functionality will be unavailable/disabled in your local build.

---

## Building

For everyday builds, double-click `tools\build.cmd` or run it from the
repository root. It installs Genetics Lab dependencies when missing, builds
the frontend and Windows solution, and keeps the window open with the result.
It uses Debug configuration and does not run tests or publish anything.

The finished executable is
`src/RustPlusDesktop/bin/Debug/net8.0-windows10.0.17763.0/RustPlusDesk.exe`.
Files under `obj`, including `apphost.exe`, are generated build intermediates;
do not launch or distribute them. They are recreated after a clean build.

### Manual setup and verification

From the repository root:

```powershell
git submodule update --init --recursive
npm --prefix src/RustPlusDesktop/Features/GeneticsLab ci
npm --prefix src/RustPlusDesktop/Features/GeneticsLab run build
dotnet restore RustPlusDesk.sln
dotnet build RustPlusDesk.sln
dotnet test tests/RustPlusDesktop.Tests/RustPlusDesktop.Tests.csproj
```

The authentication submodule requires access to its private repository. If you
have access to MapParser, place its private checkout at `vendor/MapParser/`.
Its source stays ignored; the release workflow checks it out separately.

For Hot Reload, run `tools\dev.cmd`. Alternatively, open `RustPlusDesk.sln`
in Visual Studio and run the app project. See [repository layout](../README.md)
for folder responsibilities and [release automation](RELEASING.md) for packaging.
