# Setting up RustPlusDesk-Ryyott on a teammate's machine

This is a one-time setup. ~3 minutes once you have the .exe.

## 1. Install the .NET 8 Desktop Runtime

The app is a .NET 8 WPF Windows app. Most machines don't have the runtime by
default. Run this once in PowerShell (any user can run it; it installs to the
machine, not per-user):

```powershell
winget install --id Microsoft.DotNet.DesktopRuntime.8 --source winget --accept-source-agreements --accept-package-agreements --silent
```

You only need this once per machine.

## 2. Get the .exe + its sidecar files

The app ships as a single folder containing:

- `RustPlusDesk-Ryyott.exe`
- A `runtime/` subfolder with `node-win-x64/` (including `node.exe`) and
  `rustplus-cli.zip`. **This whole subfolder must travel with the .exe** — the
  pairing listener shells out to bundled Node.js.
- `RustPlusDesk.dll`, `Microsoft.Web.WebView2.*.dll`, `RustPlusApi.dll`,
  `Google.Protobuf.dll`, etc. (output of `dotnet build -c Release`).

The simplest distribution: zip the whole
`bin\Release\net8.0-windows\` folder and send it via Discord / Drive. Your
teammate unzips it anywhere and runs `RustPlusDesk-Ryyott.exe`.

## 3. First run

1. Double-click `RustPlusDesk-Ryyott.exe`.
2. Click **Listening (Pairing)** in the app.
3. Sign in with Steam in the browser tab that opens, authorise the PC.
4. In Rust, click the **Rust+ Pairing Link** for whichever server.
5. The server appears in the app. Click **Connect**.

## 4. Enable Team sync

Once connected:

1. Open the **Tracker** tab.
2. Tick the **Team sync** checkbox at the top.
3. Within ~30 seconds, the app will pull whatever tracked players + groups
   your teammates have already published, and push whatever you do locally.

**Notes:**
- Team sync is scoped to the **current Rust+ team** (the people in your team
  list on the server). It uses live team Steam IDs — there's no team
  code or invite to manage.
- Per-user settings (announce toggles, learned cargo durations) are NOT
  shared. Only the directory of tracked players + the group definitions
  (name, colour, members, map pin) are.
- Each teammate must have ticked Team sync for it to work both ways.
- Map pins propagate too. If you `📍 Pin` H3's base, your teammates see
  the marker on their map within 30 s.
- If you want to take Team sync off, just untick the box. Your local data
  stays; you just stop pushing/pulling.

## 5. Updating when the user (Ryyott) ships a new build

You'll send the new zip; your teammate replaces the folder contents
(keeping their `%APPDATA%\RustPlusDesk-Ryyott\` config — that's where their
pairing token, tracked players, etc. live, and survives an .exe replacement).
