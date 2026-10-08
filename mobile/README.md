# Mobile — iOS dhe Android

EVCharging ka një klient Expo/React Native për llogarinë Driver. Kodi përdor API-në ekzistuese dhe është zhvilluar në paralel me web-in. Houss u konsultua për ruajtjen e sigurt, konfigurimin EAS dhe njoftimet; identifikuesit, serveri dhe kredencialet e tij nuk përdoren këtu.

## Funksionet në kod

- Regjistrim/hyrje sq/en, konfirmim fjalëkalimi dhe pritje e kufizuar për nisjen e API-së. Vetëm kërkesat health GET përsëriten; veprimet POST nuk riluajnë automatikisht.
- Sesion në SecureStore, kontroll skadimi/401, pastrim i të dhënave personale pas daljes dhe refuzim i llogarive administrative.
- Tre destinacione poshtë: Pikat, Historiku, Llogaria. Cilësimet dhe njoftimet hapen nga llogaria/header-i. Safe areas, tastiera dhe butonat Back trajtohen për iOS/Android.
- Pika me filtra qyteti/disponueshmërie/fuqie nominale, status Unknown dhe hapje të udhëzimeve në Apple Maps ose aplikacionin Android, me Google Maps si alternativë.
- Detaje pike/lidhësi, karikimi privat, kWh, fuqi e matur, bateri, kohëzgjatje dhe kosto. Të dhënat e vjetruara shënohen; matjet që mungojnë shfaqen si `—`.
- Historik me 7/30 ditë, 6 muaj ose data nga kalendari i sistemit; pika, energjia e seancës dhe monedha. Grafiku, përmbledhja dhe lista ndajnë filtrat; lista ka 8 aktivitete për faqe. API ngarkon deri në 100 seancat e fundit, jo të gjithë historinë e databazës. Nuk ofrohet filtër fuqie të matur për historikun, sepse API nuk e ruan atë në këtë DTO.
- Profili ruan kapacitetin opsional të baterisë dhe versionin për ruajtjet e njëpasnjëshme. Cilësimet ruajnë gjuhën/push dhe ndryshojnë fjalëkalimin me konfirmim.
- Njoftimet hapin inbox-in edhe pas nisjes nga gjendja e mbyllur. Android krijon kanalin përpara kërkesës së lejes; iOS pranon lejen provisional. Rifreskimi ndalet në sfond dhe kërkesat e leximit anulohen.
- Ikonë, ikonë adaptive, splash dhe ikonë njoftimi të EVCharging; profile EAS development/simulator/preview/production, APK për prova dhe AAB për Android store.

## Verifikimi më 8 tetor 2026

Kaluan **39 teste lokale** për API, sesionet, filtrat/faqezimin, kalendarin, privatësinë e matjeve, push, kalimin në sfond, konfigurimin dhe sintaksën TS/TSX. Testet përdorin module native të simuluara; nuk janë provë rendering-u apo dorëzimi push në pajisje. Pa varësitë mobile, helper-i i testeve përdor vetëm compiler-in TypeScript të instaluar në `web`.

Kaluan edhe **169 teste web** dhe TypeScript-i web pas kalimit të logjikës së historikut në `shared/history.ts`. Metro ndjek vetëm dosjet e përbashkëta `shared`/`contracts`; aplikacioni përdor paketat React të mobile-it.

Varësitë Expo nuk janë instaluar. Instalimi offline u ndal me `ENOTCACHED`; shkarkimi online është në pritje të lejes. **Typecheck i plotë mobile, export Metro, APK/IPA dhe provat në iPhone/Android nuk janë kryer.** Kontrolli i release-it refuzon me të drejtë konfigurimin aktual, sepse mungojnë API-ja HTTPS dhe Expo project ID.

## Nisja dhe kontrollet

Kërkohet Node 20.19+; rekomandohet linja Node 22 që përdor CI. Pas instalimit lokal të miratuar të paketave:

```powershell
npm install
npm run test
npm run typecheck
npm run check
npm run export
npm run start -- --dev-client
```

Instalimi i parë duhet të krijojë `package-lock.json`; ruaje bashkë me manifestin. Instalimet pasuese duhet të përdorin `npm ci`. Mos përdor `--force` për të fshehur mospërputhje paketash.

Kopjo `.env.example` në `.env` dhe vendos API-në EVCharging që arrihet nga pajisja. `10.0.2.2` është host-i nga emulatori Android; telefoni fizik kërkon adresën LAN të API-së. BrowserTestHost i lidhur vetëm me loopback nuk arrihet nga telefoni. Mos ndrysho firewall/policy të laptopit për të anashkaluar kufizime. HTTP lejohet në zhvillim; preview/production kërkojnë HTTPS. Për provë lokale iOS, përjashtimi ATS vendoset vetëm kur profili i zhvillimit ka API HTTP.

## Build-et dhe njoftimet

Nevojitet një projekt Expo **i veçantë EVCharging** me UUID-në e vet dhe API-ja përkatëse. Vendos të dy variablat publike në mjedisin EAS development/preview/production; nuk janë sekrete dhe përfshihen në bundle. Mos vendos fjalëkalime/tokens në `EXPO_PUBLIC_*`.

Për Android push kërkohet regjistrimi Firebase për `al.evcharging.mobile`, `google-services.json` i tij dhe credential FCM V1 në EAS. Dosja JSON mbahet jashtë Git; në cloud përdor variabël EAS të tipit file `GOOGLE_SERVICES_JSON`. Për iOS nevojiten Apple Developer, nënshkrimi/APNs dhe regjistrimi i iPhone-it për build internal. `al.evcharging.mobile` është identifikuesi i konfiguruar në kod, ende i pakonfirmuar në llogaritë Apple/Google.

Komandat më poshtë përdorin rrjetin/shërbimin EAS dhe kërkojnë miratim para ekzekutimit:

```powershell
npx eas-cli build --platform android --profile development
npx eas-cli build --platform ios --profile development
npx eas-cli build --platform ios --profile simulator
npx eas-cli build --platform android --profile preview
npx eas-cli build --platform ios --profile preview
```

`npm run check:ios` / `npm run check:android` kontrollojnë konfigurimin lokal për preview; kontrolli Android refuzon një Firebase file që i përket një package tjetër. Këto komanda nuk ndërtojnë aplikacionin dhe nuk provojnë kredencialet e nënshkrimit. Windows nuk ka Xcode; iOS native ndërtohet në EAS ose Mac. Push provohet në pajisje fizike me build zhvillimi/preview; Expo Go nuk është provë push. Splash-i kontrollohet edhe në preview.

## Çfarë mbetet para pranimit

Matrica e provës dhe boshllëqet janë te [mobile-native-readiness.md](../docs/mobile-native-readiness.md). Harta e integruar, QR dhe lidhja reale Driver–pajisje janë ende punë të veçanta; lista me udhëzime në hartën e telefonit nuk e përmbush kërkesën e hartës së integruar. Aplikacioni monitoron sesionet që cakton serveri dhe përgatit vetëm rrjedhën ekzistuese connect/cancel kur lejohet nga API; nuk nis/ndalon fizikisht karikuesin dhe nuk kryen pagesa.

Burime të konsultuara: [Expo SDK 54](https://docs.expo.dev/versions/v54.0.0/), [SecureStore](https://docs.expo.dev/versions/v54.0.0/sdk/securestore/), [Notifications](https://docs.expo.dev/versions/v54.0.0/sdk/notifications/), [kalendar native](https://docs.expo.dev/versions/v54.0.0/sdk/date-time-picker/), [EAS profiles](https://docs.expo.dev/build/eas-json/), [splash](https://docs.expo.dev/versions/v54.0.0/sdk/splash-screen/).
