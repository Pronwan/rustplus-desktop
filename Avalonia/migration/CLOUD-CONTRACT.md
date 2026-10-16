# Cloud discovery checkpoint — 2026-10-16

Research only; no cloud authentication, consent or device sharing port has been implemented yet.
Jawad's layout correction took priority during this continuation. Continue with these contracts after
the layout slice. Do not repeat the completed companion/native sign-in crash fix or pairing ports.

## Verified reference behavior

The active WPF `Services/Cloud/CloudBackend.cs` selects Platform, with public base URL
`https://rustplusdesktop.cloud`. The old Supabase anonymous route is not the active account flow.
Read `CloudAuthManager.cs`, `CloudApiClient.cs`, `CloudConsentService.cs`,
`Services/Data/DeviceDataModule.cs`, `Models/SharingModels.cs` and the platform sections in
`Services/SupabaseAuthManager.cs` when implementing. Graph coverage metadata was unavailable/changed;
the relevant source was read directly. Server source is available read-only in
`D:/Development/Rustplusdesktop-full-stack-api`; do not edit that repository for this migration.

- Hosted desktop sign-in opens `/desktop/connect?redirect_uri=<loopback>`. Guests go through the
  website login/MFA; authenticated users explicitly choose **Send login to {appName}**. A native form
  redirects to the provided URI with a one-time `code`. Confirmed in server
  `DesktopConnectController.php` and `resources/js/pages/desktop/connect.tsx`.
- POST `/api/v1/auth/discord/token` exchanges `{code,device_name}`; despite the route name this is
  the hosted website handoff, supporting the site's account providers. POST `auth/token` accepts
  email/password; MFA can require the hosted flow. `DesktopTokenController.php` confirms both routes.
- Success is `{data:{token,expires_at,user}}`; user contains id/email/display_name or name,
  providers and has_password. Bearer tokens stay out of presentation, exceptions and logs.
- GET `me` validates/restores a session. Reference behavior clears rejected/expired sessions and
  retains saved sessions on transient network failures. Reference logout is local, not remote revocation.
- Reference cache key is `cloud_desktop_token`, JSON fields Token/User/ExpiresAt. Determine its exact
  location and compatibility before writing. Reuse the private, revision-safe atomic writer; never
  save passwords or overwrite unknown fields. Separate cloud identity from Steam companion identity.
- POST `me/consents` records consent_type/granted/metadata. POST `profile/consent` with `{accepted}`
  confirms cloud sync permission. Confirmation is scoped to cloud-user plus Steam identity and must
  not silently enable uploads just because sign-in succeeded. Reference presence links steam_id and
  language; inspect server binder/conflict handling before porting it.
- Original Export toggles consent-based cloud sync. POST `overlay` sends server_key, steam_id and
  smart_devices.device_data (a JSON string of the safe device DTO list). Never upload an empty snapshot
  accidentally; preserve the cloud-owned startup restore barrier before enabling automatic uploads.
- Shared DTOs include EntityId/Kind/Name/Alias/IsGroup/Children/custom icon/alarm title/OilRigTrigger;
  exclude pairing credentials and codes. Preserve nested groups and unknown local metadata.
- Import fetches `sync/team-devices`, uses team identities plus own Steam identity, and can fall back
  to local overlay data with wipe checks and nested-ID deduplication. It is not a replacement file export.

## Exact next checks

1. Read server `DesktopOAuthCodeService.php` to verify allowed loopback URIs, nonce paths, expiry and
   one-time code consumption. The controller preserves the supplied path, but its validator has not
   yet been inspected. Use a random callback nonce and exact URI/query validation if supported.
2. Read route middleware, TokenIssuer/UserResource, MeConsentController and ProfileController consent/
   Steam binding. Verify version gates; never bypass official-client or minimum-version requirements.
   The WPF product version is 10.2.0; the Avalonia preview still has default assembly version metadata.
3. Reuse the proven native Steam browser unchanged. Hosted cloud login should use the system browser
   as the reference does, with an owned, cancellable loopback listener. Inspect any existing broker's
   cancellation contract before choosing it; do not leave a listener behind after cancellation.
4. Implement a bounded safe identity/session/consent feature with fake HTTP/browser/storage tests,
   then wire its real view and lifetime. Validate hosted login without entering personal credentials.
5. Port sharing/import only after identity, limits, consent and restoration safeguards are verified.

An anonymous read-only GET to the public desktop-connect URL reached `/login` with HTTP 200. No
credentials were entered, no login/consent form was submitted, and no live token/callback was accepted.
Its anonymous HTML is ignored under `artifacts/cloud-contract/`; do not commit it or print CSRF values.
