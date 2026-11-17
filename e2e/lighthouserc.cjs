// Lighthouse CI (lhci autorun), run by the e2e job against the running game: the mobile settings (Lighthouse's
// default: a mid-range phone on a slow 4G network) or the desktop ones (LIGHTHOUSE_PRESET=desktop), signed out (the
// sign-in screen) or signed in (LIGHTHOUSE_COOKIE: a guest's cookie, so the world list). Accessibility, best practices
// and SEO must score 100; performance 98 on desktop and 80 on mobile.
const desktop = process.env.LIGHTHOUSE_PRESET === "desktop";
const base = process.env.E2E_BASE_URL ?? "http://localhost:8080";
const cookie = process.env.LIGHTHOUSE_COOKIE;

module.exports = {
  ci: {
    collect: {
      url: [`${base}/`],
      numberOfRuns: 1,
      settings: {
        ...(desktop ? { preset: "desktop" } : {}),
        ...(cookie ? { extraHeaders: JSON.stringify({ Cookie: cookie }) } : {}),
        // Software WebGL, as the e2e tests run it: the runner has no GPU.
        chromeFlags: "--headless=new --no-sandbox --use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader",
      },
    },
    assert: {
      assertions: {
        "categories:performance": ["error", { minScore: desktop ? 0.98 : 0.8 }],
        "categories:accessibility": ["error", { minScore: 1 }],
        "categories:best-practices": ["error", { minScore: 1 }],
        "categories:seo": ["error", { minScore: 1 }],
      },
    },
    // Public links to each report, kept for a week (the repository is public).
    upload: { target: "temporary-public-storage" },
  },
};
