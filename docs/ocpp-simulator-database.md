# OCPP 1.6J: databaza dhe lidhja e simulatorit

## Çfarë u konfirmua nga loget

`logs simulatori.txt` tregon lidhje WebSocket me nënprotokollin `ocpp1.6` dhe rrugën
`/ws/cs/{chargePointId}`. Mesazhet e vëzhguara janë BootNotification,
Heartbeat, Authorize, StatusNotification, StartTransaction, MeterValues dhe
StopTransaction. Përgjigjja e StartTransaction jep një `transactionId` numerik.
Matjet përmbajnë vlera të veçanta për energji, fuqi, tension, rrymë, SoC dhe
temperaturë, me njësi, fazë dhe vendndodhje opsionale.

Loget nuk përmbajnë kohë të mbetur deri në përfundimin e karikimit. SoC dhe
fuqia nuk e përcaktojnë vetë këtë kohë pa kapacitetin e baterisë, objektivin e
karikimit dhe profilin e fuqisë. Një vlerësim i tillë nuk publikohet si fakt i
pajisjes derisa burimi ta japë shprehimisht dhe të verifikohet freskia e tij.

Disa `StopTransaction` në log refuzohen sepse `meterStop` është numër me presje;
OCPP 1.6 kërkon numër të plotë në Wh. Disa mesazhe të marra më 1 tetor mbajnë
timestamp të 10 shtatorit. Për këtë arsye `observed_at` dhe `received_at`
mbeten të ndara dhe statusi i vjetër nuk duhet të shfaqet si live.

## Migrimi shtues

Migrimi `OcppTelemetrySchema` shton tetë tabela dhe katër kolona opsionale te
`charging_stations` (`manual_restriction`, `status_source`, `status_observed_at`,
`status_received_at`). `manual_restriction` ruan vendimin e operatorit për
mirëmbajtje ose dalje jashtë shërbimit, në mënyrë që telemetria e re të mos e
shfuqizojë atë pa vendim të operatorit. Migrimi e plotëson këtë fushë për pikat
ekzistuese me status `OutOfService` ose `Maintenance`.

| Tabela | Qëllimi |
|---|---|
| `ocpp_charge_points` | Identiteti i pajisjes, versioni i protokollit, metadata nga BootNotification dhe last-seen. Pajisja e re është e çaktivizuar si parazgjedhje. |
| `ocpp_connectors` | Numri i lidhësit, statusi aktual dhe lidhja opsionale me `charging_stations`. Numri 0 i OCPP ruhet si event i pajisjes, jo si lidhës fizik. |
| `ocpp_connections` | Një rresht për çdo lidhje WebSocket dhe kohët e lidhjes/shkëputjes. |
| `ocpp_inbound_messages` | Korniza origjinale JSONB, `call_id`, hash, gjendja e përpunimit dhe përgjigjja. Unik brenda një lidhjeje; `call_id` nuk supozohet unik përgjithmonë. |
| `ocpp_status_events` | Historia e StatusNotification, përfshirë error/info/vendor fields. |
| `ocpp_charging_transactions` | Sesioni fizik, ID numerike e OCPP 1.6, matësi fillestar/fundor në Wh, kohët, arsyeja e ndalimit dhe snapshot opsional i çmimit. Driver lidhet vetëm pas autorizimit të verifikuar. |
| `ocpp_meter_samples` | Çdo `sampledValue` veçmas: vlerë origjinale, vlerë numerike opsionale, measurand, unit, phase, location, context dhe format. |
| `realtime_outbox` | Njoftimi për UI ruhet në të njëjtin transaksion me statusin e pikës. Mban llojin, ID/versionin e entitetit, audiencën, payload-in e kufizuar, kohën dhe gjendjen e publikimit. |
| `charging_connection_requests` | Përdoruesi që pret te një pikë/lidhës, skadimi dhe lidhja me sesionin fizik. Pritja nuk krijon histori apo faturim. |

Migrimi shtues `DriverChargingConnections` krijon tabelën e pritjes pa ndryshuar
të dhënat ekzistuese. Ka foreign keys për Driver, pikën dhe sesionin; vetëm një
pritje aktive për lidhës dhe vetëm një kërkesë të lidhur me një sesion. POST-i
i lidhjes dhe StartTransaction përdorin të njëjtin lock të pajisjes.

Përveç kolonave të zakonshme, korniza origjinale e mesazhit ruhet në JSONB. Kjo
lejon fushat opsionale ose të prodhuesit pa migrim për çdo fushë të re. JSONB
nuk e zëvendëson validimin dhe nuk përdoret si burimi i vetëm për kërkimet e
hartës. Për shkak se mesazhet mund të përmbajnë `idTag`/identifikues personal,
ruajtja dhe aksesi te payload-et do të kenë politikë kufizimi para prodhimit.

`charging_history` ekzistuese vazhdon të jetë histori e Driver-it; nuk kthehet
automatikisht në sesion real OCPP. Lidhja e verifikuar me Driver dhe çmimi real
duhen përpara se një transaksion të shfaqet si histori personale ose kosto.

## Nga pajisja te harta

1. Pajisja hap lidhje **OCPP 1.6J WebSocket** me API-në. API verifikon identitetin
   e saj dhe ruan kornizën e marrë. `Charge Point ID` duhet lidhur në mënyrë të
   kontrolluar me një ose më shumë lidhës dhe pikat publike përkatëse.
2. `StatusNotification` ndryshon gjendjen e lidhësit. `Preparing`, `Finishing`,
   `Reserved`, `SuspendedEV` dhe `SuspendedEVSE` e bëjnë të zënë; `Charging` e
   shënon në karikim; `Faulted`/`Unavailable` e nxjerrin jashtë shërbimit.
   `StartTransaction` krijon sesionin fizik dhe e konfirmon të paktën si të
   zënë; nuk vërteton vetë që energjia po rrjedh. Pas `StopTransaction`, pika
   nuk shënohet e lirë pa një `StatusNotification: Available` të freskët.
3. Një transaksion në databazë ruan eventin, gjendjen më të re të lidhësit,
   përmbledhjen e pikës publike dhe një rresht `realtime_outbox` me
   `entity_version` të ri. Mesazhet e vjetra apo të dubluara ruhen për auditim,
   por nuk e kthejnë gjendjen publike pas në kohë. Kufizimet manuale kanë
   përparësi; një pikë me disa lidhës është e disponueshme nëse të paktën një
   lidhës i verifikuar është i lirë dhe i freskët.
4. Pas commit-it, procesuesi i outbox-it publikon invalidime përmes SSE.
   Eventet private filtrohen sipas Driver-it. Shfletuesit i dërgohet vetëm
   sinjali `changed`; ai merr snapshot-in përmes endpoint-eve të autorizuara.
   Payload-i origjinal OCPP dhe idTag nuk dalin në stream.
5. Shfletuesi merr fillimisht snapshot-in REST të pikave dhe ndjek kanalin.
   Bashkon invalidimet brenda 250 ms dhe nuk bën kërkesa të mbivendosura.
   Pas rilidhjes ose rikthimit te tab-i merr snapshot të ri. Polling çdo
   30 sekonda në background shërben për rikuperim. Zoom, pan, filtri dhe pika
   e zgjedhur ruhen; nuk përdoret `router.refresh()`. Pajisja offline shfaqet
   `Unknown`, pa u paraqitur si e lirë.

`PublishedAt` në outbox do të thotë dorëzim te transporti i eventeve, jo që çdo
shfletues e ka parë. Versioni dhe snapshot-i e bëjnë klientin tolerant ndaj
eventeve të dubluara ose të humbura. Për më shumë se një instancë API,
procesuesit e outbox-it duhet të përdorin lease/`SKIP LOCKED` dhe një transport
të përbashkët; nuk mbështetemi në memorie lokale si burim të vërtetës.

Endpoint-i OCPP, inbox, përpunuesit e mesazheve, outbox dhe SSE janë
implementuar për këtë demo me një instancë API. Sesionet personale lexohen
nga databaza përmes `/api/v1/driver/charging-sessions`; historia krijohet kur
merret `StopTransaction`. Testet përdorin WebSocket të vërtetë të TestServer
me ruajtje InMemory: provojnë rrjedhën HTTP/OCPP/SSE, jo izolimin dhe
kufizimet e PostgreSQL. Prova me simulatorin online kërkon deploy-in tënd.

## Konfigurimi në Render për testin aktual

1. Bëj vetë commit dhe push. Deploy backend-in dhe web-in nga ky commit.
2. Në **backend → Environment** shto:

   ```dotenv
   EVCHARGING_Ocpp__SimulatorEnabled=true
   EVCHARGING_Ocpp__AllowAnonymousSimulators=true
   ```

   Mbaj `DATABASE_URL`, `EVCHARGING_MIGRATE_ON_START=true` dhe
   `EVCHARGING_Demo__DriverEmail` të llogarisë demo ekzistuese.
   Ky konfigurim i posaçëm testimi pranon pa Basic Auth vetëm ID-të e
   konfiguruara dhe pikat `IsDemo`. Endpoint-i është i çaktivizuar si parazgjedhje.
3. **Save, rebuild and deploy**. Krijohen automatikisht tri pika të veçanta:
   `SIM-001`, `SIM-002`, `SIM-003`, secila me lidhësin 1 dhe koordinata të
   qëndrueshme në Tiranë. Para mesazhit të parë statusi është `Unknown`.
4. Web-i ruan `EVCHARGING_API_BASE_URL=https://vcharging-api-demo.onrender.com`.

Për transport që dërgon header Basic Auth, përdor
`EVCHARGING_Ocpp__AllowAnonymousSimulators=false` dhe
`EVCHARGING_Ocpp__SimulatorPassword=<fjalëkalim 12–128 karaktere>`;
username duhet të jetë Charge Point ID. Identitetet e reja të autentikuara
krijojnë pika demo, deri në kufirin 50. Për simulatorin e shfletuesit, konfigurimi
anonim i mësipërm shmang nevojën për header Basic Auth.

Për më shumë pika në testimin anonim, cakto vargje si
`EVCHARGING_Ocpp__SimulatorIdentities__0=SIM-001`, `__1=SIM-002`,
`__2=SIM-003`, `__3=SIM-004`. Maksimumi i listës është 20. Mos përdor të
njëjtën ID për dy lidhje njëkohësisht: lidhja e dytë refuzohet.

## Konfigurimi i simulatorit

1. **Add new simulator**: emër sipas dëshirës, protokoll **OCPP 1.6**,
   `Charge Point ID=SIM-001`; `Basic Auth Password` bosh për testin anonim.
2. `WebSocket URL (CMS)` duhet të jetë host-i publik i API-së me `wss://`, p.sh.
   `wss://vcharging-api-demo.onrender.com/ws/cs`. Kontrollo që `Station connection URL` të përfundojë
   si `wss://vcharging-api-demo.onrender.com/ws/cs/SIM-001` (nëse simulatori nuk e shton ID-në vetë,
   vendos URL-në e plotë). Nënprotokolli duhet të jetë `ocpp1.6`.
3. Pas `Connect`, dërgo BootNotification, Heartbeat, StatusNotification,
   Authorize dhe një cikël StartTransaction → MeterValues → StopTransaction.
   Verifiko përgjigjen me `transactionId` te StartTransaction dhe statusin e
   ruajtur për `connectorId=1`.
4. Në konfigurimin e simulimit `meterStart`/`meterStop` jepen si **integer Wh**.
   `MeterValues.sampledValue.value` mbetet string sipas mesazhit. Mos përdor
   kohë historike në testin live të statusit.
5. Për provë të shpejtë: Tag ID `TAG-001`, lidhësi `1`, Meter start `0`,
   Total kWh `1`, Charging speed `22`, Meter interval `5` sekonda. Përdor
   orën aktuale UTC; mos mbaj një Start time të vjetër. Nëse dërgon matje
   manuale, vendos Phase bosh për matjen totale të energjisë/fuqisë.
   Matjet `L1`, `L2`, `L3` pranohen gjithashtu: fazat brenda të njëjtit
   meter value mblidhen, ndërsa një total i raportuar ka përparësi dhe nuk
   numërohet bashkë me fazat. Një matje vetëm `L1` etiketohet si e tillë;
   kur `meterStart` nuk është zero, një regjistër i vetëm faze nuk përdoret
   për të llogaritur diferencën nga regjistri total.
6. Në [web](https://evcharging-web-demo.onrender.com/driver), hyr me një Driver
   dhe kliko `SIM-001` në hartë ose listë. Hapet faqja e veçantë e asaj pike,
   `/driver/stations/{id}`. Herën e parë ruhet automatikisht pritja e përdoruesit
   për 30 minuta. Për një pikë me karikim të përfunduar, shtyp **Përgatit karikim
   të ri**. Mund të anulosh pritjen; kjo nuk ndalon një karikim aktiv.
7. Te simulatori: **Start charging** me `TAG-001`. StartTransaction lidhet me
   Driver-in që pret te ajo pikë, pastaj UI shfaq energjinë, koston deri tani,
   kohëzgjatjen dhe fuqinë/SoC kur raportohen. **Stop charging** ruan totalin dhe
   historikun një herë. Çmimi është snapshot i tarifës në nisje. Pika lirohet
   vetëm pas statusit `Available`. Reload nuk fsheh përmbledhjen e përfunduar.
8. Përsërit krijimin me `SIM-002` dhe `SIM-003`, me të njëjtën URL bazë dhe
   Tag ID. Nis dy karikime njëkohësisht dhe ndalo vetëm njërin: UI duhet të
   ndryshojë vetëm pikën përkatëse. Për dy Driver të ndryshëm përdor dy profile
   shfletuesi: secili hap pikën e vet përpara Start charging. Sesioni i tjetrit
   dhe matjet e tij nuk shfaqen në llogarinë tënde.

Pritja e ruajtur është bypass i QR vetëm për simulatorët e konfiguruar. UI nuk
hap një socket OCPP te pajisja: simulatori lidhet me backend-in përmes WebSocket,
ndërsa UI ndjek SSE dhe merr snapshot-e të autorizuara në background. Kur nuk
ka pritje aktive, mbahet sjellja e testit të mëparshëm: TAG-001 përdor Driver-in
e `Demo:DriverEmail`. Kjo nuk është autorizim i përdoruesit për pajisje reale;
integrimi real kërkon lidhjen e verifikuar të tag-ut/QR me Driver-in.

StartTransaction → StopTransaction përbën një sesion. SuspendedEV/SuspendedEVSE
tregon ndërprerje të transferimit nga makina/pika dhe ruan të njëjtin sesion,
energjinë dhe pronarin. Pauza nuk shton hyrje në historik. Komandat Pause/Resume
nga UI nuk janë implementuar; këtë test e drejton simulatori. Sjellja konkrete
varet nga pajisja dhe makina, siç ilustrojnë [testet zyrtare OCA](https://openchargealliance.org/wp-content/uploads/2026/03/CompliancyTestTool-TestCaseDocument.pdf).

Shiriti mbushet sipas SoC vetëm kur pajisja dërgon këtë matje. Pa SoC tregon
aktivitet, pa përqindje të shpikur. Nuk ka formular objektivi kWh, kapaciteti
baterie apo përqindjesh për ta simuluar këtë informacion. Koha e mbetur nuk
shfaqet në faqen e karikimit: mesazhet aktuale nuk japin një ETA të verifikueshme.
Modeli i makinës vetëm nuk e përcakton nivelin aktual, objektivin apo ritmin real.

### Matjet dhe ora e simulatorit

Nëse logu përmban vetëm `Energy.Active.Import.Register`, UI shfaq energjinë
gjatë karikimit dhe sqaron që fuqia nuk është raportuar. Parametri
`Charging speed` i simulatorit nuk është vetë një mesazh `Power.Active.Import`;
backend-i nuk e paraqet si matje të marrë. SoC dhe temperatura shfaqen vetëm
kur raportohen nga pajisja.

Koha e pajisjes dhe koha e marrjes nga serveri ruhen veçmas. Paneli live
llogarit kohëzgjatjen nga marrja e Start/Stop dhe shfaq marrjen e matjes së
fundit. Nëse ora e pajisjes ndryshon më shumë se pesë minuta, shfaqet shpjegimi
te detajet e matjeve. Vetëm për pikat IsDemo në simulator mode pranohen kohë
deri një ditë përpara serverit, sepse shpejtësia 20× përshpejton orën e burimit.
Kjo nuk shpik matje: vlerat vijnë nga mesazhet e ruajtura. Një cikël i ri
me orën aktuale rivendos renditjen e statusit pas ciklit të përfunduar.
Një lidhje aktive pa
status të freskët shfaqet në pritje të statusit; nuk quhet e shkëputur.
Statuset e vjetra nuk e bëjnë pikën të lirë në hartë.

Snapshot-i privat lexon edhe matjet e fazave të ruajtura nga versioni i
mëparshëm, pa ndryshuar inbox-in origjinal apo historikun përfundimtar.
`StopTransaction.meterStop` mbetet burimi për energjinë finale dhe koston;
nëse simulatori dërgon një vlerë ndryshe nga matja e fundit, shfaqet vlera
finale që ai raportoi. Lidhja e re e pritjes kërkon migrimin shtues të përmendur
më sipër; interpretimi i matjeve të fazave nuk rishkruan historikun.

Render e drejton trafikun HTTP dhe WebSocket në të njëjtin port të Web Service.
Për një lidhje publike përdoret `wss://`, jo IP-ja lokale `192.168.*` e kolegut.
Lidhja mund të ndërpritet nga deploy/restart; klienti OCPP duhet të rilidhet.
Plani Free shërben për provë, por monitorimi operacional kërkon konfigurim dhe
kapacitet të përshtatshëm. API kërkon heartbeat sipas intervalit të dhënë në
BootNotification (30 sekonda); pas 120 sekondash pa mesazhe pika bëhet Unknown.
Në restart duhen rilidhje dhe status i ri. Karikimi aktiv nuk mbyllet ose faturohet
automatikisht vetëm sepse socket-i humbi lidhjen.

Veprimet e implementuara janë shtatë mesazhet e përmendura më sipër.
OCPP 2.0.1, RemoteStart/RemoteStop, QR reale, autorizimi i hardware real dhe
shpërndarja e stream-it në disa instanca mbeten punë të veçanta.
