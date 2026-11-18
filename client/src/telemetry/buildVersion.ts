/**
 * The build's version, for service.version on the browser's telemetry: the commit the image is built from
 * (SOURCE_REVISION, the Dockerfile's build argument), its first seven characters as the server shortens its own; "dev"
 * without one. Read by vite.config.ts at build time.
 */
export function buildVersion(revision: string | undefined): string {
  const trimmed = revision?.trim() ?? "";
  return /^[0-9A-Za-z._-]+$/.test(trimmed) ? trimmed.slice(0, 7) : "dev";
}
