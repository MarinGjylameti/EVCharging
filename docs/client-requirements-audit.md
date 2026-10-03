# Kontrolli i kërkesave të klientit

Kontrolluar më 3 tetor 2026 mbi kodin lokal, me bazë commit-in `f9702ec`. Raporti përfshin edhe zhvendosjen e preferencës së hartës te Profili dhe makina në këtë hap.

**Dokumenti i klientit është përdorur si bazë, por funksionalitetet e Fazës 1 nuk janë përfunduar të gjitha.** Portali web i drejtuesit dhe rrjedha me simulatorë janë pjesët më të zhvilluara. Administrimi, caktimet, njoftimet operative dhe historiku i veprimeve mbeten të paplota.

Ky kontroll krahason dokumentin me implementimin aktual. Nuk konfirmon versionin e publikuar në Render ose sjelljen me karikues fizikë. Dokumenti Word origjinal është ruajtur pa ndryshime.

## Burimi dhe kuptimi i gjendjeve

Burimi është `Aplikacion pÃ«r Menaxhimin e Karikimit tÃ« Automjeteve Elektrike.docx`. Hash-i SHA256 i skedarit përputhet me `docs/reference/source-manifest.json`; dokumenti ka 308 paragrafë XML dhe nuk ka pjesë shtesë teksti. Kërkesat gjenden në `docs/reference/mvp-source.txt` dhe në matricën R01–R60 të `docs/requirements-matrix.md`.

- **Implementuar në web/demo:** funksioni ekziston në kod për portalin web ose rrjedhën me simulatorë. Kjo gjendje nuk konfirmon përfundimin e aplikacionit mobile ose integrimit me pajisje reale.
- **Pjesërisht:** ekziston një pjesë konkrete, por mungon një pjesë e kërkuar.
- **Mungon:** nuk ka rrjedhë funksionale që plotëson kërkesën.
- **Për saktësim:** dokumenti përmend një mundësi pa përcaktuar sjelljen konkrete.
- **Jashtë Fazës 1:** dokumenti e përjashton shprehimisht.

R61 dhe R62 në matricën ekzistuese vijnë nga brief-i teknik. Kërkesat U01–U04 dhe shtesat e fundit të përdoruesit kanë burime të veçanta.

## Krahasimi i kërkesave

| Kërkesat | Seksioni në Word | Gjendja aktuale | Çfarë ekziston dhe çfarë mbetet |
|---|---|---|---|
| R01 | 1 dhe 15 | Pjesërisht | Tre rolet dhe kontrollet bazë API ekzistojnë. Mungojnë caktimet e burimeve për Admin-in dhe monitorimi i plotë administrativ në kohë reale. |
| R02 | 1 dhe 7 | Implementuar në web/demo | Kostoja llogaritet nga kWh dhe tarifa e ruajtur për sesionin. Shfaqet pa kryer pagesë elektronike. |
| R03, R05 | 2 | Implementuar në web/demo | Harta dhe detaji japin vendndodhjen, emrin/ID, gjendjen, disponueshmërinë, lidhësin, fuqinë nominale dhe çmimin. Ndryshimet e rrjetit transmetohen te drejtuesi me SSE. |
| R04 | 2 | Pjesërisht | Pesë gjendjet ekzistojnë, me gjendje shtesë për informacion të panjohur. Ngjyra e mirëmbajtjes është gri e gjelbër; dokumenti kërkon të zezë. |
| R06 | 2 | Pjesërisht | Ka pritje/lidhje demo dhe nisje nga eventi i pajisjes. Ora e lidhjes fizike të automjetit nuk ekspozohet si fushë më vete; lidhja WebSocket e karikuesit nuk provon lidhjen e automjetit. Lidhja e sigurt e drejtuesit me një karikues real mbetet e papërfunduar. |
| R07 | 2 | Implementuar në web/demo | Gjendja, energjia, fuqia kur ka matje të vlefshme, kohëzgjatja dhe kostoja përditësohen live. Pauzat, defektet, shkëputjet dhe matjet e vjetruara trajtohen. |
| R08 | 2 | Pjesërisht | Stop-i mbyll sesionin; historiku ka orën e përfundimit, kohëzgjatjen, energjinë dhe koston. Pamja e karikimit të sapopërfunduar nuk shfaq veçmas orën e përfundimit. Regjistrat finalë të pavlefshëm kërkojnë verifikim. |
| R09 | 15 | Implementuar në web/demo | Historiku personal dhe detaji ekzistojnë; izolimi sipas drejtuesit është i testuar. Leximi aktual kufizohet në 100 sesione pa paginim. |
| R10–R12 | 3 | Implementuar në web/demo | Profili, të dhënat e automjetit, ndryshimi i fjalëkalimit dhe dalja ekzistojnë. Email-i shfaqet; nuk ka rrjedhë ndryshimi email-i. Dokumenti nuk kërkon shprehimisht ndryshimin e tij. |
| R13, R15 | 4 | Implementuar në web/demo | Sq/en ruhet në llogari. Preferenca Google Maps/Apple Maps gjendet te Profili dhe makina dhe ruhet në shfletuesin përkatës. |
| R14 | 4 | Pjesërisht | API ruan preferencën push dhe mobile ka kontroll për të. Web-i nuk ka kontroll për aktivizim/çaktivizim push; dërgimi real në telefon nuk është verifikuar. |
| R16–R17 | 4 | Për saktësim | Njësitë e matjes, privatësia dhe siguria përmenden me formulimin “Mund të përfshijë”. Këto nuk kanë ende cilësime të plota të dedikuara; opsionet konkrete kërkojnë përcaktim. |
| R18 | 5 dhe 15 | Mungon | Admin-i lexon listën e të gjitha pikave; nuk ka caktime ose kufizim sipas pikave të lejuara. |
| R19–R21 | 5 | Pjesërisht | Superadmin-i shton, ndryshon dhe arkivon pika, me validim dhe versionim. Admin-i ka vetëm lexim. Mungon fusha e serialit në formular/API të menaxhimit dhe lidhja me një vendndodhje të menaxhuar. Seriali i raportuar nga BootNotification nuk e zëvendëson këtë fushë. |
| R22–R23 | 6 | Pjesërisht | Superadmin-i ndryshon statusin dhe kufizimet manuale ruhen ndaj raportimeve të pajisjes. Mungojnë veprimi për Admin-in sipas caktimeve, arsyeja e ndryshimit dhe regjistrimi i gjendjes para/pas. |
| R24 | 7 | Implementuar në web/demo | Çmimi për kWh, llogaritja dhe tarifa e ruajtur për sesionin ekzistojnë. Ndryshimi i tarifës së pikës nuk ndryshon tarifën e sesionit ekzistues. |
| R25–R26 | 7 | Pjesërisht | Superadmin-i ndryshon çmimin aktual. Mungojnë historiku i çmimeve dhe regjistrimi i vlerës para/pas për çdo ndryshim. Veprimi i përgjithshëm StationUpdated nuk e plotëson historikun e kërkuar. |
| R27–R30 | 8 | Pjesërisht | Paneli ka numrin e pikave aktive, të lira dhe të qyteteve. Mungojnë të gjitha numërimet sipas statusit, sesionet aktive/të përfunduara, energjia totale, çmimet dhe aktivitetet e fundit. Paneli nuk ka abonim live. |
| R31–R33 | 9 | Mungon | Nuk ka monitor administrativ të sesioneve me drejtuesin, të gjitha fushat dhe filtrat sipas pikës, vendndodhjes, statusit, datës dhe drejtuesit. Historiku personal nuk e plotëson këtë kërkesë. |
| R34–R35 | 10 | Pjesërisht | Superadmin-i përdor të njëjtin panel bazë. Mungojnë statistikat globale për vendndodhjet, administratorët, drejtuesit, karikimet, energjinë dhe aktivitetin. |
| R36–R42 | 11 | Pjesërisht | Ka API vetëm për listimin e administratorëve dhe rol global Superadmin. Mungojnë shtimi, redaktimi, aktivizimi/çaktivizimi, heqja, caktimet te pika/vendndodhje dhe menaxhimi i aksesit. Nuk ka pamje për menaxhimin e tyre. |
| R43–R47 | 12 | Mungon | Nuk ka entitet, API ose pamje për shtim, redaktim, arkivim të vendndodhjeve dhe caktim pikash. Qyteti/adresa në një pikë nuk e plotësojnë menaxhimin e vendndodhjeve. |
| R48–R54 | 13 | Pjesërisht | Ekzistojnë shtatë lloje tekstesh sq/en, inbox, regjistrim pajisjeje, queue dhe transport push. Ngjarjet e karikimit dhe ndryshimeve të pikave nuk thërrasin EnqueueAsync; njoftimet operative nuk krijohen automatikisht. |
| R55–R56 | 14 dhe 15 | Pjesërisht | Ka audit që ndalon ndryshimin/fshirjen përmes EF. Profili/cilësimet ruajnë ndryshimet; veprimet e pikave ruajnë aktorin, rolin, ID, veprimin dhe kohën. Mungojnë para/pas për pikat, vendndodhjet dhe çmimet, si edhe API/UI e historikut sipas lejeve. |
| R57 | 15 | Pjesërisht | Superadmin-i ka akses global te pikat dhe listimi API i adminëve. Nuk ka pamjet e plota për drejtuesit, vendndodhjet, sesionet, caktimet dhe auditin. |
| R58 | 16 | Pjesërisht | Faza 1 përfshin të gjitha modulet e mësipërme. Nuk mund të quhet e përfunduar me boshllëqet aktuale. |
| R59 | 3 dhe 16 | Jashtë Fazës 1 | Pagesat, portofoli, bankat, faturimi elektronik dhe abonimet nuk janë funksionalitete që mungojnë nga kjo fazë. |
| R60 | 16 | Pjesërisht | Tarifa e ruajtur për sesionin dhe ndarja e moduleve përgatisin zgjerimin e ardhshëm. Shtimi i pagesave pa ndryshime strukturore nuk është verifikuar ende. |

## Boshllëqet që duhen mbyllur për Fazën 1

1. **Caktimet dhe lejet e Admin-it:** kufizim i leximit, ndryshimeve dhe përditësimeve live te burimet e lejuara; mundësi menaxhimi sipas dokumentit.
2. **Vendndodhjet dhe administratorët:** modelet, API-të, formularët, arkivimi, aktivizimi dhe caktimet.
3. **Panelet dhe monitorimi administrativ:** statistikat e plota, aktivitetet live, sesionet dhe të gjithë filtrat e kërkuar.
4. **Historiku i çmimeve dhe veprimeve:** vlerat para/pas, arsyeja e statusit manual dhe pamjet e historikut sipas lejeve.
5. **Njoftimet automatike:** lidhja e shtatë ngjarjeve me marrësit përkatës dhe provë reale e push në telefon.
6. **Plotësimi i pamjes së drejtuesit:** ora e lidhjes së automjetit, ora e përfundimit në përmbledhjen live dhe ngjyra e mirëmbajtjes.
7. **Integrimi dhe pranimi me pajisje reale:** identiteti i karikuesit dhe lidhja e sigurt me drejtuesin, me prova për lidhje, nisje, matje, pauza, defekte, shkëputje dhe përfundim.

## Kufijtë e integrimit dhe verifikimit

`OcppDeviceAuthentication` pranon vetëm identitete simulatorësh të aktivizuar shprehimisht dhe të lidhur me pika demo. Implementimi aktual nuk siguron kredencialet/profilin e një karikuesi fizik. Protokolli i simulatorit nuk mjafton për të deklaruar pajisjet reale të mbështetura.

Rrjedha demo lidh sesionin me drejtuesin përmes kërkesës së pritjes/claim-it demo. Verifikimi i pronësisë për përdorim real mbetet i nevojshëm.

Prova e fundit lokale e backend-it ka 114 teste të kaluara dhe 10 teste PostgreSQL të paekzekutuara, sipas `.local/test-results/public-forecast-full.trx`. Kjo provë mbulon funksionet e implementuara; nuk provon kërkesat që mungojnë, deploy-in ose hardware-in.

Për zhvendosjen e preferencës së hartës kaluan typecheck, tri testet ekzistuese të navigimit dhe build-i web. U hoq hyrja e veçantë Cilësimet nga menuja dhe footer-i; adresa e vjetër ridrejton te seksioni përkatës në profil. Menuja e telefonit përshtatet me tri hyrje.

Mobile ka kod fillestar Expo për profil, cilësime, listë pikash dhe historik. Nuk ka APK/IPA të verifikuar, hartë të integruar dhe monitorim live të karikimit të përfunduar si në web. Dokumentacioni i projektit regjistron prioritetin web përpara mobile; ky ndryshim prioriteti nuk e bën aplikacionin mobile të dorëzuar.

## Shtesat pas dokumentit të klientit

SoC, kapaciteti i përdorshëm i baterisë, vlerësimi deri në 100%, simulatori automatik, dhjetë pikat e testit dhe vlerësimi publik në hartë janë zhvilluar nga kërkesat pasuese të përdoruesit. Ato nuk mbyllin boshllëqet administrative të Word-it. QR mbetet rrjedhë e propozuar, me anashkalim demo gjatë provave.

## Referencat kryesore në kod

- Drejtuesi: `web/src/features/driver/driver-map.tsx`, `station-charging-view.tsx`, `charging-progress.tsx`, `history-view.tsx`; `backend/src/EVCharging.Infrastructure/Charging/DriverChargingService.cs`, `ChargerEventReceiver.cs` dhe komponentët e realtime.
- Rolet dhe pikat: `backend/src/EVCharging.Api/Security/ApiSecurityRegistration.cs`, `backend/src/EVCharging.Api/Endpoints/StationManagementEndpoints.cs`, `backend/src/EVCharging.Infrastructure/Charging/StationManagementService.cs`.
- Panelet: `web/src/app/dashboard/page.tsx` dhe `web/src/app/dashboard/stations/page.tsx`.
- Administratorët: `backend/src/EVCharging.Api/Endpoints/AccountEndpoints.cs`, `backend/src/EVCharging.Infrastructure/Identity/AccountService.cs`.
- Njoftimet: `backend/src/EVCharging.Infrastructure/Notifications/NotificationService.cs`; kërkimi në `backend/src` nuk gjeti thirrës të EnqueueAsync nga ngjarjet operative.
- Audit dhe modelet: `backend/src/EVCharging.Domain/Auditing/AuditEntry.cs`, `backend/src/EVCharging.Infrastructure/Persistence/ChargingDbContext.cs`.
- Pajisjet: `backend/src/EVCharging.Infrastructure/Charging/Ocpp/OcppDeviceAuthentication.cs`.
- Mobile: `mobile/README.md`, `mobile/App.tsx`, `mobile/src/StationsScreen.tsx`.

Matrica origjinale dhe raporti i mëparshëm i implementimit ruajnë gjendje nga hapa më të hershëm. Për vlerësimin aktual të plotësimit të Word-it përdor këtë raport.
