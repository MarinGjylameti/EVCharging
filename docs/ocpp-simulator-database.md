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
4. Pas commit-it, një procesues i outbox-it publikon `PointChanged` te kanali
   SignalR i hartës. Publikohet vetëm ID, versioni, statusi/disponueshmëria dhe
   koha e freskisë; `idTag`, driverId dhe matjet private nuk dalin në kanalin
   publik. `MeterValues` ruhet i plotë, por për UI mund të dërgohet i bashkuar
   periodikisht që të mos mbingarkohet harta. Ndryshimet e statusit nuk bashkohen.
5. Shfletuesi merr fillimisht snapshot-in REST të pikave dhe ndjek kanalin.
   Zbaton vetëm evente me version më të ri. Pas ndërprerjes/mbylljes së tab-it
   merr snapshot të ri; polling-u ekzistues çdo 60 sekonda mbetet rrugë
   rikuperimi. Kur të dhënat e pajisjes vjetrohen ose ajo del offline, UI duhet
   të tregojë gjendje të pasigurt, jo `Available` të vjetër.

`PublishedAt` në outbox do të thotë dorëzim te transporti i eventeve, jo që çdo
shfletues e ka parë. Versioni dhe snapshot-i e bëjnë klientin tolerant ndaj
eventeve të dubluara ose të humbura. Për më shumë se një instancë API,
procesuesit e outbox-it duhet të përdorin lease/`SKIP LOCKED` dhe një transport
të përbashkët; nuk mbështetemi në memorie lokale si burim të vërtetës.

Sot kjo është **skemë dhe rrjedhë e projektuar**. API-ja ende nuk ka endpoint
OCPP, përpunues outbox ose hub SignalR; harta aktualisht bën refresh çdo
60 sekonda. Këto pjesë duhen implementuar dhe testuar para se simulatori të
mund të ndryshojë statusin live.

## Si do të konfigurohet simulatori

API-ja duhet së pari të implementojë endpoint-in WebSocket dhe përpunuesin e
mesazheve. Migrimi i databazës vetëm nuk e hap këtë endpoint; adresa më poshtë
është formati i synuar, jo adresë aktive aktualisht.

1. Në simulator zgjidh OCPP **1.6J** dhe `Charge Point ID` të qëndrueshëm, p.sh.
   `SIM-002`. E njëjta ID duhet provision-uar te `ocpp_charge_points` dhe të
   aktivizohet vetëm pasi të përcaktohet mënyra e autentikimit të simulatorit.
2. `WebSocket URL (CMS)` duhet të jetë host-i publik i API-së me `wss://`, p.sh.
   `wss://<api-host>/ws/cs`. Kontrollo që `Station connection URL` të përfundojë
   si `wss://<api-host>/ws/cs/SIM-002` (nëse simulatori nuk e shton ID-në vetë,
   vendos URL-në e plotë). Nënprotokolli duhet të jetë `ocpp1.6`.
3. Pas `Connect`, dërgo BootNotification, Heartbeat, StatusNotification,
   Authorize dhe një cikël StartTransaction → MeterValues → StopTransaction.
   Verifiko përgjigjen me `transactionId` te StartTransaction dhe statusin e
   ruajtur për `connectorId=1`.
4. Në konfigurimin e simulimit `meterStart`/`meterStop` jepen si **integer Wh**.
   `MeterValues.sampledValue.value` mbetet string sipas mesazhit. Mos përdor
   kohë historike në testin live të statusit.

Render e drejton trafikun HTTP dhe WebSocket në të njëjtin port të Web Service.
Për një lidhje publike përdoret `wss://`, jo IP-ja lokale `192.168.*` e kolegut.
Lidhja mund të ndërpritet nga deploy/restart; klienti OCPP duhet të rilidhet.
Plani Free shërben për provë, por monitorimi operacional kërkon konfigurim dhe
kapacitet të përshtatshëm. Ende nuk ka endpoint WebSocket ose sinkronizim live
të hartës në këtë commit të databazës.
