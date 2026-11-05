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
const isGenerateNotesOnly = args.includes('--generate-notes-only');
const shouldUpdateFiles = args.includes('--update-files') || (!isDryRun && !isGenerateNotesOnly);
const shouldCreateTag = args.includes('--create-tag');

function getArgValue(flag) {
  const arg = args.find(a => a.startsWith(`--${flag}=`));
  if (arg) return arg.split('=')[1].trim();
  return null;
}

const overrideType = (getArgValue('type') || process.env.INPUT_BUMP_TYPE || 'auto').toLowerCase();
const customVersionArg = getArgValue('custom-version') || process.env.INPUT_CUSTOM_VERSION;
const fromRefArg = getArgValue('from-ref') || getArgValue('from-commit') || process.env.INPUT_FROM_REF;
const toRefArg = getArgValue('to-ref') || getArgValue('to-commit') || process.env.INPUT_TO_REF || 'HEAD';
const notesFileArg = getArgValue('notes-file') || process.env.INPUT_NOTES_FILE || '.github/release-notes.md';

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

// 1. Get repository slug (e.g. "Pronwan/rustplus-desktop")
function getRepoSlug() {
  const explicit = getArgValue('repo') || process.env.GITHUB_REPOSITORY;
  if (explicit) return explicit;
  const remoteUrl = run('git config --get remote.origin.url') || run('git config --get remote.remote.url');
  const match = remoteUrl.match(/github\.com[/:]([^/]+)\/([^/.]+)(?:\.git)?/);
  if (match) return `${match[1]}/${match[2]}`;
  return 'Pronwan/rustplus-desktop';
}

const repoSlug = getRepoSlug();

// 2. Read current version from src/RustPlusDesktop/RustPlusDesk.csproj
const csprojPath = resolve(rootDir, 'src/RustPlusDesktop/RustPlusDesk.csproj');
if (!existsSync(csprojPath)) {
  console.error('Error: RustPlusDesk.csproj not found at', csprojPath);
  process.exit(1);
}

function getCurrentVersionFromCsproj() {
  const content = readFileSync(csprojPath, 'utf-8');
  const match = content.match(/<Version>([^<]+)<\/Version>/);
  if (match) return match[1].trim();
  return null;
}

const currentCsprojVersion = getCurrentVersionFromCsproj() || '10.1.2';
console.log(`Current version in RustPlusDesk.csproj: ${currentCsprojVersion}`);

// 3. Resolve base / starting tag or commit
function getLatestSemverTag() {
  const tagsOutput = run('git tag --list "v*.*.*" --sort=-v:refname');
  if (!tagsOutput) return null;
  const tags = tagsOutput.split('\n').map(t => t.trim()).filter(Boolean);
  return tags.length > 0 ? tags[0] : null;
}

let baseRef = fromRefArg;
if (!baseRef) {
  baseRef = getLatestSemverTag();
}

console.log(baseRef ? `Using base ref: ${baseRef}` : 'No previous semver tag found (will analyze history since root).');

// 4. Fetch commits in range
function getCommitsInRange(from, to) {
  let range = '';
  if (from) {
    // If 'from' is a commit hash and not a tag, check if it's meant to be inclusive
    // If user passed a starting commit, we want from~1..to so that 'from' is included.
    const isTag = run(`git tag -l "${from}"`);
    if (!isTag && from.match(/^[0-9a-f]{7,40}$/i)) {
      // Check if from has a parent
      const parent = run(`git rev-parse "${from}~1"`);
      range = parent ? `${parent}..${to}` : to;
    } else {
      range = `${from}..${to}`;
    }
  } else {
    range = to;
  }

  // Format: hash%x1fauthor%x1fsubject%x1fbody%x1e
  const raw = run(`git log ${range} --pretty=format:"%H%x1f%an%x1f%s%x1f%b%x1e"`);
  if (!raw) return [];

  const commits = [];
  const entries = raw.split('\x1e');
  for (const entry of entries) {
    if (!entry.trim()) continue;
    const [hash, author, subject, body = ''] = entry.split('\x1f');
    if (!hash || !subject) continue;

    const trimmedSubject = subject.trim();
    // Skip release commits, merge commits or skip-ci commits
    if (
      trimmedSubject.startsWith('chore(release):') ||
      trimmedSubject.startsWith('Merge branch') ||
      trimmedSubject.startsWith('Merge pull request') ||
      trimmedSubject.includes('[skip ci]')
    ) {
      continue;
    }

    commits.push({
      hash: hash.trim(),
      shortHash: hash.trim().slice(0, 7),
      author: (author || '').trim(),
      subject: trimmedSubject,
      body: body.trim()
    });
  }
  return commits;
}

const commits = getCommitsInRange(baseRef, toRefArg);
console.log(`Found ${commits.length} candidate commit(s) in range ${baseRef || 'root'}..${toRefArg}.`);

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

    // Handle nested conventional formats, e.g. "sync(genetics-lab): feat(recipes): ..."
    let subjectToParse = commit.subject;
    const syncMatch = subjectToParse.match(/^sync\([^)]+\):\s*(.+)$/i);
    if (syncMatch) {
      subjectToParse = syncMatch[1];
    }

    const match = subjectToParse.match(conventionalPattern);
    if (!match) {
      // Check for keywords in subject
      const lower = commit.subject.toLowerCase();
      if (lower.startsWith('feat') || lower.includes('feat(')) {
        hasMinor = true;
      } else if (lower.startsWith('fix') || lower.includes('fix(') || lower.startsWith('perf')) {
        hasPatch = true;
      }
      continue;
    }

    const type = match[1].toLowerCase();
    if (type === 'feat') {
      hasMinor = true;
    } else if (['fix', 'perf', 'refactor', 'revert'].includes(type)) {
      hasPatch = true;
    } else if (type === 'sync') {
      // General sync can contain fixes/features
      hasPatch = true;
    }
  }

  if (hasMajor) return 'major';
  if (hasMinor) return 'minor';
  if (hasPatch) return 'patch';
  return null;
}

let bumpType = null;
if (customVersionArg) {
  console.log(`Using explicit custom version: ${customVersionArg}`);
} else if (['major', 'minor', 'patch'].includes(overrideType)) {
  bumpType = overrideType;
  console.log(`Using explicit bump type override: ${bumpType}`);
} else {
  bumpType = analyzeBumpType(commits);
  console.log(bumpType ? `Detected conventional bump type: ${bumpType}` : 'No conventional changes requiring bump detected.');
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

function getVersionFromRef(ref) {
  if (!ref) return null;
  const match = ref.match(/v?(\d+\.\d+\.\d+)/);
  return match ? match[1] : null;
}

const baseTagVersion = getVersionFromRef(baseRef);
const baseVersionForBump = baseTagVersion || currentCsprojVersion;

let nextVersion = customVersionArg;
if (!nextVersion) {
  if (bumpType) {
    nextVersion = bumpSemver(baseVersionForBump, bumpType);
  } else {
    nextVersion = currentCsprojVersion;
  }
}

const nextTag = `v${nextVersion}`;
const previousTagDisplay = baseRef && baseRef.startsWith('v') ? baseRef : `v${currentCsprojVersion}`;
console.log(`Target Version: ${currentCsprojVersion} -> ${nextVersion} (${nextTag})`);

// 7. Group commits for Release Notes & Changelog
function parseCommitDetails(rawSubject) {
  let subject = rawSubject;
  let outerScope = null;

  // Check nested: sync(genetics-lab): feat(recipes): something
  const nestedMatch = subject.match(/^([a-zA-Z0-9_-]+)\(([^)]+)\):\s*([a-zA-Z0-9_-]+)(?:\(([^)]+)\))?:\s*(.+)$/i);
  if (nestedMatch) {
    const [_, outerType, oScope, innerType, innerScope, message] = nestedMatch;
    return {
      type: innerType.toLowerCase(),
      scope: innerScope ? `${oScope} / ${innerScope}` : oScope,
      message: message.trim()
    };
  }

  // Standard: type(scope): message
  const standardMatch = subject.match(/^([a-zA-Z0-9_-]+)(?:\(([^)]+)\))?(!)?:\s*(.+)$/);
  if (standardMatch) {
    return {
      type: standardMatch[1].toLowerCase(),
      scope: standardMatch[2] ? standardMatch[2].trim() : null,
      message: standardMatch[4].trim()
    };
  }

  return {
    type: 'other',
    scope: null,
    message: subject.trim()
  };
}

function generateReleaseNotes(version, tag, previousTag, commitList) {
  const breaking = [];
  const features = [];
  const fixes = [];
  const performance = [];
  const syncs = [];
  const refactors = [];
  const others = [];
  const authors = new Set();

  for (const c of commitList) {
    if (c.author) authors.add(c.author);

    const fullText = `${c.subject}\n${c.body}`;
    const isBreaking =
      fullText.includes('BREAKING CHANGE:') ||
      fullText.includes('BREAKING-CHANGE:') ||
      c.subject.match(/^([a-zA-Z0-9_-]+)(\([^\)]+\))?!:/);

    if (isBreaking) {
      breaking.push(c);
      continue;
    }

    const details = parseCommitDetails(c.subject);
    c.parsed = details;

    if (details.type === 'feat') {
      features.push(c);
    } else if (details.type === 'fix') {
      fixes.push(c);
    } else if (details.type === 'perf') {
      performance.push(c);
    } else if (details.type === 'sync') {
      syncs.push(c);
    } else if (details.type === 'refactor') {
      refactors.push(c);
    } else {
      others.push(c);
    }
  }

  const dateStr = new Date().toISOString().split('T')[0];
  const lines = [];

  lines.push(`## [${version}](https://github.com/${repoSlug}/releases/tag/${tag}) (${dateStr})`);
  lines.push('');

  function formatCommitLine(c) {
    const commitUrl = `https://github.com/${repoSlug}/commit/${c.hash}`;
    const scopePrefix = c.parsed?.scope ? `**${c.parsed.scope}**: ` : '';
    const message = c.parsed?.message || c.subject;
    const authorNote = c.author ? ` (${c.author})` : '';
    return `- ${scopePrefix}${message} ([${c.shortHash}](${commitUrl}))${authorNote}`;
  }

  if (breaking.length > 0) {
    lines.push('### ⚠️ Breaking Changes');
    lines.push('');
    for (const c of breaking) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (features.length > 0) {
    lines.push('### 🚀 Features & Enhancements');
    lines.push('');
    for (const c of features) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (fixes.length > 0) {
    lines.push('### 🐛 Bug Fixes & Stability');
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

  if (syncs.length > 0) {
    lines.push('### 🔄 Submodules & Genetics Lab Updates');
    lines.push('');
    for (const c of syncs) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (refactors.length > 0) {
    lines.push('### ♻️ Code Refactoring');
    lines.push('');
    for (const c of refactors) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (others.length > 0 && features.length === 0 && fixes.length === 0) {
    lines.push('### 🛠️ Maintenance & Other Changes');
    lines.push('');
    for (const c of others) lines.push(formatCommitLine(c));
    lines.push('');
  }

  if (authors.size > 0) {
    lines.push('### 👥 Contributors');
    lines.push('');
    lines.push(`Special thanks to: ${Array.from(authors).map(a => `@${a}`).join(', ')}`);
    lines.push('');
  }

  if (previousTag) {
    lines.push(`**Full Changelog**: https://github.com/${repoSlug}/compare/${previousTag}...${tag}`);
  } else {
    lines.push(`**Release Tag**: https://github.com/${repoSlug}/releases/tag/${tag}`);
  }
  lines.push('');

  return lines.join('\n');
}

const releaseNotesMarkdown = generateReleaseNotes(nextVersion, nextTag, previousTagDisplay, commits);

// 8. Always write / export release notes file
const targetNotesPath = resolve(rootDir, notesFileArg);
const targetNotesDir = dirname(targetNotesPath);
if (!existsSync(targetNotesDir)) {
  mkdirSync(targetNotesDir, { recursive: true });
}
writeFileSync(targetNotesPath, releaseNotesMarkdown, 'utf-8');
console.log(`Generated release notes file at ${targetNotesPath}`);

if (isDryRun || isGenerateNotesOnly) {
  console.log('\n=================== RELEASE NOTES PREVIEW ===================');
  console.log(releaseNotesMarkdown);
  console.log('=============================================================\n');
  if (isDryRun) {
    console.log(`[DRY RUN] Would update RustPlusDesk.csproj version to: ${nextVersion}`);
    console.log(`[DRY RUN] Would prepend entry to docs/releases/CHANGELOG.md`);
    if (shouldCreateTag) {
      console.log(`[DRY RUN] Would create git commit and tag: ${nextTag}`);
    }
  }
  setGithubOutput('bumped', (nextVersion !== currentCsprojVersion).toString());
  setGithubOutput('version', nextVersion);
  setGithubOutput('tag', nextTag);
  setGithubOutput('previous_tag', previousTagDisplay);
  setGithubOutput('release_notes_file', notesFileArg);
  process.exit(0);
}

// 9. Update Project Files
if (shouldUpdateFiles && nextVersion !== currentCsprojVersion) {
  // 9a. Update src/RustPlusDesktop/RustPlusDesk.csproj
  let csprojContent = readFileSync(csprojPath, 'utf-8');
  csprojContent = csprojContent.replace(/<Version>[^<]+<\/Version>/, `<Version>${nextVersion}</Version>`);
  writeFileSync(csprojPath, csprojContent, 'utf-8');
  console.log(`Updated RustPlusDesk.csproj -> <Version>${nextVersion}</Version>`);

  // 9b. Update or Create docs/releases/CHANGELOG.md
  const changelogPath = resolve(rootDir, 'docs/releases/CHANGELOG.md');
  let currentChangelog = '';
  if (existsSync(changelogPath)) {
    currentChangelog = readFileSync(changelogPath, 'utf-8');
    if (currentChangelog.startsWith('# Changelog\n')) {
      currentChangelog = currentChangelog.replace('# Changelog\n\n', '');
    }
  }

  const fullChangelog = `# Changelog\n\nAll notable changes to Rust+ Desktop are documented in this file.\n\n${releaseNotesMarkdown}${currentChangelog}`;
  writeFileSync(changelogPath, fullChangelog, 'utf-8');
  console.log(`Updated docs/releases/CHANGELOG.md`);

  // 9c. Update root package.json if present
  const rootPkgPath = resolve(rootDir, 'tools/release/package.json');
  if (existsSync(rootPkgPath)) {
    try {
      const rootPkg = JSON.parse(readFileSync(rootPkgPath, 'utf-8'));
      rootPkg.version = nextVersion;
      writeFileSync(rootPkgPath, JSON.stringify(rootPkg, null, 2) + '\n', 'utf-8');
      console.log(`Updated root package.json -> version ${nextVersion}`);
    } catch { }
  }
}

// 10. Git commit & tag if requested
if (shouldCreateTag && !isDryRun) {
  // Stage only files that exist and are not ignored: one missing pathspec (the root
  // package.json is gitignored, so CI has none) fails the whole `git add`, the commit then
  // has nothing in it, and the tag used to land on the previous commit without the bump.
  // Each step now stops the run when it fails instead of carrying on.
  const toStage = [csprojPath, resolve(rootDir, 'docs/releases/CHANGELOG.md'), targetNotesPath, resolve(rootDir, 'tools/release/package.json')]
    .filter((p) => existsSync(p))
    .filter((p) => run(`git check-ignore -q "${p}" && echo ignored`) !== 'ignored');
  execSync(`git add ${toStage.map((p) => `"${p}"`).join(' ')}`, { cwd: rootDir, stdio: 'inherit' });
  execSync(`git commit -m "chore(release): bump version to ${nextVersion} [skip ci]"`, { cwd: rootDir, stdio: 'inherit' });
  execSync(`git tag -a "${nextTag}" -m "Release ${nextTag}"`, { cwd: rootDir, stdio: 'inherit' });
  console.log(`Created git commit and tag ${nextTag}`);
}

// 11. Output to GitHub Actions
setGithubOutput('bumped', (nextVersion !== currentCsprojVersion).toString());
setGithubOutput('version', nextVersion);
setGithubOutput('tag', nextTag);
setGithubOutput('previous_tag', previousTagDisplay);
setGithubOutput('release_notes_file', notesFileArg);

console.log(`Successfully completed smart version bump to ${nextVersion} (${nextTag})!`);
