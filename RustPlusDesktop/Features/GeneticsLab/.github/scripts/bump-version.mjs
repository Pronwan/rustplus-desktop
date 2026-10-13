#!/usr/bin/env node

import { execSync } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const rootDir = resolve(__dirname, '../..');

// CLI Arguments and Flags
const args = process.argv.slice(2);
const isDryRun = args.includes('--dry-run') || process.env.INPUT_DRY_RUN === 'true';

function getArgValue(flag) {
  const arg = args.find(a => a.startsWith(`--${flag}=`));
  if (arg) return arg.split('=')[1].trim();
  return null;
}

const overrideType = (getArgValue('type') || process.env.INPUT_BUMP_TYPE || 'auto').toLowerCase();

function run(cmd, cwd = rootDir) {
  try {
    return execSync(cmd, { cwd, encoding: 'utf-8', stdio: ['pipe', 'pipe', 'pipe'] }).trim();
  } catch (err) {
    return '';
  }
}

function setGithubOutput(key, value) {
  const outputFile = process.env.GITHUB_OUTPUT;
  if (outputFile) {
    writeFileSync(outputFile, `${key}=${value}\n`, { flag: 'a' });
  }
}

// 1. Get repository info
function getRepoSlug() {
  const explicit = getArgValue('repo') || process.env.GITHUB_REPOSITORY;
  if (explicit) return explicit;
  const remoteUrl = run('git config --get remote.origin.url');
  const match = remoteUrl.match(/github\.com[/:]([^/]+)\/([^/.]+)(?:\.git)?/);
  if (match) return `${match[1]}/${match[2]}`;
  return 'JawadYzbk/rust-genetics-lab';
}

const repoSlug = getRepoSlug();

// 2. Read package.json
const pkgPath = resolve(rootDir, 'package.json');
if (!existsSync(pkgPath)) {
  console.error('Error: package.json not found in', rootDir);
  process.exit(1);
}

const pkg = JSON.parse(readFileSync(pkgPath, 'utf-8'));
const currentVersion = pkg.version || '1.0.0';
console.log(`Current version in package.json: ${currentVersion}`);

// 3. Find latest git tag
function getLatestSemverTag() {
  const tagsOutput = run('git tag --list "v*.*.*" --sort=-v:refname');
  if (!tagsOutput) return null;
  const tags = tagsOutput.split('\n').map(t => t.trim()).filter(Boolean);
  return tags.length > 0 ? tags[0] : null;
}

const latestTag = getLatestSemverTag();
console.log(latestTag ? `Latest semver tag found: ${latestTag}` : 'No previous semver tag found (will analyze history since root).');

// 4. Fetch commits since latest tag
function getCommitsSinceTag(tag) {
  const range = tag ? `${tag}..HEAD` : 'HEAD';
  // Format: hash%x1fsubject%x1fbody%x1e
  const raw = run(`git log ${range} --pretty=format:"%H%x1f%s%x1f%b%x1e"`);
  if (!raw) return [];

  const commits = [];
  const entries = raw.split('\x1e');
  for (const entry of entries) {
    if (!entry.trim()) continue;
    const [hash, subject, body = ''] = entry.split('\x1f');
    if (!hash || !subject) continue;

    const trimmedSubject = subject.trim();
    // Skip release commits or skip-ci commits
    if (trimmedSubject.startsWith('chore(release):') || trimmedSubject.includes('[skip ci]')) {
      continue;
    }

    commits.push({
      hash: hash.trim(),
      shortHash: hash.trim().slice(0, 7),
      subject: trimmedSubject,
      body: body.trim()
    });
  }
  return commits;
}

const commits = getCommitsSinceTag(latestTag);
console.log(`Found ${commits.length} candidate commit(s) since last release.`);

if (commits.length === 0 && overrideType === 'auto') {
  console.log('No new commits to release. Exiting without bump.');
  setGithubOutput('bumped', 'false');
  process.exit(0);
}

// 5. Parse Conventional Commits to determine bump type
function analyzeBumpType(commitList) {
  let hasMajor = false;
  let hasMinor = false;
  let hasPatch = false;

  const conventionalPattern = /^([a-zA-Z0-9_-]+)(\([^\)]+\))?(!)?:\s*(.+)$/;

  for (const commit of commitList) {
    const fullText = `${commit.subject}\n${commit.body}`;
    if (
      fullText.includes('BREAKING CHANGE:') ||
      fullText.includes('BREAKING-CHANGE:') ||
      commit.subject.match(/^([a-zA-Z0-9_-]+)(\([^\)]+\))?!:/)
    ) {
      hasMajor = true;
      continue;
    }

    const match = commit.subject.match(conventionalPattern);
    if (!match) {
      continue;
    }

    const type = match[1].toLowerCase();
    if (type === 'feat') {
      hasMinor = true;
    } else if (['fix', 'perf', 'refactor', 'revert'].includes(type)) {
      hasPatch = true;
    }
  }

  if (hasMajor) return 'major';
  if (hasMinor) return 'minor';
  if (hasPatch) return 'patch';
  return null;
}

let bumpType = null;
if (['major', 'minor', 'patch'].includes(overrideType)) {
  bumpType = overrideType;
  console.log(`Using explicit bump type override: ${bumpType}`);
} else {
  bumpType = analyzeBumpType(commits);
  console.log(bumpType ? `Detected conventional bump type: ${bumpType}` : 'No conventional changes detected that warrant a version bump.');
}

if (!bumpType) {
  console.log('Only non-releasable commits (e.g. docs, chore, test) found. Skipping version bump.');
  setGithubOutput('bumped', 'false');
  process.exit(0);
}

// 6. Calculate next semver version
function bumpSemver(version, type) {
  const clean = version.replace(/^v/, '');
  const match = clean.match(/^(\d+)\.(\d+)\.(\d+)(?:-.*)?$/);
  if (!match) {
    throw new Error(`Invalid semver string: ${version}`);
  }

  let [_, major, minor, patch] = match.map(Number);
  if (type === 'major') {
    major += 1;
    minor = 0;
    patch = 0;
  } else if (type === 'minor') {
    minor += 1;
    patch = 0;
  } else if (type === 'patch') {
    patch += 1;
  }

  return `${major}.${minor}.${patch}`;
}

const nextVersion = bumpSemver(currentVersion, bumpType);
const nextTag = `v${nextVersion}`;
console.log(`Bumping version: ${currentVersion} -> ${nextVersion} (${nextTag})`);

// 7. Group commits for Release Notes & Changelog
function generateReleaseNotes(version, tag, previousTag, commitList) {
  const breaking = [];
  const features = [];
  const fixes = [];
  const performance = [];
  const refactors = [];
  const others = [];

  const conventionalPattern = /^([a-zA-Z0-9_-]+)(\([^\)]+\))?(!)?:\s*(.+)$/;

  for (const c of commitList) {
    const fullText = `${c.subject}\n${c.body}`;
    const isBreaking =
      fullText.includes('BREAKING CHANGE:') ||
      fullText.includes('BREAKING-CHANGE:') ||
      c.subject.match(/^([a-zA-Z0-9_-]+)(\([^\)]+\))?!:/);

    if (isBreaking) {
      breaking.push(c);
    }

    const match = c.subject.match(conventionalPattern);
    if (match) {
      const type = match[1].toLowerCase();
      if (type === 'feat') features.push(c);
      else if (type === 'fix') fixes.push(c);
      else if (type === 'perf') performance.push(c);
      else if (type === 'refactor') refactors.push(c);
      else if (!isBreaking) others.push(c);
    } else if (!isBreaking) {
      others.push(c);
    }
  }

  const dateStr = new Date().toISOString().split('T')[0];
  const lines = [];

  lines.push(`## [${version}](https://github.com/${repoSlug}/releases/tag/${tag}) (${dateStr})`);
  lines.push('');

  function formatCommitLine(c) {
    const commitUrl = `https://github.com/${repoSlug}/commit/${c.hash}`;
    return `* ${c.subject} ([${c.shortHash}](${commitUrl}))`;
  }

  if (breaking.length > 0) {
    lines.push('### ⚠️ Breaking Changes');
    lines.push('');
    for (const c of breaking) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (features.length > 0) {
    lines.push('### 🚀 Features');
    lines.push('');
    for (const c of features) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (fixes.length > 0) {
    lines.push('### 🐛 Bug Fixes');
    lines.push('');
    for (const c of fixes) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (performance.length > 0) {
    lines.push('### ⚡ Performance Improvements');
    lines.push('');
    for (const c of performance) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (refactors.length > 0) {
    lines.push('### ♻️ Code Refactoring');
    lines.push('');
    for (const c of refactors) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (others.length > 0 && features.length === 0 && fixes.length === 0) {
    lines.push('### 📦 Other Changes');
    lines.push('');
    for (const c of others) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (previousTag) {
    lines.push(`**Full Changelog**: https://github.com/${repoSlug}/compare/${previousTag}...${tag}`);
  } else {
    lines.push(`**Initial Release Tag**: https://github.com/${repoSlug}/releases/tag/${tag}`);
  }
  lines.push('');

  return lines.join('\n');
}

const releaseNotesMarkdown = generateReleaseNotes(nextVersion, nextTag, latestTag, commits);

if (isDryRun) {
  console.log('\n--- DRY RUN: Planned Release Notes ---');
  console.log(releaseNotesMarkdown);
  console.log('--- DRY RUN: End of Planned Release Notes ---\n');
  console.log(`[DRY RUN] Would update package.json version to ${nextVersion}`);
  console.log(`[DRY RUN] Would update README.md badge to version-${nextVersion}`);
  console.log(`[DRY RUN] Would prepend entry to CHANGELOG.md`);
  console.log(`[DRY RUN] Would commit and tag ${nextTag}`);
  setGithubOutput('bumped', 'true');
  setGithubOutput('version', nextVersion);
  setGithubOutput('tag', nextTag);
  process.exit(0);
}

// 8. Update files
// 8a. package.json
pkg.version = nextVersion;
writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n', 'utf-8');
console.log(`Updated package.json -> version ${nextVersion}`);

// 8b. package-lock.json (if present)
const lockPath = resolve(rootDir, 'package-lock.json');
if (existsSync(lockPath)) {
  try {
    const lock = JSON.parse(readFileSync(lockPath, 'utf-8'));
    lock.version = nextVersion;
    if (lock.packages && lock.packages['']) {
      lock.packages[''].version = nextVersion;
    }
    writeFileSync(lockPath, JSON.stringify(lock, null, 2) + '\n', 'utf-8');
    console.log(`Updated package-lock.json -> version ${nextVersion}`);
  } catch (err) {
    console.warn('Could not update package-lock.json automatically:', err.message);
  }
}

// 8c. README.md badge
const readmePath = resolve(rootDir, 'README.md');
if (existsSync(readmePath)) {
  let readmeContent = readFileSync(readmePath, 'utf-8');
  // Match [![Version](https://img.shields.io/badge/version-1.0.0-00bcd4?style=flat-square)](package.json)
  const versionBadgeRegex = /badge\/version-[0-9.]+(-[a-zA-Z0-9.]+)?-00bcd4/g;
  if (versionBadgeRegex.test(readmeContent)) {
    readmeContent = readmeContent.replace(versionBadgeRegex, `badge/version-${nextVersion}-00bcd4`);
    writeFileSync(readmePath, readmeContent, 'utf-8');
    console.log(`Updated README.md version badge -> version-${nextVersion}`);
  }
}

// 8d. CHANGELOG.md
const changelogPath = resolve(rootDir, 'CHANGELOG.md');
let currentChangelog = '';
if (existsSync(changelogPath)) {
  currentChangelog = readFileSync(changelogPath, 'utf-8');
  if (currentChangelog.startsWith('# Changelog\n')) {
    currentChangelog = currentChangelog.replace('# Changelog\n\n', '');
  }
}

const fullChangelog = `# Changelog\n\nAll notable changes to this project will be documented in this file.\n\n${releaseNotesMarkdown}${currentChangelog}`;
writeFileSync(changelogPath, fullChangelog, 'utf-8');
console.log('Updated CHANGELOG.md');

// 8e. Write notes file for GitHub release
const ghDir = resolve(rootDir, '.github');
if (!existsSync(ghDir)) {
  mkdirSync(ghDir, { recursive: true });
}
const releaseNotesPath = resolve(ghDir, 'release-notes.md');
writeFileSync(releaseNotesPath, releaseNotesMarkdown, 'utf-8');
console.log(`Created release notes file at ${releaseNotesPath}`);

// 9. Output to GitHub Actions
setGithubOutput('bumped', 'true');
setGithubOutput('version', nextVersion);
setGithubOutput('tag', nextTag);
setGithubOutput('release_notes_file', '.github/release-notes.md');

console.log(`Successfully completed version bump to ${nextVersion}!`);
