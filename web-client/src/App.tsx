import { useCallback, useEffect, useState, type FormEvent, type ChangeEvent } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

type User = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
};

type ServerProfile = {
  id: string;
  name: string;
  host: string;
  port: number;
  createdAtUtc: string;
  connectionStatus: string;
  isPaired: boolean;
};

type FcmStatus = { configured: boolean; listening: boolean };
type AlarmAlert = { serverId?: string; title: string; message: string; receivedAtUtc: string };
type PairedDevice = { id: string; entityId: string; name: string; type: string; isActive?: boolean };
type ServerSnapshot = {
  info: { name?: string; playerCount?: number; maxPlayerCount?: number } | null;
  map: { width?: number; height?: number; monuments?: { name?: string }[]; imageBase64?: string | null } | null;
  team: unknown;
  retrievedAtUtc: string;
};

async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  const response = await fetch(path, {
    ...init,
    headers,
    credentials: "same-origin"
  });

  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const body = await response.json() as { error?: string; title?: string; errors?: string[] };
      message = body.error ?? body.title ?? body.errors?.join(" ") ?? message;
    } catch {
      // Some error responses intentionally have no JSON body.
    }
    throw new Error(message);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");
  const [authError, setAuthError] = useState("");
  const [busy, setBusy] = useState(false);
  const [servers, setServers] = useState<ServerProfile[]>([]);
  const [serverError, setServerError] = useState("");
  const [showAddServer, setShowAddServer] = useState(false);
  const [fcmStatus, setFcmStatus] = useState<FcmStatus>({ configured: false, listening: false });
  const [fcmMessage, setFcmMessage] = useState("");
  const [selectedServer, setSelectedServer] = useState<ServerProfile | null>(null);
  const [snapshot, setSnapshot] = useState<ServerSnapshot | null>(null);
  const [snapshotError, setSnapshotError] = useState("");
  const [snapshotBusy, setSnapshotBusy] = useState(false);
  const [devices, setDevices] = useState<PairedDevice[]>([]);
  const [deviceError, setDeviceError] = useState("");
  const [deviceActionId, setDeviceActionId] = useState<string | null>(null);
  const [alarms, setAlarms] = useState<AlarmAlert[]>([]);

  const refreshServers = useCallback(async () => {
    try {
      setServers(await api<ServerProfile[]>("/api/servers"));
      setServerError("");
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not load server profiles.");
    }
  }, []);

  useEffect(() => {
    let active = true;
    api<User>("/api/auth/me")
      .then((currentUser) => {
        if (active) setUser(currentUser);
      })
      .catch(() => {
        if (active) setUser(null);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!user) {
      setServers([]);
      return;
    }

    void refreshServers();
    if (user.roles.includes("Admin")) {
      api<FcmStatus>("/api/admin/fcm-status").then(setFcmStatus).catch(() => undefined);
    }
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/servers")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("ServersChanged", () => { void refreshServers(); });
    connection.on("ServerStatusChanged", () => { void refreshServers(); });
    connection.on("DevicesChanged", () => { void refreshServers(); });
    connection.on("FcmStatusChanged", (status: FcmStatus) => setFcmStatus(status));
    connection.on("AlarmReceived", (alarm: AlarmAlert) => setAlarms((current) => [alarm, ...current].slice(0, 5)));
    connection.onreconnected(() => { void refreshServers(); });
    void connection.start().catch((error: unknown) => {
      console.warn("Realtime updates are unavailable; refresh the page to sync.", error);
    });

    return () => { void connection.stop(); };
  }, [user, refreshServers]);

  async function submitAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setAuthError("");
    const form = new FormData(event.currentTarget);
    const payload = authMode === "login"
      ? {
          email: String(form.get("email") ?? ""),
          password: String(form.get("password") ?? ""),
          rememberMe: true
        }
      : {
          email: String(form.get("email") ?? ""),
          displayName: String(form.get("displayName") ?? ""),
          password: String(form.get("password") ?? ""),
          inviteCode: String(form.get("inviteCode") ?? ""),
          rememberMe: true
        };

    try {
      const signedIn = await api<User>(`/api/auth/${authMode}`, {
        method: "POST",
        body: JSON.stringify(payload)
      });
      setUser(signedIn);
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : "Authentication failed.");
    } finally {
      setBusy(false);
    }
  }

  async function submitServer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setServerError("");
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    try {
      await api<ServerProfile>("/api/servers", {
        method: "POST",
        body: JSON.stringify({
          name: String(form.get("name") ?? ""),
          host: String(form.get("host") ?? ""),
          port: Number(form.get("port") ?? 28082)
        })
      });
      setShowAddServer(false);
      formElement.reset();
      await refreshServers();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not save this profile.");
    } finally {
      setBusy(false);
    }
  }

  async function removeServer(server: ServerProfile) {
    const action = server.isPaired ? "Remove the Rust+ pairing for" : "Delete the saved profile";
    if (!window.confirm(`${action} “${server.name}”?`)) return;
    try {
      const path = server.isPaired ? `/api/paired-servers/${server.id}` : `/api/servers/${server.id}`;
      await api<void>(path, { method: "DELETE" });
      await refreshServers();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not remove this server.");
    }
  }

  async function uploadFcmCredentials(event: ChangeEvent<HTMLInputElement>) {
    const inputElement = event.currentTarget;
    const file = inputElement.files?.[0];
    if (!file) return;
    setBusy(true);
    setFcmMessage("Uploading credentials and connecting to FCM…");
    try {
      const credentialsJson = await file.text();
      const status = await api<FcmStatus>("/api/admin/fcm-credentials", {
        method: "POST",
        body: JSON.stringify({ credentialsJson })
      });
      setFcmStatus(status);
      setFcmMessage(status.listening
        ? "FCM listener connected. Pair a server in the Rust+ mobile app."
        : "Credentials saved. The listener is not connected yet; check network access and retry if needed.");
    } catch (error) {
      setFcmMessage(error instanceof Error ? error.message : "Could not configure FCM credentials.");
    } finally {
      setBusy(false);
      inputElement.value = "";
    }
  }

  async function openSnapshot(server: ServerProfile) {
    setSelectedServer(server);
    setSnapshot(null);
    setDevices([]);
    setSnapshotError("");
    setDeviceError("");
    setSnapshotBusy(true);
    try {
      setSnapshot(await api<ServerSnapshot>(`/api/servers/${server.id}/snapshot`));
    } catch (error) {
      setSnapshotError(error instanceof Error ? error.message : "Could not load live server data.");
    }
    try {
      setDevices(await api<PairedDevice[]>(`/api/servers/${server.id}/devices`));
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not load paired devices.");
    } finally {
      setSnapshotBusy(false);
    }
  }

  async function toggleDevice(server: ServerProfile, device: PairedDevice) {
    setDeviceActionId(device.entityId);
    setDeviceError("");
    try {
      const result = await api<{ success: boolean; isActive: boolean | null; error?: string }>(
        `/api/servers/${server.id}/devices/${device.entityId}/toggle`,
        { method: "POST", body: JSON.stringify({}) }
      );
      if (!result.success) throw new Error(result.error ?? "Switch command failed.");
      setDevices((current) => current.map((item) =>
        item.entityId === device.entityId ? { ...item, isActive: result.isActive ?? undefined } : item
      ));
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not control this device.");
    } finally {
      setDeviceActionId(null);
    }
  }

  async function signOut() {
    try {
      await api<void>("/api/auth/logout", { method: "POST" });
    } finally {
      setUser(null);
    }
  }

  if (loading) {
    return <main className="loading-screen"><div className="spinner" /><p>Loading Rust+ Web…</p></main>;
  }

  if (!user) {
    return (
      <main className="auth-layout">
        <section className="brand-panel">
          <div className="brand-mark">R+</div>
          <p className="eyebrow">SELF-HOSTED SERVER DASHBOARD</p>
          <h1>Your Rust+ tools.<br /><span>Your own server.</span></h1>
          <p className="brand-copy">A private foundation for your team dashboard. Your account and saved profiles stay on your own deployment.</p>
          <div className="privacy-note"><span className="privacy-dot" /> No dependency on the Rust+ Desktop cloud</div>
        </section>
        <section className="auth-panel">
          <div className="auth-card">
            <p className="eyebrow">{authMode === "login" ? "WELCOME BACK" : "TEAM ACCESS"}</p>
            <h2>{authMode === "login" ? "Sign in" : "Create your account"}</h2>
            <p className="muted">{authMode === "login" ? "Sign in to manage your saved server profiles." : "An invite code from your server administrator is required."}</p>
            <form onSubmit={submitAuth} className="stack-form">
              {authMode === "register" && (
                <>
                  <label>Display name<input name="displayName" autoComplete="nickname" required maxLength={80} /></label>
                  <label>Invite code<input name="inviteCode" autoComplete="off" required /></label>
                </>
              )}
              <label>Email address<input name="email" type="email" autoComplete="email" required maxLength={254} /></label>
              <label>Password<input name="password" type="password" autoComplete={authMode === "login" ? "current-password" : "new-password"} required minLength={authMode === "register" ? 12 : 1} /></label>
              {authMode === "register" && <p className="field-hint">Use at least 12 characters with uppercase, lowercase, a number, and a symbol.</p>}
              {authError && <div className="error-banner" role="alert">{authError}</div>}
              <button className="primary-button" type="submit" disabled={busy}>{busy ? "Please wait…" : authMode === "login" ? "Sign in" : "Create account"}</button>
            </form>
            <div className="auth-switch">
              {authMode === "login" ? "Have an invite?" : "Already have an account?"}
              <button className="text-button" onClick={() => { setAuthMode(authMode === "login" ? "register" : "login"); setAuthError(""); }}>
                {authMode === "login" ? "Create account" : "Sign in"}
              </button>
            </div>
          </div>
        </section>
      </main>
    );
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand-lockup"><div className="brand-mark small">R+</div><div><strong>Rust+ Web</strong><span>SELF-HOSTED</span></div></div>
        <div className="sidebar-label">WORKSPACE</div>
        <div className="nav-item active"><span className="nav-icon">◫</span> Server dashboard</div>
        <div className="sidebar-bottom">
          <div className="profile-avatar">{user.displayName.slice(0, 1).toUpperCase()}</div>
          <div className="profile-text"><strong>{user.displayName}</strong><span>{user.email}</span></div>
          <button className="icon-button" title="Sign out" onClick={() => void signOut()}>↗</button>
        </div>
      </aside>

      <section className="main-panel">
        <header className="topbar">
          <div><p className="eyebrow">WORKSPACE / SERVERS</p><h1>Server dashboard</h1></div>
          <div className="topbar-actions"><span className="secure-pill"><span /> Private workspace</span><button className="primary-button compact" onClick={() => setShowAddServer(true)}>＋ Add server</button></div>
        </header>

        <div className="content-area">
          {alarms.length > 0 && <section className="alarm-dock"><div className="alarm-dock-heading"><span className="alarm-indicator">!</span><div><strong>Smart alarm activity</strong><p>Recent alarms received by this web session</p></div><button className="text-button" onClick={() => setAlarms([])}>Clear</button></div>{alarms.map((alarm, index) => <div className="alarm-row" key={`${alarm.serverId ?? "server"}-${alarm.receivedAtUtc}-${index}`}><div><strong>{alarm.title || "Smart alarm"}</strong><p>{alarm.message}</p></div><time>{new Date(alarm.receivedAtUtc).toLocaleTimeString()}</time></div>)}</section>}
          <section className="welcome-strip">
            <div className="welcome-icon">⌁</div>
            <div><strong>Welcome in, {user.displayName}</strong><p>Your self-hosted workspace is ready. Add a profile to start organizing your servers.</p></div>
            <span className="role-chip">{user.roles.join(" · ")}</span>
          </section>

          {user.roles.includes("Admin") && (
            <section className="pairing-setup">
              <div className="pairing-setup-copy"><p className="eyebrow">ONE-TIME SETUP</p><h2>Rust+ pairing listener</h2><p>Upload a native RustPlusApi credentials file or an existing rustplus.js config. Credentials are encrypted on the server and never sent back to the browser.</p><div className="pairing-status"><span className={fcmStatus.listening ? "status-light online" : "status-light"} />{fcmStatus.listening ? "Listener connected" : fcmStatus.configured ? "Configured, not connected" : "Not configured"}</div>{fcmMessage && <p className="pairing-message" role="status">{fcmMessage}</p>}</div>
              <label className="upload-button">Upload credentials<input type="file" accept=".json,application/json" disabled={busy} onChange={(event) => void uploadFcmCredentials(event)} /></label>
            </section>
          )}

          <div className="section-heading"><div><h2>Your servers</h2><p>{servers.length} saved {servers.length === 1 ? "profile" : "profiles"}</p></div><button className="secondary-button" onClick={() => void refreshServers()}>↻ Refresh</button></div>

          {serverError && <div className="error-banner" role="alert">{serverError}</div>}

          {servers.length === 0 ? (
            <section className="empty-state">
              <div className="empty-illustration"><span>⌖</span><i /><i /><i /></div>
              <h3>No server profiles yet</h3>
              <p>Add your first server address. This saves a profile only; live Rust+ pairing and connection support are still being built.</p>
              <button className="primary-button" onClick={() => setShowAddServer(true)}>＋ Add your first server</button>
            </section>
          ) : (
            <div className="server-grid">
              {servers.map((server) => (
                <article className="server-card" key={server.id}>
                  <div className="server-card-top"><div className="server-symbol">⌘</div><span className={`status-pill ${server.connectionStatus.toLowerCase()}`}><i /> {server.connectionStatus}</span></div>
                  <h3>{server.name}</h3><p className="server-address">{server.host}:{server.port}</p>
                  <div className="server-card-divider" />
                  <div className="server-card-bottom"><span>{server.isPaired ? "Paired via Rust+" : "Profile saved"}</span>{(!server.isPaired || user.roles.includes("Admin")) && <button className="text-button danger-text" onClick={() => void removeServer(server)}>{server.isPaired ? "Remove pairing" : "Delete"}</button>}</div>
                  {server.isPaired ? <button className="secondary-button snapshot-button" onClick={() => void openSnapshot(server)}>View live data ↗</button> : <p className="adapter-note">Pair this server in the Rust+ mobile app after the FCM listener is configured.</p>}
                </article>
              ))}
            </div>
          )}

          <section className="roadmap-note"><div className="roadmap-icon">↗</div><div><strong>Foundation build</strong><p>Authentication, private server profiles, and realtime profile updates are in place. Pairing, live map data, team state, and device control are the next milestones.</p></div></section>
        </div>
      </section>

      {selectedServer && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelectedServer(null); }}>
          <section className="modal-card snapshot-modal" role="dialog" aria-modal="true" aria-labelledby="snapshot-title">
            <button className="modal-close" aria-label="Close" onClick={() => setSelectedServer(null)}>×</button>
            <p className="eyebrow">LIVE RUST+ DATA</p><h2 id="snapshot-title">{selectedServer.name}</h2>
            <p className="muted">{selectedServer.host}:{selectedServer.port}</p>
            {snapshotBusy && <p className="muted">Requesting server info, map, and team data…</p>}
            {snapshotError && <div className="error-banner" role="alert">{snapshotError}</div>}
            {snapshot && <>
              <div className="snapshot-stats"><div><span>Players online</span><strong>{snapshot.info?.playerCount ?? "?"} / {snapshot.info?.maxPlayerCount ?? "?"}</strong></div><div><span>Map size</span><strong>{snapshot.map?.width ?? "?"} × {snapshot.map?.height ?? "?"}</strong></div></div>
              {snapshot.map?.imageBase64 && <img className="snapshot-map" alt="Rust server map" src={`data:image/jpeg;base64,${snapshot.map.imageBase64}`} />}
              {snapshot.map?.monuments?.length ? <><h3 className="snapshot-subheading">Monuments</h3><div className="monument-list">{snapshot.map.monuments.slice(0, 24).map((monument, index) => <span key={`${monument.name ?? "monument"}-${index}`}>{monument.name ?? "Unknown monument"}</span>)}</div></> : null}
              <h3 className="snapshot-subheading">Team data</h3><pre className="snapshot-json">{JSON.stringify(snapshot.team, null, 2)}</pre>
              <p className="snapshot-timestamp">Retrieved {new Date(snapshot.retrievedAtUtc).toLocaleString()}</p>
            </>}
            {selectedServer.isPaired && <>
              <h3 className="snapshot-subheading">Paired smart devices</h3>
              {deviceError && <div className="error-banner" role="alert">{deviceError}</div>}
              {devices.length === 0 ? <p className="muted">No paired devices have been received yet. Pair switches, alarms, or storage monitors from the Rust+ mobile app.</p> : <div className="device-list">{devices.map((device) => <div className="device-row" key={device.id}><div className="device-symbol">{device.type === "Switch" ? "⏻" : device.type === "Alarm" ? "◉" : "▤"}</div><div className="device-row-info"><strong>{device.name}</strong><span>{device.type} · ID {device.entityId}</span></div><div className="device-row-action">{device.type === "Switch" ? <><span className={device.isActive === undefined ? "device-state unknown" : device.isActive ? "device-state on" : "device-state"}>{device.isActive === undefined ? "State unknown" : device.isActive ? "On" : "Off"}</span>{(user.roles.includes("Admin") || user.roles.includes("Member")) && <button className="secondary-button" disabled={deviceActionId === device.entityId} onClick={() => void toggleDevice(selectedServer, device)}>{deviceActionId === device.entityId ? "Sending…" : "Toggle"}</button>}</> : <span className="device-state unknown">Read only</span>}</div></div>)}</div>}
            </>}
            <div className="modal-actions"><button className="secondary-button" onClick={() => void openSnapshot(selectedServer)} disabled={snapshotBusy}>↻ Refresh data</button><button className="primary-button" onClick={() => setSelectedServer(null)}>Close</button></div>
          </section>
        </div>
      )}

      {showAddServer && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setShowAddServer(false); }}>
          <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="add-server-title">
            <button className="modal-close" aria-label="Close" onClick={() => setShowAddServer(false)}>×</button>
            <p className="eyebrow">SERVER CONFIGURATION</p><h2 id="add-server-title">Add server profile</h2>
            <p className="muted">This saves a private placeholder profile. For live data, an admin must configure the FCM listener and pair the server through the Rust+ mobile app.</p>
            <form onSubmit={submitServer} className="stack-form">
              <label>Profile name<input name="name" placeholder="Main base" required maxLength={80} /></label>
              <label>Host or IP address<input name="host" placeholder="play.example.net" required maxLength={255} /></label>
              <label>Rust+ port<input name="port" type="number" defaultValue={28082} min={1} max={65535} required /></label>
              {serverError && <div className="error-banner" role="alert">{serverError}</div>}
              <div className="modal-actions"><button type="button" className="secondary-button" onClick={() => setShowAddServer(false)}>Cancel</button><button type="submit" className="primary-button" disabled={busy}>{busy ? "Saving…" : "Save profile"}</button></div>
            </form>
          </section>
        </div>
      )}
    </main>
  );
}
