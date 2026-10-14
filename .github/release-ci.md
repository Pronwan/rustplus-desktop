# Release CI

The `Release` GitHub Actions workflow builds the RustPlusDesk Windows app, bundles MapParser into the app output, then creates a Velopack installer and update packages.

## Smart Version Bump (Automated & Manual)

The repository provides a smart conventional-commit-based version bumper in `scripts/bump-version.mjs` and the `.github/workflows/bump-version.yml` workflow.

### Using the GitHub Actions Workflow:
1. Open GitHub Actions and select **Smart Version Bump**.
2. Select the `bump_type`:
   - `auto` (default): inspects commits since the previous tag. If new features (`feat`) were committed, it bumps `minor`. If only bug fixes/improvements (`fix`, `perf`, `refactor`) were committed, it bumps `patch`. If breaking changes exist, it bumps `major`.
   - `patch`, `minor`, `major`: forces the specified bump.
   - `custom`: bumps to a custom version string provided in `custom_version`.
3. Optionally set `from_ref` to analyze from a specific tag or commit.
4. Set `dry_run` to true to preview the calculated version and release notes without tagging or committing.
5. With `trigger_release` enabled (default), pushing the tag or triggering the release workflow automatically generates and attaches modern, categorized release notes to the GitHub Release.

### Local CLI Usage:
```bash
# Preview bump and release notes without making changes:
npm run version:preview

# Apply bump to RustPlusDesk.csproj, CHANGELOG.md, and create a git commit + tag:
node scripts/bump-version.mjs --update-files --create-tag
```

## Manual Release

1. Open GitHub Actions.
2. Run the `Release` workflow.
3. Set `version` to the package version you want, for example `7.1.2`.
4. Leave `MapParser` on `Pronwan/MapParser` and `main`, or point it at a branch/tag when testing parser changes.
5. Leave `create_github_release` enabled when you want the installer and update files uploaded to a GitHub Release.

## Tag Release

Pushing a tag that starts with `v` builds and publishes release assets automatically:

```powershell
git tag v7.1.2
git push origin v7.1.2
```

The workflow trims the leading `v` and uses `7.1.2` as the package version.

## Outputs

- `artifacts/velopack`: the Velopack installer, `.nupkg`, and release metadata.

Tag releases and manual runs with `create_github_release` enabled upload these files directly to the GitHub Release. Manual runs with release creation disabled retain them as a workflow artifact instead.

MapParser is checked out during CI and included in the RustPlusDesk publish output. It is not published as a separate NuGet package.

## Genetics Lab

Genetics Lab updates itself independently of desktop releases:

1. A push to `master` in [rust-genetics-lab](https://github.com/JawadYzbk/rust-genetics-lab) bumps the version, tags it and starts its Release workflow.
2. That workflow builds the site with `genetics-lab-manifest.json` (every file's SHA-256 and the recent changelog), commits the build to the `dist` branch and tags it `dist-vX.Y.Z`.
3. Installed desktops check the manifest on the `dist` branch about a minute after start-up and whenever Genetics Lab opens (at most hourly). If GitHub cannot be reached they try `https://genetics.rustplusdesktop.cloud/genetics-lab-manifest.json` instead.
4. Only files whose hash changed are downloaded; the rest are reused from the bundled copy or the previous download. The build is staged and verified under `%LocalAppData%\RustPlusDesk\GeneticsLab\versions` before it replaces the current one.
5. Genetics Lab shows an "update ready" banner with the release notes; **Reload now** switches to the new build, which then shows "What's new".

This workflow bundles the latest Genetics Lab release into the installer (`geneticslab_release` input: a tag, `latest`, or `source` to build the copy in `RustPlusDesktop/Features/GeneticsLab`). A release without an update manifest falls back to building from source.

To require a newer desktop for a Genetics Lab release, set `"desktop": { "minVersion": "X.Y.Z" }` in its `package.json`; older desktops keep their current copy.

## Caching

The workflow uses `actions/setup-node` npm caching for the MapParser and GeneticsLab lock files. NuGet packages are restored directly because saving the release cache takes longer than restoring the packages.

Do not add `.env`, generated secret files, tokens, or release outputs to cache paths.

## Secrets & Variables

- `MAP_PARSER_TOKEN`: required when `MapParser` is private or belongs to a different account/org. Use a fine-grained GitHub token with read-only Contents access to the MapParser repository.
- `OVERLAY_SYNC_SECRET_HEX`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `OVERLAY_SYNC_BASEURL`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `SUPABASE_URL`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `SUPABASE_ANON_KEY`: used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- `CLOUD_API_BASEURL`: optional secret/variable override (defaults to `https://rustplusdesktop.cloud`) used to generate `RustPlusDesktop/Services/Data/ObfuscatedSecrets.cs` during CI builds.
- GitHub Releases use the workflow `GITHUB_TOKEN`.
