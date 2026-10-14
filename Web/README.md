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
- Admin-only role assignment and protection against demoting the final administrator.
- React frontend served from the same origin, avoiding a separate CORS surface.
- SQLite data and ASP.NET Data Protection keys persisted in the Docker volume.

## Important current limitation

**The FCM pairing and connection code has been added but is not yet verified against a real Rust+ account/server.** The CI workflow builds the frontend, backend, and container; live FCM registration, pairing delivery, Linux network behavior, and reconnect behavior still require runtime testing with real credentials. Smart-device commands, full realtime team/map updates, chat, and alarm-history UI are not complete yet.

## Run with Docker

1. Copy `web.env.example` to `.env` (the existing root `.env.example` belongs to the desktop app).
2. Set a strong unique `ADMIN_PASSWORD` (12+ characters including uppercase, lowercase, digit, and symbol).
3. Set a long random `WEB_INVITE_CODE` to allow teammates to register. Leave it empty to disable registration.
4. Run `docker compose up --build -d`.
5. Put the service behind an HTTPS reverse proxy before exposing it to the internet. The production auth cookie is Secure-only.
6. Open `http://localhost:8080` for a local smoke test. For HTTPS deployments, configure the reverse proxy to preserve the original scheme and restrict direct access to the app port.
7. Obtain an FCM credentials file. The recommended native route is the upstream [RustPlusApi Register Console App](https://github.com/HandyS11/RustPlusApi/tree/develop/samples/RustPlus.Register.ConsoleApp); its README explains the one-time Steam login and credential file. An existing legacy `rustplus.config.json` is also accepted. Never commit or share this file publicly.
8. In the Rust+ Web dashboard as the admin, upload the credentials file. Pair the desired server from the Rust+ mobile app. The server profile should appear after the FCM pairing notification is received. The hosted listener retries after disconnects.

The database and Data Protection key ring are stored in the `rustplus-web-data` volume. Treat this volume and its backups as sensitive. Back it up before upgrading. This first version uses EF Core `EnsureCreated`; schema migrations must be added before production schema evolution.

## Endpoints

- `GET /api/health`: minimal health status only.
- `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/logout`.
- `GET /api/auth/me`.
- `GET/POST /api/servers`, `DELETE /api/servers/{id}`, `GET /api/servers/{id}/snapshot`.
- `GET /api/admin/fcm-status`, `POST /api/admin/fcm-credentials` (admin only).
- `DELETE /api/paired-servers/{id}` (admin only).
- `GET /api/admin/users`, `PUT /api/admin/users/{id}/role` (admin only).
- `/hubs/servers`: authenticated SignalR hub; profile changes send `ServersChanged`.

All application endpoints except the minimal health check and invite-gated registration require an authenticated session. Server profile reads and mutations are scoped to the current account. Paired servers are visible to all signed-in accounts on this self-hosted instance; only admins can upload/replace FCM credentials or remove a pairing. View-only accounts can read data but cannot create or delete server profiles.
