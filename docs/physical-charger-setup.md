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

Për caktimin e të dhënave te një Driver, Superadmin mund të zgjedhë
përdoruesin dhe të lërë bosh fushën **Kodi i kartës / JSON** për demo, ose
të shkruajë idTag-n e kartës për caktim vetëm sipas saj. [Udhëzimi i
plotë i dy mënyrave](physical-driver-assignment.md). Lista e veçantë e
kartave të lejuara mund të përdoret për karikime pa pronar Driver.

## Regjistrimi nga web-i

1. Hyr me llogarinë ekzistuese **Superadmin** në faqen normale të hyrjes.
2. Te **Vendndodhjet**, krijo emrin, qytetin, adresën dhe koordinatat.
3. Te **Pikat**, shto pikën: kod, emër, vendndodhje, adresë, koordinata,
   lloj/numër lidhësish, fuqi dhe tarifë EUR/kWh. Për të marrë gjendjen e
   pajisjes, mos lër kufizim manual Mirëmbajtje/Jashtë funksionit.
4. Te pika e ruajtur, seksioni **Karikuesi fizik**, shkruaj ID-në dhe serialin;
   prodhuesi/modeli dhe lista e kartave janë opsionalë. Regjistro pajisjen,
   pastaj zgjidh përdoruesin dhe mënyrën demo/kartë në caktimin Driver.
5. Kopjo vetëm URL-në e plotë për kolegun; username/password nuk nevojiten.
6. Për një llogari **Admin**, Superadmin hap **Administratorët**, krijon
   llogarinë dhe cakton vendndodhje ose pika të veçanta.

Caktimi i një vendndodhjeje lejon pikat e saj dhe krijimin e pikave aty.
Caktimi i një pike lejon vetëm atë pikë. Admin pa caktime nuk merr akses
automatik në rrjet. Një pikë mund të ketë disa lidhës; paneli operativ i
monitoron veçmas. Për një pajisje fizike mund të rritësh **Numrin e lidhësve**
kur nuk ka karikim aktiv; ruajtja shton lidhësit dhe hartëzimin OCPP. Lidhësit
ekzistues nuk hiqen, që historiku të mbetet i lidhur me të njëjtin identitet.

### Ndryshimi i ID-së së një pajisjeje të regjistruar

Te **Karikuesi fizik → Charge Point ID**, vendos ID-në e plotë të pajisjes
dhe shtyp **Ruaj ID-në e karikuesit**. Për shembull, për pajisjen
`NKR2023072805`, URL-ja bëhet
`wss://vcharging-api-demo.onrender.com/ws/cs/NKR2023072805`.
Fusha e URL-së është vetëm për kopjim dhe përditësohet pas ruajtjes.
Kodi i pikës, p.sh. `CH-001`, mbetet i veçantë nga ID-ja fizike.

Ndryshimi lejohet kur nuk ka karikim aktiv dhe ID-ja nuk i përket një pajisjeje
tjetër. Ruhet e njëjta pajisje në bazë, bashkë me lidhësit, historikun dhe
caktimin Driver; ndryshimi regjistrohet në audit. Një lidhje e pranuar më parë
vazhdon të punojë, ndërsa lidhjet e reja pranohen vetëm me ID-në e re.
Kjo nuk ndryshon konfigurimin e pajisjes nga larg: URL-ja në karikues duhet
të përputhet. Publiko backend-in dhe web-in për të shfaqur këtë fushë;
ndryshimi nuk kërkon migrim të ri.

### Loget nga web-i

Superadmin hap **Loget e lidhjes** në menu (`/dashboard/logs`), ose lidhjen
**Hap loget e lidhjes OCPP** te karikuesi. Pamja përditësohet çdo 5 sekonda
dhe lejon filtrimin sipas OCPP/HTTP dhe Charge Point ID. Lëre ID-në bosh
për të parë edhe tentativat me ID të paregjistruara dhe gabimet 400/401/409.
Kërkesat HTTP tregojnë metodën, rrugën, statusin dhe kohëzgjatjen; OCPP tregon
lidhjen, veprimet e përpunuara/refuzuara dhe shkëputjet.

Këto loge janë për diagnostikim të çastit: mbahen 1 000 ngjarjet e fundit në
memorien e instancës API dhe shfaqen deri në 200 rezultate. Restart/deploy
i pastron; nëse ka disa instanca, secila ka buffer të vetin. Historiku i
karikimeve mbetet në databazë. Tokenët, header-at, query string, trupat HTTP,
JSON-i i kartave dhe payload-et OCPP nuk shfaqen në këtë pamje. Aksesi është
vetëm për Superadmin dhe nuk varet nga plani i logeve të Render-it.

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
`OperationsRealtimeAudience` dhe `PhysicalChargerDriverAssignments`, panelin,
lejet, lidhjen e pajisjes vetëm me URL dhe teste HTTP/WebSocket lokale. Nuk është
provë që pajisja reale është lidhur me Render. Pas commit/push duhen deploy i
backend/web, migrimet dhe regjistrimi i pajisjes përpara provës në terren.
OCPP 2.0.1 dhe komandat për ndryshim konfigurimi/nisje/ndalim në distancë nuk
janë implementuar në këtë integrim. Caktimi automatik te Driver-i
konfigurohet nga Superadmin; hapja e faqes vetëm nuk jep pronësi.

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

### Nëse karikuesi tregon `101` dhe më pas shkëputet

Në Render hap shërbimin API → **Logs**, zgjidh intervalin e tentativës ose
**Live**, dhe filtro me Charge Point ID. Ora me `Z`/`GMT` është UTC; më 6 tetor
2026, `14:40 UTC` përputhet me `16:40` në Berlin/Tiranë. Loget e nisjes së
shërbimit përpara tentativës nuk tregojnë shkakun e shkëputjes.

Diagnostikimi i lidhjes regjistron vetëm metadata, jo fjalëkalime, karta ose
payload-e:

- `connection attempt`: kërkesa arriti te endpoint-i; refuzimet shënojnë
  `400` (WebSocket/protokoll), `401` (pajisja) ose `409` (lidhje ekzistuese).
- `connected`: serveri ka pranuar WebSocket-in.
- `first text message`: mbërriti mesazhi i parë; `BootNotification accepted
  and committed` konfirmon që Boot u pranua dhe u ruajt.
- `disconnected`/`ended`: numri i mesazheve, përgjigjeve, koha e kaluar,
  gjendja dhe kodi i mbylljes ndihmojnë në dallimin e transportit nga OCPP.
  `received=0` do të thotë se nuk u mor asnjë mesazh i plotë tekst.
- `OCPP processing failed`: shiko exception-in pasues për dështimin në server.

Përgjigjja `101` në ekran nuk provon vetë një lidhje të qëndrueshme ose
pranimin e BootNotification. Mos ndrysho portin, TLS-in ose formatin OCPP
pa krahasuar loget e të njëjtës tentativë. Shtimi i këtyre logeve kërkon
publikimin e backend-it, por nuk kërkon migrim databaze.
