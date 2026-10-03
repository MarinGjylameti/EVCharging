# Dhjetë pika për provën me simulatorin

Konfigurimi standard krijon `SIM-001` deri te `SIM-010`, secila me lidhësin 1,
Type 2, fuqi nominale 22 kW dhe tarifë 0.35 EUR/kWh. Gjendja fillestare është
`Unknown`; statusi dhe matjet e marra nga simulatori përcaktojnë hartën.

| Pikat | Përdorimi | Llogaria |
| --- | --- | --- |
| SIM-001, SIM-002, SIM-003 | Provat e tua, njëra pas tjetrës | Driver-i me të cilin ke hyrë |
| SIM-004…SIM-010 | Shtatë përdorues të veçantë | `simulator-004@example.test`…`simulator-010@example.test` |

Një Driver ka vetëm një lidhje aktive. SIM-001…003 mbeten pa pronar deri sa
ta përgatitësh pikën nga llogaria jote. Pikat 004…010 marrin pritje demo nga
llogaritë përkatëse; nisja e tyre në simulator krijon sesionin fizik për atë
përdorues. Vetëm statusi publik ndryshon në hartën tënde. Matjet, energjia,
kostoja, komandat dhe historiku i tyre mbeten privatë.

## Konfigurimi i API-së demo

Përdor këto cilësime vetëm në mjedisin e demonstrimit:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development
EVCHARGING_Ocpp__SimulatorEnabled=true
EVCHARGING_Ocpp__AllowAnonymousSimulators=true
EVCHARGING_Demo__PrepareOtherDrivers=true
```

Për krijimin e parë cakto edhe `EVCHARGING_Demo__OtherDriverPassword` në
mjedisin e shërbimit. Vlera duhet të plotësojë rregullat e fjalëkalimit të
llogarisë. Nëse mungon, përdoret `EVCHARGING_Demo__DriverPassword` ekzistues.
Ky është fjalëkalim i përbashkët vetëm për shtatë llogaritë e provës; nuk ka
vlerë të shkruar në kod. Krijimi nuk ndryshon fjalëkalimet e llogarive që
ekzistojnë. Pas krijimit fjalëkalimi mund të hiqet nga konfigurimi; llogaritë
e shënuara si pjesë e skenarit vazhdojnë të njihen nga API-ja.

Përgatitja është aktive si parazgjedhje kur simulator mode është i aktivizuar
në Development/Testing dhe jepen kredencialet për llogaritë e reja. Vlera
`EVCHARGING_Demo__PrepareOtherDrivers=false` e çaktivizon. Production nuk
përgatit këto llogari apo pritje.

Nisja e API-së krijon pikat e konfiguruara dhe pastaj përgatit pritjet. Pritja
zgjat 30 minuta. Një restart ose ekzekutim i ri i seed-it rinovon vetëm pritjen
e skaduar të të njëjtit përdorues prove. Pritjet që ende vlejnë, sesionet aktive
dhe lidhjet e përdoruesve të tjerë ruhen. Pas përfundimit të një prove, përgatit
sërish përdoruesin nga llogaria e tij ose rinis API-në demo për një cikël të ri.

`--seed-demo` kërkon `Demo:DriverEmail` dhe `Demo:DriverPassword`, krijon
llogarinë tënde demo nëse mungon dhe përgatit të njëjtat dhjetë pika. Seed-i
ruan historikun ekzistues. Pikat e vjetra ilustrative `EV-TIR-*`, `EV-DUR-*`
dhe `EV-LUS-001` arkivohen vetëm kur janë demo pa pajisje të lidhur, pa
sesion të hapur dhe pa pritje/lidhje aktive. Të dhënat e tyre dhe referencat
në histori mbeten në databazë. Një pikë operacionale e mbrojtur mund të mbetet
në hartë përveç dhjetë simulatorëve.

Nëse një email i rezervuar i përket një llogarie ekzistuese që nuk është
krijuar nga ky skenar, API-ja e lë llogarinë dhe pronësinë e saj të pandryshuar.

## Prova

Provat automatike përdorin host lokal me HTTP, WebSocket dhe SSE: ndryshimi i SIM-004 nga përdoruesi tjetër ndryshon vetëm atë pikë në hartë; matjet e tij nuk hyjnë në sesionin/historikun e përdoruesit të SIM-001. Provohen edhe kalimet e llogarisë kryesore te SIM-002 dhe SIM-003. Databaza e këtyre provave është InMemory; kufizimet dhe migrimi real PostgreSQL kërkojnë provën veçmas.

1. Krijo dhjetë simulatorë OCPP 1.6 me ID-të `SIM-001`…`SIM-010`. Përdor
   të njëjtën URL bazë të API-së `/ws/cs`; adresa përfundimtare duhet të ketë
   ID-në përkatëse. Secili përdor lidhësin 1 dhe Tag ID `TAG-001`.
2. Lidhi dhe dërgo BootNotification, Heartbeat dhe StatusNotification
   `Available/NoError`. Dhjetë pikat duhet të përditësohen në hartë.
3. Nga llogaria jote përgatit SIM-001 dhe nis karikimin në simulatorin 001.
   Dërgo energji dhe `Power.Active.Import` për të provuar fuqinë aktuale.
   Kontrollo `transactionId` të pranuar përpara matjeve dhe ndalimit.
4. Nis SIM-004…010. Harta jote duhet të ndryshojë për secilën pikë.
   Faqja dhe historiku yt shfaqin vetëm sesionin tënd. Një hyrje më vete me
   secilën llogari prove duhet të shfaqë vetëm karikimin e asaj llogarie.
5. Ndrysho një pikë: SuspendedEV, SuspendedEVSE, Faulted dhe kode gabimi,
   pastaj rikthe Charging/NoError. Vetëm pika përkatëse ndryshon status;
   pronari merr edhe matjet dhe diagnozën përkatëse në stream.
6. Përfundo SIM-001 me StopTransaction dhe ID-në aktive, pastaj dërgo
   Available/NoError. Përgatit SIM-002 nga llogaria jote, provo një cikël dhe
   përsërite me SIM-003. Sesionet e përfunduara mbeten në historikun tënd.

Koha e përshpejtuar dhe shpejtësia 22 kW e formularit nuk janë telemetri.
Fuqia aktuale kërkon një sampledValue `Power.Active.Import` me njësi W/kW.
Te formulari **MeterValues**, gjatë një transaksioni aktiv, përdor:

| Fusha | Prova e fuqisë | Prova e baterisë |
| --- | --- | --- |
| Connector ID | Connector 1 | Connector 1 |
| Transaction ID | ID-ja aktive te diagnostikimi i sesionit | E njëjta ID aktive |
| Timestamp | Ora aktuale në UTC | Ora aktuale në UTC |
| Context | Sample.Periodic | Sample.Periodic |
| Measurand | Power.Active.Import | SoC |
| Phase | Pa fazë nëse ofrohet; L1 për provën e këtij simulatori | Pa fazë nëse ofrohet; L1 pranohet për këtë demo |
| Location | Outlet | Outlet |
| Unit | kW | Percent |
| Meter value | 22 | 45 |

Dërgo **Send MeterValues** për secilën provë dhe prit përgjigjen RESP.
UI duhet të shfaqë 22 kW (ose 22 kW · L1) dhe baterinë 45% pa rifreskim.
Vlera 45% është vetëm një matje prove. Ajo nuk krijon automatikisht një
vlerësim të kohës së mbetur. Aktualisht aplikacioni nuk ka kapacitetin e
baterisë ose një llogaritje të kësaj kohe. Fuqia skadon pas 120 sekondash
pa matje të re; ridërgoje gjatë provës që të mbetet aktuale.

**Nisja e karikimit** shfaq orën në formatin 24-orësh dhe datën sipas zonës
Europe/Tirane. Kohëzgjatja shfaqet veçmas si **Kohë e kaluar**, përfshirë
pauzat. Në demo përdoret koha kur serveri pranoi nisjen; për pajisjet reale
përdoret nisja e raportuar nga pajisja. Një StartTransaction i refuzuar nuk
ndryshon orën e nisjes së transaksionit ekzistues.

StopTransaction duhet të shfaqet si mesazh i dërguar dhe i konfirmuar në
trafikun OCPP; përfundimi i animacionit të simulatorit nuk e mbyll sesionin.

## Dokumentacioni dhe mbulimi

U shqyrtuan PDF-ja OCPP 1.6 edition 2 FINAL (2017-09-28) e dërguar nga përdoruesi dhe dy artikujt: [hyrja në 2.0.1](https://ocpp-ws-io.rohittiwari.me/blog/introduction-to-ocpp-2-0-1), [lista e mesazheve](https://ocpplab.com/blog/ocpp-message-types-complete-reference). PDF-ja është referenca teknike për statuset, gabimet dhe enum-et MeterValues. Artikujt nuk përdoren si burim normativ kur ndryshojnë nga standardi.

Implementimi aktual përpunon BootNotification, Authorize, Heartbeat, StatusNotification, StartTransaction, MeterValues dhe StopTransaction. Mostrat mbajnë të 22 measurands, 16 units, 10 phases, 5 locations, 8 contexts dhe 2 formats të standardit. Përmbledhja llogarit vetëm energjinë aktive të importuar, fuqinë aktive të importuar, SoC dhe temperaturën kur vlera/metadata e lejon; pjesa tjetër shfaqet në diagnostikimin privat.

Ky mbulim nuk është certifikim i plotë i profilit Core ose i profileve opsionale. [OCA shpjegon ndryshimet në 2.0.1](https://openchargealliance.org/wp-content/uploads/2023/07/new_in_ocpp_201-v10.pdf): TransactionEvent dhe modeli i pajisjes kërkojnë adapter veçmas. OCPP 1.6 nuk raporton kohën deri në bateri të plotë si MeterValues standard; UI e shënon të paraportuar kur mungon një burim i verifikuar. Prova në faqen e jashtme të simulatorit mbetet e papërfunduar sepse mjeti refuzoi aksesin; provat lokale nuk pretendojnë verifikimin e deploy-it në Render.
