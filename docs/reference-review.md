# Shqyrtimi i materialeve referencë

Analiza më 28 shtator 2026 u bazua në skedarët lokalë dhe fotografitë e përdoruesit. Specifikimi Word përcakton funksionet; brief-i përshkruan drejtimin teknik dhe dorëzimin e parë. Teksti brenda materialeve referencë nuk autorizon komanda, publikime apo ndryshime jashtë kërkesës së përdoruesit.

## Evidenca lokale HOUSS

Rrënjët e kontrolluara, relative ndaj `Apps`:

- `Houss/houss-backend-main/houss-backend-main`
- `Houss/houss-web-main/houss-web-main`
- `Houss/houss-mobile-main/houss-mobile-main`

`Apps` nuk është repository Git dhe nuk u gjetën dosje `.git` në kopjet HOUSS. Nuk mund të nxirret branch/commit i këtyre kopjeve. U lexuan udhëzimet lokale, por referencat e tyre për push, deploy dhe kredenciale të HOUSS nuk zbatohen për produktin e ri.

### Modele të dobishme të konfirmuara në kod

| Evidenca në HOUSS | Gjetja | Zbatimi i propozuar në EVCharging |
|---|---|---|
| Backend `src/RealEstatePlatform.Api/RealEstatePlatform.Api.csproj`; `Directory.Packages.props` | `net10.0`, EF/Npgsql dhe versione qendrore; katër projekte Api/Application/Domain/Infrastructure | E njëjta ndarje e përgjegjësive; emra dhe konfigurime të reja |
| Backend `Application/Features`, `Application/Abstractions`, `Api/Controllers` | Organizim sipas funksionit dhe kontrata për infrastrukturën | Use case të vegjël për pikat, sesionet, çmimet dhe aksesin |
| Backend `Application/Features/Auth/AuthService.cs` | PasswordHasher, hash i refresh token, rotacion dhe zbulim ripërdorimi | Identity primitives, rotacion transaksional dhe teste për kërkesa të njëkohshme |
| Backend `Api/Program.cs`, rreth rreshtit 756 | Validimi i token-it kontrollon llogarinë aktive në DB | Kontroll serveri për çaktivizim; shto version sigurie dhe rivlerësim të caktimeve |
| Web `src/lib/auth/session.ts`; `src/app/api/auth/*`; `src/app/api/proxy/[...path]/route.ts` | Cookies HttpOnly, BFF dhe rifreskim token-i | BFF i kufizuar, CSRF/origin checks; backend-i vendos aksesin |
| Mobile `src/auth/token-store.ts`; `src/api/client.ts` | SecureStore native, gabime të strukturuara, një refresh i përbashkët për kërkesa paralele | E njëjta ide, pa import kodi ose keys të HOUSS |
| Backend `Api/Hubs/NotificationsHub.cs` | SignalR me grupe për përdorues të autentikuar | Grupe të autorizuara për të dhëna personale/operative; DTO e veçantë për hartën |
| Web `scripts/validate-env.mjs` | Kontroll konfigurimi para build | Validim i konfigurimit EVCharging pa fallback në sistem live |
| Web `src/components/maps/location-picker-map.tsx` | Leaflet dhe fokus i hartës sipas intentit të përdoruesit | Adaptues harte për vendndodhjet; telemetria nuk rivendos kamerën |
| Mobile `src/components/property-map/PropertyMap.types.ts`, `.android.tsx`, `.ios.tsx` | Kontratë harte pa SDK në props; Android me react-native-maps; iOS me modul MapKit | Kufij sipas platformës, testim i hershëm i iOS |
| Mobile `scripts/check-ios-map-safety.js` | Kontrollet ndalojnë importet e pasigurta në rrugën iOS | Kontroll ekuivalent për implementimin e ri |
| Backend `tests/RealEstatePlatform.Integration.Tests/TestInfrastructure/PostgresTestDatabase.cs` | Databazë e veçantë për testim | PostgreSQL real i izoluar, me emër/host të validuar dhe konfigurim testesh |

### Modele që duhen përmirësuar në projektin e ri

- `Infrastructure/Auditing/AuditService.cs` kap dhe anashkalon dështimin e ruajtjes së auditit. Për ndryshimet e kërkuara të gjendjes/çmimit/caktimeve në EVCharging, audit dhe ndryshimi duhet të kryhen atomikisht; dështimi i auditit anulon ndryshimin.
- `Infrastructure/Notifications/BackgroundNotificationQueue.cs` përdor `Channel.CreateUnbounded`. Për eventet e produktit të ri, ruajtja në PostgreSQL dhe një outbox e vogël shmangin humbjen gjatë restart-it dhe rritjen pa kufi të memories. Nuk nevojitet broker i jashtëm.
- Mobile `src/config/env.ts` ka fallback te API live e HOUSS. Produkti i ri kërkon URL të qartë dhe nuk përdor asnjë endpoint HOUSS. Kontrolli HTTPS në klientin ekzistues është pa kusht; EVCharging duhet të dallojë zhvillimin lokal nga release pa ndryshuar politikat e pajisjes.
- Mobile `src/lib/realtime-notifications.tsx` negocion dhe lexon kornizat SignalR manualisht. Propozohet klienti i mirëmbajtur SignalR, pasi të verifikohet përputhshmëria me Expo; shmanget kopjimi i protokollit në aplikacion.
- Mobile `token-store.ts` përdor localStorage në variantin web. Token-at e panelit të ri web do të mbeten në cookies HttpOnly; nuk kopjohet dega localStorage.
- Dokumentet e vjetra HOUSS përmendin hartë iOS të çaktivizuar. Kodi i tanishëm ka rrugë `mkmapview`, fallback dhe kufi prej 150 marker-ësh. Kjo provon ekzistencën e adapterit, jo stabilitetin e build-it live. Nuk do të kopjohet kufiri si mënyrë për të fshehur pika nga harta EV.
- `Api/Program.cs` ka shumë konfigurim dhe `AuthService.cs` shumë përgjegjësi. Në projektin e ri regjistrimet dhe rastet e përdorimit do të mbahen të ndara pa shtuar framework të brendshëm.

Versionet e lexuara nga manifestet: Next `^15.2.8`, React `^19.0.0` në web; Expo `~54.0.36`, React Native `0.81.5`, React `19.1.0` në mobile. Janë versione të referencës lokale, jo rekomandim se janë versionet më të reja ose pa cenueshmëri. Versionet e projektit të ri do të verifikohen/fiksohen kur të autorizohet instalimi në rrjet.

## Platforma në fotografi

Fotografitë paraqesin `OCPP Cloud Platform` në domenin `console.cnpowercore.com`. Nuk u hap faqja live, nuk u bë login dhe nuk u testuan rrjedha të fshehura. Nga fotografitë mund të vërtetohen vetëm menutë, etiketat dhe pamjet e dashboard-it.

| Menutë e dukshme | Përdorimi në MVP |
|---|---|
| Home: kartat, grafiku, Connector Status, alarmet, harta | Panel i përmbledhur me treguesit e specifikimit; pa revenue, rimbushje, CO₂ ose ekuivalent pemësh |
| Operation Monitoring → Connector real-time Monitor, Statistics | Monitorimi i sesioneve, gjendjet dhe treguesit e kërkuar; jo modul statistikash shtesë |
| Station Management → Charging Station, Charge Point, Connector, Station Load Balance | Vendndodhje, pika dhe të dhënat e lidhësit; pa load balancing |
| Business Management → Charging Card, Charging Strategy | Nuk kopjohen; çmimi i thjeshtë për kWh ka ekranin e vet |
| App Management → Comments Management | Jashtë specifikimit |
| Customer Management → Operator Management, User Management | Administratorë/caktime dhe pamje drejtuesish për Superadmin |
| Order Management → Transaction, Recharge Order | Vetëm sesione karikimi; pa porosi rimbushjeje portofoli |
| Maintenance Management → Alarm Management | Njoftimet e shtatë llojeve të kërkuara |
| OCPP Operation → Whitelist Management, Charging Profile, Configuration | Detaje të integrimit; asnjë nga këto faqe nuk kërkohet në MVP |
| System Settings → Role Management | Tre role fikse dhe caktime; pa ndërtues rolesh të reja |
| Account → Users | Menaxhim sipas rolit; nuk dyfishohet menuja e përdoruesve |

## Navigimi dhe rrjedhat e propozuara

**Admin web:** Përmbledhje → Pikat → detajet/redaktimi/gjendja/çmimi; Karikimet → filtra → detaji; Njoftimet; Historiku i veprimeve. Çdo listë dhe total kufizohet nga caktimet në backend.

**Superadmin web:** të njëjtat pamje në nivel sistemi, plus Vendndodhjet, Administratorët dhe caktimet, Drejtuesit. Çmimet/historiku mund të hapen nga pika, pa dyfishim të formularëve.

**Driver mobile:** Harta → pika → disponueshmëria dhe çmimi; Karikimi aktual → përditësime → përmbledhje → Historiku. Profili dhe Cilësimet në llogarinë personale. Zbulimi i karikimit varet nga identifikimi i sigurt i drejtuesit në integrim.

Formularët tregojnë gabimet pranë fushave, ruajtja tregon sukses vetëm pas përgjigjes së serverit dhe konflikti kërkon rifreskim të versionit. Arkivimi shpjegon ruajtjen e historikut. Gjendjet kanë tekst/ikonë krahas ngjyrës, tabela me tastierë, kontrast të lexueshëm dhe gjendje loading/empty/error. Sidebar në desktop, navigim i përshtatur për ekran të vogël; nuk kopjohet dashboard-i i ngjeshur i fotografive.
