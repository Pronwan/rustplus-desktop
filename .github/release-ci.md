# Release CI

The `Release` GitHub Actions workflow builds the Windows app, NuGet package artifacts, an Inno Setup installer, and Velopack update packages.

## Manual Release

1. Open GitHub Actions.
2. Run the `Release` workflow.
3. Set `version` to the package version you want, for example `7.1.2`.
4. Leave `MapParser` on `Pronwan/MapParser` and `main`, or point it at a branch/tag when testing parser changes.
5. Enable `publish_nuget` only when the packages should be pushed to GitHub Packages.
6. Leave `create_github_release` enabled when you want the installer and update files uploaded to a GitHub Release.

## Tag Release

Pushing a tag that starts with `v` builds and publishes release assets automatically:

```powershell
git tag v7.1.2
git push origin v7.1.2
```

The workflow trims the leading `v` and uses `7.1.2` as the package version.

## Outputs

- `artifacts/nuget`: `.nupkg` and `.snupkg` from `dotnet pack`.
- `artifacts/inno`: `RustPlusDesk-Setup-<version>.exe` from `Setup.iss`.
- `artifacts/velopack`: Velopack `.nupkg`, installer `.exe`, portable `.zip`, and release metadata.

## Secrets

- `MAP_PARSER_TOKEN`: required when `MapParser` is private or belongs to a different account/org. Use a fine-grained GitHub token with read-only Contents access to the MapParser repository.
- `OVERLAY_SYNC_SECRET_HEX`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `OVERLAY_SYNC_BASEURL`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `SUPABASE_URL`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `SUPABASE_ANON_KEY`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- GitHub Packages and GitHub Releases use the workflow `GITHUB_TOKEN`.

To publish to nuget.org later, add a `NUGET_API_KEY` secret and add a `dotnet nuget push` step that targets `https://api.nuget.org/v3/index.json`.
