# Automated Version Bumping & Release Workflow

Rust Genetics Lab uses an automated version bumping and release pipeline built on **GitHub Actions** and **Conventional Commits**.

---

## 🚀 How It Works

1. **Automatic on Push to `master`**:
   - Every time code is pushed or merged into `master`, the workflow runs.
   - It analyzes all commits since the last semver tag (`v*.*.*`).
   - If commits contain:
     - `BREAKING CHANGE:` or `feat!:` / `fix!:` ➡️ **Major bump** (`X.0.0`)
     - `feat:` or `feat(...)` ➡️ **Minor bump** (`1.X.0`)
     - `fix:`, `perf:`, `refactor:`, `revert:` ➡️ **Patch bump** (`1.0.X`)
     - Only `docs:`, `chore:`, `test:`, `ci:`, `style:` ➡️ **Skipped** (no release generated)
   - If a bump is warranted:
     - Bumps `package.json` and `package-lock.json`.
     - Updates the version badge in `README.md`.
     - Prepends structured release notes to `CHANGELOG.md`.
     - Commits changes as `chore(release): vX.Y.Z [skip ci]`.
     - Creates an annotated Git tag `vX.Y.Z` and pushes to `master`.
     - Creates an official GitHub Release with categorised release notes.

2. **Manual Trigger (`workflow_dispatch`)**:
   - Navigate to **Actions** ➡️ **Auto Version Bump & Release** ➡️ **Run workflow**.
   - Select bump type override:
     - `auto`: Automatically determine bump from commit history (default).
     - `patch`: Force patch bump.
     - `minor`: Force minor bump.
     - `major`: Force major bump.
   - Check `dry_run` to preview the bump and release notes without committing or publishing.

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
