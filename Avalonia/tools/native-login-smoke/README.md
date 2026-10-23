# Native sign-in browser smoke check

Run `dotnet run --project tools/native-login-smoke/Smoke.csproj -c Release` from the standalone Avalonia folder on a desktop session. It opens the production authentication browser with a local HTTP fixture, checks its callback, then checks cancellation. It never contacts Steam or Facepunch and never reads or saves account credentials. Exit code 0 means both checks passed.

This uses the same Windows compatibility manifest as the desktop executable. Without that declaration, Windows native browser hosting crashes outside the authentication task. Headless UI tests do not exercise native window creation, so run this check when changing desktop hosting or browser dependencies. Linux/macOS require their native WebView prerequisites and separate runtime verification.
