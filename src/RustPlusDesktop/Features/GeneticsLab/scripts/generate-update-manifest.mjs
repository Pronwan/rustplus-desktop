#!/usr/bin/env node

// Writes dist/genetics-lab-manifest.json after `vite build`.
//
// Rust+ Desktop reads this file from the hosted site to update its embedded copy of
// Genetics Lab without a new desktop release. Each entry carries the file's SHA-256, so
// the desktop downloads only the files whose hash changed and reuses everything else
// (the ~40 MB of Tesseract data is fetched once, not on every update).
//
// GENETICS_LAB_DIST_REF (set by the release workflow) names the immutable git ref the
// release's files are published under on GitHub (the `dist-vX.Y.Z` tag of the `dist`
// branch). Desktops that read this manifest from GitHub download files from that ref.
// Builds without it (the Coolify site) serve files next to the manifest.
//
// The newest CHANGELOG.md entries ride along as `changelog`, so Genetics Lab can say what
// an update brings before it is applied and show "What's new" after it is.
//
// Optional package.json field:
//   "desktop": { "minVersion": "10.2.0" }
// Desktop builds older than minVersion keep their current copy instead of updating, for
// releases that depend on something new in the desktop host.

import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, statSync, writeFileSync, existsSync } from 'node:fs';
import { join, relative, resolve, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

export const MANIFEST_NAME = 'genetics-lab-manifest.json';

// Source maps are only for debugging; the desktop never requests them.
const EXCLUDED = [/\.map$/i, new RegExp(`^${MANIFEST_NAME.replace('.', '\\.')}$`)];

function walk(dir, root, out) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, root, out);
    } else if (entry.isFile()) {
      const path = relative(root, full).split(sep).join('/');
      if (!EXCLUDED.some((re) => re.test(path))) out.push({ full, path });
    }
  }
  return out;
}

const SAFE_REF = /^[A-Za-z0-9._-]{1,100}$/;
const CHANGELOG_ENTRIES = 15;

// "* feat(livestock): show sex with icons ([02d5a0a](https://...))" -> "Show sex with icons"
function cleanChangelogItem(line) {
  let text = line
    .replace(/\s*\(\[[0-9a-f]{7,40}\]\([^)]*\)\)\s*$/i, '')
    .replace(/^[a-z]+(\([^)]*\))?!?:\s*/i, '')
    .trim();
  return text ? text[0].toUpperCase() + text.slice(1) : '';
}

/** Parses the release-please style CHANGELOG.md into [{ version, date, sections }]. */
export function parseChangelog(markdown, limit = CHANGELOG_ENTRIES) {
  const entries = [];
  let entry = null;
  let section = null;

  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trim();
    const heading = line.match(/^##\s+\[([^\]]+)\](?:\([^)]*\))?\s*(?:\(([^)]+)\))?/);
    if (heading) {
      if (entries.length >= limit) break;
      entry = { version: heading[1].replace(/^v/, ''), date: heading[2] ?? null, sections: [] };
      section = null;
      entries.push(entry);
      continue;
    }
    if (!entry) continue;

    const sub = line.match(/^###\s+(.+)$/);
    if (sub) {
      // Drop the leading emoji: "🚀 Features" -> "Features".
      section = { title: sub[1].replace(/^[^\p{L}\p{N}]+/u, '').trim(), items: [] };
      entry.sections.push(section);
      continue;
    }

    const item = line.match(/^[*-]\s+(.+)$/);
    if (item) {
      if (!section) {
        section = { title: 'Changes', items: [] };
        entry.sections.push(section);
      }
      const text = cleanChangelogItem(item[1]);
      if (text) section.items.push(text);
    }
  }

  return entries.filter((e) => e.sections.some((s) => s.items.length > 0));
}

export function buildManifest(distDir, pkg, distRef = null, changelogMarkdown = '') {
  if (distRef != null && !SAFE_REF.test(distRef)) {
    throw new Error(`Invalid GENETICS_LAB_DIST_REF '${distRef}'`);
  }

  const files = walk(distDir, distDir, [])
    .sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0))
    .map(({ full, path }) => {
      const data = readFileSync(full);
      return { path, sha256: createHash('sha256').update(data).digest('hex'), size: statSync(full).size };
    });

  // Content id of the whole build: changes whenever any shipped file changes, even when
  // the package version does not (the site redeploys on every push to master).
  const buildId = createHash('sha256')
    .update(files.map((f) => `${f.path}\n${f.sha256}\n`).join(''))
    .digest('hex');

  return {
    schema: 1,
    name: pkg.name,
    version: pkg.version,
    buildId,
    minDesktopVersion: pkg.desktop?.minVersion ?? null,
    distRef: distRef || null,
    changelog: parseChangelog(changelogMarkdown),
    generatedAt: new Date().toISOString(),
    files
  };
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  const rootDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const distDir = join(rootDir, 'dist');
  if (!existsSync(join(distDir, 'index.html'))) {
    console.error(`[manifest] ${distDir}/index.html not found; run vite build first.`);
    process.exit(1);
  }

  const pkg = JSON.parse(readFileSync(join(rootDir, 'package.json'), 'utf-8'));
  const changelogPath = join(rootDir, 'CHANGELOG.md');
  const changelog = existsSync(changelogPath) ? readFileSync(changelogPath, 'utf-8') : '';
  const manifest = buildManifest(distDir, pkg, process.env.GENETICS_LAB_DIST_REF || null, changelog);
  writeFileSync(join(distDir, MANIFEST_NAME), JSON.stringify(manifest, null, 2) + '\n');

  const totalMb = manifest.files.reduce((n, f) => n + f.size, 0) / 1024 / 1024;
  console.log(
    `[manifest] ${MANIFEST_NAME}: v${manifest.version}, ${manifest.files.length} files, ` +
      `${totalMb.toFixed(1)} MB, build ${manifest.buildId.slice(0, 12)}`
  );
}
