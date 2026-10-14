import { useCallback, useEffect, useState, type FormEvent } from "react";
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
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/servers")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("ServersChanged", () => { void refreshServers(); });
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
    const form = new FormData(event.currentTarget);
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
      event.currentTarget.reset();
      await refreshServers();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not save this profile.");
    } finally {
      setBusy(false);
    }
  }

  async function removeServer(server: ServerProfile) {
    if (!window.confirm(`Delete the saved profile “${server.name}”?`)) return;
    try {
      await api<void>(`/api/servers/${server.id}`, { method: "DELETE" });
      await refreshServers();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not delete this profile.");
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
          <section className="welcome-strip">
            <div className="welcome-icon">⌁</div>
            <div><strong>Welcome in, {user.displayName}</strong><p>Your self-hosted workspace is ready. Add a profile to start organizing your servers.</p></div>
            <span className="role-chip">{user.roles.join(" · ")}</span>
          </section>

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
                  <div className="server-card-top"><div className="server-symbol">⌘</div><span className="status-pill"><i /> Not connected</span></div>
                  <h3>{server.name}</h3><p className="server-address">{server.host}:{server.port}</p>
                  <div className="server-card-divider" />
                  <div className="server-card-bottom"><span>Profile saved</span><button className="text-button danger-text" onClick={() => void removeServer(server)}>Delete</button></div>
                  <p className="adapter-note">Rust+ connection adapter pending</p>
                </article>
              ))}
            </div>
          )}

          <section className="roadmap-note"><div className="roadmap-icon">↗</div><div><strong>Foundation build</strong><p>Authentication, private server profiles, and realtime profile updates are in place. Pairing, live map data, team state, and device control are the next milestones.</p></div></section>
        </div>
      </section>

      {showAddServer && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setShowAddServer(false); }}>
          <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="add-server-title">
            <button className="modal-close" aria-label="Close" onClick={() => setShowAddServer(false)}>×</button>
            <p className="eyebrow">SERVER CONFIGURATION</p><h2 id="add-server-title">Add server profile</h2>
            <p className="muted">Enter a label and the server endpoint. Connection credentials and pairing will be added in a later step.</p>
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
