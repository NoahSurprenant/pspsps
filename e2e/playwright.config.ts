import { defineConfig, devices } from "@playwright/test";

const suite = process.env.E2E_SUITE;
const layout = "**/layout.spec.ts";
const accessibility = "**/a11y.spec.ts";

// The game must already be running at E2E_BASE_URL (CI runs the Docker image against PostgreSQL; locally, run the
// server and point this at it).
export default defineConfig({
  testDir: "tests",
  testMatch: suite === "layout" ? layout : suite === "accessibility" ? accessibility : undefined,
  testIgnore: suite === "gameplay" ? [layout, accessibility] : undefined,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  // Shard by individual case; layout runs two browser workers on the 4-CPU runner, the rest one.
  fullyParallel: suite !== undefined,
  workers: suite === "layout" ? 2 : 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:8080",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 1280, height: 800 },
        // WebGL without a GPU: SwiftShader, Chromium's software renderer.
        launchOptions: { args: ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"] },
      },
    },
  ],
});
