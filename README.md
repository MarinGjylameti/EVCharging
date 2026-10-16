# EVCharging

An electric vehicle charging platform with a web application, backend API, and mobile app.

- `backend/` — .NET API and charging services.
- `web/` — Next.js web application.
- `mobile/` — React Native / Expo mobile application.

The project is under active development.

See [CHANGELOG.md](CHANGELOG.md) for the change history.

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

### Next coordinated charger test

Keep the current deployed version and temporary tunnel running until the planned
test on Saturday, 17 October 2026. These local changes do not change its hostname,
Cloudflare settings, Render services, or deployment configuration.

1. During the test window, push/deploy the API and web together with
   `EVCHARGING_Features__TestMode=true`. Keep the existing charger URL for the
   first regression check.
2. To exercise the browser simulator through that same temporary tunnel, set
   the **web** variable `EVCHARGING_SIMULATOR_WS_BASE_URL` to the current tunnel's
   `wss://<current-host>/ws/cs/` address. This is a deployment choice, not a
   hardcoded temporary hostname. Verify simulator and physical IDs separately.
3. Verify real charging with the assigned card, StopTransaction/history, and
   disconnection/reconnection. Then set the backend master flag to `false`,
   restart/reload, and repeat the physical card flow. Verify that simulator
   access, no-card assignments and Driver diagnostic tools are unavailable.
4. Prepare the persistent tunnel below once a domain is available. Validate its
   hostname with the physical charger before retiring the temporary tunnel. The
   feature flag does not select or replace the network route.

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
