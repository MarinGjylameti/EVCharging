# Sesionet e karikimit dhe përditësimi i menjëhershëm

Gjendja: 2 tetor 2026. [Auditi fillestar](charging-architecture-audit.md) shpjegon vendimet mbi kodin ekzistues. Ndryshimet vazhdojnë monolitin modular, PostgreSQL, inbox-in OCPP, outbox-in dhe SSE.

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
2. **Pajisja e para:** krijohet një sesion pa pronar. Claim-i i verifikuar lidhet me atë ID ekzistuese; nuk krijon sesion tjetër dhe nuk rivendos matësin, energjinë, tarifën apo kohën.

Retry në të njëjtën pikë është i sigurt. Pritja mund të anulohet/skadojë. Stop fizik e kalon lidhjen në `Completed` dhe liron kufirin e Driver-it. Humbja e rrjetit nuk e liron pronësinë dhe nuk e mbyll sesionin.

`Demo:DriverEmail` mbetet vetëm konfigurim i seed-it të llogarisë demo. Nuk cakton automatikisht pronësinë në rrjedhën OCPP.

## Koha, gjendja dhe matjet

- `StartedAt` / `EndedAt`: ora e raportuar nga pajisja.
- `StartReceivedAt` / `StopReceivedAt`: kur backend-i mori mesazhin.
- `LinkedAt`: kur përdoruesi u lidh me sesionin. Mund të jetë disa minuta pas nisjes fizike.
- Tarifa fiksohet kur përpunohet nisja fizike. Një ndryshim tarife para claim-it nuk ndryshon tarifën e sesionit ekzistues.

Për simulatorin me orë të përshpejtuar/të vjetër, UI përdor kohën e marrjes së mesazheve vetëm kur `IsDemo` dhe devijimi i orës janë të konfirmuar. Kohët origjinale ruhen. Kjo përjashtim nuk zbatohet për hardware real.

Përkthimi OCPP → gjendje semantike është i centralizuar. Për shembull, `Preparing` → `Connecting`, `SuspendedEV` → `PausedByVehicle`, `SuspendedEVSE` → `PausedByCharger`. Statuset bruto mbeten në historinë e integrimit. Kufizimet manuale Maintenance/OutOfService vazhdojnë të kenë përparësi në hartë.

Energjia vjen nga matësi. Fuqia shfaqet vetëm kur pajisja dërgon `Power.Active.Import` në W/kW; nuk nxirret nga energjia ose nga fuqia nominale e pikës. Matja e fuqisë ka afat freskie 120 sekonda nga marrja dhe pastrohet gjatë ndryshimit të gjendjes, pauzës ose humbjes së lidhjes. SoC shfaqet vetëm kur dërgohet një vlerë e vlefshme. `100%` nuk krijon StopTransaction dhe nuk është parashikim kohe.

Mostrat normalizohen vetëm kur mbërrijnë. Energjia, fuqia, SoC dhe temperatura kanë kohët e tyre të renditjes, që një matje e një fushe të mos bllokojë një përditësim të vlefshëm të një fushe tjetër. Fuqia nga përpara rifillimit nuk paraqitet si fuqi aktuale. Përgjigjja e sesionit lexon projeksionin e ruajtur dhe lidhjen me pronarin me kërkesa të kufizuara; nuk skanon të gjitha matjet historike. Matjet bruto ruhen edhe kur janë të papërshtatshme për përmbledhjen.

## Realtime publik dhe privat

Envelope përmban `eventId`, `eventType`, `entityType`, `entityId`, `entityVersion`, `occurredAt` dhe `stationId`. Identiteti i marrësit qëndron në server.

- **Harta:** merr vetëm projeksionin publik të pikës. Statusi/çmimi/koordinatat përditësohen lokalisht sipas versionit. Nuk dërgohen ID Driver-i, email, idTag ose matje personale.
- **Karikimi personal:** eventet mbajnë DTO-n e sesionit dhe filtrohen sipas pronarit. Matja/statusi ndryshojnë drejtpërdrejt UI-në, pa GET për çdo matje.
- **Historiku:** kërkohet veçmas kur vjen `SessionCompleted`; nuk ringarkohet nga matjet.
- **Rikuperimi:** snapshot i plotë në hyrje, rilidhje dhe rikthim te tab-i/rrjeti. Ndryshimet gjatë snapshot-it riaplikohen me mbrojtje nga versionet e vjetra. EventId-të e përsëritura injorohen në një cache të kufizuar.

SSE ka heartbeat çdo 10 sekonda dhe rilidhje pas mbylljes së stream-it. UI zbulon heshtjen pas 25 sekondash, kufizon GET-in në 10 sekonda dhe përdor polling çdo 5 sekonda gjatë shkëputjes. Tregon gjendjen e rilidhjes dhe mban të dhënat e fundit. Këto janë mekanizma rikuperimi, jo SLA e matur në internet.

Pas commit-it një sinjal zgjon dispatcher-in menjëherë; polling i outbox-it çdo sekondë mbetet rikuperim. `StationEventWorker` punon edhe kur simulatori është i çaktivizuar. `ChargerLivenessWorker` merret veçmas me humbjen e komunikimit të pajisjes.

Radhët e stream-it janë të kufizuara. Kur një klient ngec, stream-i mbyllet për të detyruar rilidhje/snapshot; nuk përdoret DropOldest që do të fshihte në heshtje nisjen ose përfundimin. Dorëzimi është at-least-once. `PublishedAt` tregon publikimin te bus-i, jo konfirmim nga çdo shfletues.

## Migrimi

`20261002125831_ChargingBusinessSessions`:

- krijon sesionet/lidhësit e pavarur nga protokolli dhe i lidh me rekordet OCPP;
- kopjon sesionet ekzistuese me të njëjtat ID, kohë, matje dhe tarifë;
- transferon pronësinë ekzistuese në claim kur ajo mungon;
- skadon pritjet e vjetra dhe shënon lidhjet e përfunduara;
- shton indeksat dhe foreign keys për invariantet e reja;
- heq kolonat e dyfishuara të pronarit/tarifës/matjeve nga tabela OCPP.

Migrimi ndalet me gabim të qartë nëse ka pronarë aktivë në konflikt ose rekorde pa mapping/tarifën origjinale. Nuk zgjedh vetë pronarin dhe nuk shpik një tarifë nga çmimi i sotëm. Këto të dhëna duhen pajtuar në një kopje testimi para deploy-it. Down refuzohet sepse do të humbiste sesionet e modelit të ri; kthimi pas kërkon plan eksporti/rikthimi të të dhënave.

`20261002180835_ChargingMeasurementClocks` është migrim i ri shtues për kohën e veçantë të energjisë, me backfill nga matjet ekzistuese. Migrimi i publikuar në commit-in c36bdb2 ruhet i pandryshuar, kështu që përditësimi funksionon edhe për instalimet që e kanë zbatuar atë.

Migrimet dhe indekset janë kontrolluar nga modeli dhe SQL i gjeneruar offline. Zbatimi real në PostgreSQL mbetet i paprovuar në këtë laptop.

## Provat

Rezultati lokal: **79 teste backend të kaluara, 4 prova PostgreSQL të paekzekutuara; 45 teste web të kaluara.** Typecheck dhe build production i web-it kaluan. Replay offline dha 20 kWh, 7.00 EUR dhe një dublikatë të injoruar.

- Backend: lifecycle OCPP → DB → payload privat SSE; SIM-003 me orë të vjetër, pause/resume/fault/stop/restart; fuqi e raportuar dhe e skaduar; SoC; privatësi; claim para/pas nisjes, tarifa fillestare dhe rihapja nga një klient tjetër; konflikte të Driver-it; model/migration snapshot.
- Web: deltat e sesionit pa GET, 1,000 matje pa ringarkim harte/historiku, deduplikim, ruajtja e versionit të ri, snapshot që nuk rikthen një pikë të arkivuar, rilidhje, timeout dhe skadim autentikimi.
- Rruga PostgreSQL: katër teste të veçuara për migrimin e të dhënave ekzistuese, garat claim/start dhe dy Driver/pika, indeksat unikë dhe rollback të claim/outbox. Janë **të çaktivizuara si parazgjedhje dhe nuk u ekzekutuan**. InMemory nuk provon locking/FK/rollback.

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
