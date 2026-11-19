import { defineConfig } from "vitest/config";
import { buildVersion } from "./src/telemetry/buildVersion";

// In development the server runs on :5080; the browser only talks to Vite, which forwards these.
const server = "http://localhost:5080";

export default defineConfig({
  // The commit the image is built from, for service.version on the browser's telemetry (src/telemetry/version.ts).
  define: {
    // The config runs in Node, whose types the client (and this file) are checked without.
    __APP_VERSION__: JSON.stringify(buildVersion(
      (globalThis as { process?: { env: Record<string, string | undefined> } }).process?.env.SOURCE_REVISION)),
  },
  // The cat model (src/models/cat.glb) is an asset: the scene imports it ?url, and cat.test.ts ?inline, which Vite
  // otherwise tries to parse as JavaScript.
  assetsInclude: ["**/*.glb"],
  build: {
    // Two pages: the game, and the admin page (served at /admin, to admins only), which loads none of the game.
    rollupOptions: {
      input: {
        game: "index.html",
        admin: "admin.html",
      },
    },
  },
  server: {
    proxy: {
      "/auth": server,
      "/api": server,
      // The page itself only (the server checks who asks); admin.html, in development, is Vite's.
      "^/admin$": server,
      "/hubs": { target: server, ws: true },
    },
  },
  test: {
    environment: "node",
  },
});
