# Gjendja e implementimit — 1 tetor 2026

Prioriteti i konfirmuar: **backend dhe web përpara mobile**. Pas avancimit të web-it, përdoruesi kërkoi nisjen edhe të aplikacionit iOS/Android.

## Çfarë funksionon në kod

| Pjesa | Implementimi | Verifikimi |
|---|---|---|
| Identiteti | Signup krijon vetëm Driver; email unik; password hash Identity; token rastësor i ruajtur si hash | Teste HTTP in-process për signup, hyrje, role të manipuluara, logout, çaktivizim dhe ndryshim fjalëkalimi |
| Autorizimi | Tre role, endpoint operatori dhe listë adminësh vetëm Superadmin; identiteti rivlerësohet në DB | Driver refuzohet nga endpoint-et administrative |
| Profili/cilësimet | Profil personal, sq/en, preferencë push, version për konfliktet, audit atomik me ndryshimin | Ruajtje gjuhe dhe konflikt 412 të testuara |
| Web | Hyrje Driver/Admin/Superadmin; Driver ka hartë OpenStreetMap që qendërzohet te pozicioni i lejuar nga shfletuesi, zgjedhje opsionale të 61 bashkive, renditje sipas largësisë dhe zoom me scroll; kohën e mbetur të karikimit kur ka vlerësim të freskët; hapje të pikës së zgjedhur në Google Maps ose Apple Maps; filtra e histori personale; menu llogarie me gjuhën dhe daljen; cookie HttpOnly/SameSite, BFF me Origin check dhe body limit | Typecheck, build dhe 16 teste web |
| Menaxhimi i pikave | Admin lexon listën dhe përmbledhjen; Superadmin krijon, ndryshon dhe arkivon pikat me audit, validim dhe version. Koordinatat mund të zgjidhen me klikim në hartë dhe scroll për zmadhim | 3 teste API për rolet/ndryshimet dhe teste të gjeometrisë së hartës |
| Mobile | Një kod Expo/React Native për iOS/Android: regjistrim/hyrje Driver, profil, cilësime, push scaffold, listë pikash me filtra dhe hapje në hartën e telefonit, histori personale | Sintaksa e 10 skedarëve TS/TSX kaloi; paketat Expo, typecheck i plotë dhe prova në pajisje mbeten të bllokuara |
| Harta dhe historiku | Lexim i pikave jo të arkivuara dhe i maksimumi 100 seancave të Driver-it të hyrë; host-i lokal krijon 10 pika dhe 6 seanca demo, përfshirë një pikë në Lushnjë me vlerësim ilustrues prej 40 minutash | Teste API për autorizimin, filtrimin e arkivimit, freskinë e vlerësimit dhe izolimin e historikut sipas përdoruesit |
| Njoftimet | Shtatë tekste sq/en, inbox personal, regjistrim pajisjeje, push queue me retries/receipts | 7 teste të radhës: gjuha aktuale, opt-out, logout, ndërrim llogarie, çaktivizim, retry i kufizuar dhe deduplikim |
| OCPP | WebSocket 1.6 për BootNotification, Heartbeat, Authorize, StatusNotification, StartTransaction, MeterValues, StopTransaction; inbox, sesione dhe matje persistente | Parser/replay dhe testet WebSocket → DB → stream, disa simulatorë, duplicate dhe status i vjetër |
| Domeni | Gjendje manuale me përparësi, freshness, arkivim i bllokuar kur është aktiv, snapshot çmimi, matje monotone dhe decimal cost | 8 raste domeni/validimi |
| PostgreSQL | EF mappings dhe pesë migrime, përfshirë tabelat OCPP/outbox | Modeli përputhet me migrimin e fundit; kufizimet dhe izolimi në PostgreSQL lokal nuk janë testuar |

Referencat e kodit: `backend/src`, `backend/tests/EVCharging.Tests`, `web/src`, `backend/tools/EVCharging.OcppReplay`.

## Vendime të këtij hapi

- Drejtuesi regjistrohet vetë. UI mobile u zgjerua me pikat dhe historikun, por nuk është provuar në iOS/Android.
- Push në telefon përdor gjuhën aktuale të llogarisë, sq ose en. Transporti Expo është i shkruar dhe i çaktivizuar si parazgjedhje. Për dërgim real duhen konfigurimi Expo/APNs/FCM dhe pajisje prove. Nuk është dërguar push real.
- Sesionet e hyrjes përdorin token opaque me 32 bytes rastësore, hash në DB dhe skadim absolut të konfigurueshëm (24 orë parazgjedhje). Refresh token rotation/JWT mbeten për më vonë. Web-i mban token-in në cookie HttpOnly; token-i nuk futet në JSON të kthyer nga BFF.
- Password change revokon sesionet dhe pajisjet push. Login ndryshon versionin e llogarisë, që krijimi i sesionit të konfliktojë me një ndryshim paralel fjalëkalimi.
- Push worker fillestar kërkon **një instancë aktive**; claim/lease për shumë instanca ende mungon. Rezultati i receipt është konfirmim nga provider-i, jo provë se përdoruesi e pa njoftimin.
- Gateway OCPP ka inbox persistent dhe retry të përgjigjeve. Në test, `TAG-001` lidhet me Driver-in që ka pritje aktive për pikën përkatëse; pa pritje përdoret `Demo:DriverEmail`. Autorizimi i përdoruesve/pajisjeve reale kërkon kontraktin e hardware.
- Të dhënat demo krijohen vetëm kur aktivizohet shprehimisht seed/simulator mode, ose në host-in lokal të testit. Koordinatat dhe çmimet e simulatorëve janë ilustrime; statusi dhe matjet vijnë nga mesazhet OCPP. Historia ruan çmimin e momentit si snapshot dhe nuk nënkupton pagesë të kryer.
- Vlerësimi i kohës së mbetur lidhet me një lidhës dhe shfaqet vetëm sa kohë pika është në karikim, vlerësimi nuk ka skaduar dhe burimi është i njohur. Për burimin e pajisjes kufiri i freskisë është 15 minuta. Në Lushnjë burimi është vetëm simulim demo; nuk ka ende marrje reale të kohës nga pajisja. Drejtuesi nuk fut kohë udhëtimi. Parashikimi nuk rezervon lidhësin. Pajisja duhet të ofrojë një vlerësim të verifikueshëm; energjia ose fuqia e matur nuk mjaftojnë për të premtuar minutat e mbetura.
- Udhëzimet për pikën e zgjedhur hapen nga Google Maps si parazgjedhje. Te `/driver/settings` përdoruesi mund të zgjedhë Apple Maps. Preferenca mbahet në shfletuesin lokal dhe nuk sinkronizohet me llogarinë në pajisje të tjera.
- Pikat OCPP të simulatorëve marrin status nga telemetria; shkëputja shfaqet Unknown. Pikat e tjera mbeten manuale/demo. Ndryshimi i pikave është kufizuar te Superadmin deri në implementimin e caktimeve/scope për Admin.

## Çfarë nuk është përfunduar

Caktimet dhe scope sipas pikës për Admin; çmimet me versionim dhe statistikat; provat me hardware real, autorizimi real Driver–sesion dhe QR; OCPP 2.0.1 dhe komandat RemoteStart/RemoteStop; shpërndarja e eventeve në shumë instanca; audit read API/DB grants; rikuperim/verifikim email-i dhe konfigurimi production. Mobile kërkon hartë të integruar, QR, build-e native dhe prova në pajisje. Web-i ka faqe të veçantë karikuesi nga harta, pritje të ruajtur në DB, bypass QR për simulatorët dhe matje private të karikimit. Pauza mban të njëjtin sesion. SoC shfaqet vetëm kur raportohet; nuk shtohen objektiva manualë, kapacitet baterie apo ETA sintetike.

Faqja e përmbledhjes llogarit pikat nga API-ja dhe i shënon qartë të dhënat demo. Nuk paraqet histori ose telemetri sintetike si sesione reale.

## Provat dhe kufijtë

- `dotnet test`: **66 kaluar, 0 dështuar**, përfshirë WebSocket/SSE, pronësinë e sesionit, pauzën/rinisjen, matjet me orën e përshpejtuar dhe dy Driver në pika të ndryshme. EF InMemory përdoret vetëm në test; ID-të numerike simulohen me interceptor sepse InMemory nuk zbaton identity të PostgreSQL në kolona jo-key. Nuk zëvendëson provën PostgreSQL për unique/FK/locking/rollback. Modeli EF përputhet me migrimin e fundit; SQL i migrimit të pritjeve u gjenerua offline dhe u kontrollua.
- `npm test`: **27 kaluar**. `npm run build` përfshin kontrollin e tipeve dhe kalon. Testet provojnë faqen e një karikuesi, SoC të raportuar, mungesën e përqindjeve të shpikura, pauzën dhe përmbledhjen; kanali live bashkon eventet dhe shmang mbivendosjen e snapshot-eve ose zëvendësimin e përgjigjes së re të lidhjes me një GET të vjetër. Pozicioni real i drejtuesit kërkon lejen e shfletuesit; refuzimi mban pamjen e pikave të regjistruara pa qytet të zgjedhur. Lidhjet Google Maps dhe Apple Maps përdorin koordinatat e pikës; hapja e aplikacioneve të jashtme nuk është provuar në pajisje.
- Replay offline: **20 kWh, 7.00 EUR, 1 matje e përsëritur e injoruar**. Çmimi dhe ID-të janë fixture; nuk janë tarifë ose pajisje reale.
- Host-i demo dhe web-i u lanë në `127.0.0.1:5202` dhe `127.0.0.1:3100`; të dy portet u konfirmuan në gjendje LISTENING. Host-i përdor InMemory dhe të dhëna sintetike që zhduken pas ndalimit. Pllakat OpenStreetMap ngarkohen nga shfletuesi kur hapet harta; përdoruesi e autorizoi këtë. Mjeti i shfletuesit refuzoi hapjen e localhost-it duke raportuar leje të refuzuar. Nuk u krye test vizual ose klikim end-to-end nga agjenti.
- Docker ekziston në PATH, por daemon-i lokal refuzoi aksesin. Nuk u ndryshuan lejet/politikat e laptopit. Nuk u krijua databazë reale.
- Paketat .NET/Next u morën nga cache-i ekzistues; kopjet dhe build-et janë brenda projektit. Versionet bazë nuk janë verifikuar kundrejt advisories aktuale. Kontrolli i sigurisë online mbetet para release.
- Një build i parë Next tentoi riparim automatik online të lockfile-it dhe u bllokua. Wrapper-i lokal tani çaktivizon këtë riparim dhe telemetrinë. Build-i pasues kaloi pa atë tentativë.
- Nisja e parë e SDK .NET raportoi krijimin e një certifikate zhvillimi. Nuk u krye komandë trust/remove. Komandat pasuese e çaktivizojnë gjenerimin. Host-i i provës logon ende një tentativë të ASP.NET DataProtection për të shkruar çelësa në AppData, të cilën mjedisi e bllokon; API-ja mbetet aktive. Ky konfigurim duhet zgjidhur para përdorimit real. Një provë me dosje lokale çelësash u kthye mbrapsht dhe la vetëm një dosje bosh të injoruar te `.local/test-dp-keys`.
- `npm install` në mobile u autorizua nga përdoruesi, por registry ktheu `EACCES`. Komanda u ndal; nuk u instaluan paketat Expo. U kontrollua vetëm sintaksa e 10 skedarëve TypeScript/TSX me TypeScript-in lokal të web-it.

## Rendi i ardhshëm

1. PostgreSQL i izoluar dhe test migrimi nga zero; bootstrap i Superadmin.
2. Caktimet/scope i Admin, lidhësit e detajuar dhe çmimet me versionim; verifikim vizual i menaxhimit të pikave.
3. Sesione/telemetri persistente dhe dashboard nga të dhëna reale; prova OCPP me kolegun.
4. Verifikim vizual/funksional i web-it dhe forcim për release.
5. Instalimi i paketave Expo, typecheck/build dhe provat në iOS/Android, pastaj QR/monitorimi me pajisje reale.
