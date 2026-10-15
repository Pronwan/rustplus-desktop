# Rust+ Web (Phase 1 foundation)

This is the first isolated web-service foundation for the self-hosted port. It deliberately does not reference the Windows-only WPF project.

## Current functionality

- Cookie-based authentication with ASP.NET Core Identity.
- Bootstrap administrator from `ADMIN_EMAIL` and `ADMIN_PASSWORD`.
- Invite-code-gated account registration (`WEB_INVITE_CODE`).
- Roles seeded as `Admin`, `Member`, and `ViewOnly`.
- Authenticated server profile create/list/delete endpoints with owner scoping.
- Authenticated SignalR hub that notifies signed-in clients when profiles or connection states change.
- Admin-only FCM credential upload, protected credential storage, pairing notifications, and persisted notification de-duplication IDs.
- Shared Rust+ server profiles created from FCM pairing events, with one backend-owned Rust+ socket per paired server and bounded exponential reconnect attempts.
- Live snapshot endpoint for supported server info, map image/monuments, and team data.
- Smart switch, alarm, and storage-monitor pairing metadata; authenticated smart-switch toggle commands with view-only role protection.
- Admin-only role assignment UI and API, plus protection against demoting the final administrator.
- 2D map/team-position snapshots that auto-refresh every 30 seconds while open and are cached per server for 30 seconds; a shared smart-alarm and expired-timer history persisted to SQLite (capped at 2,000 events), and durable server-scoped event timers; optional browser desktop notifications for incoming smart alarms and expired timers. The dashboard shows SignalR connection health, retries failed initial connections every five seconds, and refreshes server data and saved alert history after reconnecting.
- React frontend served from the same origin, avoiding a separate CORS surface.
- SQLite data and ASP.NET Data Protection keys persisted in the Docker volume.

## Security notes

The web API enforces same-host Origin checks for browser state-changing requests, sets CSP and common security headers, marks API responses no-store, and uses HttpOnly/SameSite=Strict cookies (Secure-only outside Development). Keep the application port private behind an HTTPS reverse proxy.

## Important current limitation

**The FCM pairing and connection code has been added but is not yet verified against a real Rust+ account/server.** The CI workflow builds the frontend, backend, and container; live FCM registration, pairing delivery, Linux network behavior, and reconnect behavior still require runtime testing with real credentials. Map snapshots refresh every 30 seconds while the server view is open and are cached server-side for 30 seconds, but positions are not streamed continuously. Persistent history for smart alarms and expired server timers is available in the dashboard. Browser desktop notifications require browser permission and a secure context (HTTPS, or localhost during local testing). Other Rust+ event types are not yet recorded in the shared history.

## Run with Docker

1. Copy `web.env.example` to `.env` (the existing root `.env.example` belongs to the desktop app).
2. Set a strong unique `ADMIN_PASSWORD` (12+ characters including uppercase, lowercase, digit, and symbol).
3. Set a long random `WEB_INVITE_CODE` to allow teammates to register. Leave it empty to disable registration.
4. Run `docker compose up --build -d`.
5. Put the service behind an HTTPS reverse proxy before exposing it to the internet. The production auth cookie is Secure-only.
6. Open `http://localhost:8080` for a local smoke test. For HTTPS deployments, configure the reverse proxy to preserve the original scheme and restrict direct access to the app port.
7. Obtain an FCM credentials file. The upstream [RustPlusApi credentials helper](https://github.com/HandyS11/RustPlusApi/blob/develop/docs/articles/credentials.md) can run locally without the .NET SDK:
   `docker run --rm -p 127.0.0.1:8081:8080 ghcr.io/handys11/rustplusapi-credentials`
   Open `http://localhost:8081` on that same machine, complete the Steam login, and download `rustplus.config.json`. Alternatively, use the upstream [RustPlusApi Register Console App](https://github.com/HandyS11/RustPlusApi/tree/develop/samples/RustPlus.Register.ConsoleApp). Close the helper/sample after the credentials file is written so it does not compete with the web app's FCM listener. An existing legacy `rustplus.config.json` is also accepted.
8. In the Rust+ Web dashboard as the admin, upload the credentials file. Pair the desired server from the Rust+ mobile app. The server profile should appear after the FCM pairing notification is received. The hosted listener retries with exponential backoff after disconnects.

The database and Data Protection key ring are stored in the `rustplus-web-data` volume. Treat this volume and its backups as sensitive. Back it up before upgrading. This first version uses EF Core `EnsureCreated`; schema migrations must be added before production schema evolution.

## Endpoints

- `GET /api/health`: minimal health status only.
- `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/logout`.
- `GET /api/auth/me`.
- `GET/POST /api/servers`, `DELETE /api/servers/{id}`, `GET /api/servers/{id}/snapshot` (30-second per-server cache), `GET /api/alarms?limit=50` (authenticated, capped at 100), `GET/POST /api/servers/{id}/chat`, `GET/POST /api/servers/{id}/timers`, `DELETE /api/servers/{id}/timers/{timerId}`, `GET /api/servers/{id}/devices`, `GET/POST /api/servers/{id}/device-groups`, `DELETE /api/servers/{id}/device-groups/{groupId}`, `PUT /api/servers/{id}/devices/{entityId}/group`, `GET /api/servers/{id}/devices/{entityId}/info`, `POST /api/servers/{id}/devices/{entityId}/toggle`.
- `GET /api/admin/fcm-status`, `POST /api/admin/fcm-credentials` (admin only).
- `DELETE /api/paired-servers/{id}` and `POST /api/paired-servers/{id}/connection` (admin only).
- `GET /api/admin/users`, `PUT /api/admin/users/{id}/role` (admin only).
- `/hubs/servers`: authenticated SignalR hub; profile changes send `ServersChanged`.

All application endpoints except the minimal health check and invite-gated registration require an authenticated session. Server profile reads and mutations are scoped to the current account. Paired servers are visible to all signed-in accounts on this self-hosted instance; only admins can upload/replace FCM credentials or remove a pairing. View-only accounts can read data but cannot create or delete server profiles.


**Token note:** Rust+ player tokens are signed integers and can be negative. The web backend preserves the exact signed value when storing and using the token.

**Startup behavior:** FCM network connection attempts run in the background so an upstream FCM outage does not block the dashboard from starting.

Automated tests also cover shared timer visibility and owner/admin delete permissions.

The same-host Origin check also protects SignalR negotiation and WebSocket requests against cross-site connection attempts.

Device detail requests are on-demand to avoid polling every paired entity; storage monitors show item IDs, quantities, capacity, and TC-protection expiry when Rust+ returns them.

Storage-monitor details are rendered defensively because Rust+ may omit inventory or tool-cupboard protection fields for a given entity.
