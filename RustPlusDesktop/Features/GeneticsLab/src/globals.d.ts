/// <reference types="vite/client" />

// Injected at build time by Vite (see vite.config.ts) from package.json.
declare const __APP_VERSION__: string;

interface Window {
  /**
   * Set by the desktop host when the Genetics Lab is embedded inside the paid
   * RUST+ Desktop app, so the web feature can hide promotional slots for
   * Premium users. Absent on the free standalone web build.
   */
  __RGL_PREMIUM__?: boolean;
}
