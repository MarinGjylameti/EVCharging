# Current simulator verification

Scope: existing backend/web and OCPP 1.6 simulator flow. No architecture replacement, mobile work, new dependencies, database migration or infrastructure setup. The public simulator and deployed services were not accessed during this review.

## Necessary corrections

- Operations snapshots previously reloaded on every meter notification, private command receipt and SSE heartbeat. Forecast changes could also trigger full analytics reloads. Forecast-only public station deltas now carry additive `telemetryOnly` metadata through the committed outbox and authorized SSE notice. Driver deltas retain their existing payload/event type. Operations views ignore metering/command/forecast notices; the session monitor still refreshes metering snapshots. Ready/reconnect, visibility recovery, unknown notices and lifecycle/status changes still refresh snapshots.
- Simulator provisioning, anonymous simulator access and demo QR bypass could be enabled in Production through simulator flags. API startup now rejects either simulator flag in Production. Explicit Development/Testing/Staging simulator configuration remains supported. Physical charger admission and existing simulator credentials/allowlist are unchanged.
- Stored operational logs lacked direct physical session/station/connector/inbox correlation. Processing/rejection/replay diagnostics now include known identifiers using already loaded entities, without extra database queries. Successful meter processing/replies use Debug; the inbox and normalized telemetry still retain the readings. Rejections remain visible, and outbox payload failures include a stable event name, error code and attempt count. Secrets and raw frame contents are not added to operational logs.

## Local evidence

- Backend compilation: passed, no warnings/errors; used installed dependencies with `--no-restore` and single-process MSBuild.
- Existing project-local in-process xUnit runner: 266 passed, 0 failed, 16 PostgreSQL cases skipped. Uses TestServer/in-memory persistence; does not prove SQL isolation or durability.
- Web coverage command: 148 passed, 0 failed. Loaded production source coverage: 90.03% lines, 71.65% branches across 43/81 files. Unloaded files are outside this measurement.
- TypeScript: passed. New operations hook tests exercise 100 meter notices without analytics reloads, coalesced session-monitor refreshes, SSE reconnect/visibility recovery, legacy notices, failure and cancellation.
- Existing offline OCPP replay: passed (20 kWh, calculated cost EUR 7.00, one duplicate ignored). This fixture replay does not connect hardware or verify live simulator interoperability.
- Existing local WebSocket tests cover BootNotification, Heartbeat, Authorize, statuses, StartTransaction, metering, pause/resume, StopTransaction, duplicates and reconnect. Extended recovery assertions preserve session ID, physical start, accumulated energy/cost and private ownership before stopping. Existing tests cover manual Waiting/Linked exclusivity, expiry, completion release, application reopen, multiple clients, privacy, bounded SSE delivery and outbox recovery.
- New log assertions verify persisted lifecycle identifiers, retained normal meter inbox/samples, visible rejected meters and absence of tag/raw invalid-value leakage. Forecast metadata is verified from projection through the operations bus/SSE without private payload exposure.

## Deliberately deferred verification

**PostgreSQL/SQL isolation, locking, unique constraints, migrations, committed outbox persistence and analytics integration: not executed — requires CI/staging/real PostgreSQL.** Use the existing Verification workflow and its disposable database. Do not enable these tests against a live database; PostgreSQL opt-in requires both `EVCHARGING_TEST_POSTGRES_ENABLED=1` and a dedicated `evcharging_test*` connection.

**Docker/Render startup and public simulator/browser testing: not executed — requires CI/staging.** No PostgreSQL installation, Docker/security/permission changes or external-network commands were attempted. The current laptop's native Next production build and standard VSTest host were previously blocked and were not retried. No security or permission changes were made to unblock them. CI already runs the normal backend test/coverage command, offline replay, web production build and startup shell regression.

Before accepting the staging run, connect the existing public simulator with its configured identity and `ocpp1.6` subprotocol. Execute the full charging sequence; disconnect/reconnect the charger and browser SSE separately; reopen the same Driver's charging page in another client; confirm the session ID, physical start, energy, saved tariff and ownership stay unchanged. Stop once, confirm history/claim release, and confirm other Drivers cannot see or claim that active connector. Correlate connection, call/inbox and physical session IDs in stored logs. Keep demo flags off in Production.

Health code remains unchanged: `/health/live` checks the process; `/health/ready` checks required database connectivity and returns 503 without dependency details on failure. Local tests verify healthy responses; actual database outage and hosted HTTPS/CORS behavior still require staging. Existing bounded history/session/log queries and server-side analytics aggregation were retained; no SQL performance or production availability claim is made from in-memory tests.

No commit or push was performed.
