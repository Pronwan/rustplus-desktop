import { useCallback, useEffect, useRef, useState, type FormEvent, type ChangeEvent } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

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
  connectionEnabled: boolean;
};

type FcmStatus = { configured: boolean; listening: boolean };
type AlarmAlert = { serverId?: string; title: string; message: string; receivedAtUtc: string };
type AdminUser = { id: string; email: string; displayName: string; roles: string[] };
type PairedDevice = { id: string; entityId: string; name: string; type: string; groupId?: string | null; isActive?: boolean };
type DeviceGroup = { id: string; name: string; createdAtUtc: string; deviceCount: number };
type DeviceInfoData = { isActive?: boolean; capacity?: number | null; hasProtection?: boolean | null; protectionExpiry?: string; items?: { id: number; quantity?: number | null; isItemBlueprint?: boolean | null }[] | null };
type TeamChatMessage = { steamId: string; name: string; message: string; time: string };
type ServerTimer = { id: string; name: string; expiresAtUtc: string; createdAtUtc: string; fired: boolean; isOwner: boolean };
type TeamMember = { name?: string | null; x: number; y: number; isOnline: boolean; isAlive: boolean };
type ServerSnapshot = {
  info: { name?: string; playerCount?: number; maxPlayerCount?: number; mapSize?: number | null } | null;
  map: { width?: number; height?: number; oceanMargin?: number | null; monuments?: { name?: string }[]; imageBase64?: string | null } | null;
  team: { members?: TeamMember[] | null } | null;
  retrievedAtUtc: string;
};

function showDesktopNotification(title: string, body: string): void {
  if (typeof Notification !== "undefined" && Notification.permission === "granted") {
    try {
      new Notification(title, { body, tag: "rustplus-web-alert" });
    } catch {
      // The in-dashboard alert remains available if desktop notifications are blocked.
    }
  }
}

function formatProtectionExpiry(value?: string): string {
  if (!value) return "Unknown";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) || date.getFullYear() <= 1 ? "Unknown" : date.toLocaleString();
}

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
  const [realtimeStatus, setRealtimeStatus] = useState<"connecting" | "connected" | "reconnecting" | "disconnected">("connecting");
  const [notificationPermission, setNotificationPermission] = useState<NotificationPermission | "unsupported">(
    typeof Notification === "undefined" ? "unsupported" : Notification.permission
  );
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
  const [deviceGroups, setDeviceGroups] = useState<DeviceGroup[]>([]);
  const [groupName, setGroupName] = useState("");
  const [groupBusy, setGroupBusy] = useState(false);
  const [deviceError, setDeviceError] = useState("");
  const [deviceActionId, setDeviceActionId] = useState<string | null>(null);
  const [deviceInfoBusyId, setDeviceInfoBusyId] = useState<string | null>(null);
  const [deviceDetails, setDeviceDetails] = useState<Record<string, DeviceInfoData>>({});
  const [connectionActionId, setConnectionActionId] = useState<string | null>(null);
  const [alarms, setAlarms] = useState<AlarmAlert[]>([]);
  const [adminUsers, setAdminUsers] = useState<AdminUser[]>([]);
  const [roleBusyId, setRoleBusyId] = useState<string | null>(null);
  const [roleMessage, setRoleMessage] = useState("");
  const [chatMessages, setChatMessages] = useState<TeamChatMessage[]>([]);
  const [chatInput, setChatInput] = useState("");
  const [chatBusy, setChatBusy] = useState(false);
  const [chatError, setChatError] = useState("");
  const [timers, setTimers] = useState<ServerTimer[]>([]);
  const [timerName, setTimerName] = useState("");
  const [timerDurationMinutes, setTimerDurationMinutes] = useState(15);
  const [timerBusy, setTimerBusy] = useState(false);
  const [timerError, setTimerError] = useState("");
  const selectedServerIdRef = useRef<string | null>(null);

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
    selectedServerIdRef.current = selectedServer?.id ?? null;
  }, [selectedServer?.id]);

  useEffect(() => {
    if (!selectedServer?.isPaired || !selectedServer.connectionEnabled) return;

    let active = true;
    const refreshSnapshot = async () => {
      try {
        const next = await api<ServerSnapshot>(`/api/servers/${selectedServer.id}/snapshot`);
        if (active) {
          setSnapshot(next);
          setSnapshotError("");
        }
      } catch (error) {
        if (active) setSnapshotError(error instanceof Error ? error.message : "Live map refresh failed.");
      }
    };

    // Refresh only while a paired server's modal is open. The backend caches snapshots
    // per server so teammates share Rust+ reads rather than multiplying API traffic.
    const intervalId = window.setInterval(() => { void refreshSnapshot(); }, 30_000);
    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, [selectedServer?.id, selectedServer?.isPaired, selectedServer?.connectionEnabled]);

  useEffect(() => {
    if (!user) {
      setServers([]);
      setRealtimeStatus("disconnected");
      return;
    }

    setRealtimeStatus("connecting");

    void refreshServers();
    api<AlarmAlert[]>("/api/alarms?limit=50").then(setAlarms).catch(() => undefined);
    if (user.roles.includes("Admin")) {
      api<FcmStatus>("/api/admin/fcm-status").then(setFcmStatus).catch(() => undefined);
      api<AdminUser[]>("/api/admin/users").then(setAdminUsers).catch(() => undefined);
    }
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/servers")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    let active = true;
    let startInProgress = false;
    let retryTimer: number | undefined;
    const scheduleStartRetry = () => {
      if (!active || retryTimer !== undefined) return;
      retryTimer = window.setTimeout(() => {
        retryTimer = undefined;
        void startConnection();
      }, 5_000);
    };
    const startConnection = async () => {
      if (!active || startInProgress || connection.state !== HubConnectionState.Disconnected) return;
      startInProgress = true;
      setRealtimeStatus("connecting");
      try {
        await connection.start();
        if (active) {
          setRealtimeStatus("connected");
          void refreshServers();
          void api<AlarmAlert[]>("/api/alarms?limit=50").then(setAlarms).catch(() => undefined);
        }
      } catch (error) {
        if (active) {
          setRealtimeStatus("disconnected");
          console.warn("Realtime connection failed; retrying in five seconds.", error);
          scheduleStartRetry();
        }
      } finally {
        startInProgress = false;
      }
    };

    connection.on("ServersChanged", () => { void refreshServers(); });
    connection.on("ServerStatusChanged", () => { void refreshServers(); });
    connection.on("DevicesChanged", (payload: { serverId: string }) => {
      void refreshServers();
      if (selectedServerIdRef.current === payload.serverId) {
        void api<PairedDevice[]>(`/api/servers/${payload.serverId}/devices`).then(setDevices).catch(() => undefined);
        void api<DeviceGroup[]>(`/api/servers/${payload.serverId}/device-groups`).then(setDeviceGroups).catch(() => undefined);
      }
    });
    connection.on("FcmStatusChanged", (status: FcmStatus) => setFcmStatus(status));
    connection.on("AlarmReceived", (alarm: AlarmAlert) => {
      setAlarms((current) => [alarm, ...current].slice(0, 50));
      showDesktopNotification(alarm.title || "Rust+ smart alarm", alarm.message);
    });
    connection.on("TimersChanged", (payload: { serverId: string }) => {
      if (selectedServerIdRef.current === payload.serverId) {
        void api<ServerTimer[]>(`/api/servers/${payload.serverId}/timers`).then(setTimers).catch(() => undefined);
      }
    });
    connection.on("TimerExpired", (payload: { serverId: string; name: string; firedAtUtc: string }) => {
      setAlarms((current) => [{ serverId: payload.serverId, title: "Timer expired", message: payload.name, receivedAtUtc: payload.firedAtUtc }, ...current].slice(0, 50));
      showDesktopNotification("Rust+ timer expired", payload.name);
      if (selectedServerIdRef.current === payload.serverId) {
        void api<ServerTimer[]>(`/api/servers/${payload.serverId}/timers`).then(setTimers).catch(() => undefined);
      }
    });
    connection.on("TeamChatMessageReceived", (payload: { serverId: string; message: TeamChatMessage }) => {
      if (selectedServerIdRef.current === payload.serverId) {
        setChatMessages((current) => [...current, payload.message].slice(-100));
      }
    });
    connection.onreconnecting(() => { setRealtimeStatus("reconnecting"); });
    connection.onreconnected(() => {
      setRealtimeStatus("connected");
      void refreshServers();
      void api<AlarmAlert[]>("/api/alarms?limit=50").then(setAlarms).catch(() => undefined);
    });
    connection.onclose(() => {
      if (!active) return;
      setRealtimeStatus("disconnected");
      scheduleStartRetry();
    });
    void startConnection();

    return () => {
      active = false;
      if (retryTimer !== undefined) window.clearTimeout(retryTimer);
      void connection.stop();
    };
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
    setDeviceGroups([]);
    setDeviceDetails({});
    setGroupName("");
    setSnapshotError("");
    setDeviceError("");
    setChatMessages([]);
    setChatInput("");
    setChatError("");
    setTimers([]);
    setTimerError("");
    setTimerName("");
    setSnapshotBusy(true);
    try {
      setSnapshot(await api<ServerSnapshot>(`/api/servers/${server.id}/snapshot`));
    } catch (error) {
      setSnapshotError(error instanceof Error ? error.message : "Could not load live server data.");
    }
    try {
      setDevices(await api<PairedDevice[]>(`/api/servers/${server.id}/devices`));
      setDeviceGroups(await api<DeviceGroup[]>(`/api/servers/${server.id}/device-groups`));
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not load paired devices.");
    }
    try {
      setChatMessages(await api<TeamChatMessage[]>(`/api/servers/${server.id}/chat`));
    } catch (error) {
      setChatError(error instanceof Error ? error.message : "Could not load team chat.");
    }
    try {
      setTimers(await api<ServerTimer[]>(`/api/servers/${server.id}/timers`));
    } catch (error) {
      setTimerError(error instanceof Error ? error.message : "Could not load event timers.");
    } finally {
      setSnapshotBusy(false);
    }
  }

  async function updateUserRole(account: AdminUser, role: string) {
    setRoleBusyId(account.id);
    setRoleMessage("");
    try {
      const updated = await api<User>(`/api/admin/users/${account.id}/role`, {
        method: "PUT",
        body: JSON.stringify({ role })
      });
      setAdminUsers((current) => current.map((item) =>
        item.id === updated.id ? { ...item, roles: updated.roles } : item
      ));
      setRoleMessage(`Updated ${account.email} to ${role}.`);
    } catch (error) {
      setRoleMessage(error instanceof Error ? error.message : "Could not update the account role.");
      const refreshed = await api<AdminUser[]>("/api/admin/users").catch(() => null);
      if (refreshed) setAdminUsers(refreshed);
    } finally {
      setRoleBusyId(null);
    }
  }

  async function setServerConnection(server: ServerProfile, enabled: boolean) {
    setConnectionActionId(server.id);
    setServerError("");
    try {
      await api<{ id: string; connectionStatus: string }>(`/api/paired-servers/${server.id}/connection`, {
        method: "POST",
        body: JSON.stringify({ enabled })
      });
      await refreshServers();
    } catch (error) {
      setServerError(error instanceof Error ? error.message : "Could not change the server connection.");
    } finally {
      setConnectionActionId(null);
    }
  }

  async function createServerTimer(server: ServerProfile) {
    const name = timerName.trim();
    if (!name || timerDurationMinutes < 1 || timerDurationMinutes > 1440) return;
    setTimerBusy(true);
    setTimerError("");
    try {
      await api<ServerTimer>(`/api/servers/${server.id}/timers`, {
        method: "POST",
        body: JSON.stringify({ name, durationSeconds: Math.round(timerDurationMinutes * 60) })
      });
      setTimerName("");
      setTimers(await api<ServerTimer[]>(`/api/servers/${server.id}/timers`));
    } catch (error) {
      setTimerError(error instanceof Error ? error.message : "Could not create the timer.");
    } finally {
      setTimerBusy(false);
    }
  }

  async function deleteServerTimer(server: ServerProfile, timer: ServerTimer) {
    setTimerError("");
    try {
      await api<void>(`/api/servers/${server.id}/timers/${timer.id}`, { method: "DELETE" });
      setTimers((current) => current.filter((item) => item.id !== timer.id));
    } catch (error) {
      setTimerError(error instanceof Error ? error.message : "Could not delete the timer.");
    }
  }

  async function sendTeamMessage(server: ServerProfile) {
    const message = chatInput.trim();
    if (!message || message.length > 200) return;
    setChatBusy(true);
    setChatError("");
    try {
      await api<{ success: boolean }>(`/api/servers/${server.id}/chat`, {
        method: "POST",
        body: JSON.stringify({ message })
      });
      setChatInput("");
      setChatMessages(await api<TeamChatMessage[]>(`/api/servers/${server.id}/chat`));
    } catch (error) {
      setChatError(error instanceof Error ? error.message : "Could not send the team chat message.");
    } finally {
      setChatBusy(false);
    }
  }

  async function loadDeviceInfo(server: ServerProfile, device: PairedDevice) {
    setDeviceInfoBusyId(device.entityId);
    setDeviceError("");
    try {
      const result = await api<{ entityId: string; type: string; info: DeviceInfoData }>(
        `/api/servers/${server.id}/devices/${device.entityId}/info`
      );
      setDeviceDetails((current) => ({ ...current, [device.entityId]: result.info }));
      if (typeof result.info.isActive === "boolean") {
        setDevices((current) => current.map((item) => item.entityId === device.entityId ? { ...item, isActive: result.info.isActive } : item));
      }
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not load device details.");
    } finally {
      setDeviceInfoBusyId(null);
    }
  }

  async function createDeviceGroup(server: ServerProfile) {
    const name = groupName.trim();
    if (!name || name.length > 50) return;
    setGroupBusy(true);
    setDeviceError("");
    try {
      await api<DeviceGroup>(`/api/servers/${server.id}/device-groups`, {
        method: "POST",
        body: JSON.stringify({ name })
      });
      setGroupName("");
      const [nextGroups, nextDevices] = await Promise.all([
        api<DeviceGroup[]>(`/api/servers/${server.id}/device-groups`),
        api<PairedDevice[]>(`/api/servers/${server.id}/devices`)
      ]);
      setDeviceGroups(nextGroups);
      setDevices(nextDevices);
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not create the device group.");
    } finally {
      setGroupBusy(false);
    }
  }

  async function deleteDeviceGroup(server: ServerProfile, group: DeviceGroup) {
    setDeviceError("");
    try {
      await api<void>(`/api/servers/${server.id}/device-groups/${group.id}`, { method: "DELETE" });
      const [nextGroups, nextDevices] = await Promise.all([
        api<DeviceGroup[]>(`/api/servers/${server.id}/device-groups`),
        api<PairedDevice[]>(`/api/servers/${server.id}/devices`)
      ]);
      setDeviceGroups(nextGroups);
      setDevices(nextDevices);
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not delete the device group.");
    }
  }

  async function assignDeviceGroup(server: ServerProfile, device: PairedDevice, groupId: string | null) {
    setDeviceError("");
    try {
      await api<{ entityId: string; groupId: string | null }>(`/api/servers/${server.id}/devices/${device.entityId}/group`, {
        method: "PUT",
        body: JSON.stringify({ groupId })
      });
      setDevices((current) => current.map((item) => item.entityId === device.entityId ? { ...item, groupId } : item));
      setDeviceGroups(await api<DeviceGroup[]>(`/api/servers/${server.id}/device-groups`));
    } catch (error) {
      setDeviceError(error instanceof Error ? error.message : "Could not assign the device group.");
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
          <div className="topbar-actions"><span className="secure-pill"><span /> Private workspace</span><span className={`secure-pill realtime-pill realtime-${realtimeStatus}`} title="Live dashboard updates use this connection"><span /> Realtime: {realtimeStatus}</span>{notificationPermission !== "unsupported" && notificationPermission !== "granted" && <button className="secondary-button" disabled={notificationPermission === "denied"} title={notificationPermission === "denied" ? "Allow notifications in your browser site settings." : "Show Rust+ alerts even when this tab is in the background"} onClick={() => { void Notification.requestPermission().then(setNotificationPermission); }}> {notificationPermission === "denied" ? "Notifications blocked" : "Enable notifications"} </button>}<button className="primary-button compact" onClick={() => setShowAddServer(true)}>＋ Add server</button></div>
        </header>

        <div className="content-area">
          {alarms.length > 0 && <section className="alarm-dock"><div className="alarm-dock-heading"><span className="alarm-indicator">!</span><div><strong>Recent alerts</strong><p>Smart alarms and expired server timers are saved on the server</p></div><button className="text-button" onClick={() => setAlarms([])}>Clear view</button></div>{alarms.map((alarm, index) => <div className="alarm-row" key={`${alarm.serverId ?? "server"}-${alarm.receivedAtUtc}-${index}`}><div><strong>{alarm.title || "Smart alarm"}</strong><p>{alarm.message}</p></div><time>{new Date(alarm.receivedAtUtc).toLocaleTimeString()}</time></div>)}</section>}
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


          {user.roles.includes("Admin") && <section className="admin-users-panel"><div className="admin-users-heading"><div><p className="eyebrow">ACCESS CONTROL</p><h2>Team accounts</h2><p>New invite-code registrations start as members. Assign view-only access here.</p></div><button className="secondary-button" onClick={() => void api<AdminUser[]>("/api/admin/users").then(setAdminUsers).catch((error: unknown) => setRoleMessage(error instanceof Error ? error.message : "Could not load accounts."))}>↻ Refresh</button></div>{roleMessage && <p className="role-message" role="status">{roleMessage}</p>}<div className="admin-users-list">{adminUsers.map((account) => <div className="admin-user-row" key={account.id}><div className="admin-user-avatar">{account.displayName.slice(0, 1).toUpperCase()}</div><div className="admin-user-info"><strong>{account.displayName}</strong><span>{account.email}</span></div><select aria-label={`Role for ${account.email}`} value={account.roles[0] ?? "Member"} disabled={roleBusyId === account.id || account.id === user.id} onChange={(event) => void updateUserRole(account, event.target.value)}><option value="Member">Member</option><option value="ViewOnly">View only</option><option value="Admin">Admin</option></select>{account.id === user.id && <span className="self-role-label">You</span>}</div>)}</div></section>}

          <div className="section-heading"><div><h2>Your servers</h2><p>{servers.length} saved {servers.length === 1 ? "profile" : "profiles"}</p></div><button className="secondary-button" onClick={() => void refreshServers()}>↻ Refresh</button></div>

          {serverError && <div className="error-banner" role="alert">{serverError}</div>}

          {servers.length === 0 ? (
            <section className="empty-state">
              <div className="empty-illustration"><span>⌖</span><i /><i /><i /></div>
              <h3>No server profiles yet</h3>
              <p>Add a server address to save a profile. For live data, pair the server through Rust+ after an admin configures the FCM pairing listener.</p>
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
                  {server.isPaired ? <>
                    <button className="secondary-button snapshot-button" onClick={() => void openSnapshot(server)}>{server.connectionEnabled ? "View live data ↗" : "Manage timers ↗"}</button>
                    {user.roles.includes("Admin") && <button className="secondary-button snapshot-button" disabled={connectionActionId === server.id} onClick={() => void setServerConnection(server, !server.connectionEnabled)}>{connectionActionId === server.id ? "Updating…" : server.connectionEnabled ? "Disconnect" : "Connect"}</button>}
                  </> : <p className="adapter-note">Pair this server in the Rust+ mobile app after the FCM listener is configured.</p>}
                </article>
              ))}
            </div>
          )}

          <section className="roadmap-note"><div className="roadmap-icon">↗</div><div><strong>Foundation build</strong><p>Authentication, pairing, map/team snapshots, chat, device controls, groups, and timers are in place. Live map snapshots refresh every 30 seconds while open; remaining features are listed in the project README.</p></div></section>
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
              {snapshot.map?.imageBase64 && <div className="snapshot-map-wrap" style={{ aspectRatio: `${snapshot.map.width ?? 1} / ${snapshot.map.height ?? 1}` }}><img className="snapshot-map" alt="Rust server map" src={`data:image/jpeg;base64,${snapshot.map.imageBase64}`} /><svg className="snapshot-map-overlay" viewBox={`0 0 ${snapshot.map.width ?? 1} ${snapshot.map.height ?? 1}`} preserveAspectRatio="none" aria-label="Team member positions">{snapshot.team?.members?.map((member, index) => { const width = snapshot.map?.width ?? 0; const height = snapshot.map?.height ?? 0; const mapSize = snapshot.info?.mapSize ?? 0; if (!width || !height || !mapSize) return null; const margin = snapshot.map?.oceanMargin ?? 0; const x = member.x * ((width - 2 * margin) / mapSize) + margin; const y = height - (member.y * ((height - 2 * margin) / mapSize) + margin); return <g key={`${member.name ?? "player"}-${index}`}><circle className={`player-dot ${member.isAlive ? "" : "dead"} ${member.isOnline ? "" : "offline"}`} cx={x} cy={y} r={Math.max(5, width * 0.005)} /><text className="player-label" x={x} y={y - Math.max(8, width * 0.009)} textAnchor="middle">{member.name ?? "Player"}</text></g>; })}</svg></div>}
              {snapshot.map?.monuments?.length ? <><h3 className="snapshot-subheading">Monuments</h3><div className="monument-list">{snapshot.map.monuments.slice(0, 24).map((monument, index) => <span key={`${monument.name ?? "monument"}-${index}`}>{monument.name ?? "Unknown monument"}</span>)}</div></> : null}
              <h3 className="snapshot-subheading">Team data</h3><pre className="snapshot-json">{JSON.stringify(snapshot.team, null, 2)}</pre>
              <p className="snapshot-timestamp">Updated {new Date(snapshot.retrievedAtUtc).toLocaleTimeString()} · refreshes every 30 seconds while open</p>
            </>}
            {selectedServer.isPaired && <section className="server-timers"><div className="chat-panel-heading"><div><h3 className="snapshot-subheading">Event timers</h3><p>Timers run on the server and persist when browsers close.</p></div></div>
              {timerError && <div className="error-banner" role="alert">{timerError}</div>}
              {(user.roles.includes("Admin") || user.roles.includes("Member")) && <form className="timer-composer" onSubmit={(event) => { event.preventDefault(); void createServerTimer(selectedServer); }}><input value={timerName} onChange={(event) => setTimerName(event.target.value)} maxLength={80} placeholder="Cargo, Oil Rig, custom event…" aria-label="Timer name" disabled={timerBusy} /><label><span>Minutes</span><input type="number" min={1} max={1440} value={timerDurationMinutes} onChange={(event) => setTimerDurationMinutes(Number(event.target.value))} aria-label="Timer duration in minutes" disabled={timerBusy} /></label><button className="primary-button" type="submit" disabled={timerBusy || !timerName.trim() || timerDurationMinutes < 1 || timerDurationMinutes > 1440}>{timerBusy ? "Adding…" : "Add timer"}</button></form>}
              {timers.length === 0 ? <p className="muted">No event timers yet.</p> : <div className="timer-list">{timers.map((timer) => <div className="timer-row" key={timer.id}><div className="timer-icon">{timer.fired ? "✓" : "◷"}</div><div className="timer-info"><strong>{timer.name}</strong><span>{timer.fired ? "Expired" : `Expires at ${new Date(timer.expiresAtUtc).toLocaleTimeString()}`}</span></div>{(user.roles.includes("Admin") || (user.roles.includes("Member") && timer.isOwner)) && <button className="text-button danger-text" onClick={() => void deleteServerTimer(selectedServer, timer)}>Remove</button>}</div>)}</div>}
            </section>}
            {selectedServer.isPaired && <section className="team-chat-panel"><div className="chat-panel-heading"><div><h3 className="snapshot-subheading">Team chat</h3><p>Messages are relayed through the paired Rust+ connection.</p></div><button className="text-button" onClick={() => void api<TeamChatMessage[]>(`/api/servers/${selectedServer.id}/chat`).then(setChatMessages).catch((error: unknown) => setChatError(error instanceof Error ? error.message : "Could not refresh team chat."))}>Refresh</button></div>
              {chatError && <div className="error-banner" role="alert">{chatError}</div>}
              <div className="chat-messages">{chatMessages.length === 0 ? <p className="muted">No team chat messages returned.</p> : chatMessages.slice(-100).map((message, index) => <div className="chat-message" key={`${message.steamId}-${message.time}-${index}`}><div className="chat-message-meta"><strong>{message.name}</strong><time>{new Date(message.time).toLocaleTimeString()}</time></div><p>{message.message}</p></div>)}</div>
              {(user.roles.includes("Admin") || user.roles.includes("Member")) && <form className="chat-composer" onSubmit={(event) => { event.preventDefault(); void sendTeamMessage(selectedServer); }}><input value={chatInput} onChange={(event) => setChatInput(event.target.value)} maxLength={200} placeholder="Message your team…" aria-label="Team chat message" disabled={chatBusy || !selectedServer.connectionEnabled} /><button className="primary-button" type="submit" disabled={chatBusy || !chatInput.trim() || !selectedServer.connectionEnabled}>{chatBusy ? "Sending…" : "Send"}</button></form>}
            </section>}
            {selectedServer.isPaired && <>
              <h3 className="snapshot-subheading">Paired smart devices</h3>
              {(user.roles.includes("Admin") || user.roles.includes("Member")) && <form className="device-group-composer" onSubmit={(event) => { event.preventDefault(); void createDeviceGroup(selectedServer); }}><input value={groupName} onChange={(event) => setGroupName(event.target.value)} maxLength={50} placeholder="New group name…" aria-label="New device group name" disabled={groupBusy} /><button className="secondary-button" type="submit" disabled={groupBusy || !groupName.trim()}>{groupBusy ? "Saving…" : "Create group"}</button></form>}
              {deviceGroups.length > 0 && <div className="device-group-list">{deviceGroups.map((group) => <div className="device-group-chip" key={group.id}><span>{group.name} <small>{group.deviceCount}</small></span>{(user.roles.includes("Admin") || user.roles.includes("Member")) && <button className="text-button danger-text" onClick={() => void deleteDeviceGroup(selectedServer, group)} aria-label={`Delete ${group.name} group`}>×</button>}</div>)}</div>}

              {deviceError && <div className="error-banner" role="alert">{deviceError}</div>}
              {devices.length === 0 ? <p className="muted">No paired devices have been received yet. Pair switches, alarms, or storage monitors from the Rust+ mobile app.</p> : <div className="device-list">{devices.map((device) => <div className="device-row" key={device.id}><div className="device-symbol">{device.type === "Switch" ? "⏻" : device.type === "Alarm" ? "◉" : "▤"}</div><div className="device-row-info"><strong>{device.name}</strong><span>{device.type} · ID {device.entityId}</span>{(user.roles.includes("Admin") || user.roles.includes("Member")) && <select aria-label={`Group for ${device.name}`} value={device.groupId ?? ""} onChange={(event) => void assignDeviceGroup(selectedServer, device, event.target.value || null)}><option value="">Ungrouped</option>{deviceGroups.map((group) => <option value={group.id} key={group.id}>{group.name}</option>)}</select>}</div><div className="device-row-action">{device.type === "Switch" ? <><span className={device.isActive === undefined ? "device-state unknown" : device.isActive ? "device-state on" : "device-state"}>{device.isActive === undefined ? "State unknown" : device.isActive ? "On" : "Off"}</span>{(user.roles.includes("Admin") || user.roles.includes("Member")) && <button className="secondary-button" disabled={deviceActionId === device.entityId || !selectedServer.connectionEnabled} onClick={() => void toggleDevice(selectedServer, device)}>{deviceActionId === device.entityId ? "Sending…" : "Toggle"}</button>}</> : <span className="device-state unknown">Read only</span>}<button className="text-button" disabled={deviceInfoBusyId === device.entityId || !selectedServer.connectionEnabled} onClick={() => void loadDeviceInfo(selectedServer, device)}>{deviceInfoBusyId === device.entityId ? "Loading…" : deviceDetails[device.entityId] ? "Refresh details" : "Details"}</button></div>{deviceDetails[device.entityId] && <div className="device-details">{typeof deviceDetails[device.entityId].isActive === "boolean" ? <p>Device state: <strong>{deviceDetails[device.entityId].isActive ? "Active" : "Inactive"}</strong></p> : <><p>Capacity: <strong>{deviceDetails[device.entityId].capacity ?? "Unknown"}</strong> slots</p><p>Tool cupboard protection: <strong>{deviceDetails[device.entityId].hasProtection === true ? "Active" : deviceDetails[device.entityId].hasProtection === false ? "Inactive" : "Unknown"}</strong></p><p>Protection expiry: <strong>{formatProtectionExpiry(deviceDetails[device.entityId].protectionExpiry)}</strong></p>{(deviceDetails[device.entityId].items?.length ?? 0) > 0 && <div className="storage-items">{deviceDetails[device.entityId].items?.map((item, index) => <span key={`${item.id}-${index}`}>Item {item.id}: {item.quantity ?? "?"}{item.isItemBlueprint ? " (BP)" : ""}</span>)}</div>}</>}</div>}</div>)}</div>}
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
