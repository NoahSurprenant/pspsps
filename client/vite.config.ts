import { defineConfig } from "vitest/config";

// In development the server runs on :5080; the browser only talks to Vite, which forwards these.
const server = "http://localhost:5080";

export default defineConfig({
  // The cat model (src/models/cat.glb) is an asset: the scene imports it ?url, and cat.test.ts ?inline, which Vite
  // otherwise tries to parse as JavaScript.
  assetsInclude: ["**/*.glb"],
  server: {
    proxy: {
      "/auth": server,
      "/api": server,
      "/hubs": { target: server, ws: true },
    },
  },
  test: {
    environment: "node",
  },
});
