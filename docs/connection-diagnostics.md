# Diagnostikimi i lidhjes dhe historiku i logeve

## Çfarë dimë nga tentativa

Loget e dërguara më 7 tetor 2026 tregojnë migrime të përfunduara, API-në duke
dëgjuar në `0.0.0.0:10000` dhe deploy të suksesshëm. Ato janë loge nisjeje;
nuk përmbajnë një tentativë OCPP dhe nuk përcaktojnë shkakun e dështimit.
`No open ports detected` përpara `Now listening` nuk është dështim i portit.
Paralajmërimet DataProtection nuk provojnë dështim të handshake-it OCPP.
Mjedisi `Development` është konfigurimi aktual demo; para përdorimit real
duhet kaluar në `Production`, me demo seed të çaktivizuar.

Dokumentacioni publik i Render-it u kontrollua më 7 tetor 2026:

- Free mund të flejë pas 15 minutash pa trafik hyrës; zgjimi mund të marrë
  rreth një minutë. Mesazhet hyrëse WebSocket llogariten si trafik.
- Free mund të riniset edhe gjatë përdorimit. Faqja web dhe API-ja janë dy
  shërbime: navigimi e mban API-në aktive vetëm kur shkakton kërkesa tek ajo.
- `Request logs` kërkon workspace Pro ose më lart. `Application logs` mban
  daljen e aplikacionit dhe mund të përdoret për mesazhet tona OCPP.
- Për internetin përdoret `wss://`; `ws://` mund të marrë redirect 301, të
  cilin klienti i karikuesit mund të mos e ndjekë.

Burime: [Render Free](https://render.com/docs/free),
[WebSockets](https://render.com/docs/websocket),
[loget](https://render.com/docs/logging).

Nëse API-ja po përgjigjej normalisht në të njëjtën minutë, gjumi bëhet një
hipotezë më pak bindëse. Nuk përjashtohen restart, database failure ose një
problem specifik i rrugës nga karikuesi. Nuk u lexuan loget live të dashboard-it.

## Dallimet nga projekti referencë Python

| Pika | Referenca | EVCharging | Prova që duhet |
| --- | --- | --- | --- |
| URL | Merr segmentin e fundit nga disa formate path-i | Merr segmentin e fundit; pranon `/ID`, `/ws/ID`, `/ocpp/ID`, `/ws/cs/ID` dhe ID të përsëritur | Kontrollo URL-në përfundimtare dhe ID-në në `handshake_received`; `/api` dhe `/health` janë të rezervuara |
| Hostimi | Host lokal/Cloudflare sipas provës së kolegut | Render, API e veçantë nga web-i | Domain-i i API-së, WSS/443, jo porti lokal 9000/10000 |
| Identiteti | Lejon edhe `.`, `:`, deri 100 karaktere | Shkronja, numra, `_`, `-`, deri 40 | `NKR2023072805` kalon të dyja; një ID tjetër mund të refuzohet |
| Pranimi | WebSocket hapet para përpunimit me SQLite | Pranimi dhe hapja ruhen në PostgreSQL para upgrade | Vonesë/gabim DB përpara `websocket_connected` |
| Rilidhja | Menaxhim i regjistrit në Python | Një lidhje aktive për pajisje brenda procesit | 409 kur socket-i i mëparshëm nuk është mbyllur; 429 për shumë tentativa |
| TLS/rrjeti | Hyrja Cloudflare | Hyrja Render | DNS, certifikata/orë pajisjeje, SNI, rrjeti/APN; nuk provohen nga simulatori në browser |

Të dy projektet kërkojnë `ocpp1.6`. Nuk e dobësojmë TLS-in ose kontrollin e
protokollit për të provuar një hipotezë. Testi lokal i krahasimit përshkruhet te
[ocpp-cloudflare-comparison.md](ocpp-cloudflare-comparison.md).

Pranimi i formateve alternative u shtua më 7 tetor 2026. Nëse firmware-i
shton ID-në dy herë, të dyja rrugët identifikojnë të njëjtën pajisje. Loget
ruajnë rrugën e marrë dhe ID-në e segmentit të fundit për ta krahasuar me
konfigurimin real. Ky ndryshim kërkon deploy të backend-it; nuk kërkon
migrim shtesë dhe nuk provon vetë shkakun e tentativës së mëparshme.

## Çfarë ruhet

`application_logs` mban loget operative përtej restart/deploy, me UTC,
Event ID unik, instancën e procesit, nivelin, pajisjen dhe kontekstin:

- Tentativa WebSocket edhe në rrugë të gabuar, refuzime 400/401/409/429,
  hapje, mesazhi i parë, shkëputje dhe arsyet e mbylljes.
- Veprimi OCPP, Call ID, Connection ID, përpunimi/commit, refuzimi/replay,
  dhe ngjarje më vete pas dërgimit të përgjigjes. Një send i suksesshëm nuk
  garanton se firmware-i e ka konsumuar përgjigjen.
- Kërkesat API: route, status, kohëzgjatje, Trace ID dhe kodet e gabimeve.
  `X-Request-ID` në përgjigje dhe `traceId` në gabimet JSON ndihmojnë hetimin.
- Nisje, gatishmëri dhe kërkesë ndalimi të procesit; heartbeat i skaduar;
  gabime të punëtorëve të realtime dhe push.
- Lloji i exception-it, stack trace pa mesazhin e exception-it dhe SQLSTATE
  kur disponohet, pa kopjuar SQL ose kredenciale.

Nuk kopjohen query strings, Authorization, cookies, karta, fjalëkalime apo
payload-e në këtë historik. Health checks të suksesshme dhe polling i vetë
logeve përjashtohen. Gjurmët ekzistuese të transaksioneve, auditimeve,
matjeve dhe mesazheve të plota OCPP mbeten në tabelat e tyre. Arkivi raw OCPP
mund të përmbajë të dhëna sensitive të protokollit; mos e eksportoni publikisht.

## Përdorimi në incident

Hap **Dashboard → Logs** si Superadmin. `Saved history` lexon PostgreSQL;
`Live / this instance` lexon buffer-in e procesit, i dobishëm kur ruajtja ka
probleme. Filtrat përfshijnë burimin, pajisjen, nivelin, Trace ID dhe intervalin
kohor. Ora e futur në UI është lokale; kërkesat dhe eksportet përdorin UTC.
`Older` kalon në faqet e vjetra pa i lëvizur nga polling. `Export this page
(JSON)` shkarkon vetëm faqen e shfaqur, me filtrat dhe identifikuesit.

Endpoint-et, vetëm Superadmin:

```text
GET /api/v1/admin/logs                         # deri 200 nga 1000 eventet live
GET /api/v1/admin/logs/history                 # 200 evente të ruajtura
GET /api/v1/admin/logs/history?source=OCPP&identity=NKR&level=Warning
GET /api/v1/admin/logs/history?traceId=...&connectionId=...&from=...&until=...
GET /api/v1/admin/logs/history?beforeId=123&limit=200
```

`nextBeforeId` është cursor i faqes tjetër; `limit` lejon 1–1000. Përsërit
cursor-in me të njëjtat filtra për të mbledhur më shumë faqe. Historiku renditet
sipas rendit të ruajtjes; ora UTC brenda çdo eventi është koha e regjistrimit.

| E fundit që shihet | Çfarë kontrollohet më pas |
| --- | --- |
| Asnjë `handshake_received` | Intervali/instanca/deploy-i i saktë; URL, DNS, TLS, APN dhe hyrja Render. Mungesa e logut nuk provon vetë ku u ndal kërkesa. |
| `handshake_received`, 404 | ID e munguar/palejuar, path i rezervuar, kërkesë pa upgrade WebSocket, hosti API kundrejt hostit web; ID e përsëritur pranohet pas deploy-it të këtij ndryshimi |
| 400 / 401 / 409 / 429 | Subprotocol / lejimi i pajisjes / socket ekzistues / shumë tentativa |
| Tentativë, pa `websocket_connected` | Pranimi ose DB; kërko Trace ID dhe gabimin |
| `websocket_connected`, `received=0` | Lidhja u hap, por nuk erdhi mesazh i plotë; kontrollo firmware/rrjetin dhe timeout-in |
| `message_rejected` | Action, Call ID dhe kodi OCPP |
| `message_committed`, pa `reply_sent` | Transporti dështoi gjatë dërgimit ose procesi u ndal; jo domosdoshmërisht dështim commit-i |
| `heartbeat_expired` / `server_shutdown` | Humbje trafiku ose restart; krahaso me Render Events në të njëjtën orë |

## Ruajtja dhe kufijtë

```text
EVCHARGING_Diagnostics__RetentionDays=90
EVCHARGING_Diagnostics__QueueCapacity=10000
EVCHARGING_Diagnostics__FlushSeconds=2
```

Default-i ruan diagnostikën 90 ditë. Pas skadimit pastrohen gradualisht vetëm
`application_logs`, jo historia e karikimit/auditimi/raw OCPP. Retention mund
të jetë 1–3650 ditë. Kapaciteti i databazës dhe norma e logeve duhen monitoruar.
Në ngarkesa të mëdha përdorni arkiv të jashtëm për stdout dhe dimensiononi DB.

Ngjarjet kalojnë në një radhë të kufizuar dhe shkruhen në batch, pa bllokuar
karikuesin. Batch-i mbahet për retry kur DB dështon; Event ID parandalon
dublikimin pas një commit-i me acknowledgement të humbur. Çdo ngjarje dërgohet
edhe te stdout si `DiagnosticEvent` JSON. UI tregon numrin në pritje dhe
ngjarjet që nuk hynë në radhë kur ajo u mbush.

Kjo nuk është garanci kundër çdo humbjeje: crash/OOM/SIGKILL mund të humbasë
batch-in ende në RAM; stdout ka kufijtë e ofruesit. SIGTERM normal provon
flush-in brenda afatit të ndalimit. Kur DB ose radha ka problem, kontrolloni
Render Application logs dhe log streaming. Për prodhim nevojiten DB e
qëndrueshme me backup dhe hostim i përshtatshëm për lidhje të vazhdueshme;
një DB Free që skadon nuk jep ruajtje afatgjatë. Dështimet DNS/TLS para hyrjes
në aplikacion kërkojnë prova nga karikuesi/ofruesi.

## Publikimi

Migrimi i ri është `20261007092825_PersistentApplicationLogs`. Ai shton tabelën
dhe indekset pa prekur historikun ekzistues të karikimit. Pas commit/push nga
përdoruesi, deploy-i duhet të aplikojë migrimin para nisjes së backend-it
(`EVCHARGING_MIGRATE_ON_START=true`), pastaj të publikohet web-i. Nuk u krye
deploy ose ndryshim në Render nga kjo punë. Loget e humbura përpara publikimit
nuk mund të rikthehen nga ky ndryshim.

## Verifikimi lokal i ndryshimit

- Build backend: kaloi, pa warnings/errors.
- 230 raste backend kaluan me runner-in lokal në të njëjtin proces. VSTest-i
  standard u ndal nga timeout-i i lidhjes me testhost; nuk raportohet si kalim.
- Tetë formate URL-je kaluan me BootNotification/Heartbeat dhe të njëjtën
  pajisje në DB. U verifikuan ID-ja në loge, refuzimi i protokollit tjetër,
  pajisja e çaktivizuar, politika SIM, lidhja aktive dhe rrugët API/health.
- 15 raste PostgreSQL u anashkaluan sepse nuk ishte konfiguruar databazë
  testuese. Migrimi u kontrollua kundrejt modelit dhe u gjenerua SQL pa lidhje
  me DB; nuk u aplikua në PostgreSQL real gjatë këtij ndryshimi.
- 135 teste web dhe TypeScript typecheck kaluan.
- Build-i i plotë Next.js u bllokua nga sandbox-i Windows: `Access is denied`
  gjatë canonicalize të `jsc.baseUrl` në ngarkimin e `next.config.ts`.
  Duhet përsëritur në mjedisin normal të zhvillimit ose në build-in Render.
