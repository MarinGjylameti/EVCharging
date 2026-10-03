# EVCharging

Platformë për menaxhimin e karikimit elektrik, në një projekt të ri pranë HOUSS.

## Gjendja

**Implementimi po vazhdon.** Web-i dhe API-ja kanë përparësi; aplikacioni iOS/Android ka hyrë në fazën e parë të zhvillimit.

- **Backend C#/.NET:** regjistrim Driver, hyrje/dalje, role, profil, gjuhë, ndryshim fjalëkalimi, audit, njoftime dhe radhë push. Migrimet PostgreSQL përfshijnë pikat, historikun, sesionet, pronësinë dhe diagnostikimin live.
- **Web Next.js:** hyrje Driver/Admin/Superadmin. Driver-i ka hartën me pika karikimi, filtra, detaje të pikës, hapje të udhëzimeve në Google Maps ose Apple Maps, kohën e mbetur të karikimit kur ka vlerësim të freskët dhe historikun personal. Operatori sheh pikat dhe përmbledhjen; Superadmin mund t’i krijojë, ndryshojë dhe arkivojë me audit. Sesioni mbahet në cookie HttpOnly.
- **Simulatorët:** seed-i përgatit SIM-001…SIM-010. Tre pika janë për provat e përdoruesit kryesor; shtatë kanë përdorues të veçantë testues. Statuset dhe karikimet krijohen nga mesazhet e simulatorit. [Udhëzimi i provës me disa përdorues](docs/demo-simulator-scenario.md).
- **Demo në Render:** API-ja ka Dockerfile, pranon URL-në e brendshme PostgreSQL dhe mund të krijojë në mënyrë të kontrolluar të dhëna demo në databazë. [Udhëzimet hap pas hapi](docs/deploy-render.md).
- **OCPP 1.6:** Boot, Authorize, Heartbeat, Status, Start, Meter dhe Stop; sesione persistente, status/paralajmërime publike dhe matje/komanda private me SSE. UI merr deltat drejtpërdrejt; diagnostikimi nuk bën polling. Mbulimi aktual dhe kufijtë janë te [sesionet live](docs/charging-session-realtime.md).
- **Mobile iOS/Android:** kodi Expo për llogarinë Driver, pikat, historikun, profilin dhe njoftimet ka filluar. Paketat Expo dhe provat në pajisje mbeten të bllokuara nga qasja e mjedisit në npm.
- **Ende për t’u ndërtuar/verifikuar:** caktimet e pikave te Admin, statistikat, QR dhe autorizimi i pajisjeve reale, OCPP 2.0.1 dhe shpërndarja në shumë instanca.

**Versioni i plotë ende nuk është gati për përdorim real.** PostgreSQL, pajisjet OCPP dhe rrjedhat vizuale nuk janë verifikuar fund më fund.

## Verifikimi lokal

98 teste backend dhe 66 teste web kaluan në verifikimin e rrjedhës së simulatorit më 3 tetor 2026. TypeScript dhe build-i Next kaluan. Nëntë prova PostgreSQL mbeten të paekzekutuara; testet API me InMemory nuk provojnë kufizimet/transaksionet e PostgreSQL. Testet me 10 pika provojnë HTTP/WebSocket/SSE dhe privatësinë mes përdoruesve. Detajet dhe kufijtë: [sesionet live](docs/charging-session-realtime.md).

Komandat e kontrollit janë te [scripts/verify-local.ps1](scripts/verify-local.ps1). Ato përdorin paketat lokale dhe nuk shkarkojnë nga interneti. [Udhëzimet backend](backend/README.md) dhe [udhëzimet web](web/README.md) përshkruajnë nisjen dhe konfigurimin.

## Burimet dhe dokumentacioni

- [Sesionet, pronësia dhe përditësimet live](docs/charging-session-realtime.md)
- [Dhjetë simulatorë dhe prova me disa përdorues](docs/demo-simulator-scenario.md)
- [Analiza e serverit OCPP të kolegut](docs/ocpp-reference-review.md)
- [Matrica e kërkesave](docs/requirements-matrix.md)
- [Arkitektura](docs/architecture.md), [modeli i domenit](docs/domain.md), [databaza](docs/database.md)
- [Siguria dhe API](docs/security-and-api.md), [integrimi/realtime/QR](docs/integration-and-realtime.md)
- [Plani i implementimit](docs/implementation-plan.md)
- [Saktësimet e përdoruesit](docs/reference/conversation-notes.md)

HOUSS dhe projekti i kolegut janë burime referimi dhe nuk janë ndryshuar. Pagesat, portofoli, bankat, faturimi, abonimet, rezervimet dhe komandat remote nuk janë pjesë e këtij implementimi.
