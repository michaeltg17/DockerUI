// Lighthouse CI (https://github.com/GoogleChrome/lighthouse-ci).
//
// The CI build (ci.sh) brings up the lighthouse/ scenario (its own dashboard
// instance + demo stacks) before running the audit, so the page under test is
// the real dashboard with content. Scores are measured on Lighthouse's
// throttled mobile profile.
module.exports = {
  ci: {
    collect: {
      url: ['http://127.0.0.1:5015/'],
      // One run keeps CI fast; the page is small and local, so scores are stable.
      numberOfRuns: 1,
      settings: {
        // The CI container runs Chrome as root.
        chromeFlags: '--headless=new --no-sandbox --disable-dev-shm-usage',
      },
    },
    assert: {
      // The scores sit a little below the current baseline (performance ~95 with
      // the compressed static assets, everything else 100) and the metric budgets
      // are the standard "good" LCP/TBT/CLS values, all with headroom so real
      // regressions fail the build without flapping on run-to-run jitter.
      assertions: {
        'categories:performance': ['error', { minScore: 0.8 }],
        'categories:accessibility': ['error', { minScore: 0.95 }],
        'categories:best-practices': ['error', { minScore: 0.95 }],
        'categories:seo': ['error', { minScore: 0.95 }],
        'largest-contentful-paint': ['error', { maxNumericValue: 2500 }],
        'total-blocking-time': ['error', { maxNumericValue: 200 }],
        'cumulative-layout-shift': ['error', { maxNumericValue: 0.1 }],
      },
    },
  },
};
