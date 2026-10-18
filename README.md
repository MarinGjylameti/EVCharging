# EVCharging

An electric vehicle charging platform with a web application, backend API, and mobile app.

- `backend/` — .NET API and charging services.
- `web/` — Next.js web application.
- `mobile/` — React Native / Expo mobile application.

The project is under active development.

See [CHANGELOG.md](CHANGELOG.md) for the change history.

## Mobile scope and store readiness — 18 October 2026

Phase 1 remains monitoring, station administration, personal history, profile and
notifications. The Driver uses the Expo app or Driver web portal; Admin and
Superadmin use the responsive web workspace. Electronic payments, wallet,
subscriptions and remote start/stop are not added by this mobile refinement.

The phone station editor now opens separately from the station list, with a back
action and a warning before discarding edits. Session rows become labelled cards
on phones; filters are expandable. Web and native Driver views share guidance for
offline, unavailable, occupied and card-linked chargers. A station view does not
claim a charging session. The native charge uses its saved session tariff, and
declining push permission does not interrupt normal monitoring.

**Store submission is not ready yet.** Configure the client's actual pages in the
mobile build environment:

```text
EXPO_PUBLIC_PRIVACY_URL=
EXPO_PUBLIC_ACCOUNT_DELETION_URL=
```

Use public HTTPS pages without user identifiers, tokens or credentials. Privacy
is accessible before login and in Settings; the account-deletion entry appears in
Settings. The latter must open the actual deletion request/completion flow,
including identity verification and confirmation. A homepage, email-only support
link, or merely disabling an account is insufficient. No deletion service or
client retention policy is implemented by configuring these links. The client
must supply the pages, approved retention rules and an operational deletion
process. Production configuration rejects missing/invalid pages; internal preview
builds remain available for hardware testing. These checks validate configuration,
not the live page or the outcome of store review.

From `mobile`, run `npm run check:store:ios` and
`npm run check:store:android` before submission. Existing API HTTPS, Expo project
and Android Firebase checks also apply. The normal `check:ios` / `check:android`
commands continue to support internal preview.

Before submission, the release owner must complete:

- Client-approved privacy page and actual account/data deletion workflow, reachable
  both from the app and the Google Play listing. Declare retained data and timing.
  See [Apple account deletion](https://developer.apple.com/help/app-review/guideline-reference/5-1-1-account-deletion)
  and [Google Play account deletion](https://support.google.com/googleplay/android-developer/answer/13327111?hl=en).
- App Store App Privacy / Google Play Data safety disclosures matching the actual
  build, backend and SDKs: account/contact/vehicle information, charging history,
  push tokens and diagnostics as applicable. Verify the final native permissions
  and SDK privacy manifests; configuration source alone does not prove the binary.
- A working reviewer Driver account, reachable backend and clear instructions for
  the card/charger flow. For hardware-dependent review prepare a video showing
  the app on a physical device interacting with the charger; do not silently turn
  on global production TestMode to imitate real charging. See
  [Apple review information](https://developer.apple.com/help/app-review/before-submitting-for-review/complete-review).
- Signed iOS/Android builds, store identifiers and credentials, current platform
  submission requirements, and real-device tests: registration/login/logout,
  large text/VoiceOver/TalkBack, keyboard and Back, connection loss/recovery,
  notifications allowed/declined, own-versus-other-user data, and charging history.
  Test iPad too while `supportsTablet` remains enabled.
- Resolve the existing native-map acceptance gap: the Expo screen currently lists
  stations and opens the phone's map; it does not contain the embedded station map
  described in the client's specification. Do not advertise embedded maps, QR
  scanning, payments or remote start as completed native features.

## QR, card and ownership boundary

The implemented physical flow is:

```text
Registered card -> charger sends idTag -> backend matches assigned Driver
               -> StartTransaction creates the owned session
               -> MeterValues/StopTransaction update that session and its history
Signed-in Driver -> authenticated API/live updates -> own session only
```

The current station configuration supports **one assigned Driver/card per physical
charger**, plus an allowed-card list whose charges have no personal Driver owner.
It is not a general multi-customer RFID registry. Do not change assignments during
a charge; past history retains its original owner. Confirm this operational model
with the client before treating it as a shared public charging network.

A QR printed on a charger should identify only the station and connector. The
authenticated app already identifies the person; scanning a public label must not
grant ownership of an existing or subsequent charge. QR resolution/scanning and
QR-initiated charging are not implemented in this refinement. If later required,
their server-verified authorization must be designed from the actual charger
capabilities, without accepting an arbitrary user ID from a QR or HTTP body.

In OCPP 1.6, card authorization and transaction identity are separate from a
vendor's prepaid balance or payment system. An `idTag` alone does not establish a
balance or prove a payment. Verify the actual device/card provider instead of
assuming money is stored on the card. The OCA's
[OCPP 1.6 test cases](https://openchargealliance.org/wp-content/uploads/2025/09/CompliancyTestTool-TestCaseDocument.pdf)
cover authorization and transaction behavior; the implementation here calculates
cost but does not collect payment.

During the physical trial record the exact card `idTag`, station identity and
connector number. Check Authorize, StartTransaction, meter values and stop; verify
that a second Driver sees none of the first Driver's readings. Also check an
unassigned/unknown card, two connectors, reconnection and app background/foreground.
Use internal preview while these observations and store prerequisites are pending.

## Production presentation and test mode

Set **one backend environment variable**, then restart the API:

```text
EVCHARGING_Features__TestMode=false
```

The default is `false`, including when the variable is absent. Set it to `true`
on a test deployment to enable the existing demonstration tools. This switch is
independent of `DOTNET_ENVIRONMENT`; changing it requires an API restart, not a
database migration. Reload the web application after changing modes. The web
reads the authenticated `/api/v1/features` endpoint and defaults to OFF if that
lookup fails. Mobile uses the same server permissions and filtered responses.

| Behavior | OFF (default) | ON (testing) |
| --- | --- | --- |
| Physical OCPP connection, heartbeat and meter processing | Enabled | Enabled |
| Card authorization and real session ownership/history | Enabled | Enabled |
| Simulator connections, stations and direct simulator page | Disabled/hidden | Enabled |
| Demo map entries, history, sessions and station analytics | Hidden | Included |
| Driver protocol diagnostics, raw samples and diagnostic downloads | Hidden in HTTP, live events and UI | Available to the authorized session owner/demo recipient |
| Physical device assignment without a card | Rejected; old no-card assignments are inactive for new charges | Available for demonstrations |
| Superadmin operational logs and normal station management | Available, with role checks | Available, with the same role checks |

Stored demo data is retained, so enabling test mode makes it visible again. No
physical history or existing session ownership is deleted. With testing OFF,
new physical charges at a mapped station require a matching assigned card or an explicitly allowed
card; an allowed card without an assigned Driver does not expose its session to
another account. Configure the Driver's card in the station's device settings
before testing the normal user experience.

The master switch overrides the old `EVCHARGING_Ocpp__SimulatorEnabled` and
`EVCHARGING_Ocpp__AllowAnonymousSimulators` settings when OFF. With the master ON,
both default to true for the built-in browser simulator. Either subordinate
option can still be set false; authenticated external simulators then require
`EVCHARGING_Ocpp__SimulatorPassword` (12–128 characters). A browser simulator
cannot supply that Basic credential. Use test mode only for deliberate testing.

`--seed-demo` additionally requires the master switch and a Development/Testing
host. In the Render entrypoint, `EVCHARGING_SEED_DEMO_ON_START=true` is ignored
while the master is OFF, even if left over from an earlier demo. When the master
is ON and neither host environment is specified, the entrypoint selects
Development. Existing explicitly configured host environments remain authoritative.
An explicit Production host can run test tools with the flag ON, but cannot seed
demo accounts. Production database and HTTPS CORS checks remain enforced.

This switch prepares the normal product experience; it does not implement QR or
payment-provider verification, change physical device admission/credentials,
or certify unattended hardware reconnection. Those are separate production
requirements. Operational logs remain available to Superadmin to investigate them.

## Three-route charger test — 17 October 2026

Deploy this backend Docker image and the web once before the test. All three
approaches then use that same code, API, database, card configuration and charger
identity. Switching requires environment changes/redeploys and updating the
charger's URL, not another code edit. An API deploy interrupts existing sockets.
Keep one API instance, upgrade its compute plan before the test, and retain the
previous working connector until a new route has been verified.

The **backend** environment variable `EVCHARGING_Tunnel__Mode` selects where the
connector runs. Its default is `direct`; a misspelled mode fails configuration
validation. It is independent of `EVCHARGING_Features__TestMode`. Direct OCPP
access to the API remains available in every mode for rollback.

| Order | Backend mode | Physical charger URL | Extra connector service |
| --- | --- | --- | --- |
| 1 | `direct` | `wss://vcharging-api-demo.onrender.com/ws/cs/NKR2023072805` | None |
| 2 | `embedded` | `wss://<embedded-host-from-API-logs>.trycloudflare.com/ws/cs/NKR2023072805` | None; API manages cloudflared |
| 3, only if 1 and 2 fail | `external` | `wss://<external-host-from-worker-logs>.trycloudflare.com/ws/cs/NKR2023072805` | Paid Background Worker, created only for this fallback |

Both Quick Tunnel variants use the pinned `cloudflared:2026.9.0`, HTTP/2 to
Cloudflare, the public API's HTTPS origin, matching Host/SNI, verified TLS and
HTTP/1.1 to origin. The embedded route deliberately forwards to the same public
API origin as the separate connector, rather than introducing a fourth,
loopback-only topology during the comparison.

### Common preparation

1. In the existing API service's **Environment**, set:

   ```text
   EVCHARGING_Tunnel__Mode=direct
   EVCHARGING_Tunnel__Origin=https://vcharging-api-demo.onrender.com
   EVCHARGING_Features__TestMode=true
   EVCHARGING_SEED_DEMO_ON_START=false
   ```

   Origin is required only for `embedded`; when absent on Render it defaults to
   `RENDER_EXTERNAL_URL`. Keep the existing database, credentials, allowed hosts
   and HTTPS CORS settings. No migration, bootstrap or demo re-seed is required
   for route switching. If legacy `Ocpp:SimulatorEnabled` or
   `Ocpp:AllowAnonymousSimulators` variables are explicitly false, set them true
   for the built-in simulator during this test.
2. Deploy the API and web from this same code revision. Use **Environment > Save
   only**, then **Manual Deploy > Deploy latest commit** if selecting the new
   revision manually. Once only the environment changes, **Save and deploy** is
   sufficient. Set the API health check path to `/health/ready` if not already set.
3. Use the read-only PowerShell helper to generate matching URLs and environment
   values. It does not access the network or change a service or device:

   ```powershell
   .\scripts\ocpp-route-plan.ps1 -Mode direct | Format-List
   ```

### Trial 1 — direct Render API

Keep `EVCHARGING_Tunnel__Mode=direct`. Set the **web** environment variable
explicitly and redeploy the web:

```text
EVCHARGING_SIMULATOR_WS_BASE_URL=wss://vcharging-api-demo.onrender.com/ws/cs/
```

Configure the physical URL shown in row 1. If the charger appends its identity
itself, enter only `wss://vcharging-api-demo.onrender.com/ws/cs`. Probe readiness
and OCPP as described under "Checks for every route" below. If the physical
charger passes the entire flow, stop the comparison here.

### Trial 2 — Quick Tunnel inside the existing API

Set **backend** `EVCHARGING_Tunnel__Mode=embedded` and **Save and deploy**. The
API starts cloudflared after the HTTP host starts. Its stdout/stderr go to the
API service's **Render Logs**. Search for `OCPP embedded Quick Tunnel` to find
the new HTTPS hostname, then generate the matching configuration:

```powershell
.\scripts\ocpp-route-plan.ps1 -Mode embedded -TunnelBaseUrl "https://<embedded-host>.trycloudflare.com" | Format-List
```

Set the generated **web** variable, redeploy the web, and update the physical
charger URL. Keep the normal web application's API base pointed at Render;
only the simulator WebSocket base uses the tunnel. This variant uses the
existing API compute plan and needs neither a domain nor another paid worker.
The API and connector share that instance's CPU/RAM; monitor memory during the
physical test. Use a paid, always-on API instance for these comparisons.

The .NET host manages the child process and stops it on shutdown. A connector
exit or startup failure is logged and retried after five seconds while the API
continues serving direct requests. Each connector restart can create a **new
hostname**; update both charger and simulator settings after that happens. An
API deploy also restarts the embedded connector. Disabling TestMode does not
stop the tunnel or physical charging. Set mode back to `direct` and redeploy
to stop the embedded connector.

### Trial 3 — separate Quick Tunnel worker, last fallback

Set the **backend** mode to `external` and redeploy so it stops any embedded
connector. This mode does not create, pay for, or stop an external service.
Only if the first two physical trials fail, create the separate worker:

| Render field | Value |
| --- | --- |
| Service type / source | Background Worker / Existing Image |
| Image | `docker.io/cloudflare/cloudflared:2026.9.0` |
| Name | `evcharging-ocpp-quick-tunnel` |
| Region / instances | Frankfurt / 1 |
| Compute | Smallest paid instance; review the displayed price |
| Docker Command | `tunnel --protocol http2 --url https://vcharging-api-demo.onrender.com --http-host-header vcharging-api-demo.onrender.com --origin-server-name vcharging-api-demo.onrender.com --no-tls-verify=false --http2-origin=false --grace-period 10s` |

No connector token, Cloudflare account or domain is needed for this Quick
Tunnel. Alternatively apply [render.quick.yaml](tools/cloudflare-tunnel/render.quick.yaml)
as a **separate** Blueprint, using that exact path, only when ready for the paid
fallback. Use one creation method. Do not apply the named-tunnel `render.yaml`
for this trial, and do not create a second API or database. The image supplies
the `cloudflared --no-autoupdate` entrypoint; Docker Command contains its arguments.

Read the random HTTPS hostname from this **worker's Render Logs**, then run:

```powershell
.\scripts\ocpp-route-plan.ps1 -Mode external -TunnelBaseUrl "https://<external-host>.trycloudflare.com" | Format-List
```

Update the charger and the web's simulator base using that output. Worker
restarts change this URL too. Reverting the backend mode alone does **not**
stop the worker or its bill; remove the separate worker after a failed trial.
Deleting this disposable connector does not remove the API or database.

### Checks for every route

1. Open that route's `/health/ready` and confirm HTTP 200 with `{"status":"ready"}`.
   This tests HTTP only, not physical charger compatibility.
2. Run the OCPP probe with a separate setup identity:

   ```powershell
   . .\scripts\charger-test-tools.ps1
   Test-PublicOcpp -BaseUrl "https://<host-of-current-trial>"
   ```

   It throws on handshake/subprotocol/BootNotification failure. `VoidTaskResult`
   output alone can occur on success. A simulator passing does not establish
   that the physical device passes.
3. With the physical charger idle, change its URL, note the exact test time and
   allow at least five minutes for the observed hardware reconnect delay. In
   Superadmin logs confirm identity `NKR2023072805`, accepted BootNotification
   and continuing Heartbeat replies. Record the actual HTTP status/CF-Ray when
   a handshake fails; do not treat a missing Worker event as a diagnosis.
4. Test a brief idle network interruption and automatic recovery. Then test an
   actual authorized charging session, MeterValues, StopTransaction and saved
   history. Register the correct card first: a StartTransaction `SecurityError`
   is separate from successful WebSocket connectivity. Do not reuse the physical
   identity in a simulator/probe because it can replace its connection.
5. On the successful route, set **backend** `EVCHARGING_Features__TestMode=false`,
   redeploy/reload, and repeat the authorized physical flow. The simulator and
   Driver diagnostics should be hidden. Keep the route mode unchanged.

Quick Tunnels have temporary hostnames and no uptime guarantee. These three
trials are prepared alternatives, not proof that any untested hardware route
will succeed. The named tunnel below is a later option for a stable hostname.

## Persistent OCPP tunnel

The optional [Render Blueprint](tools/cloudflare-tunnel/render.yaml) runs
`cloudflared` as a separate Background Worker. It replaces the computer running
the temporary tunnel while retaining the existing Render API and database:

```text
Charger --WSS--> Cloudflare Tunnel --> cloudflared on Render --HTTPS--> Render API
```

This uses a named, remotely managed Cloudflare Tunnel. It does not execute the
JavaScript proxy in `tools/cloudflare-ocpp`. The current Quick Tunnel can remain
available during setup. A new hostname and connector location still require a
physical charger test, including automatic reconnection; success through the
temporary tunnel does not establish the cause of earlier HTTP 403 responses.

### Requirements

- A domain you control, active in Cloudflare DNS. `ocpp.example.com` below is a
  placeholder. A `workers.dev` address cannot serve as this domain.
- A named tunnel in Cloudflare and its connector token.
- A paid Render Background Worker. Review the price shown by Render before
  creating it; domain registration and API/database hosting are separate costs.
  The optional Blueprint selects one `0.5c-512mb` instance in Frankfurt and does
  not upgrade the existing API or database. Production availability also requires
  an API that stays running and a durable database.

Cloudflare documents [named tunnels](https://developers.cloudflare.com/tunnel/get-started/)
and the temporary hostname/lifetime limits of
[Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).
Render documents [Background Workers](https://render.com/docs/background-workers)
and [image deployments](https://render.com/docs/deploying-an-image).

### 1. Create the named tunnel and connector

In Cloudflare, open **Networking > Tunnels**, create a tunnel named
`evcharging-ocpp`, and select the Docker installation instructions. Copy only
the connector token into Render's secret environment variable `TUNNEL_TOKEN`.
Keep it out of source files, command overrides, screenshots, and chat.

In Render, choose **New > Background Worker > Existing Image**:

| Setting | Value |
| --- | --- |
| Name | `evcharging-ocpp-tunnel` |
| Image | `docker.io/cloudflare/cloudflared:2026.9.0` |
| Region | Frankfurt |
| Instance count | 1 |
| Docker Command | `tunnel --protocol http2 --grace-period 30s run` |
| Environment | `TUNNEL_TOKEN` = the connector token |
| Maximum shutdown delay | 60 seconds |

Alternatively, apply `tools/cloudflare-tunnel/render.yaml` as a separate
Blueprint using that file's path. Use one creation method to avoid duplicate
workers. The image supplies the `cloudflared --no-autoupdate` entrypoint; the
command above supplies its arguments. Version `2026.9.0` matches the successful temporary
connector test. The official
[release](https://github.com/cloudflare/cloudflared/releases/tag/2026.9.0) is pinned
so a restart does not silently select a new version.

The worker initiates outbound connections to Cloudflare using HTTP/2; it needs
outbound TCP port 7844. It does not need an incoming port or a Render public URL.
Confirm `Registered tunnel connection` in Render logs and `Healthy` in
Cloudflare before continuing. The
[run parameters](https://developers.cloudflare.com/tunnel/reference/run-parameters/)
document token environment variables, transport selection, and shutdown handling.

### 2. Publish the OCPP hostname

On the tunnel's **Routes** page, add a **Published application** route using a
subdomain of your domain, for example `ocpp.example.com`:

| Setting | Value |
| --- | --- |
| Hostname | Your actual OCPP subdomain |
| Path | `^/(health/ready\|ws/cs/[A-Za-z0-9_-]{1,40}/?)$` |
| Service | `https://vcharging-api-demo.onrender.com` |
| HTTP Host Header | `vcharging-api-demo.onrender.com` |
| Origin Server Name | `vcharging-api-demo.onrender.com` |
| Disable TLS certificate verification / No TLS Verify | Off |
| Use HTTP/2 to origin | Off |

The path expression contains a literal `|` between `health/ready` and `ws/cs`.
These origin settings preserve the working comparison's Host, TLS verification,
and HTTP/1.1 WebSocket upgrade to Render. The public hostname belongs to the
tunnel; do not add it as a custom domain of the Render API. The Cloudflare
dashboard creates the tunnel's DNS route. If configuring DNS separately, its
proxied CNAME target is `<TUNNEL_ID>.cfargotunnel.com`.

[ingress.example.json](tools/cloudflare-tunnel/ingress.example.json) is the
equivalent Cloudflare configuration API body, with a final 404 catch-all. Replace
its example hostname before use and apply it only to this new tunnel. Render
does not load this file: remotely managed tunnel routes are configured in
Cloudflare. Keep interactive Access login and browser challenges off this OCPP
hostname; admission and transaction authorization remain in the API.
See Cloudflare's
[origin settings](https://developers.cloudflare.com/tunnel/reference/origin-parameters/).

### 3. Verify and switch the charger

1. Request `https://<your-ocpp-hostname>/health/ready` and confirm HTTP 200 with
   `{"status":"ready"}`. This checks HTTP readiness only.
2. Run the existing PowerShell test against the new hostname:

   ```powershell
   . .\scripts\charger-test-tools.ps1
   Test-PublicOcpp -BaseUrl "https://<your-ocpp-hostname>"
   ```

   The helper throws if the connection, `ocpp1.6` negotiation, or accepted
   BootNotification fails. It may print `VoidTaskResult` on success.
3. Configure the physical charger with
   `wss://<your-ocpp-hostname>/ws/cs/<charge-point-id>`. If its configuration
   appends the ID automatically, enter only the base ending in `/ws/cs`.
4. Confirm the physical ID, accepted BootNotification, status messages, and
   continuing Heartbeat replies in the application logs. Test a brief network
   interruption and recovery while the charger is idle, then verify an actual
   authorized charging session separately. Connector or API restarts can close
   existing sockets even though the public hostname stays the same.
5. Once the new route passes these checks, stop the temporary connector. To make
   the built-in simulator use the same new route, set the web service's
   `EVCHARGING_SIMULATOR_WS_BASE_URL` to `wss://<your-ocpp-hostname>/ws/cs` and
   redeploy the web service. This is a later cutover step, not part of the worker
   Blueprint.

Until the new hostname is verified, the running Quick Tunnel is the fallback.
If that temporary process is stopped, its URL cannot be assumed reusable.
