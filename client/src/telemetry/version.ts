/**
 * The build this page runs: the commit it was built from (SOURCE_REVISION, which the image build passes; its first seven
 * characters, as the server's service.version), or "dev". Put in by Vite at build time (vite.config.ts).
 */
export const appVersion: string = __APP_VERSION__;
