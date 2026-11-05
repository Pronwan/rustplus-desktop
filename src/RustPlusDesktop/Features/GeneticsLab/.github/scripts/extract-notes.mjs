#!/usr/bin/env node

import { readFileSync, writeFileSync, existsSync, appendFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const rootDir = resolve(__dirname, '../..');

const version = (process.argv[2] || process.env.VERSION || '').replace(/^v/, '').trim();
const outputFile = process.env.GITHUB_OUTPUT;

function setGithubOutput(key, value) {
  if (outputFile) {
    appendFileSync(outputFile, `${key}=${value}\n`);
  }
}

// 1. Check if release notes file was already generated in this run
const directNotes = resolve(rootDir, '.github/release-notes.md');
if (existsSync(directNotes)) {
  console.log(`Using existing release notes file: ${directNotes}`);
  setGithubOutput('notes_file', '.github/release-notes.md');
  process.exit(0);
}

// 2. Extract from CHANGELOG.md
const changelogPath = resolve(rootDir, 'CHANGELOG.md');
if (existsSync(changelogPath) && version) {
  const content = readFileSync(changelogPath, 'utf-8');
  const lines = content.split('\n');
  const targetPrefix = `## [${version}]`;
  let capturing = false;
  const extracted = [];

  for (const line of lines) {
    if (line.startsWith(targetPrefix)) {
      capturing = true;
      extracted.push(line);
      continue;
    }
    if (capturing && line.startsWith('## [')) {
      break;
    }
    if (capturing) {
      extracted.push(line);
    }
  }

  const resultText = extracted.join('\n').trim();
  if (resultText) {
    const extractedFile = resolve(rootDir, 'extracted-notes.md');
    writeFileSync(extractedFile, resultText + '\n', 'utf-8');
    console.log(`Extracted release notes for version ${version} to ${extractedFile}`);
    setGithubOutput('notes_file', 'extracted-notes.md');
    process.exit(0);
  }
}

console.log('No release notes found in CHANGELOG.md; will fallback to --generate-notes');
setGithubOutput('notes_file', '');
