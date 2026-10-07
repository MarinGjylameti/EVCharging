# Optional E2E foundation

**Prepared but intentionally not executed.** No browser, Playwright runner, E2E OCPP client or E2E server was launched; no packages or browser binaries were downloaded.

E2E exercises a real user flow across the browser, web BFF, backend, database and charger connection. It complements the existing class/HTTP/in-memory tests; those tests cannot prove browser rendering or deployed interoperability.

## Normal development stays unchanged

`dotnet test`, `npm test`, `npm run test:coverage`, `npm run build`, `npm run typecheck`, application startup and the existing Verification workflow do not run E2E. Definitions live in `web/e2e/specs/*.spec.mjs`, outside Node's `web/tests/*.test.mjs` discovery. Normal tests may validate the **pure environment validator** and read configuration files; they never import the E2E entry point, Playwright config or OCPP/browser fixtures.

Only these dedicated commands, run from `web`, execute E2E when intentionally requested:

```powershell
npm run test:e2e
npm run test:e2e:smoke
```

The entry point validates settings before starting Playwright. Direct Playwright invocation is rejected by the configuration unless the dedicated command set its internal opt-in marker. There is no `webServer`, global setup, auto-installation, package hook or new CI workflow. The suite never starts backend/web or resets a database.

## Requirements before the first intentional run

Use an **approved, isolated test environment with its own test database**, already running compatible API and web deployments. No local PostgreSQL or Docker is required by this foundation. Do not point the API/web at a real production database. Hostname checks cannot prove database isolation: the operator must confirm the configured servers use the dedicated test database before acknowledging test resources.

- Supply `E2E_ENVIRONMENT` (`local`, `ci` or `staging`), explicit `E2E_BASE_URL` and `E2E_API_URL`, and both exact origins in comma-separated `E2E_APPROVED_ORIGINS`. There are no URL defaults. Local permits loopback only; remote endpoints require HTTPS and a test/staging/demo hostname. Production hostnames/environments and URLs containing credentials, paths or queries are rejected.
- Set `E2E_RESOURCE_ACK=dedicated-test-resources` only after confirming isolation. Use a dedicated existing **Driver** account whose email starts with `e2e-`; supply its password through the environment. The suite logs in through the real UI, retains credentials/tokens in memory and does not save authenticated storage state. Trace, screenshots and video are disabled. Treat even ignored local test output as potentially sensitive.
- For the full suite, reserve a disconnected, idle `SIM-E2E-*` device with one connector, mapped to a non-archived demo station whose code matches the identity. Configure it explicitly in the backend's existing `Ocpp:SimulatorIdentities` allowlist, with `Ocpp:SimulatorEnabled=true`, a non-anonymous simulator password and matching tag. Use Development/Testing/Staging as appropriate; current API startup deliberately forbids simulator flags in Production. Preserve existing manual simulator identities if using a shared test environment; never repurpose their active devices.
- Supply `E2E_CHARGER_ID`, `E2E_CHARGER_PASSWORD` and `E2E_TAG_ID`. The test account/device must have no prior active or waiting claim, driver assignment, existing socket or charge. Busy state fails preflight and is never forcibly reset. Smoke only needs the account and URL settings.
- The web BFF's existing API URL and same-origin configuration must match this environment. The tests do not weaken auth, ownership, QR/claim rules, TLS checks, OCPP validation or database constraints. The current demo claim path is used explicitly; real QR workflows are outside this initial foundation.

`web/e2e/.env.example` contains placeholders. Nothing auto-loads it; export settings deliberately in an approved session. Do not commit real `.env` files or credentials. For loopback URLs, use the ports of an already available safe test host; the suite will not create one.

Playwright is **not currently installed**. Its package declaration is isolated in `web/e2e/package.json`, so normal `web/package-lock.json` and `npm ci` remain unchanged. The declared Playwright range matches the existing Next optional peer range; `ws` supplies the Node OCPP WebSocket with Basic headers. Before installing later, review/approve dependency versions and create the optional package lock in that approved environment.

Only after explicit approval for network/dependency setup, an operator can install this isolated package without install hooks/browser download, then intentionally install the Chromium dependency in a suitable environment. For example, these are **instructions for later, not commands executed by Codex**:

```powershell
# From web, in the approved environment only:
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = "1"
npm --prefix e2e install --ignore-scripts
# Browser installation is a separate explicit decision, never an install hook:
node e2e/node_modules/@playwright/test/cli.js install chromium
```

On the work laptop, do not automatically perform either step, install global tools/services, change security/browser policies or use privileged workarounds. If packages or an approved browser are unavailable, leave execution for CI/staging. No workflow was added to push/PR Actions.

## Three representative tests

1. **Authentication smoke:** real Driver login reaches the Driver area and map.
2. **Charging flow:** charger status reaches the map without a document reload; a UI claim precedes a physical OCPP start; the page shows charging, energy/power/SoC, pause/resume and completion; the completed record is visible through the Driver's history flow. The physical `StartedAt` is checked against the sent timestamp. Demo UI elapsed time intentionally uses `StartReceivedAt`, matching current product behavior.
3. **Recovery:** browser reload and a second page retain the same session ID, physical start, accumulated cost/energy and one authorized active session. An E2E-only EventSource control closes the browser's stream, then permits real SSE/REST recovery; no synthetic session payload or product hook is introduced. A document marker detects unintended full-page refreshes.

The small project-owned client uses the existing backend tests' OCPP 1.6 CALL/reply envelope and only BootNotification, Heartbeat, Authorize, StatusNotification, StartTransaction, MeterValues and StopTransaction. It opens its socket and heartbeat timer only inside an explicitly executed charging fixture. Manual use of the public simulator is unchanged.

Tests run with one worker and no automatic retries because physical starts have side effects. Each charging scenario performs its own preflight and claim/start; it does not require another test to have run first. Do not run two suites against the same reserved account/device concurrently.

Cleanup stops only the transaction acknowledged to the current fixture and cancels only its own still-waiting claim. It does not delete accounts/stations/history, clean pre-existing resources, broadly delete rows or reset databases. Completed E2E history is intentionally retained. A lost Start reply or transport/cleanup failure may leave a charge requiring manual inspection of **that reserved test device** before another run; do not reset shared data. These definitions remain unverified against a real browser/backend until the first intentional E2E run.
