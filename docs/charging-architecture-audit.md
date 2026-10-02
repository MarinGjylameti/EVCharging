# Charging architecture audit — 2026-10-02

Reviewed branch `main`, starting at `92faa6e`, including the uncommitted realtime fixes and existing tests. Existing authentication/readiness edits are preserved.

## Keep

- Modular monolith; OCPP inbox, immutable raw readings and protocol replies in Infrastructure.
- Per-device PostgreSQL row lock; transactional outbox; authenticated SSE and authorized recovery snapshots.
- Existing `ChargingConnectionRequest` lifecycle, price-at-physical-start snapshot, separate observed/received times.
- In-memory device registry and event bus for the explicitly single-instance deployment.

## Change

- `ChargingSession` was only an offline fixture aggregate. Live ownership, pricing, readings and history were driven by `OcppChargingTransaction`; `IChargerEventReceiver` had no implementation. Make persisted protocol-neutral sessions authoritative and link protocol records to them.
- Waiting claims were unique only per connector. There was no one-Driver invariant across Waiting and Linked. Use the existing claim table for both and enforce partial unique indexes plus stable conflict responses.
- `ConnectAsync` rejected unowned running sessions; StartTransaction silently used `Demo:DriverEmail`. Claim the original physical session and remove this fallback from runtime.
- Realtime worker stopped outside simulator mode and also detected offline devices. Separate dispatch from device liveness.
- SSE dropped station identity. Use a safe typed envelope and public station projections; route private events only to their owner.
- Map updates reloaded stations, history and all session samples. Use targeted invalidations/deltas and persisted current session readings; reserve full snapshots for startup/recovery.
- Supersede abandoned persisted device connections under the existing lock.

## Do not implement

No new messaging infrastructure, microservices, distributed ownership, SignalR migration, remote charging commands, payments or final production QR/authentication scheme. No synthesized energy/power or battery-completion forecast. Greencharge's actual hardware messages and ownership proof remain required.

## Verification boundary

Fast in-process tests remain useful, but cannot establish PostgreSQL locking, rollback or unique/FK enforcement. Provide separately gated tests against an explicitly disposable PostgreSQL database. Do not change corporate security, networking, Docker permissions or system configuration to run them.
