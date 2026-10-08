# iOS/Android — zhvillimi dhe verifikimi, 8 tetor 2026

Klienti `mobile` u zgjerua mbi API-në ekzistuese EVCharging. Gjendja është **kod dhe teste lokale**, jo APK/IPA e provuar ose release i aprovuar. [Komandat dhe konfigurimet](../mobile/README.md).

## Përdorimi i Houss si referencë

U lexuan lokalisht konfigurimet Expo/EAS, API/auth/token store, push dhe historiku i problemeve të hartës iOS në `Houss/houss-mobile-main/houss-mobile-main`. Houss nuk u ndryshua; serveri, project IDs dhe kredencialet e tij nuk u përdorën për prova EVCharging.

Nga kjo referencë u morën parimet: SecureStore për tokenin native; mjedise të ndara për development/preview/production; gabime API të dallueshme; kanali Android para lejes së push; Expo project ID nga EAS config; pastrim i të dhënave pas daljes. API EVCharging ka token opak dhe nuk ka endpoint refresh, ndaj rrjedha JWT/refresh e Houss nuk u kopjua. Problemet historike të native maps iOS mbeten arsye për provë të hershme në pajisje të adapterit që do të zgjidhet.

## Sjellja e implementuar

| Pjesa | Sjellja | Evidenca lokale |
|---|---|---|
| Hyrja/regjistrimi | Gjuhë sq/en, konfirmim, përgatitje e API-së deri 120 s; POST dërgohet një herë | API/health unit tests; sintaksë |
| Sesioni | SecureStore device-only, skadim/401, refuzim Admin, anulim/pastrim pas logout; përgjigjet e një login-i të mëparshëm hidhen | Teste me storage/fetch të simuluar |
| Navigimi/layout | 3 tabs kryesore, safe area, fusha 48 pt/butona 44+ pt, keyboard avoidance, Back Android; përmbajtje me gjerësi të kufizuar | Kontroll kodi; pa rendering native |
| Pikat | Qytet/disponueshmëri/fuqi nominale, status Unknown/fallback, shënim kur shfaqet gjendja e fundit, Maps i telefonit | Filtra dhe URL të testuara |
| Karikimi | Zgjedhje lidhësi, vetëm sesioni personal, matje reale/mungesa/afate, kosto, kohëzgjatje; polling 5 s vetëm në foreground | Teste privatësie, freskie dhe lifecycle; pa karikues/pajisje |
| Historiku | Kalendar native; 7/30 ditë/6 muaj/custom; pika/energji/monedhë; kolonat përzgjedhin periudhën; 8 aktivitete/faqe | Teste shared + komponent me kontrolle native të simuluara |
| Profili | Kapaciteti kWh nuk humbet; versioni përditësohet pas ruajtjes; rifreskimi rikthen draft-in | Test payload/validimi; sintaksë |
| Push | Android channel para lejes; iOS provisional; aktivizimi/refuzimi; cold-start response hap inbox-in | Teste API/permission; cold-start routing vetëm kontroll kodi |
| Paketimi | Ikona EVCharging, splash, dev client, simulator, APK preview/AAB production, mbrojtje konfigurimi | Teste config/PNG; pa Metro export ose native build |

39 teste mobile kaluan; 169 teste web dhe typecheck web kaluan pas ndarjes së logjikës së historikut. Sintaksa mobile kontrollohet me compiler-in ekzistues të web-it kur varësitë mobile mungojnë. Kjo nuk provon përputhshmërinë me tipat/module-t reale React Native/Expo.

## Kufijtë konkretë

- `mobile/node_modules` dhe lockfile mungojnë. Instalimi offline dha `ENOTCACHED`; instalimi online është në pritje të lejes. Typecheck mobile dhe Metro export kryhen sapo të instalohen paketat; çdo mospërputhje duhet rregulluar para build-it.
- Mungojnë konfigurimet e vetë EVCharging: API HTTPS, Expo UUID, Firebase Android/FCM dhe Apple signing/APNs. Preflight iOS/Android ndalon kur mungojnë; nuk përdor Houss si fallback.
- Nuk është kryer rendering në emulator/telefon, build APK/IPA, provë push ose test OCPP nga telefoni. iOS build kërkon Mac ose EAS; Windows nuk e zëvendëson Xcode.
- Harta brenda aplikacionit dhe QR mbeten të papërfunduara. Lista dhe hapja e hartës së telefonit janë funksione të dobishme, por nuk plotësojnë kërkesën e hartës së integruar.
- Historiku bazohet në deri 100 seancat që kthen API, me faqezim lokal. Për histori më të gjatë duhet faqezim/filterim serveri. Nuk shpiket fuqi e matur nga energjia apo fuqia nominale e pikës.
- Connect/cancel për lidhësin 1 përdor vetëm rrjedhën ekzistuese të serverit kur `canConnect` e lejon. Për Card/Demo automatic shfaqen udhëzimet përkatëse. Nuk shtohen komanda RemoteStart/RemoteStop ose pagesa.

## Prova në pajisje para pranimit

| Mjedisi/rasti | Çfarë duhet provuar | Gjendja |
|---|---|---|
| iPhone 16 Pro Max | Safe areas, header, tabs, kalendar, forma me tastierë, përmbledhja/grafiku dhe detajet | Pa kryer |
| iPhone i vogël dhe Android 360/412 dp | Pa overflow, tekst më i madh, swipe i grafikut dhe zgjedhje të sakta | Pa kryer |
| iPad nëse përfshihet në release | Orientimi/ndarja e ekranit dhe përmbajtja e kufizuar në gjerësi | Pa kryer |
| Cold start API; lidhje e ndërprerë | Pritje e kuptueshme; retry; asnjë POST i dyfishuar; gjendje të vjetra të shënuara | Vetëm unit tests |
| Background/resume dhe logout | Nuk vazhdon polling; rifreskim në resume; asnjë e dhënë nga llogaria e mëparshme | Vetëm unit tests |
| Sesion real/simulator | Energji, fuqi dhe bateri sipas matjeve; pauzë/stop/defekt; privatësi me dy Driver | Vetëm unit tests |
| Push iOS/Android | Leje e refuzuar/ndryshuar, foreground/background/killed, APNs/FCM delivery dhe logout | Vetëm permission tests |
| Preview native | Splash/ikonë, instalim/nënshkrim dhe API e saktë HTTPS | Pa build |

Burime: [Expo 54](https://docs.expo.dev/versions/v54.0.0/), [SecureStore](https://docs.expo.dev/versions/v54.0.0/sdk/securestore/), [push](https://docs.expo.dev/versions/v54.0.0/sdk/notifications/), [DateTimePicker](https://docs.expo.dev/versions/v54.0.0/sdk/date-time-picker/), [EAS](https://docs.expo.dev/build/eas-json/), [Dev Client](https://docs.expo.dev/versions/v54.0.0/sdk/dev-client/), [Splash](https://docs.expo.dev/versions/v54.0.0/sdk/splash-screen/).
