# Rust+ Web (Phase 1 foundation)

This is the first isolated web-service foundation for the self-hosted port. It deliberately does not reference the Windows-only WPF project.

## Current functionality

- Cookie-based authentication with ASP.NET Core Identity.
- Bootstrap administrator from `ADMIN_EMAIL` and `ADMIN_PASSWORD`.
- Invite-code-gated account registration (`WEB_INVITE_CODE`).
- Roles seeded as `Admin`, `Member`, and `ViewOnly`.
- Authenticated server profile create/list/delete endpoints with owner scoping.
- Authenticated SignalR hub that notifies the signed-in owner when their profile list changes.
- React frontend served from the same origin, avoiding a separate CORS surface.
- SQLite data and ASP.NET Data Protection keys persisted in the Docker volume.

## Important current limitation

**Server profiles are saved configuration only. The Rust+ pairing listener, FCM registration, live Rust+ connection, map/team/device data, and control commands are not implemented yet.** The UI labels profiles as disconnected and does not pretend a Rust+ server is connected. The next implementation step is a headless Linux compatibility spike for the exact RustPlusAPI dependency and pairing flow.

## Run with Docker

1. Copy `.env.example` to `.env`.
2. Set a strong unique `ADMIN_PASSWORD` (12+ characters including uppercase, lowercase, digit, and symbol).
3. Set a long random `WEB_INVITE_CODE` to allow teammates to register. Leave it empty to disable registration.
4. Run `docker compose up --build -d`.
5. Put the service behind an HTTPS reverse proxy before exposing it to the internet. The production auth cookie is Secure-only.
6. Open `http://localhost:8080` for a local smoke test. For HTTPS deployments, configure the reverse proxy to preserve the original scheme and restrict direct access to the app port.

The database and Data Protection key ring are stored in the `rustplus-web-data` volume. Treat this volume and its backups as sensitive. Back it up before upgrading. This first version uses EF Core `EnsureCreated`; schema migrations must be added before production schema evolution.

## Endpoints

- `GET /api/health`: minimal health status only.
- `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/logout`.
- `GET /api/auth/me`.
- `GET/POST /api/servers`, `DELETE /api/servers/{id}`.
- `/hubs/servers`: authenticated SignalR hub; profile changes send `ServersChanged`.

All application endpoints except the minimal health check and invite-gated registration require an authenticated session. Server profile reads and mutations are scoped to the current account. Role-based admin/member/view-only policy enforcement is the next step before broader sharing/control features are enabled.
