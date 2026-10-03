# Simulatori automatik dhe koha e mbetur

Ky simulator është pjesë e web-it të projektit dhe punon vetëm kur API-ja lejon
provën demo në Development/Testing. Ai përdor WebSocket-in real OCPP 1.6 të
API-së; UI e karikimit përditësohet nga stream-i ekzistues i serverit.

## Prova me SIM-001

1. Pas commit/push dhe deploy-t të backend-it e web-it, hap **Profili dhe makina**
   (`/driver/account`). Vendos kapacitetin e përdorshëm në kWh dhe ruaj profilin.
2. Zgjidh SIM-001 në hartë dhe shtyp **Hap pikën e karikimit**. Pastaj shtyp
   **Hap simulatorin automatik**; hapet në një skedë tjetër.
3. Në simulator, kontrollo kapacitetin, përqindjen fillestare, objektivin dhe
   fuqinë. Shembulli 60 kWh është vlerë demo. Për provën tënde përdor të njëjtin
   kapacitet si në profil. Intervali fillestar është 5 sekonda, shpejtësia 1×.
4. Lëre të zgjedhur **Lidhe karikimin me llogarinë time**. Shtyp **Lidh simulatorin**,
   pastaj **Nis karikimin**.
5. Te skeda e pikës, energjia, fuqia totale, përqindja dhe koha e mbetur duhet të
   përditësohen me matjet e reja. Nuk nevojitet rifreskim i faqes.
6. Provo pauzën nga makina ose pika, vazhdimin, kodet e defektit, ndryshimin e fuqisë
   dhe shkëputjen/rilidhjen. Pauza dhe defekti ngrijnë energjinë dhe përqindjen;
   gjatë tyre vlerësimi i kohës fshihet.
7. Shtyp **Ndalo karikimin**, ose prit objektivin e baterisë. Simulatori dërgon
   regjistrin final, `StopTransaction` me ID-në e pranuar, pastaj `Available`.
   UI duhet të mbyllë sesionin dhe të shfaqë përmbledhjen e konfirmuar.

Nëse serveri ka një transaksion ekzistues të llogarisë tënde, nisja e re nuk
kryhet. Paneli shfaq ID-në dhe fushën e regjistrit final të testit. Kontrollo
vlerën dhe shtyp **Përfundo transaksionin ekzistues**, pastaj nis ciklin e ri.
Regjistri i sugjeruar bazohet në energjinë e pranuar të këtij sesioni demo;
leximi fizik përfundimtar i pajisjes duhet përdorur kur ai është i disponueshëm.

## Çfarë dërgohet automatikisht

- `BootNotification` kur lidhet dhe `Heartbeat` në intervalin e pranuar nga API-ja.
- `Authorize`, `Preparing`, `StartTransaction`, pastaj `Charging` pas pranimit.
- Një `MeterValues` me energjinë aktive në Wh, fuqinë totale në kW dhe `SoC` në
  Percent. Nuk vendoset fazë për këto totale. SoC llogaritet nga energjia e
  simuluar, kapaciteti dhe niveli fillestar.
- Statuset e pauzës, vazhdimit, defektit dhe disponibilitetit nga butonat përkatës.
- Matje finale dhe `StopTransaction` kur ndalet ose arrihet objektivi. Për ndalimin
  manual mund të zgjedhësh secilën nga 11 arsyet OCPP 1.6.

Paneli mbulon ciklin bazë të karikimit, nëntë statuset dhe 15 kodet e defektit.
Komandat nga serveri për firmware, rezervime ose administrim të pajisjes kërkojnë
skenarë të tjerë; paneli kthen `NotImplemented` për komandat që nuk mbështet.
Mesazhet dhe përgjigjet e fundit shfaqen në panelin e trafikut.

## SIM-004…010 dhe përdoruesit e tjerë

Hap `/driver/simulator?station=SIM-004` deri te `SIM-010` në skeda të veçanta.
Kutia e lidhjes me llogarinë tënde është bosh si parazgjedhje. Kjo ruan pronësinë
ekzistuese të përdoruesve të provës dhe dërgon ndryshimet publike në hartë.
Pronësia private kërkon që pritja e përdoruesit tjetër të jetë ende aktive;
seed-i i skenarit përgatit pritje 30-minutëshe. Nëse pritja ka skaduar, përgatite
nga llogaria përkatëse ose rinis API-në demo sipas `demo-simulator-scenario.md`.
Një sesion pa pronar vazhdon të ndryshojë statusin publik, por nuk futet në
historikun e një llogarie. Matjet private të të tjerëve nuk shfaqen te llogaria jote.

Koha e mbetur shfaqet edhe në panelin e pikës në hartë. Simulatori automatik
dërgon pas çdo matjeje një `DataTransfer` me `vendorId=EVCharging.Simulator`,
`messageId=ChargingEstimate` dhe vetëm ID-në e transaksionit e sekondat e mbetura.
[Forma e mesazhit OCPP 1.6](https://github.com/mobilityhouse/ocpp/blob/master/ocpp/v16/schemas/DataTransfer.json)
lejon të dhëna të furnizuesit si tekst JSON. Ky zgjerim pranohet vetëm për
simulatorët demo të lejuar dhe transaksionin e tyre aktiv.
Vlerësimi përdor objektivin e zgjedhur dhe shpejtësinë 1×/10×/30×/60×.
Nuk kërkon lidhjen me llogarinë e personit që sheh hartën ose ruajtjen e një
kapaciteti shembull në profilin e përdoruesit tjetër.

Për sesionet e lidhura me një profil, API-ja llogarit edhe një vlerësim nga
kapaciteti i përdorshëm, SoC dhe fuqia totale aktuale. Harta merr vetëm vlerësimin
publik; kapaciteti, SoC, identiteti dhe sesioni mbeten privatë. Ndryshimi i
kapacitetit në profil rifreskon menjëherë edhe vlerësimin publik të llogaritur.
Në pauzë, defekt, shkëputje, përfundim ose pas skadimit të matjeve, vlerësimi
hiqet. Përfundimi i karikimit nuk garanton që makina është larguar nga lidhësi.

Mbaji skedat e simulatorëve hapur gjatë provës. Mbyllja e skedës shkëput WebSocket-in
dhe nuk është konfirmim ndalimi. Rilidhja brenda panelit ruan ID-në aktive;
rinisja e faqes kërkon rikuperimin e transaksionit nga serveri për llogarinë tënde.

## Llogaritja e kohës deri në 100%

`minuta ≈ kapaciteti_kWh × (100 − SoC) / 100 / fuqia_totale_kW × 60`

Vlera rrumbullakohet lart në minuta dhe shënohet si vlerësim. Kapaciteti ruhet në
profil dhe kopjohet në sesionin e lidhur. Ndryshimi i tij gjatë karikimit dërgon
një përditësim privat menjëherë, pa pritur matjen tjetër dhe pa ndryshuar historikun.
Nuk ka kapacitet të supozuar për llogaritë reale.

Pauza, shkëputja, defekti, stream-i i vjetruar, mungesa e SoC/kapacitetit dhe fuqia
për një fazë të vetme e fshehin vlerësimin me një shpjegim. Fuqia duhet rifreskuar
brenda 2 minutave dhe SoC brenda 5 minutave. Matjet e energjisë nuk e rifreskojnë
afatin e SoC. Përqindja dhe vlerësimi privat nuk ndryshojnë nga një orë ose nga
energjia e marrë pa një SoC të ri të raportuar.

Koha e mbetur supozon të njëjtën fuqi deri në 100%; humbjet dhe ulja e fuqisë afër
fundit mund ta zgjasin. Një kufi i makinës në 80% nuk mund të zbulohet nga SoC
vetëm. Koha e sesionit mat kohën reale, edhe nëse shpejtësia e simulatorit është
10×, 30× ose 60×; vlerësimi deri në 100% mbetet në ritmin e fuqisë së raportuar.

## Konfigurimi dhe deploy

Simulatori në browser kërkon konfigurimin ekzistues të demonstrimit:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development
EVCHARGING_Ocpp__SimulatorEnabled=true
EVCHARGING_Ocpp__AllowAnonymousSimulators=true
```

Web-i përdor origin-in e `EVCHARGING_API_BASE_URL` për adresën WebSocket dhe nuk
publikon fjalëkalime të karikuesve. Simulatori nuk shfaqet kur API-ja çaktivizon
provën demo. Nëse karikuesi mbrohet vetëm me Basic Auth, browser-i nuk mund të
dërgojë atë header: përdor simulatorin e jashtëm ose mjedisin demo anonim.

Migrimi `20261003134511_BatteryCapacityForChargingEstimates` shton kapacitete
nullable në profil/sesion dhe kohën e marrjes së SoC. Nuk fshin sesione apo
histori. `render-entrypoint.sh` kryen migrimet para nisjes sipas konfigurimit
ekzistues. Duhen deploy-t e backend-it dhe web-it për këtë veçori.
Migrimi `20261003144227_PublicChargingForecastExpiry` shton vetëm afatin nullable
të vlerësimit publik në pikën e karikimit. Afati i simulatorit është 2 minuta;
vlerësimi i llogaritur skadon me fuqinë (2 minuta) ose SoC (5 minuta), cilado e para.

Testet përfshijnë ruajtjen/validimin e profilit, përditësimin privat live,
telemetrinë automatike, refuzimin e nisjes, timeout-in, pauzën, defektin,
rilidhjen, regjistrin final dhe kushtet e vlerësimit. Provat e PostgreSQL
ekzekutohen vetëm me opt-in dhe databazën e posaçme të testimit.
