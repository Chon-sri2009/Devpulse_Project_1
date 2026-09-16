# DevPulse

A .NET 9 Blazor Server dashboard with host diagnostics and a Spotify remote player.

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
- **Operations:** owner-allowlisted HTTP/TCP watchlist, ping matrix, DNS lookup, TLS certificate expiry, database connectivity, and live log tailing.
- **Telemetry:** persisted metric history, request traces, threshold incidents, deployment identity, and optional OTLP export.
- **Inspectors:** collapsible JSON explorer, SHA-256 file hashing, local JWT decoding, and a bounded HTTP load tester.
- **Administration:** separate administrator cookie, process listing/termination, maintenance mode, audit history, and redacted diagnostics ZIP export.
- **Alerts:** memory, disk, and request-latency thresholds with in-app incidents plus optional HTTPS webhook and SMTP delivery.
- **Spotify:** protected OAuth/PKCE sessions, track/episode metadata, artwork, progress, devices, volume, playback controls, refresh, and rate-limit handling.
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

Restart, open /spotify, connect, then open Spotify on a device and start a track. Playback controls require Spotify Premium and a compatible device. Development-mode apps may require allowed users in the developer dashboard; consult Spotify's current restrictions. Some devices do not support volume control.

This is a remote controller for an existing Spotify client. Audio plays on that device. The page refreshes every 10 seconds and offers manual refresh; commands can take a moment to appear.

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
| Operations__ApprovedUrls__0 | https://api.github.com | Numbered HTTP/JSON/load-test allowlist |
| Operations__ApprovedLogFiles__0 | empty | Exact owner-approved file paths for live tailing |
| Operations__Databases__0__Name / Host / Port / Kind | empty | Database connectivity targets; Kind may be TCP or Redis |
| Operations__WatchIntervalSeconds | 300 | Scheduled service-watchlist interval; set to 0 to disable |
| Operations__LoadTestMaxRequests / LoadTestMaxConcurrency | 25 / 5 | Server-side load-test safety caps |
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

HTTP targets, network hosts, ports, logs, and databases cannot be supplied freely by visitors. They must be placed in the owner-controlled allowlists. This prevents the diagnostic server from becoming a general network proxy or load generator. The load tester is additionally capped at 25 requests and five workers by default. Log output and uploaded file contents are never included in request telemetry; uploaded files are streamed for hashing and are not stored.

For email alerts, configure `Alerts__Smtp__Host`, `Port`, `Username`, `Password`, `From`, `To`, and `EnableSsl` through secrets/environment settings. Webhook alerts accept HTTPS only. Redis health sends an unauthenticated `PING`; other database kinds verify TCP acceptance without running queries or exposing credentials.

Metric, incident, and audit history is stored as bounded in-memory views backed by JSONL journals under `Storage__DataPath`. Request traces are memory-only and omit query strings. Configure `Telemetry__OtlpEndpoint` to export ASP.NET Core, HttpClient, and runtime metrics/traces using OpenTelemetry Protocol.

## Docker / Render

```sh
docker build -t devpulse .
docker run --rm -p 10000:10000 -v devpulse-data:/app/data devpulse
```

Use the repository Dockerfile, port 10000 and health check /healthz on Render. The image runs as the non-root app user. The platform terminates HTTPS. Configure its trusted proxy addresses/networks so OAuth receives the correct HTTPS scheme; do not trust every network indiscriminately. Register the public HTTPS callback in Spotify.

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

The regression executable exits nonzero on failure and uses no extra test-framework packages. Spotify responses are simulated, fixture data is temporary and no Spotify account is used. The HTTP smoke script runs its own loopback production process with dummy credentials and checks routes, the diagnostics gate, 404s, OAuth/PKCE, trusted forwarding and logout antiforgery.

GitHub Actions runs build, regression checks, formatting, smoke checks and publish on Windows and Linux after push.

## Owner review for live deployment

1. Set Spotify credentials, redirect URIs and allowed users in your developer account.
2. Choose persistent hosting storage and ensure the non-root app user can write it.
3. Configure trusted proxy addresses and the public hostname.
4. Decide whether diagnostics should be exposed and choose a scan root.
5. Configure a unique administrator password and test privileged actions behind HTTPS; enable process termination only if you explicitly need it.
6. Review every operations allowlist, log path, database target, and alert destination.
7. Optionally configure an OTLP collector and SMTP/webhook alerts.
8. Test real Spotify login/playback using a Premium account and active device.

## References

- [Spotify redirects](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)
- [Spotify playback requirements](https://developer.spotify.com/documentation/web-api/reference/start-a-users-playback)
- [Token refresh](https://developer.spotify.com/documentation/web-api/tutorials/refreshing-tokens)
- [ASP.NET Core trusted proxies](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-9.0)
