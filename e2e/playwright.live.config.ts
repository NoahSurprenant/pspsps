import { defineConfig, devices } from "@playwright/test";

// The walk of the world map (tests/walkMap.spec.ts) against a running game: E2E_BASE_URL is the live game, `npm run test:live`.
export default defineConfig({
  testDir: "tests",
  testMatch: "**/walkMap.spec.ts",
  timeout: 240_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
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
        launchOptions: { args: ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-dev-shm-usage"] },
      },
    },
  ],
});
