# Plani i implementimit dhe verifikimit

Discovery është përfunduar dhe implementimi ka nisur. Gjendja konkrete është te [implementation-status.md](implementation-status.md). Backend-i dhe web-i kanë përparësi; mobile është shtyrë. Tabela e mëposhtme përshkruan kushtet e pranimit të fazave të plota, jo pretendim që janë përfunduar. Mund të fillojnë me fixture dhe simulator të izoluar; integrimi real dhe pranimi i monitorimit personal presin të dhënat e karikuesit.

## Fazat dhe kushtet e kalimit

| Faza | Rezultati konkret | Kushti i verifikimit | Varësia |
|---|---|---|---|
| 0 — Discovery | Burimet, matrica, arkitektura, domain, DB, autorizimi dhe plani | Burime të lexuara, lidhje dokumentesh dhe traceability të kontrolluara | Përfunduar në këtë dorëzim |
| 1 — Skeleti dhe provat teknike | Solution .NET, Next, Expo, config i izoluar, CI; provë harte iOS/Android | Build/lint/typecheck; asnjë varësi nga HOUSS; harta pan/zoom/select në pajisje | Leje për rrjet në instalimet lokale; akses Mac/iPhone për iOS |
| 2 — DB dhe identiteti | Migrimi fillestar, hyrje, refresh/logout, tre role, bootstrap Superadmin, caktime | Teste API me role/ID të manipuluara; DB isolation; refresh race/revokim | Saktësim regjistrimi Driver dhe semantikës së caktimeve |
| 3 — Vendndodhje, pika, çmime | CRUD i kërkuar, arkivim, gjendje manuale, price history, audit atomik | Kufijtë Admin, konflikt versioni, audit rollback, snapshot çmimi | Rregullat e çmimit/arkivimit dhe pika logjike |
| 4 — Sesione dhe integrim | Domain lifecycle, inbox/outbox, adapter contract, simulator | Duplicate/out-of-order/concurrent tests; energji/kosto e saktë | Fixture normalizimi; protokolli real nuk nevojitet për unit tests |
| 5 — Driver dhe realtime | Harta, detajet, sesioni personal, përmbledhja/historiku, rikthimi pas rrjetit; QR sipas saktësimit | Data isolation, snapshot/reconnect, permissions, zero/null/freshness, pajisje reale | Mekanizmi i verifikuar i lidhjes Driver–session për pranimin real |
| 6 — Panelet operative | Admin dashboard/filtra/aktivitete; Superadmin global/adminë/caktime/drejtues | Agregime nga serveri; të gjitha listat dhe eventet sipas scope-it | Modulet dhe politika e historikut |
| 7 — Njoftime, profil, cilësime | Vetëm llojet/fushat/kanalet e dakordësuara | Deduplikim, marrës të saktë, loading/empty/error, logout cache | Konfirmim i formulimeve opsionale |
| 8 — Hardware dhe gatishmëria | Adapter real, test fund-më-fund, operim/backup, kontrata dhe migrime stabile | Pajisje reale, prova mobile, replay/reconnect, recovery dhe UAT | Dokumentacion, test credentials dhe leje për rrjet/integrim |

Pas çdo faze: build dhe testet përkatëse, rregullim regresionesh dhe përditësim i matricës. Status Implemented kërkon sjellje reale; status Tested kërkon evidencë testesh. Faqe me mock data nuk konsiderohet kërkesë e përfunduar.

## Skenarët e domosdoshëm të testimit

| Fusha | Skenarë me vlerë reale | Shtresa |
|---|---|---|
| Role/caktime | Driver kërkon API Admin; Admin kërkon point/session jashtë scope; Superadmin global; lista/count nuk rrjedhin të dhëna | API + PostgreSQL |
| Caktime | Grant location kundrejt point; krijim i pikës në location të palejuar; transferim burim/destinacion; revokim gjatë lidhjes hub | Application + API/SignalR |
| Sesioni i përdoruesit | Login invalid/rate limit; refresh paralel; refresh i ripërdorur; logout; ndryshim fjalëkalimi; admin i çaktivizuar me token të vlefshëm | API + DB |
| Gjendje | Available→Occupied→Charging; stop/disconnect; defekt; heartbeat gjatë mirëmbajtjes; heqje override pa raport të freskët | Domain |
| Konflikte administrative | Dy adminë ndryshojnë të njëjtën pikë; ndryshim çmimi gjatë Start; override gjatë matjes | Integration në PostgreSQL real |
| Pricing | 20×0.35=7; ndryshim 0.35→0.40 nuk ndryshon sesionin historik; vlera me shumë dhjetore; çmim/matje mungon; start i vonuar | Domain + DB |
| Lifecycle | Start/Stop i përsëritur; Stop para Start; Start pa Connected; missing final meter; disconnect i papritur; sesion i dytë në të njëjtën dalje | Domain + adapter fixtures + DB |
| Matje | Wh→kWh; kumulative kundrejt delta; duplicate; out-of-order; reset matësi; null power; overflow/kufij | Domain + adapter contract |
| Arkivim | Pikë me sesion aktiv refuzohet; location me pika aktive refuzohet; histori mbetet; event i vonë nuk çarkivon | API + DB |
| Audit | Actor/role/before/after/reason të sakta; audit fail anulon ndryshimin; runtime nuk editon/fshin audit | Integration |
| Inbox/outbox | Restart mes pranimit dhe përpunimit; crash pas commit para delivery; redelivery nuk dyfishon njoftimin/sesionin | Integration |
| Realtime | Subscribe pa leje; revokim gjatë stream; token skadon; ngjarje gjatë snapshot; reconnect me versione të vjetra; hartë publike pa PII | API/hub + klient |
| QR i propozuar | Kod i gabuar/host i huaj; kamera e refuzuar; kod i kopjuar; dy skanues; sesion i huaj aktiv; challenge i skaduar nëse mbështetet | Mobile + API + adapter |
| Harta | Pan/zoom nuk rivendoset nga update; koordinata të gabuara; shumë pika pa fshehje; iOS/Android native; provider failure | Pajisje/emulator i miratuar |
| Mobile | Offline gjatë refresh; resume; session cache pas logout; location/notification permission denied; loading/empty/error | Teste të fokusuara + pajisje |
| UI operative | Kërkim/filtra me scope; konflikt 412; arkivim; histori çmimi; tastierë/kontrast; responsive | Web E2E |
| Migrime | DB nga zero; upgrade nga migrimi i mëparshëm; FK/unique/check constraints; backup/restore i izoluar | PostgreSQL integration |
| Scope | Pa payment/wallet/subscription endpoints, tabela ose faqe; pa komanda remote të maskuara si QR | Rishikim kodi/kontrate |

Unit testet nuk kërkojnë hardware. Testet që varen nga locking/indexet/transactions përdorin PostgreSQL real, jo EF InMemory. Testet e DB kanë host dhe prefix emrash të lejuar vetëm për EVCharging test. Testet e domenit/API/rradhës push dhe build-i web tani ekzekutohen lokalisht. EF InMemory teston HTTP/autorizimin, ndërsa testet PostgreSQL mbeten të pakryera.

## Paqartësitë dhe rekomandimet

Këto pika nuk e ndalin discovery. Zgjidhen përpara fazës që varet prej tyre; propozimet nuk janë marrëveshje biznesi të konfirmuara.

| ID | Paqartësia | Rekomandimi minimal / çfarë kërkohet | Kur bëhet e domosdoshme |
|---|---|---|---|
| A01 | Prodhuesi, modeli, API/OCPP dhe versioni | Merr dokumentacionin, event samples dhe mjedis prove; përdor adapter e simulator deri atëherë | Integrim real, F8 |
| A02 | Identifikimi i Driver dhe QR | Përdoruesi propozoi skanim; QR identifikon pikën, provider-i duhet të provojë pronësinë e sesionit | Monitorim personal real, F5/F8 |
| A03 | Një pajisje apo dalje e pavarur për “pikë” | Model një ChargePoint për një dalje që karikon; konfirmo serial/connectorId dhe sesionet simultane | Skema për pajisje reale, F3/F4 |
| A04 | Regjistrimi Driver u saktësua | Driver regjistrohet vetë; API është implementuar. Rikuperimi/verifikimi i email-it mbeten për saktësim | Përpara release |
| A05 | Caktimi location kundrejt listës së pikave | Propozim grants additive: location = të gjitha pikat + create; point = vetëm ajo pikë | Caktimet, F2 |
| A06 | Historia pas ricaktimit/zhvendosjes | Current point scope për leje; locationAtStart për histori; caktimet konkrete të rishikohen në transferim | Autorizim historie, F3/F6 |
| A07 | Monedha, rounding, taksa, momenti i çmimit | EUR nga shembulli; decimal; çmim në start; dy shifra cost si propozim. Mos shto tarifë/taksë pa rregull | Pricing, F3/F4 |
| A08 | Kërkesat opsionale të profilit/cilësimeve | Konfirmo fushat dhe gjuhët; një automjet opsional, pa flotë | Profili/cilësimet, F7 |
| A09 | Push dhe gjuha u konfirmuan | Push në telefon sq/en sipas gjuhës. Rregullat e marrësve për eventet reale dhe prova në pajisje mbeten | Pas stabilizimit web |
| A10 | Stop me të dhëna të mangëta/koha e pajisjes/pauzat | Status i paplotë deri në reconciliim; ruaj observed/received dhe timeQuality; kohë nisje–fund si propozim | Adapter, F4/F8 |
| A11 | SLA real-time, interval heartbeat, prag stale | Mat dhe konfiguro sipas hardware; mos deklaro të lirë një pikë me të dhëna të vjetruara | F4/F5/F8 |
| A12 | Arkivim/override kur pika po karikon | Blloko arkivim/transferim aktiv; override nuk ndalon fizikisht karikimin | F3 |
| A13 | Kuptimi “pika aktive”, periudha e statistikave | Propozim aktive = jo të arkivuara, freshness veç; default agregime mbi historinë e ruajtur, interval i shprehur qartë në UI | Dashboard, F6 |
| A14 | Ruajtja/anonimizimi dhe përmbajtja e privatësisë | Asnjë fshirje automatike e pakthyeshme pa politikë të dhënë; minimizo të dhënat | Përpara production |
| A15 | Provider hartash, trafik/numër pikash, pajisje testimi | Test i hershëm iOS; API viewport pa prerje të heshtur; zgjidh tiles/licenca në implementim | F1/F5 |
| A16 | Akses global te Driver nënkupton CRUD? | Implemento pamjen e kërkuar; mos shto bllokim/fshirje/redaktim Driver pa saktësim | F6 |

Përdoruesi konfirmoi se informacionin e karikuesit mund ta dërgojë më vonë. Nuk nevojitet tani për strukturën/arkitekturën. Duhet përpara lidhjes reale dhe verifikimit të QR–Driver–session.

## Rreziqet teknike kryesore

1. **QR statike pa identifikim sesioni:** rrezik që skanuesi të shohë sesionin e dikujt tjetër. QR përdoret vetëm për zbulim derisa provider-i të japë provën e lidhjes.
2. **Harta iOS:** HOUSS ka histori crash dhe adapter të veçantë. Prova në pajisje është e hershme; lëshimi nuk kalon me hartë të çaktivizuar.
3. **Matje dhe evente të papërsosura:** deduplikim, inbox, counter handling dhe reconciliim; simulatori nuk zëvendëson testin me pajisjen.
4. **Caktime vetëm në UI:** query policies dhe kontrolle realtime parandalojnë rrjedhje të dhënash në detaj/count/stream.
5. **Histori e pasaktë:** snapshots, immutable revisions dhe audit në transaksion; arkivim pa cascade delete.
6. **Versione/framework dhe shërbime të jashtme:** u lexuan manifestet lokale; nuk u verifikuan release/advisories/çmime ose përputhshmëri aktuale në rrjet. Kontrollohen para fiksimit të dependencies.
7. **Laptop pune:** nuk ndryshohen endpoint protection, firewall, VPN, policy, registry ose certifikatat e sistemit. Nëse një komandë bllokohet, ndryshohet qasja me mjetet e lejuara, jo kontrollet e sigurisë.

## Çfarë u verifikua në këtë dorëzim

- Word u lexua i plotë si tekst OOXML me identifikues paragrafësh; metadata/hash ruhen te `docs/reference/source-manifest.json`.
- Të 12 fotografitë u analizuan për menutë dhe dashboard. Faqja live nuk u vizitua dhe rrjedhat e fshehura nuk u verifikuan.
- U inspektuan struktura dhe rrugët konkrete auth/API/DB/audit/realtime/map/config të HOUSS. Build/test HOUSS nuk u ekzekutua dhe kodet e tij nuk u ndryshuan.
- Dokumentet u kontrolluan për lidhje lokale, mbulim të seksioneve, ID të kërkesave dhe përputhje të hash-it të HOUSS me baseline-in e discovery. Rezultati i kontrollit final ruhet në `docs/reference/verification.json`.
- Ky seksion përmbledh kontrollin historik të discovery. Verifikimi i kodit të ri, paketat lokale dhe kufijtë aktualë janë te implementation-status.md.
