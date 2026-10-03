# Sesionet e karikimit dhe përditësimi i menjëhershëm

Gjendja: 3 tetor 2026. [Auditi fillestar](charging-architecture-audit.md) shpjegon vendimet mbi kodin ekzistues. Ndryshimet vazhdojnë monolitin modular, PostgreSQL, inbox-in OCPP, outbox-in dhe SSE. [Skenari me 10 simulatorë](demo-simulator-scenario.md) shpjegon provën me disa përdorues.

## Burimi autoritativ

`charging_sessions` është burimi i sesionit fizik: nisja/fundi, tarifa fillestare, energjia, matjet e fundit, gjendja semantike dhe versioni. `charging_connectors` ruan gjendjen dhe lidhjen e pajisjes. UI dhe leximi i sesionit nuk kërkojnë më `OcppChargingTransaction` ose historinë e mostrave OCPP.

`ocpp_charging_transactions` ruan identifikuesin OCPP 1.6, meterStart/meterStop, mesazhet, kohët dhe lidhjen `session_id`. Inbox-i, statuset bruto dhe çdo mostër e pajisjes ruhen për diagnostikim. Pronari, tarifa dhe përmbledhja financiare nuk mbahen më si burim paralel në këtë tabelë.

Rrjedha aktuale:

```text
Pajisja → autentikim/transport OCPP → inbox dhe bllokim i pajisjes
        → normalizim në ChargerEvent → ChargingSession/ChargingConnector
        → histori/projeksion/outbox në të njëjtin transaksion
        → dispatcher pas commit-it → SSE → përditësim i UI
```

`IChargerEventReceiver` zbatohet nga procesuesi i sesionit. Adapteri i ardhshëm duhet të verifikojë burimin, të deduplikojë në inbox dhe të marrë bllokimin përkatës përpara se ta thërrasë; receiver-i merr pjesë në transaksionin e adapterit. HashSet-i i fixtures offline nuk përdoret si deduplikim persistent.

## Tre cikle të ndara

- Pajisja krijon dhe përfundon sesionin fizik.
- `ChargingConnectionRequest` është lidhja e Driver-it me atë sesion ose pritja për nisjen e tij.
- SSE vëzhgon të dhënat. Hapja/mbyllja/rilidhja e aplikacionit nuk nis dhe nuk ndalon karikimin.

Një Driver lejohet të ketë maksimumi **një** kërkesë `Waiting` ose `Linked`, në total. Një pikë/lidhës lejohet të ketë maksimumi **një** kërkesë në këto gjendje. Shumë telefona/tab-e të të njëjtit Driver vëzhgojnë të njëjtin sesion.

PostgreSQL i zbaton me indeksat unikë të pjesshëm `ux_claim_active_driver` dhe `ux_claim_active_connector`, me filtrin `state IN ('Waiting', 'Linked')`. Ka edhe unikësi për një claim për sesion dhe një sesion fizik aktiv për lidhës. Foreign keys të përbëra pengojnë lidhjen e claim/sesionit me pikë ose lidhës të gabuar.

Claim merr bllokimin ekzistues `FOR UPDATE` të pajisjes dhe bllokimin e llogarisë. I njëjti bllokim i pajisjes serializon claim, start dhe stop. Kufizimet në DB mbrojnë edhe kërkesat e të njëjtit Driver drejt pajisjeve të ndryshme. Konfliktet e pritshme kthehen `409 driver_already_connected` ose `409 charger_in_use`.

### Dy rendet e nisjes

1. **Aplikacioni i pari:** krijohet `Waiting` me afat 30 minuta. Nisja fizike e lidh atomikisht dhe e kalon në `Linked`.
2. **Pajisja e para:** krijohet një sesion pa pronar. Vizitori sheh vetëm statusin dhe vlerësimin publik. Claim-i i mëvonshëm refuzohet me `charger_in_use`; pronësia përgatitet para një nisjeje të re.

Retry në të njëjtën pikë është i sigurt. Pritja mund të anulohet/skadojë. Stop fizik e kalon lidhjen në `Completed` dhe liron kufirin e Driver-it. Humbja e rrjetit nuk e liron pronësinë dhe nuk e mbyll sesionin.

`Demo:DriverEmail` mbetet vetëm konfigurim i seed-it të llogarisë demo. Nuk cakton automatikisht pronësinë në rrjedhën OCPP.

## Koha, gjendja dhe matjet

- `StartedAt` / `EndedAt`: ora e raportuar nga pajisja.
- `StartReceivedAt` / `StopReceivedAt`: kur backend-i mori mesazhin.
- `LinkedAt`: kur nisja fizike lidhi pritjen ekzistuese me sesionin. Ora e pajisjes mund të ndryshojë nga ora e marrjes.
- Tarifa fiksohet kur përpunohet nisja fizike. Ndryshimi i mëvonshëm i tarifës nuk ndryshon sesionin ekzistues.

Për çdo sesion `IsDemo`, UI përdor marrjen e nisjes dhe ndalimit nga serveri për kohëzgjatjen reale. Shfaq orë, minuta, sekonda dhe ditë kur duhen. Adapteri pranon rendin e marrjes së formularëve manualë vetëm kur simulator mode dhe `IsDemo` janë aktivë: një timestamp i vjetër mund të vijë pas një simulimi të përshpejtuar. Kohët origjinale ruhen. Hardware real ruan renditjen sipas kohës së pajisjes.

Përkthimi OCPP → gjendje semantike është i centralizuar. Për shembull, `Preparing` → `Connecting`, `SuspendedEV` → `PausedByVehicle`, `SuspendedEVSE` → `PausedByCharger`. Statuset bruto mbeten në historinë e integrimit. Kufizimet manuale Maintenance/OutOfService vazhdojnë të kenë përparësi në hartë.

OCPP 1.6 §4.9 lejon kod gabimi me status jo-Faulted si paralajmërim. `Charging` + `HighTemperature` ruan Charging dhe fuqinë, por shfaq paralajmërim. `connectorId=0` raporton kontrolluesin qendror; gjendja/gabimi i tij shfaqen veçmas dhe nuk mbishkruajnë statuset individuale të lidhësve. Harta merr `warningCode` publik edhe kur gjendja e përgjithshme nuk ndryshon.

Energjia vjen nga matësi. Fuqia shfaqet vetëm kur pajisja dërgon `Power.Active.Import` në W/kW; nuk nxirret nga energjia ose nga fuqia nominale e pikës. Matja e fuqisë ka afat freskie 120 sekonda nga marrja dhe pastrohet gjatë ndryshimit të gjendjes, pauzës ose humbjes së lidhjes. SoC shfaqet vetëm kur dërgohet një vlerë e vlefshme. `100%` nuk krijon StopTransaction dhe nuk është parashikim kohe.

Mostrat normalizohen vetëm kur mbërrijnë. Energjia, fuqia, SoC dhe temperatura kanë kohët e tyre të renditjes, që një matje e një fushe të mos bllokojë një përditësim të vlefshëm të një fushe tjetër. Fuqia nga përpara rifillimit nuk paraqitet si fuqi aktuale. Përgjigjja e sesionit lexon projeksionin e ruajtur dhe lidhjen me pronarin me kërkesa të kufizuara; nuk skanon të gjitha matjet historike. Matjet bruto ruhen edhe kur janë të papërshtatshme për përmbledhjen.

Çdo grup matjesh të pranuara mbart edhe vlerat, njësitë, fazat, vendndodhjet, kontekstin dhe formatin në delta private. Diagnostikimi shfaq edhe Voltage, Current, Frequency, matjet reactive/export/interval dhe SignedData; këto nuk paraqiten si energji aktive e importuar ose fuqi e verifikuar. Vlerat numerike jashtë precision-it ruhen si tekst pa shkaktuar overflow në PostgreSQL.

StopTransaction i vlefshëm mbyll sesionin fizik edhe nëse regjistri final bie nën nisjen ose energjinë e pranuar. Në atë rast `completionWarning=FinalMeterRegression`, energjia/kostoja finale mbeten null dhe nuk krijohet përmbledhje financiare e gabuar. `meterStop` origjinal ruhet për verifikim. Një pauzë ose animacioni i përfunduar në simulator nuk krijon përfundim fizik.

## Realtime publik dhe privat

Envelope përmban `eventId`, `eventType`, `entityType`, `entityId`, `entityVersion`, `occurredAt` dhe `stationId`. Identiteti i marrësit qëndron në server.

- **Harta:** merr vetëm projeksionin publik të pikës. Statusi/çmimi/koordinatat përditësohen lokalisht sipas versionit. Nuk dërgohen ID Driver-i, email, idTag ose matje personale.
- **Karikimi personal:** eventet mbajnë DTO-n e sesionit dhe filtrohen sipas pronarit. Matja/statusi ndryshojnë drejtpërdrejt UI-në, pa GET për çdo matje.
- **Komandat:** Boot, Heartbeat, Authorize, Status, Start, Meter dhe Stop dërgojnë `DeviceCommandChanged` privat për pronarin ose pritjen aktive. Authorize Invalid shfaqet i refuzuar edhe kur transporti kthen CALLRESULT. Sekuenca e persistuar shmang humbjen e dy komandave me të njëjtën kohë marrjeje. Refuzimi mbetet i dukshëm deri në suksesin përkatës; Heartbeat nuk e fshin. Diagnostikimi nuk bën polling.
- **Historiku:** kërkohet veçmas kur vjen `SessionCompleted`; nuk ringarkohet nga matjet.
- **Rikuperimi:** snapshot i plotë në hyrje, rilidhje dhe rikthim te tab-i/rrjeti. Ndryshimet gjatë snapshot-it riaplikohen me mbrojtje nga versionet e vjetra. EventId-të e përsëritura injorohen në një cache të kufizuar.

SSE ka heartbeat çdo 10 sekonda dhe rilidhje pas mbylljes së stream-it. UI zbulon heshtjen pas 25 sekondash, kufizon GET-in në 10 sekonda dhe përdor polling çdo 5 sekonda gjatë shkëputjes. Tregon gjendjen e rilidhjes dhe mban të dhënat e fundit. Këto janë mekanizma rikuperimi, jo SLA e matur në internet.

Pas commit-it një sinjal zgjon dispatcher-in menjëherë; polling i outbox-it çdo sekondë mbetet rikuperim. `StationEventWorker` punon edhe kur simulatori është i çaktivizuar. `ChargerLivenessWorker` merret veçmas me humbjen e komunikimit të pajisjes.

Radhët e stream-it janë të kufizuara. Kur një klient ngec, stream-i mbyllet për të detyruar rilidhje/snapshot; nuk përdoret DropOldest që do të fshihte në heshtje nisjen ose përfundimin. Dorëzimi është at-least-once. `PublishedAt` tregon publikimin te bus-i, jo konfirmim nga çdo shfletues.

## Migrimi

`20261002125831_ChargingBusinessSessions`:

- krijon sesionet/lidhësit e pavarur nga protokolli dhe i lidh me rekordet OCPP;
- kopjon sesionet ekzistuese me të njëjtat ID, kohë, matje dhe tarifë;
- transferon pronësinë ekzistuese në claim kur ajo mungon dhe nuk ka konflikt demo;
- skadon pritjet e vjetra dhe shënon lidhjet e përfunduara;
- shton indeksat dhe foreign keys për invariantet e reja;
- heq kolonat e dyfishuara të pronarit/tarifës/matjeve nga tabela OCPP.

Kodi i vjetër caktonte `Demo:DriverEmail` në nisjet pa kërkesë lidhjeje. Kjo mund të linte disa sesione demo aktive për të njëjtin Driver. Migrimi i korrigjuar më 3 tetor 2026 trajton vetëm caktimet në pika `IsDemo` që nuk kanë një kërkesë ekzistuese për atë sesion:

- Sesionet e përfunduara dhe caktimet pa konflikt transferohen si më parë.
- Nëse një caktim demo aktiv bie ndesh me një kërkesë `Waiting`/`Linked` ose me një tjetër caktim aktiv për transferim, sesioni ruhet pa pronar. Vendimi merret për të gjithë kandidatët para insert-it; rendi i rekordeve nuk zgjedh një fitues.
- Çdo caktim demo i lënë pa pronar ruhet në `audit_entries`, me action `ChargingBusinessSessions.DemoOwnerUnlinked`. `changes_json.legacyTransaction` përmban rekordin e mëparshëm, përfshirë Driver-in, pikën, kohët, matjet dhe tarifën. `actor_role` është `Migration`; `actor_id` është UUID zero.
- Kërkesat ekzistuese mbeten autoritative. Migrimi nuk ndryshon pronarin e tyre. Pritjet e skaduara dhe lidhjet me sesione të ndaluara normalizohen përpara kontrollit.
- Sesionet, rekordet OCPP dhe historiku ruhen. Një sesion demo pa pronar që ka filluar nuk mund të merret nga një vizitor; përgatitja e pronësisë kërkohet para nisjes së re. Kufizimi i një lidhjeje aktive për Driver mbetet në fuqi.

Konfliktet mes kërkesave eksplicite ose pronarëve të pikave jo-demo ende e ndalin migrimin. Gabimi përfshin `DETAIL` me ID-të e kërkesave/Driver-ëve/pikave dhe `HINT` për pajtimin e tyre (shfaqja e `DETAIL` varet nga konfigurimi Npgsql). Rekordet pa mapping/tarifën origjinale gjithashtu refuzohen. Dështimi kthen pas edhe ndryshimet dhe auditin e këtij migrimi. Down refuzohet sepse do të humbiste sesionet e modelit të ri; kthimi pas kërkon plan eksporti/rikthimi të të dhënave.

Korrigjimi është brenda `ChargingBusinessSessions`, sepse një migrim i ri pas tij nuk do të arrihej nga instalimi që po dështon. Instalimet që e kanë përfunduar atë nuk e ekzekutojnë përsëri. Për instalimin e dështuar, publiko kodin e korrigjuar dhe përsërit komandën e zakonshme `--migrate`; nuk duhet fshirë databaza ose historia e migrimeve.

`20261002180835_ChargingMeasurementClocks` është migrim shtues për kohën e veçantë të energjisë, me backfill nga matjet ekzistuese. Skema përfundimtare dhe snapshot-i nuk ndryshojnë nga korrigjimi i caktimeve demo.

`20261003113430_ChargerFaultDiagnostics` është shtues: ruan kodet/mesazhet e lidhësit dhe kontrolluesit, paralajmërimin publik, sekuencën e komandave, grupin e fundit të matjeve dhe arsyen/verifikimin e ndalimit. Kopjon arsyen ekzistuese të ndalimit nga rekordi i protokollit. Aplikohet përpara nisjes së API-së së përditësuar.

Migrimet dhe indekset janë kontrolluar nga modeli dhe SQL i gjeneruar offline. Zbatimi real në PostgreSQL mbetet i paprovuar në këtë laptop.

## Provat

Verifikimi i rrjedhës së simulatorit më 3 tetor: **98 teste backend të kaluara, 9 PostgreSQL të paekzekutuara; 66 teste web, TypeScript dhe build production të kaluara.** Provat e reja përfshijnë HTTP/WebSocket/SSE real brenda host-it lokal të testit, 10 simulatorë/disa përdorues, të gjitha enum-et MeterValues, 11 arsyet e ndalimit, paralajmërimet dhe izolimin e transaksioneve. Aksesi ndërveprues te simulatori i jashtëm u refuzua nga mjeti, ndaj nuk pretendohet provë live pas deploy-it. Standardi i përdorur është PDF-ja OCPP 1.6 edition 2 që dërgoi përdoruesi, sidomos §4.9, §7.6, §7.16–7.17 dhe §7.32. Dokumenti bazë nuk zëvendëson specifikimin JSON/errata ose certifikimin.

Verifikimi i korrigjimit të migrimit më 3 tetor 2026: **79 teste backend të kaluara, 9 prova PostgreSQL të paekzekutuara.** SQL-ja e përditësimit u gjenerua offline me EF dhe kontrolli i modelit/snapshot-it kaloi. Docker-i lokal refuzoi aksesin; migrimi nuk u ekzekutua mbi një databazë PostgreSQL. Në verifikimin e mëparshëm kaluan 45 teste web, typecheck dhe build production; replay offline dha 20 kWh, 7.00 EUR dhe një dublikatë të injoruar. Web-i dhe replay nuk u ndryshuan nga ky korrigjim.

- Backend: lifecycle OCPP → DB → payload privat SSE; SIM-003 me orë të vjetër, pause/resume/fault/stop/restart; fuqi e raportuar dhe e skaduar; SoC; privatësi; claim para/pas nisjes, tarifa fillestare dhe rihapja nga një klient tjetër; konflikte të Driver-it; model/migration snapshot.
- Web: deltat e sesionit pa GET, 1,000 matje pa ringarkim harte/historiku, deduplikim, ruajtja e versionit të ri, snapshot që nuk rikthen një pikë të arkivuar, rilidhje, timeout dhe skadim autentikimi.
- Rruga PostgreSQL: katër teste për migrimin bazë, garat claim/start dhe dy Driver/pika, indeksat unikë dhe rollback të claim/outbox. Pesë teste shtesë mbulojnë konfliktet demo, auditin/historikun, pronarin eksplicit, konfliktet me pritje sipas Driver-it ose konektorit, normalizimin e gjendjeve, retry dhe rollback për konflikte eksplicite/jo-demo. Janë **të çaktivizuara si parazgjedhje dhe nuk u ekzekutuan**. InMemory nuk provon locking/FK/rollback.

Për provën PostgreSQL kërkohet paraprakisht një databazë bosh, e përkohshme, me emër që fillon `evcharging_test`, dhe autorizimi për atë lidhje. Testet krijojnë dhe pastrojnë vetëm një skemë me emër rastësor brenda saj; nuk përdorin skemën `public`. Nuk përdoret production dhe nuk krijohet shërbim Docker nga testet.

```powershell
# Ekzekuto vetëm pasi të jetë siguruar dhe autorizuar databaza e testimit.
$env:EVCHARGING_TEST_POSTGRES_ENABLED = '1'
$env:EVCHARGING_TEST_POSTGRES = '<connection string për databazën evcharging_test...>'
dotnet test backend/EVCharging.slnx --no-restore --filter FullyQualifiedName~PostgreSqlChargingTests
```

## Kufijtë që mbeten

Një instancë API: registry i lidhjeve dhe bus-i janë në memorie; restart-i mbyll rekordet e lidhjeve të braktisura dhe pajisja rilidhet. Një lidhje e re supersedon të vjetrën nën bllokimin e pajisjes. Para shkallëzimit në shumë instanca duhen vendime për pronësinë e socket-eve dhe shpërndarjen/claim të outbox-it.

Greencharge është pajisje e prodhuar nga kolegu. Nevojiten mesazhe nga pajisja reale për SoC/fuqi, sjelljen në pauzë/shkëputje, autentikimin dhe provën e pronësisë për QR. `IChargingClaimVerification` është kufiri për këtë verifikim; implementimi aktual pranon vetëm bypass-in e aktivizuar shprehimisht për pika demo. QR statike me station ID nuk konsiderohet provë pronësie. Politika aktuale OCPP pranon vetëm identitete dhe kredenciale demo të konfiguruara; autentikimi real nuk është shpikur.

Nuk u implementuan komandat remote start/stop, pagesa ose llogaritje sintetike e kohës së mbetur. Nuk u ndryshuan konfigurime sigurie/sistemi të laptopit dhe nuk u krye deploy.
