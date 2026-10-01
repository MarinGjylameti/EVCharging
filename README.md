# EVCharging

Platformë për menaxhimin e karikimit elektrik, në një projekt të ri pranë HOUSS.

## Gjendja

**Implementimi po vazhdon.** Web-i dhe API-ja kanë përparësi; aplikacioni iOS/Android ka hyrë në fazën e parë të zhvillimit.

- **Backend C#/.NET:** regjistrim Driver, hyrje/dalje, role, profil, gjuhë, ndryshim fjalëkalimi, audit, njoftime dhe radhë push. Katër migrime PostgreSQL janë krijuar, përfshirë pikat, historikun, versionin e pikës dhe fushat opsionale të vlerësimit të disponueshmërisë.
- **Web Next.js:** hyrje Driver/Admin/Superadmin. Driver-i ka hartën me pika karikimi, filtra, detaje të pikës, hapje të udhëzimeve në Google Maps ose Apple Maps, kohën e mbetur të karikimit kur ka vlerësim të freskët dhe historikun personal. Operatori sheh pikat dhe përmbledhjen; Superadmin mund t’i krijojë, ndryshojë dhe arkivojë me audit. Sesioni mbahet në cookie HttpOnly.
- **Demo lokale:** host-i i përkohshëm krijon një Driver, 10 pika shembull në Tiranë, Durrës dhe Lushnjë dhe 6 karikime historike. Pika në Lushnjë ka vlerësim ilustrues prej 40 minutash. Harta përdor pllakat OpenStreetMap kur hapet në shfletues. Vendndodhjet, statuset dhe parashikimi janë vetëm ilustrime.
- **OCPP:** kodi i kolegut u lexua. Interpretimi C# mbulon status/start/matje/stop; prova offline jep 20 kWh × 0.35 EUR = 7 EUR dhe injoron matjen e përsëritur.
- **Mobile iOS/Android:** kodi Expo për llogarinë Driver, pikat, historikun, profilin dhe njoftimet ka filluar. Paketat Expo dhe provat në pajisje mbeten të bllokuara nga qasja e mjedisit në npm.
- **Ende për t’u ndërtuar:** caktimet e pikave te Admin, të dhëna reale të sesioneve/statistikave, QR i verifikuar, SignalR dhe serveri WebSocket me pajisje reale.

**Versioni i plotë ende nuk është gati për përdorim real.** PostgreSQL, pajisjet OCPP dhe rrjedhat vizuale nuk janë verifikuar fund më fund.

## Verifikimi lokal

44 teste backend dhe 16 teste web kalojnë. TypeScript dhe build-i Next kalojnë. Testet API përdorin ruajtje të përkohshme vetëm për testim; nuk provojnë kufizimet/transaksionet e PostgreSQL. Detajet dhe kufijtë: [gjendja e implementimit](docs/implementation-status.md).

Komandat e kontrollit janë te [scripts/verify-local.ps1](scripts/verify-local.ps1). Ato përdorin paketat lokale dhe nuk shkarkojnë nga interneti. [Udhëzimet backend](backend/README.md) dhe [udhëzimet web](web/README.md) përshkruajnë nisjen dhe konfigurimin.

## Burimet dhe dokumentacioni

- [Analiza e serverit OCPP të kolegut](docs/ocpp-reference-review.md)
- [Matrica e kërkesave](docs/requirements-matrix.md)
- [Arkitektura](docs/architecture.md), [modeli i domenit](docs/domain.md), [databaza](docs/database.md)
- [Siguria dhe API](docs/security-and-api.md), [integrimi/realtime/QR](docs/integration-and-realtime.md)
- [Plani i implementimit](docs/implementation-plan.md)
- [Saktësimet e përdoruesit](docs/reference/conversation-notes.md)

HOUSS dhe projekti i kolegut janë burime referimi dhe nuk janë ndryshuar. Pagesat, portofoli, bankat, faturimi, abonimet, rezervimet dhe komandat remote nuk janë pjesë e këtij implementimi.
