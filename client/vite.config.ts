import { defineConfig } from "vitest/config";

// In development the server runs on :5080; the browser only talks to Vite, which forwards these.
const server = "http://localhost:5080";

export default defineConfig({
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
