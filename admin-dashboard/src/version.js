/* global __APP_VERSION__ */
// The dashboard release, injected by Vite from package.json at build time.
export const APP_VERSION = `v${typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : '0.0.0'}`;
