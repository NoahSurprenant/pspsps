import { defineConfig, devices } from "@playwright/test";

// The two-pod run (cluster/cluster.spec.ts): CI's two-pod job starts the pods with cluster/pods.sh first. Not part of
// `npm test`, which runs against one pod.
export default defineConfig({
  testDir: "cluster",
  // A rolling update starts a pod and waits for another to leave the cluster.
  timeout: 240_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [["list"], ["html", { open: "never", outputFolder: "playwright-report-cluster" }]] : "list",
  use: {
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
