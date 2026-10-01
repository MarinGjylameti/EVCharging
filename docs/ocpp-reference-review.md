# Leximi i serverit OCPP të kolegut

Burimi lokal: `Apps/universal_ocpp16_csms/universal_ocpp16_csms`. U lexuan `server.py`, `csms.py`, `database.py`, `config.py`, `test_client.py`, README dhe konfigurimet. Nuk u ekzekutua ose ndryshua serveri Python. `.env` nuk u lexua.

Fotografia e bisedës e përshkruan kodin si provë, me strukturë/tabela për t’u rishikuar dhe synim C#. Kjo është kontekst nga kolegu; kërkesa e përdoruesit është që kodi të lexohet dhe të përdoret në logjikën e projektit.

## Rrjedha që tregon kodi

| Pjesa | Sjellja në burim |
|---|---|
| Transport | WebSocket, nënprotokolli `ocpp1.6`; porti i parazgjedhur 9000 |
| Adresa | `/ws/cs/<charge_point_id>` ose `/<id>`; parseri në fakt merr segmentin e fundit të shumë rrugëve |
| BootNotification | Ruhet vendor/model/firmware; kthehen Accepted, ora UTC dhe interval 30 sekonda |
| Heartbeat | Përditëson last_seen dhe connected; kthen orën UTC |
| StatusNotification | Gjendje në memorie për connector; DB ruan një last_status për gjithë pajisjen |
| Authorize | Pranon çdo idTag jo bosh; është placeholder në kod |
| StartTransaction | Merr connectorId, idTag, meterStart, timestamp; DB krijon ID dhe përgjigjja kthen transactionId + Accepted |
| MeterValues | Merr seri matjesh, timestamp, measurand, unit, phase, location; transactionId opsional |
| StopTransaction | Përditëson transaksionin sipas chargerId + transactionId, meterStop, timestamp dhe reason |
| REST | Porti 8000; health, chargers, transactions, meter-values dhe komanda remote |
| Test client | Dërgon BootNotification dhe pret komanda remote. Nuk simullon vetë një cikël të plotë karikimi |

Pikat kryesore në burim: `server.py:33,54,59,110`; `csms.py:47,118,132,187,238,366`; `database.py:32,44,58`; `test_client.py:50`.

Nga këto skedarë mund të nxirret forma e integrimit. Nuk mund të konfirmohet marka/modeli i pajisjes, përputhshmëria e firmware-it ose një provë reale e suksesshme.

## Shembull sintetik i shkëmbimit

```json
[2,"start-1","StartTransaction",{"connectorId":1,"idTag":"SIMULATED-TAG","meterStart":123000,"timestamp":"2026-09-28T16:00:00Z"}]
[3,"start-1",{"transactionId":1,"idTagInfo":{"status":"Accepted"}}]
[2,"meter-1","MeterValues",{"connectorId":1,"transactionId":1,"meterValue":[{"timestamp":"2026-09-28T16:30:00Z","sampledValue":[{"value":"133000","unit":"Wh"}]}]}]
[3,"meter-1",{}]
[2,"stop-1","StopTransaction",{"transactionId":1,"meterStop":143000,"timestamp":"2026-09-28T17:00:00Z","reason":"Local"}]
[3,"stop-1",{"idTagInfo":{"status":"Accepted"}}]
```

Këto janë shembuj të ndërtuar nga handler-at, jo log i kapur nga pajisja. Energjia e sesionit është diferenca `(143000 − 123000) / 1000 = 20 kWh`. Përgjigjja Accepted nuk duhet kopjuar pa kontrollin e autorizimit.

## Çfarë u përdor në C#

`Ocpp16TelemetryParser` interpreton katër mesazhet e telemetrisë, ruan connector/transaction reference, normalizon Wh/kWh dhe W/kW, refuzon matje të pavlefshme dhe shmang numërimin e matjeve për fazë bashkë me totalin. idTag mbetet shprehimisht i paverifikuar.

`EVCharging.OcppReplay` provon të njëjtën rrjedhë offline me tarifë fixture dhe matje të përsëritur. Nuk është server WebSocket dhe nuk lidh hardware.

## Çfarë kërkon rishikim për integrimin real

1. **Identiteti:** socket-i pranon identifikuesin nga URL pa autentikim pajisjeje; idTag nuk lidhet me Driver. QR statike identifikon pikën, jo pronarin e transaksionit.
2. **Tabelat:** duhen pajisja dhe connector si entitete më vete, mapping transaksioni, snapshot çmimi dhe matje decimal. SQLite aktual ruan regjistrat si REAL dhe nuk ruan histori statusesh për connector.
3. **Idempotenca:** Start i ridërguar mund të hapë rresht tjetër. Duhet inbox persistent me identitet mesazhi dhe përgjigje të ripërdorshme.
4. **Reconnect:** transaksionet aktive ruhen në memorie. Lidhja e vjetër mund të heqë regjistrimin e një lidhjeje të re me të njëjtën ID; pronësia e lidhjes duhet kontrolluar.
5. **Siguria REST:** API_KEY bosh e lë REST-in pa kontroll çelësi. Identifikuesit dhe payload-et DataTransfer shkruhen në log.
6. **Kosto:** price_per_kwh përcillet te objekti, por nuk përdoret për llogaritje/ruajtje të kostos. Monedha/tarifa në config nuk është rregull i konfirmuar biznesi.
7. **Scope:** remote start/stop/reset/unlock në Python nuk përfshihen automatikisht në aplikacionin tonë.

## Informacioni i dobishëm nga testi i radhës

Marka/modeli dhe versioni OCPP; një shkëmbim i redaktuar Boot/Status/Start/Meter/Stop; numri i lidhësve; sjellja pas shkëputjes; si krijohet/verifikohet idTag; mënyra e autentikimit të pajisjes. Kredencialet reale nuk duhen futur në dokumente ose log-e.
