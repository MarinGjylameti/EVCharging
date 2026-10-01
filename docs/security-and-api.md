# Rolet, autorizimi dhe kufijtë API

Identiteti, profili, gjuhët, pajisjet push dhe inbox personal janë implementuar në API. Caktimet, hubs dhe modulet operative më poshtë mbeten projektim. Kontratat reale dhe statusi janë te [backend/README](../backend/README.md) dhe [implementation-status](implementation-status.md).

## Autorizimi

Tre role funksionale: DRIVER, ADMIN, SUPERADMIN. Scope i të dhënave kontrollohet në backend për GET, listë, dashboard, write, njoftime dhe realtime. Identiteti i përdoruesit merret nga sesioni i verifikuar; `role`, `actorId` dhe `driverId` të dërguara nga klienti nuk japin privilegje.

Propozimi minimal i caktimeve është **bashkimi i lejeve**:

- Caktimi te vendndodhja lejon menaxhimin e të gjitha pikave të saj aktive dhe krijimin e pikave të reja aty.
- Caktimi te një pikë lejon vetëm atë pikë; nuk lejon krijimin e pikave të tjera në vendndodhje.
- Superadmin menaxhon caktimet. Nuk futen deny rules, role dinamike ose permission builder.
- Admin që ndryshon location të pikës duhet të ketë leje në burim dhe destinacion. Zhvendosja kërkon rishikim të caktimeve konkrete nga Superadmin; propozimi i sigurt është të mos mbarten automatikisht te destinacioni.
- Aktor i çaktivizuar/arkivuar nuk ka akses edhe kur token-i ende nuk ka skaduar.

Dokumenti nuk thotë nëse caktimi në vendndodhje jep automatikisht të gjitha pikat apo kërkon përzgjedhje. Kjo semantikë rekomandohet dhe duhet konfirmuar para implementimit të caktimeve. Deri atëherë, default deny.

| Veprimi | Driver | Admin | Superadmin |
|---|---|---|---|
| Hartë dhe detaje publike të pikave aktive | Po | Po | Po |
| Sesione personale | Vetëm të vetat | Vetëm sipas pikave të lejuara | Të gjitha |
| Menaxhim profili/cilësimesh Driver | Vetëm të vetat | Jo | Pamja e kërkuar; pa CRUD të supozuar |
| Krijim pike | Jo | Vetëm në location të caktuar për krijim | Po |
| Redaktim/çmim/gjendje/arkivim pike | Jo | Vetëm pika të lejuara | Po |
| Krijim/redaktim/arkivim location | Jo | Jo | Po |
| Adminë dhe caktime | Jo | Jo | Po |
| Audit/alarme operative | Jo | Sipas burimit | Global |
| Ngjarje nga karikuesi | Jo | Jo | Jo përmes sesionit të zakonshëm |

Kredencialet teknike të adapterit janë identitete infrastrukture, jo rol i katërt produkti. Provider mund të raportojë vetëm bindings e veta.

Scope i sesioneve historike pas ricaktimit nuk është specifikuar. Rekomandim për konfirmim: aksesi Admin vlerësohet sipas caktimit aktual të pikës, ndërsa location historik ruhet për raportim. Revokimi heq aksesin historik nga ish-admini; administratori i ri merr historikun e pikës. Ky efekt në privatësi duhet miratuar si rregull biznesi para migrimit të parë me të dhëna reale.

## Autentikimi dhe sesioni i përdoruesit

Përdoruesi konfirmoi regjistrim publik të Driver. API pranon emër, mbiemër, email, fjalëkalim dhe sq/en; roli caktohet Driver në server. Verifikimi/rikuperimi i email-it mbetet i papërcaktuar. Nuk janë shtuar social login ose OTP.

Implementimi përdor Identity për hashing, rate limiting dhe kufij inputi. Token-i i sesionit është 32 bytes rastësore, ruhet vetëm si hash SHA-256 dhe ka skadim absolut 24 orë si parazgjedhje. Çdo kërkesë kontrollon sesionin, revokimin dhe llogarinë aktuale në DB. JWT dhe refresh rotation nuk janë implementuar në këtë hap. Pas skadimit përdoruesi hyn përsëri.

Web: BFF vendos cookies HttpOnly, Secure në release, SameSite dhe path/domain minimal. Ndryshimet kontrollojnë Origin/CSRF; cookie me të dhënat e profilit nuk është provë autorizimi. Proxy ka allowlist routes/methods, timeout, size limit, pa cache private dhe pa përcjellje URL-je arbitrare.

Mobile është shtyrë. Kodi fillestar ruan token-in opaque në SecureStore; nuk ka refresh endpoint ose retry me rotacion. Dështimi i rrjetit nuk fshin kredencialet; 401/403 i konfirmuar në refresh e mbyll sesionin. Logout pastron cache private dhe revokon sesionin në server. Ndryshimi i fjalëkalimit verifikon të vjetrin dhe revokon sesionet e mëparshme sipas politikës së përcaktuar.

## Sipërfaqja e propozuar e API

Emrat më poshtë janë kontrata pune. DTO/input validation/OpenAPI do të krijohen me kodin; kjo listë mbulon edhe endpoint-e të ardhshme. Lista e saktë e endpoint-eve të implementuara është te backend/README.md.

| Rruga nën `/api/v1` | Veprimet | Kufiri |
|---|---|---|
| `/auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/password` | POST | Sesioni i përdoruesit; pa endpoint ndërrimi roli nga klienti |
| `/me`, `/me/profile`, `/me/settings` | GET; PATCH për fushat e lejuara | Përdoruesi aktual |
| `/map/points`, `/points/{id}` | GET | Të dhëna pikash, pa PII/seriale ose konfigurim teknik të panevojshëm |
| `/me/sessions`, `/me/sessions/active`, `/me/sessions/{id}` | GET | Vetëm sesione të verifikuara të Driver |
| `/admin/points`, `/admin/points/{id}` | GET/POST/PATCH | Scope i caktimeve; Superadmin global |
| `/admin/points/{id}/archive` | POST | Pa sesion/lidhje aktive; audit |
| `/admin/points/{id}/restriction` | PUT | Reason + expectedVersion; vetëm kufizime manuale të lejuara |
| `/admin/points/{id}/prices` | GET/POST | Histori/ndryshim çmimi; version dhe audit |
| `/admin/dashboard`, `/admin/sessions`, `/admin/sessions/{id}` | GET | Agregime, filtra të kërkuar, paginim dhe scope |
| `/locations`, `/locations/{id}`, `/locations/{id}/archive` | GET/POST/PATCH/POST sipas rrugës | Shkrim Superadmin; leximi Admin vetëm aty ku ka të drejta |
| `/administrators`, `/administrators/{id}` | GET/POST/PATCH | Superadmin |
| `/administrators/{id}/status`, `/administrators/{id}/archive` | PUT/POST | Aktivizim/çaktivizim dhe heqje pa fshirë historinë |
| `/administrators/{id}/assignments` | GET/PUT | Versionim dhe audit; scope i qartë |
| `/drivers`, `/drivers/{id}` | GET | Superadmin; pa menaxhim të shpikur të llogarive Driver |
| `/notifications`, `/audit` | GET | Marrës/scopes të autorizuara, paginim |
| `/qr/resolve` | POST, e propozuar nga përdoruesi | Kthen pikën nga kod i validuar; nuk kthen sesion privat ose nis karikim |

Nuk ka endpoint pagesash, portofoli, rezervimi, start/stop remote, whitelist ose charging profile. Ingress i pajisjeve qëndron veç API-së së përdoruesve dhe përcaktohet kur të dihet adapteri. Simulatori nuk publikohet si endpoint production.

## DTO dhe gabimet

Përdor ISO 8601 UTC për kohën dhe string decimal në JSON për `energyKWh`, `unitPrice`, `cost`; JavaScript i formaton pa kryer aritmetikë autoritative. `version`, `updatedAt`, `dataQuality` dhe `isFinal` tregojnë freskinë. Madhësitë e listës kufizohen në server. Intervalet e datës janë `[from, to)` pasi koha lokale shndërrohet në UTC.

Gabimi standard: `status`, `code`, `title`, `traceId` dhe `errors` për fushat. Vetëm mesazhe të sigurta; nuk dërgohen exception messages/SQL/stacktrace.

| Rasti | Përgjigjja |
|---|---|
| Input i pavlefshëm, koordinata, enum, arsye e munguar | 400, kod i qëndrueshëm dhe gabime fushash |
| Pa sesion/token i pavlefshëm | 401 |
| Rol pa leje | 403 |
| ID që mungon ose burim jashtë scope-it të lexuesit | 404 sipas politikës së moszbulimit |
| Duplikim identifier ose konflikt biznesi/tranzicioni/arkivimi | 409 |
| Version i vjetër me If-Match | 412; UI rifreskon para riprovimit |
| Shumë kërkesa | 429 me Retry-After |
| DB/infrastrukturë e përkohësisht paarritshme | 503 me traceId |
| Gabim i papritur | 500 pa detaje të brendshme |

Idempotency keys propozohen për krijimet dhe veprimet ku retry pas timeout mund të dyfishojë ndryshimin. Key lidhet me actor, endpoint dhe hash të payload-it; i njëjti key me payload ndryshe jep 409. Rishikimi i lejeve bëhet edhe kur kthehet rezultati i ruajtur. `If-Match` është për concurrency dhe nuk e zëvendëson deduplikimin.

Identifikuesit e URL-ve dhe QR nuk janë sekrete që provojnë pronësinë. Gjithë API-ja dhe hub-i testohen me kërkesa manuale që anashkalojnë UI-në.
