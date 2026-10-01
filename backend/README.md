# Backend C# / .NET 10

API me EF Core/PostgreSQL për identitetin, profilin, njoftimet dhe leximin e pikave/historikut të Driver-it. Domeni dhe parseri OCPP janë të testueshëm offline. [Gjendja e plotë](../docs/implementation-status.md).

## Organizimi i kodit

| Projekti | Përgjegjësia |
|---|---|
| `src/EVCharging.Domain` | Entitetet dhe rregullat e biznesit, pa EF ose HTTP. |
| `src/EVCharging.Application` | Kontratat dhe validimi i rasteve të përdorimit. Interfacet e query/command për pikat dhe Driver-in janë këtu. |
| `src/EVCharging.Infrastructure` | Implementimi me EF/PostgreSQL, shërbimet e ruajtjes, Expo dhe adapterët OCPP. Konfigurimet e entiteteve janë ndarë në `Persistence/Configurations`. |
| `src/EVCharging.Api` | Autentikimi, konfigurimi i shërbimeve, endpoint-et HTTP dhe komandat e nisjes. Endpoint-et delegojnë te shërbimet; pyetjet EF nuk qëndrojnë në to. |

Varësitë e projekteve shkojnë nga Domain → Application → Infrastructure → API;
API është vendi ku lidhen implementimet me interfacet. Parseri OCPP është
offline; endpoint-i WebSocket, procesuesi i eventeve dhe publikimi SignalR janë
ende për t'u implementuar. [Skema OCPP dhe rrjedha realtime](../docs/ocpp-simulator-database.md).

## Formatimi C#

CSharpier përdor `.editorconfig` të projektit dhe `backend/.csharpierignore`
për të lënë jashtë migrimet e gjeneruara. Nga folderi `backend`:

```powershell
csharpier format src tests tools
csharpier check src tests tools
```

Formatimi nuk ndryshon sjelljen. Pas tij ekzekuto `dotnet test EVCharging.slnx --no-restore`.

## Kontrolli offline nga rrënja EVCharging

```powershell
.\scripts\verify-local.ps1
```

Nuk ndryshon ExecutionPolicy, PATH ose konfigurimin global. Nëse politika e laptopit e ndalon një skript, përdor komandat përkatëse të lejuara; mos ndrysho politikën.

## PostgreSQL dhe nisja

Kërkohet databazë e dedikuar për EVCharging. Skedari `.env.example` është shembull; .NET nuk e lexon automatikisht. Vendos konfigurimet në mjedisin e procesit, me sekretin tënd lokal.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:EVCHARGING_ConnectionStrings__Default = 'Host=127.0.0.1;Port=5440;Database=evcharging_dev;Username=evcharging;Password=YOUR_LOCAL_PASSWORD'
dotnet run --project src/EVCharging.Api --no-restore -- --migrate
```

Migrimi bëhet vetëm me komandë të qëllimshme. [initial-schema.sql](../docs/initial-schema.sql)
është SQL-ja e skemës fillestare; [ocpp-telemetry-schema.sql](../docs/ocpp-telemetry-schema.sql)
është migrimi shtues për OCPP dhe outbox. Ky migrim nuk është aplikuar ende në
PostgreSQL-in e Render-it.

Për Superadmin-in e parë vendos `EVCHARGING_Bootstrap__Email` dhe `EVCHARGING_Bootstrap__Password` dhe nis me `--bootstrap-superadmin`. Komanda refuzon kur ekziston tashmë një Superadmin. Pastaj hiqi këto dy variabla nga procesi. Mos përdor kredenciale HOUSS.

```powershell
dotnet run --project src/EVCharging.Api --no-restore
```

Adresa lokale nga launchSettings: `http://127.0.0.1:5201`. Health: `/health/live` dhe `/health/ready`; ready kthen 503 kur DB nuk lidhet.

## API e implementuar

- POST `/api/v1/auth/register`, `login`, `logout`, `password`.
- GET `/api/v1/me`; PATCH `/me/profile`, `/me/settings` me version.
- POST/DELETE `/api/v1/me/devices`; GET `/me/notifications`.
- GET `/api/v1/admin/access` për operator; GET `/api/v1/administrators` vetëm Superadmin.
- GET `/api/v1/driver/stations` për pikat aktive dhe GET `/api/v1/driver/history` për historikun e Driver-it të hyrë.
- GET `/api/v1/admin/stations` për Admin/Superadmin; POST, PATCH `/{id}` dhe POST `/{id}/archive` vetëm për Superadmin. Ndryshimet kontrollojnë versionin dhe shkruajnë audit. Arkivimi refuzohet për pikë në gjendje Charging/Occupied.

Body contracts: `src/EVCharging.Application/Identity/Contracts.cs`. Auth përdor bearer opaque, jo JWT; roli merret nga DB në çdo kërkesë. Signup krijon vetëm Driver.

## OCPP offline

```powershell
dotnet run --project tools/EVCharging.OcppReplay --no-restore
```

Fixture verifikon 20 kWh, kosto 7 EUR dhe një matje duplicate. Nuk dërgon mesazhe në rrjet.

## Host i përkohshëm për test web

```powershell
dotnet run --project tools/EVCharging.BrowserTestHost --no-restore -- --local-browser-test
```

Vetëm loopback `127.0.0.1:5202`, InMemory e zbrazur në çdo nisje. Llogari sintetike:

| Roli | Email | Fjalëkalimi |
|---|---|---|
| Driver | `driver@example.test` | `Local-driver-demo-2026!` |
| Superadmin | `operator@example.test` | `Local-test-only-2026!` |

Host-i krijon 10 pika demo (6 në Tiranë, 3 në Durrës, 1 në Lushnjë) dhe 6 karikime historike për Driver-in. Pika e Lushnjës ka një vlerësim demo të përfundimit pas 40 minutash. Koordinatat, statuset, tarifat dhe vlerësimi janë ilustrime, jo pika reale të verifikuara. Nuk referencohet nga projekti Api dhe nuk përdoret për deployment. Ndalimi heq të gjitha të dhënat e provës. Ky host nuk provon sjelljen e PostgreSQL.

Për një vlerësim real, adapteri i pajisjes duhet të marrë kohën e parashikuar të përfundimit për lidhësin përkatës dhe ta kalojë te `AvailabilityForecastRules.SetDeviceEstimate`. API e Driver-it e publikon vetëm kur pika është në karikim dhe vlerësimi është ende i freskët (maksimumi 15 minuta nga përditësimi). OCPP/WebSocket dhe lidhja me pajisje reale nuk janë implementuar ende. Në mungesë të këtyre të dhënave, API kthen `availabilityForecast: null`.

Push është i çaktivizuar. Aktivizimi real kërkon konfigurim Expo, pajisje dhe leje rrjeti. Worker fillestar ekzekutohet në një instancë.
