# Lidhja e karikuesit real dhe paneli administrativ

## Adresa për kolegun

Adresa bazë e shërbimit që përdor ky projekt demo:

`wss://vcharging-api-demo.onrender.com/ws/cs`

Për një pajisje që regjistron me ID `CP-001`, lidhja përfundimtare është:

`wss://vcharging-api-demo.onrender.com/ws/cs/CP-001`

Nëse programi i karikuesit bashkon vetë adresën bazë me Charge Point ID,
vendos adresën bazë dhe ID-në në fushat përkatëse. Nëse pret një URL të plotë,
vendos lidhjen përfundimtare. ID-ja duhet të shfaqet vetëm një herë në URL.
Paneli e ndërton URL-në nga adresa API që ka të konfiguruar web-i, kështu që
nuk kërkon ndryshim kodi kur ndryshon domeni.

| Parametri në karikues | Vlera |
|---|---|
| Protokolli | OCPP **1.6 JSON**; WebSocket subprotocol `ocpp1.6` |
| Serveri | URL-ja e plotë që tregon paneli |
| Charge Point ID | ID-ja që regjistron te “Karikuesi fizik”, me të njëjtat shkronja |
| Lidhja | Vetëm URL-ja mbi TLS (`wss://`); pa username/password ose header `Authorization` |
| Lidhësit | Numrat **1…N**, sipas numrit të regjistruar; 0 përdoret për gjendjen e gjithë pajisjes |
| Heartbeat | Përdor intervalin e kthyer nga `BootNotification.conf`: aktualisht 30 sekonda |
| Matjet | Dërgo çdo 10–30 sekonda gjatë karikimit për provën e parë |
| Ora | UTC me ISO 8601, p.sh. `2026-10-04T12:00:00Z`; mbaje orën e pajisjes të sinkronizuar |

Karikuesi hap WebSocket-in me këtë URL dhe subprotocol `ocpp1.6`, pastaj
dërgon `BootNotification`, heartbeat dhe gjendjet. Serveri pranon vetëm ID
të regjistruara, aktive dhe të lidhura me lidhësit e një pike fizike aktive.
ID-të e panjohura nuk krijojnë automatikisht pajisje fizike. Çaktivizimi
mbyll lidhjen dhe ndalon rilidhjen; bllokohet gjatë një sesioni aktiv.

URL-ja identifikon pajisjen, por nuk provon identitetin e saj me një sekret.
Ky është konfigurimi i kërkuar për pajisjet që lidhen vetëm me URL.

Duhet edhe lista e **idTag** që përdor programi i karikuesit për
`Authorize`/`StartTransaction`. Regjistro vlerat reale te pajisja, një për
rresht ose me presje. Serveri pranon vetëm ato vlera për atë pajisje.
Kjo listë autorizon identifikuesit; nuk lidh automatikisht një kartë me një
llogari Driver në aplikacion.

## Regjistrimi nga web-i

1. Hyr me llogarinë ekzistuese **Superadmin** në faqen normale të hyrjes.
2. Te **Vendndodhjet**, krijo emrin, qytetin, adresën dhe koordinatat.
3. Te **Pikat**, shto pikën: kod, emër, vendndodhje, adresë, koordinata,
   lloj/numër lidhësish, fuqi dhe tarifë EUR/kWh. Për të marrë gjendjen e
   pajisjes, mos lër kufizim manual Mirëmbajtje/Jashtë funksionit.
4. Te pika e ruajtur, seksioni **Karikuesi fizik**, shkruaj ID-në dhe serialin;
   prodhuesi/modeli janë opsionalë. Vendos idTag-t e autorizuar dhe regjistroje.
5. Kopjo vetëm URL-në e plotë për kolegun; username/password nuk nevojiten.
6. Për një llogari **Admin**, Superadmin hap **Administratorët**, krijon
   llogarinë dhe cakton vendndodhje ose pika të veçanta.

Caktimi i një vendndodhjeje lejon pikat e saj dhe krijimin e pikave aty.
Caktimi i një pike lejon vetëm atë pikë. Admin pa caktime nuk merr akses
automatik në rrjet. Një pikë mund të ketë disa lidhës; paneli operativ i
monitoron veçmas. Numri i lidhësve nuk ndryshohet pas regjistrimit të pajisjes.

## Nëse nuk ka ende Superadmin

Llogaria e parë krijohet nga konfigurimi i backend-it, përpara hyrjes në web.
Për Render vendos:

- `EVCHARGING_MIGRATE_ON_START=true`
- `EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START=true`
- `EVCHARGING_Bootstrap__Email=<emaili yt>`
- `EVCHARGING_Bootstrap__Password=<fjalëkalim unik, 12–128 karaktere>`

Krijimi fillestar kryhet pas migrimeve. Nëse ka tashmë Superadmin në bazë,
ruhet llogaria ekzistuese dhe fjalëkalimi i saj; këto variabla nuk e rivendosin.
Regjistrimi publik krijon vetëm Driver. Superadmin krijon administratorët
nga paneli; nuk ka një fjalëkalim administrativ të përbashkët në kod.

## Prova e parë me pajisjen

1. Hap **Pikat** ose **Karikimet** në panel dhe lidhe pajisjen.
2. Verifiko `BootNotification` → `Accepted` dhe pajisjen “I lidhur”.
3. Dërgo gjendjen reale me `StatusNotification`, për secilin lidhës.
   Lidhja e WebSocket-it vetëm nuk e shpall automatikisht pikën të lirë.
4. `Authorize` me një idTag të regjistruar duhet të kthejë `Accepted`.
5. `StartTransaction` dërgon `connectorId`, `idTag`, `meterStart` në Wh dhe
   timestamp. Ruaj `transactionId` që kthen serveri.
6. `MeterValues` përdor të njëjtin transactionId. Për energjinë përdor
   `Energy.Active.Import.Register` me Wh/kWh; për fuqinë `Power.Active.Import`
   me W/kW. Dërgo fuqinë totale pa phase kur vlera është totale. `SoC` me
   `Percent` përdoret kur pajisja e disponon realisht.
7. `StopTransaction` përdor transactionId e serverit, `meterStop` në Wh,
   timestamp dhe arsyen reale, p.sh. `EVDisconnected` kur hiqet kablloja nga
   makina. Pastaj raporto gjendjen reale të lidhësit.
8. Testo një pauzë, një defekt dhe rilidhjen e rrjetit. Pauza ose humbja e
   WebSocket-it nuk mbyllin vetë një sesion fizik; ndalimi duhet konfirmuar.

Paneli përditësohet nga ngjarjet pas ruajtjes në bazë. Në rilidhje merr
përsëri të dhënat e autorizuara. Admin sheh vetëm burimet e caktuara;
Driver sheh të dhënat private vetëm kur pronësia është verifikuar.
Një sesion real pa llogari të verifikuar shfaqet në panelin operativ si i tillë.
Niveli i baterisë dhe koha e mbetur varen nga të dhënat që disponon pajisja;
nuk krijohen nga energjia e matur vetëm.

## Gjendja e dorëzimit

Ky ndryshim përfshin migrimet `AdministrationAndPhysicalChargers` dhe
`OperationsRealtimeAudience`, panelin,
lejet, lidhjen e pajisjes vetëm me URL dhe teste HTTP/WebSocket lokale. Nuk është
provë që pajisja reale është lidhur me Render. Pas commit/push duhen deploy i
backend/web, migrimet dhe regjistrimi i pajisjes përpara provës në terren.
OCPP 2.0.1 dhe komandat për ndryshim konfigurimi/nisje/ndalim në distancë nuk
janë implementuar në këtë integrim. Lidhja e identifikuesit fizik me llogarinë
Driver mbetet një integrim i veçantë; të dhënat e klientëve nuk i caktohen
një përdoruesi vetëm sepse hap faqen e pikës.

Heqja e fjalëkalimit më 5 tetor 2026 nuk kërkon migrim të ri. Edhe pajisjet
e regjistruara më parë lidhen me URL-në ekzistuese pa kredenciale. Kolona e
vjetër e hash-it ruhet për pajtueshmërinë e databazës, por nuk përdoret për
lidhjen ose autorizimin e kartave dhe nuk plotësohet për pajisje të reja.
Backend-i dhe web-i duhen publikuar bashkë që paneli të tregojë konfigurimin
e ri. Konfigurimi i simulatorëve mbetet i veçantë.

### Nëse API-ja dështon me `ck_realtime_outbox_audience`

Gabimi `23514` për këtë kufizim vjen kur databaza ende pranon vetëm audiencat
e vjetra dhe aplikacioni ruan ngjarje `Operations` për panelin administrativ.
Migrimi `OperationsRealtimeAudience` përditëson kufizimin pa fshirë ngjarjet
ose sesionet. Ai ruan kushtin që një ngjarje private `Driver` të ketë marrës.

Pas push-it të rregullimit, bëj redeploy të backend-it me
`EVCHARGING_MIGRATE_ON_START=true`. Në log duhet të shfaqet aplikimi i
`OperationsRealtimeAudience` përpara nisjes normale të shërbimit.
