// How Genetics Lab learns about its own updates.
//
// Inside Rust+ Desktop (WebView2) the host downloads new builds in the background and
// tells the page over `window.chrome.webview` messages; the page asks it to switch with
// `apply-update`. On the web build there is no host, so the page polls the deployed
// genetics-lab-manifest.json and reloads itself.

export interface ChangelogSection {
  title: string;
  items: string[];
}

export interface ChangelogEntry {
  version: string;
  date: string | null;
  sections: ChangelogSection[];
}

export type UpdateState = 'downloading' | 'ready';

export interface UpdateNotice {
  state: UpdateState;
  version: string | null;
  changelog: ChangelogEntry[];
}

interface UpdateManifest {
  version?: string;
  changelog?: ChangelogEntry[];
}

interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

const MANIFEST_URL = './genetics-lab-manifest.json';
const WEB_POLL_INTERVAL_MS = 30 * 60 * 1000;

function webview(): WebViewBridge | undefined {
  return (window as unknown as { chrome?: { webview?: WebViewBridge } }).chrome?.webview;
}

export const isDesktopHost = (): boolean => webview() !== undefined;

/** Compares dotted versions ("1.10.0" > "1.9.2"); a pre-release suffix is ignored. */
export function compareVersions(a: string, b: string): number {
  const parts = (v: string) => v.replace(/^v/, '').split(/[-+]/)[0].split('.').map((n) => parseInt(n, 10) || 0);
  const pa = parts(a);
  const pb = parts(b);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff !== 0) return diff;
  }
  return 0;
}

/** Entries newer than `from` and no newer than `to`, newest first. */
export function changesBetween(changelog: ChangelogEntry[], from: string, to: string): ChangelogEntry[] {
  return changelog
    .filter((e) => compareVersions(e.version, from) > 0 && compareVersions(e.version, to) <= 0)
    .sort((x, y) => compareVersions(y.version, x.version));
}

function isChangelog(value: unknown): value is ChangelogEntry[] {
  return Array.isArray(value) && value.every((e) => e && typeof e.version === 'string' && Array.isArray(e.sections));
}

/** The changelog shipped with the build that is running now. */
export async function fetchOwnChangelog(): Promise<ChangelogEntry[]> {
  try {
    const response = await fetch(MANIFEST_URL, { cache: 'no-store' });
    if (!response.ok) return [];
    const manifest = (await response.json()) as UpdateManifest;
    return isChangelog(manifest.changelog) ? manifest.changelog : [];
  } catch {
    return [];
  }
}

export interface UpdateChannelHandlers {
  onNotice: (notice: UpdateNotice) => void;
  /** The version this page replaced, when the host has just switched builds. */
  onPreviousVersion: (version: string) => void;
  /** The desktop host's own "Patch notes" button was pressed. */
  onOpenPatchNotes?: () => void;
}

/** Starts listening for updates. Returns a function that stops listening. */
export function connectUpdateChannel(handlers: UpdateChannelHandlers): () => void {
  const bridge = webview();
  return bridge ? connectDesktop(bridge, handlers) : connectWeb(handlers);
}

function connectDesktop(bridge: WebViewBridge, handlers: UpdateChannelHandlers): () => void {
  const listener = (event: { data: unknown }) => {
    const data = event.data as Record<string, unknown> | null;
    if (!data || typeof data !== 'object') return;

    if (data.type === 'genetics-lab-update' && (data.state === 'downloading' || data.state === 'ready')) {
      handlers.onNotice({
        state: data.state,
        version: typeof data.version === 'string' ? data.version : null,
        changelog: isChangelog(data.changelog) ? data.changelog : []
      });
    } else if (data.type === 'genetics-lab-host' && typeof data.previousVersion === 'string') {
      handlers.onPreviousVersion(data.previousVersion);
    } else if (data.type === 'open-patch-notes') {
      handlers.onOpenPatchNotes?.();
    }
  };

  bridge.addEventListener('message', listener);
  // Tells the host this build shows update notices itself, and asks for anything pending.
  bridge.postMessage({ type: 'host-hello', version: __APP_VERSION__, capabilities: ['update-notice', 'whats-new', 'patch-notes'] });
  return () => bridge.removeEventListener('message', listener);
}

function connectWeb(handlers: UpdateChannelHandlers): () => void {
  let stopped = false;

  const check = async () => {
    if (stopped || document.visibilityState === 'hidden') return;
    try {
      const response = await fetch(`${MANIFEST_URL}?t=${Date.now()}`, { cache: 'no-store' });
      if (!response.ok) return;
      const manifest = (await response.json()) as UpdateManifest;
      if (!stopped && manifest.version && compareVersions(manifest.version, __APP_VERSION__) > 0) {
        handlers.onNotice({
          state: 'ready',
          version: manifest.version,
          changelog: isChangelog(manifest.changelog) ? manifest.changelog : []
        });
      }
    } catch {
      // Offline or not deployed with a manifest; try again later.
    }
  };

  const timer = window.setInterval(check, WEB_POLL_INTERVAL_MS);
  document.addEventListener('visibilitychange', check);
  return () => {
    stopped = true;
    window.clearInterval(timer);
    document.removeEventListener('visibilitychange', check);
  };
}

/** Switches to the new build: the desktop host remaps the files, the web build just reloads. */
export function applyUpdate(): void {
  const bridge = webview();
  if (bridge) {
    bridge.postMessage({ type: 'apply-update' });
  } else {
    window.location.reload();
  }
}
