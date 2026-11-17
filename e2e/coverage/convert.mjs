#!/usr/bin/env node
// Converts the e2e suite's browser coverage into an LCOV report for SonarCloud.
//
// Inputs (all produced during a ci.sh / local e2e run):
//   e2e/coverage/<scenario>/*.v8.json  raw CDP Coverage.stopJSCoverage payloads,
//                                      written by the .NET BrowserSession;
//   e2e/coverage/assets/               the e2e image's /app/wwwroot/assets,
//                                      extracted with docker cp (bundles + .map).
// Output:
//   e2e/coverage/ui.lcov               merged, ui/src-relative line coverage,
//                                      plus a per-folder hit-rate summary on stdout.
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import v8toIstanbul from 'v8-to-istanbul';

const coverageDir = path.dirname(fileURLToPath(import.meta.url));
const assetsDir = path.join(coverageDir, 'assets');

// repo path -> (line -> hits), covered wherever hits > 0 in any dump
const merged = new Map();
const skippedAssets = new Set();

function dumpFiles() {
  const files = [];

  for (const entry of readdirSync(coverageDir, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name === 'api' || entry.name === 'assets' || entry.name === 'node_modules')
      continue;

    const scenarioDir = path.join(coverageDir, entry.name);
    for (const file of readdirSync(scenarioDir))
      if (file.endsWith('.v8.json'))
        files.push(path.join(scenarioDir, file));
  }

  return files;
}

// v8-to-istanbul resolves the map's sources against the asset file's directory;
// the repo path is the part from the last 'src/' on ('../../src/features/x.ts'
// became '<...>/e2e/src/features/x.ts' by then, which still ends in the real
// ui/src path). node_modules, \0-prefixed virtual modules and ?-queried urls
// are not first-party code.
function toRepoPath(istanbulKey) {
  const normalized = istanbulKey.split('\\').join('/');

  if (normalized.includes('node_modules') || normalized.includes('\0') || normalized.includes('?'))
    return null;

  const match = normalized.match(/(?:^|\/)(src\/.*)$/);
  return match ? `ui/${match[1]}` : null;
}

async function convertScript(scriptCoverage) {
  const base = (scriptCoverage.url ?? '').split('/').pop().split('?')[0];
  const asset = path.join(assetsDir, base);
  const mapPath = `${asset}.map`;

  if (!base.endsWith('.js') || !existsSync(asset) || !existsSync(mapPath)) {
    if (base) skippedAssets.add(base);
    return;
  }

  const converter = new v8toIstanbul(asset, 0, {
    source: readFileSync(asset, 'utf8'),
    sourceMap: { sourcemap: JSON.parse(readFileSync(mapPath, 'utf8')) },
  });

  await converter.load();
  converter.applyCoverage(scriptCoverage.functions ?? []);

  for (const [key, data] of Object.entries(converter.toIstanbul())) {
    const repoPath = toRepoPath(key);
    if (!repoPath)
      continue;

    const lines = merged.get(repoPath) ?? new Map();

    for (const [id, location] of Object.entries(data.statementMap ?? {})) {
      const hits = data.s?.[id] ?? 0;
      for (let line = location.start.line; line <= location.end.line; line += 1)
        lines.set(line, Math.max(lines.get(line) ?? 0, hits));
    }

    merged.set(repoPath, lines);
  }
}

const files = dumpFiles();

if (files.length === 0) {
  console.log('ui coverage: no .v8.json dumps under e2e/coverage; nothing to convert.');
  process.exit(0);
}

if (!existsSync(assetsDir)) {
  console.error(`ui coverage: ${assetsDir} is missing (docker cp the e2e image's /app/wwwroot/assets first); nothing to convert.`);
  process.exit(0);
}

for (const file of files) {
  const { result } = JSON.parse(readFileSync(file, 'utf8'));
  for (const scriptCoverage of result ?? [])
    await convertScript(scriptCoverage);
}

let lcov = '';
let totalLines = 0;
let hitLines = 0;
const perFolder = new Map();

for (const [sf, lines] of [...merged].sort(([a], [b]) => a.localeCompare(b))) {
  const records = [...lines].sort((a, b) => a[0] - b[0]);
  const covered = records.filter(([, hits]) => hits > 0).length;

  totalLines += records.length;
  hitLines += covered;

  const folder = sf.split('/').slice(0, 3).join('/');
  const stats = perFolder.get(folder) ?? { lines: 0, covered: 0 };
  stats.lines += records.length;
  stats.covered += covered;
  perFolder.set(folder, stats);

  lcov += `SF:${sf}\n${records.map(([line, hits]) => `DA:${line},${hits}`).join('\n')}\nLF:${records.length}\nLH:${covered}\nend_of_record\n`;
}

writeFileSync(path.join(coverageDir, 'ui.lcov'), lcov);

console.log(`ui coverage: ${files.length} dump(s), ${merged.size} source file(s).`);
for (const [folder, stats] of [...perFolder].sort(([a], [b]) => a.localeCompare(b)))
  console.log(`  ${folder}: ${stats.covered}/${stats.lines} lines (${((stats.covered / stats.lines) * 100).toFixed(1)}%)`);
console.log(`  total: ${hitLines}/${totalLines} lines (${((hitLines / totalLines) * 100).toFixed(1)}%)`);

if (skippedAssets.size > 0)
  console.log(`  skipped (no local asset/source map): ${[...skippedAssets].sort().join(', ')}`);
