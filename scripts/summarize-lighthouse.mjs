#!/usr/bin/env node
// Builds a small JSON summary from a Lighthouse report (lhr-*.json) for the
// lighthouse-audit artifact: scores for every category plus the core web
// vitals. Usage: node scripts/summarize-lighthouse.mjs <lhr.json> [out.json]
import { readFileSync, writeFileSync } from "node:fs";

const [lhrPath, outPath] = process.argv.slice(2);
if (!lhrPath) {
  console.error("usage: node scripts/summarize-lighthouse.mjs <lhr.json> [out.json]");
  process.exit(2);
}

const report = JSON.parse(readFileSync(lhrPath, "utf8"));
const metric = (auditId) => report.audits?.[auditId]?.numericValue ?? null;

const summary = {
  url: report.finalDisplayedUrl ?? report.requestedUrl,
  fetchedAt: report.fetchTime,
  formFactor: report.configSettings?.formFactor,
  categories: Object.fromEntries(
    Object.entries(report.categories ?? {}).map(([id, category]) => [
      id,
      category.score === null ? null : Math.round(category.score * 100),
    ]),
  ),
  metrics: {
    lcpMs: metric("largest-contentful-paint"),
    tbtMs: metric("total-blocking-time"),
    cls: metric("cumulative-layout-shift"),
    speedIndexMs: metric("speed-index"),
    ttfbMs: metric("server-response-ttfb"),
  },
};

const json = JSON.stringify(summary, null, 2);
if (outPath) writeFileSync(outPath, `${json}\n`);
console.log(json);
