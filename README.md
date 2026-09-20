# DevPulse

A .NET 9 Blazor Server dashboard with host diagnostics and a Spotify Connect/browser player.

## Run locally

Install the .NET 9 SDK, then:

```powershell
dotnet restore
dotnet run --launch-profile http
```

Open http://127.0.0.1:5214. The dashboard works without Spotify credentials. Visual Studio 2022 version 17.12 or later supports .NET 9.

Diagnostics describe the machine running DevPulse. A hosted deployment reports its server/container. Windows uses WMI for detailed hardware information; other platforms show OS, process architecture, processors and memory available to the process.

## Features

- **Overview:** API health, process metrics, live CPU/memory/thread charts, host information, disk charts, folder usage, and TCP scanning.
- **Operations:** owner-allowlisted service watchlist plus bounded one-off public-host ping, DNS, TLS, and database connectivity checks, with live log tailing.
- **Telemetry:** persisted metric history, request traces, threshold incidents, deployment identity, and optional OTLP export.
- **Inspectors:** public-URL JSON explorer, SHA-256 file hashing, password-based AES-256-GCM file encryption/decryption, local JWT decoding, and a bounded HTTP load tester.
- **Network Calculator:** IPv4 and IPv6 subnet planning, usable address ranges, CIDR/mask/wildcard conversion, VLSM allocation, route summarization, DHCP/firewall range counts, transfer-time and bandwidth-delay estimates, and MTU/MSS sizing.
- **Website Audit:** bounded crawling, broken-link and asset checks, security/cache headers, response timing, JSON dataset validation, SEO checks, Lighthouse, axe accessibility, scripted browser journeys, desktop/mobile screenshots, visual regression, and scheduled uptime monitoring.
- **Administration:** separate administrator cookie, process listing/termination, maintenance mode, audit history, and redacted diagnostics ZIP export.
- **Alerts:** memory, disk, and request-latency thresholds with in-app incidents plus optional HTTPS webhook and SMTP delivery.
- **Spotify:** protected OAuth/PKCE sessions, catalog song search, Liked Songs, exact-track selection, album/playlist track browsing, queue viewing/additions, saved albums, created/followed/private/collaborative playlists, shuffle/repeat, device-first Spotify Connect control, optional browser audio, live progress, seek/volume controls, and rate-limit handling.
- Health endpoint, diagnostics production gate, friendly error/404 pages, persistent encrypted sessions, CI, and production smoke tests.

## Spotify setup

Create/use an app at https://developer.spotify.com/dashboard and register:

```text
http://127.0.0.1:5214/signin-spotify
```

Spotify does not allow localhost redirect URIs. Use the http launch profile for local testing. For hosting, register https://YOUR-DOMAIN/signin-spotify.

```powershell
dotnet user-secrets set "Spotify:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Spotify:ClientSecret" "YOUR_CLIENT_SECRET"
```

Restart, open /spotify, and connect. Playback controls and browser audio require Spotify Premium. Development-mode apps may require allowed users in the developer dashboard; consult Spotify's current restrictions. Some external devices do not support volume control.

The page follows the active Spotify Connect device by default, so opening or closing DevPulse does not take playback away from the Spotify desktop or mobile app. Choose another device and select **Continue on this device** to transfer while preserving the current playing/paused state. Browser audio is opt-in: first select **Enable browser audio**, then **Play in this browser**. Users can search the Spotify catalog, browse Liked Songs, choose an exact song from an album or owned/collaborative playlist, inspect and add to the queue, and control shuffle or repeat. The page also browses saved albums and created, followed, private, and collaborative playlists. Progress updates locally every second and reconciles with Spotify every four seconds while the tab is visible. External-device changes are near-real-time because Spotify does not provide playback webhooks. Existing users must disconnect and reconnect once after this upgrade to grant the `playlist-read-private` and `playlist-read-collaborative` scopes in addition to the existing playback and library scopes.

Tokens are encrypted on the server; the cookie contains only an opaque session ID. Sessions last at most seven days. Disconnect invalidates the app session, including other open tabs. To revoke the OAuth grant completely, remove DevPulse from Spotify account Apps settings.

## Configuration

Use user-secrets for Development credentials, or double-underscore environment variables on a host.

| Variable | Default | Purpose |
| --- | --- | --- |
| Spotify__ClientId / Spotify__ClientSecret | empty | Spotify app credentials |
| Diagnostics__Enabled | Development only | Expose diagnostics to visitors |
| Diagnostics__FolderRoot | empty | Owner-selected folder; scanning disabled when empty |
| Storage__DataPath | ./data | Persistent keys and encrypted sessions |
| Storage__ProtectKeysWithDpapi | false | Encrypt the persisted key ring for the current Windows account; enable only after verifying that the deployment identity has a persistent Windows profile |
| ReverseProxy__KnownProxies__0 | framework trusts loopback | Trusted proxy IP; add numbered entries |
| ReverseProxy__KnownNetworks__0 | framework defaults | Trusted proxy CIDR; add numbered entries |
| Hosting__RedirectToHttps | false | Only enable with Kestrel HTTPS configured |
| Admin__Username | admin | Administrator login name |
| Admin__Password | empty | Administrator secret; access remains disabled below 12 characters |
| Admin__AllowProcessTermination | false | Explicitly enables administrator process termination; keep disabled unless required |
| Operations__ApprovedHosts__0 | 127.0.0.1 | Numbered host allowlist for ping, DNS, TLS and TCP checks |
| Operations__ApprovedPorts__0 | predefined safe list | Numbered TCP port allowlist |
| Operations__ApprovedUrls__0 | https://api.github.com | Numbered URL suggestions and trusted private-service exceptions for JSON/load testing |
| Operations__ApprovedLogFiles__0 | empty | Exact owner-approved file paths for live tailing |
| Operations__Databases__0__Name / Host / Port / Kind | empty | Database connectivity targets; Kind may be TCP or Redis |
| Operations__WatchIntervalSeconds | 300 | Scheduled service-watchlist interval; set to 0 to disable |
| Operations__LoadTestMaxRequests / LoadTestMaxConcurrency | 25 / 5 | Server-side load-test safety caps |
| WebsiteAudit__DefaultUrl | configured project website | Initial URL shown on the Website Audit page |
| WebsiteAudit__MonitoredUrls__0 | configured project website | Owner-approved URL for scheduled monitoring and browser audits |
| WebsiteAudit__MonitorIntervalSeconds | 300 | Uptime interval; minimum 60 seconds, set to 0 to disable |
| WebsiteAudit__MaxResources | 75 | Crawl cap, clamped between 10 and 150 resources |
| WebsiteAudit__BrowserTimeoutSeconds | 120 | Lighthouse/browser-worker timeout, clamped to 30–300 seconds |
| WebsiteAudit__BrowserEnabled | true | Enables the bounded Chromium worker when installed |
| WebsiteAudit__NodePath / ChromiumPath | auto-detected | Optional explicit browser-worker executable paths |
| Alerts__MemoryMb | 1024 | Process-memory warning threshold |
| Alerts__DiskUsedPercent | 90 | Disk usage warning threshold |
| Alerts__RequestLatencyMs | 2000 | Slow-request incident threshold |
| Alerts__WebhookUrl | empty | Optional HTTPS webhook destination |
| Telemetry__OtlpEndpoint | empty | Optional HTTP(S) OTLP collector endpoint |

To enable a local folder scan:

```powershell
$env:Diagnostics__FolderRoot = 'D:\\VS_Project\\MiniProject_Everything_1\\Service'
dotnet run --launch-profile http
```

Scans accept only the configured root, read metadata, skip links/junctions and inaccessible entries, and stop at 15 seconds, 100,000 entries or depth 64. Results explicitly report limits/skipped entries. Totals are logical sizes; hard links can be counted more than once. Configure a narrow trusted directory whose names and sizes may be shown to dashboard users.

Production diagnostics are disabled by default. When disabled, `/operations`, `/telemetry`, and `/tools` return 404. Enable them only behind a private access gateway or when you intentionally want visitors to use the configured diagnostics. The administrator login protects termination, maintenance, audit, and export actions; Spotify login is separate and is not administrator authentication.

Configure the administrator password through user-secrets or the hosting secret manager—never in `appsettings.json`:

```powershell
dotnet user-secrets set "Admin:Password" "A-unique-password-of-at-least-12-characters"
```

Scheduled watchlist targets, the TCP port scanner, and log files remain owner-controlled through configuration. Arbitrary log paths are never accepted from visitors.

The JSON explorer accepts a user-entered public HTTP or HTTPS URL and uses configured URLs as suggestions. Its outbound client disables redirects, resolves and pins the destination address at connection time, and rejects loopback, private, link-local, and reserved targets to prevent server-side request forgery. Owner-approved URLs remain available for intentionally configured private services. JSON responses are limited to 2 MB. Log output and uploaded file contents are never included in request telemetry; uploaded files are processed in memory and are not stored.

The mini load tester accepts public HTTP or HTTPS URLs with the same network protections and configured private-service exceptions. Users must acknowledge that they own or have permission to test the target. Each run is capped at 25 GET requests, five workers, and a 10-second client timeout; only one arbitrary public run can execute at a time across the application. These controls reduce accidental abuse but do not replace authentication or a private access gateway for a production diagnostics deployment.

The Operations page accepts a one-off public domain, IP address, or URL for a single Ping, DNS resolution, or port-443 TLS inspection. It also accepts one public host and port for a TCP or Redis health check. Private and reserved destinations are blocked unless the exact target is owner-configured, and the UI requires a permission acknowledgment for active network checks. Scheduled checks and broad TCP scanning remain allowlisted.

The Website Audit crawler accepts a public URL, pins public network destinations at connection time, disables redirects, and limits response sizes and crawl count. Browser-based audits are more powerful and therefore run only for origins configured under `WebsiteAudit:MonitoredUrls` or `WebsiteAudit:DefaultUrl`, one at a time. They run Lighthouse, axe, a bounded interaction journey, and desktop/mobile screenshot comparison. Visual baselines and current/difference images are stored under `Storage__DataPath/website-audit`.

The file encryption tool creates authenticated `.devpulse` packages with AES-256-GCM and a key derived from the user's password. Use a unique password of at least 12 characters. The password is not stored, and a lost password cannot be recovered. SHA-256 remains available for integrity checking; hashes cannot be decrypted.

For email alerts, configure `Alerts__Smtp__Host`, `Port`, `Username`, `Password`, `From`, `To`, and `EnableSsl` through secrets/environment settings. Webhook alerts accept HTTPS only. Redis health sends an unauthenticated `PING`; other database kinds verify TCP acceptance without running queries or exposing credentials.

Metric, incident, and audit history is stored as bounded in-memory views backed by JSONL journals under `Storage__DataPath`. Request traces are memory-only and omit query strings. Configure `Telemetry__OtlpEndpoint` to export ASP.NET Core, HttpClient, and runtime metrics/traces using OpenTelemetry Protocol.

## Docker / Render

```sh
docker build -t devpulse .
docker run --rm -p 10000:10000 -v devpulse-data:/app/data devpulse
```

Use the repository Dockerfile, port 10000 and health check /healthz on Render. The image runs as the non-root app user. The platform terminates HTTPS. Configure its trusted proxy addresses/networks so OAuth receives the correct HTTPS scheme; do not trust every network indiscriminately. Register the public HTTPS callback in Spotify.

The production image includes Node.js and headless Chromium for Website Audit. This materially increases image size and memory use. If the hosting plan cannot support Chromium, set `WebsiteAudit__BrowserEnabled=false`; crawling, links, headers, JSON, SEO, timing, cache, and uptime checks continue to work without it.

For file-based production secrets on Render, create a Secret File named `devpulse-secrets.json`, set `DEVPULSE_SECRETS_FILE=/etc/secrets/devpulse-secrets.json`, and keep that file out of the repository and Docker image. The container user belongs to Render's secret-file group.

Mount persistent storage at /app/data, writable by the container's app user. It holds both keys and encrypted tokens. Protect filesystem access and backups: access to both keys and tokens permits decryption. On Windows, optionally enable DPAPI protection only when the deployment identity has a persistent profile. Without persistent storage, users must reconnect after container replacement.

Use one instance with the file-backed session store. Multiple replicas require shared session storage and distributed refresh locking.

## Verification

```powershell
dotnet build -c Release
dotnet run --project tests/DevPulse.Tests/DevPulse.Tests.csproj -c Release
dotnet format MiniProject_Everything_1.csproj --verify-no-changes --no-restore
dotnet publish MiniProject_Everything_1.csproj -c Release -o artifacts/publish
pwsh -File scripts/Smoke-Test.ps1
```

The regression executable exits nonzero on failure and uses no extra test-framework packages. Spotify responses are simulated, fixture data is temporary and no Spotify account is used. The HTTP smoke script runs its own loopback production process with dummy credentials and checks routes, static browser-player assets, the diagnostics gate, 404s, OAuth/PKCE and browser scopes, the protected token endpoint, trusted forwarding, and logout antiforgery.

GitHub Actions runs build, regression checks, formatting, smoke checks and publish on Windows and Linux after push.

## Owner review for live deployment

1. Set Spotify credentials, redirect URIs and allowed users in your developer account; enable Web API and Web Playback SDK use.
2. Choose persistent hosting storage and ensure the non-root app user can write it.
3. Configure trusted proxy addresses and the public hostname.
4. Decide whether diagnostics should be exposed and choose a scan root.
5. Configure a unique administrator password and test privileged actions behind HTTPS; enable process termination only if you explicitly need it.
6. Review every operations allowlist, log path, database target, and alert destination.
7. Optionally configure an OTLP collector and SMTP/webhook alerts.
8. Test real Spotify login, saved-album and playlist paging/playback, seamless external-device transfer, and optional browser audio using a Premium account.

## References

- [Spotify redirects](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)
- [Spotify playback requirements](https://developer.spotify.com/documentation/web-api/reference/start-a-users-playback)
- [Token refresh](https://developer.spotify.com/documentation/web-api/tutorials/refreshing-tokens)
- [ASP.NET Core trusted proxies](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-9.0)
