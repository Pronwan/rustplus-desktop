# Automated Version Bumping & Release Workflow

Rust Genetics Lab uses an automated version bumping and release pipeline built on **GitHub Actions** and **Conventional Commits**.

---

## 🚀 Decoupled Workflow Architecture

Version bumping and release publishing are decoupled into two dedicated, standalone GitHub Actions workflows:

### 1. `Bump Version` (`.github/workflows/bump-version.yml`)
- **Trigger**: Automatic on push to `master` (excluding release commits with `[skip ci]`), or manual `workflow_dispatch`.
- **Purpose**:
  - Analyzes Conventional Commits since the last semver tag (`v*.*.*`).
  - Calculates the next SemVer (`major`, `minor`, or `patch`).
  - Updates `package.json`, `package-lock.json`, `README.md`, and `CHANGELOG.md`.
  - Commits `chore(release): vX.Y.Z [skip ci]`.
  - Creates and pushes git tag `vX.Y.Z`.
  - Can run in `dry_run` mode or with bump type overrides.
  - Automatically triggers the `Release` workflow if `publish_release` is enabled (default: true).

### 2. `Release` (`.github/workflows/release.yml`)
- **Trigger**:
  - Automatically on any tag push (`push: tags: ['v*']`).
  - Manually via `workflow_dispatch` with a tag input (e.g. `v1.2.0`).
  - Reusable via `workflow_call` invoked by `bump-version.yml`.
- **Purpose**:
  - Checks out the code at the tagged commit.
  - Restores cached `node_modules` and TypeScript build info for instant execution.
  - Builds the production bundle (`npm run build`).
  - Archives `dist/` into `genetics-lab-dist-v<version>.zip` and `genetics-lab-dist.zip`.
  - Extracts the changelog section for that tag.
  - Publishes the GitHub Release with the attached assets.

---

## 🛠️ Manual & Local Usage

1. **Manual Tag Release (Git Tag)**:
   ```bash
   git tag v1.2.0
   git push origin v1.2.0
   ```
   The `Release` workflow will build and publish the release automatically.

2. **Manual Trigger via GitHub UI**:
   - **Bump Version**: Actions ➡️ `Bump Version` ➡️ Run workflow (select `auto`, `patch`, `minor`, or `major`, and optional `dry_run`).
   - **Publish Release**: Actions ➡️ `Release` ➡️ Run workflow (enter tag name or leave blank for latest).

3. **Local Preview & Execution**:
   - **Preview next release without modifying files**:
     ```bash
     npm run version:preview
     ```
   - **Preview with explicit override**:
     ```bash
     node .github/scripts/bump-version.mjs --dry-run --type=patch
     ```
   - **Run version bump locally**:
     ```bash
     npm run version:bump
     ```

---

## 📦 Built Release Assets

Every release builds the production frontend (`npm run build`) with the updated version injected into the bundle and attaches two zip files to the GitHub Release:

1. **`genetics-lab-dist-v<version>.zip`**: Version-tagged bundle containing all built assets (`index.html`, `assets/`, etc.).
2. **`genetics-lab-dist.zip`**: Constant-name bundle representing the latest build.

### Updating Local Genetics Lab in RustPlusDesktop

To download the latest built release and update `RustPlusDesktop/Features/GeneticsLab/dist`:

```powershell
# From RustPlusDesktop or Features/GeneticsLab:
pwsh ./Features/GeneticsLab/scripts/download-release-dist.ps1

# Or specify an explicit version:
pwsh ./Features/GeneticsLab/scripts/download-release-dist.ps1 -Version v1.1.0
```

This allows updating the embedded Genetics Lab in RustPlusDesktop without requiring Node.js or running `npm run build` locally.

---

## ⚙️ GitHub Repository Requirements

For GitHub Actions to push the version commit and tag back to the repository:

1. Open repository **Settings**.
2. Go to **Actions** ➡️ **General**.
3. Under **Workflow permissions**, ensure **"Read and write permissions"** is selected.
4. Ensure **"Allow GitHub Actions to create and approve pull requests"** is enabled (if branch protection is enabled).

---

## 📝 Commit Convention Cheatsheet

| Commit Prefix | SemVer Impact | Example |
|---|---|---|
| `feat:` | Minor (`+0.1.0`) | `feat: add phone camera scanner` |
| `fix:` | Patch (`+0.0.1`) | `fix: threshold glyphs with Otsu` |
| `perf:` | Patch (`+0.0.1`) | `perf: prune unreliable breeding routes` |
| `refactor:` | Patch (`+0.0.1`) | `refactor: extract desktop slot extractor` |
| `BREAKING CHANGE:` or `!` | Major (`+1.0.0`) | `feat!: change route persistence schema` |
| `docs:`, `chore:`, `test:`, `ci:` | None (skipped) | `docs: update deployment instructions` |
