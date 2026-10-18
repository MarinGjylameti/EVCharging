# Changelog

Historiku i ndryshimeve sipas commit-it, me hyrjet më të reja në fillim.
Datat, hash-et dhe titujt vijnë nga Git; përmbledhjet bazohen në ndryshimet e skedarëve.
Përmendja e testeve të shtuara nuk nënkupton që ato janë ekzekutuar.
Ndryshimet në panele të jashtme nuk regjistrohen si commit-e.

## Unreleased

### 2026-10-18

- Korrigjon intervalin e rilidhjes OCPP: përfundon ruajtjen e shkëputjes në DB dhe liron regjistrimin aktiv përpara dërgimit të Close, si për mbylljen nga pajisja ashtu edhe për mbylljet nga serveri.
- Çdo regjistrim aktiv ka pronarin e vet dhe lirim vetëm një herë; pastrimi i vonuar i lidhjes së vjetër nuk mund të heqë lidhjen e re. Refuzimi HTTP 409 ruhet për një lidhje tjetër aktive dhe pronësia lirohet edhe kur ruajtja, upgrade-i ose pastrimi dështojnë.
- Shton teste regresioni me sinjale të kontrolluara, timeout dhe cleanup për rendin e ruajtjes/Close/rilidhjes, Heartbeat pas rilidhjes, refuzimin e lidhjes së tretë dhe rrugët me përjashtime. Ruan testin ekzistues të varianteve të URL-së dhe logeve; TestMode, simulatori dhe konfigurimi direct/embedded/external nuk ndryshojnë.
- Verifikimi: build-i .NET me një worker të MSBuild-it kaloi me 0 gabime/paralajmërime; kaluan 139 raste backend me ekzekutuesin lokal brenda procesit, pa dështime ose anashkalime. Përfshihen të 14 rastet e reja, testi ekzistues i URL-ve/logeve dhe mbulimi për TestMode, simulatorin e diagnostikimin. Kaluan CSharpier dhe `git diff --check`.
- Kufijtë: VSTest u ndërpre pas 90 sekondash pa u lidhur me testhost-in; rezultatet e mësipërme janë nga ekzekutuesi rezervë i miratuar. Ruajtja/rilidhja u testua me InMemory dhe transport të kontrolluar/TestServer; nuk u ekzekutuan PostgreSQL, E2E ose prova fizike, dhe nuk u nis aplikacion publik, tunel, Docker, migrim apo deploy. Logu CI i dhënë nga përdoruesi konfirmonte 312 teste të kaluara dhe HTTP 409 në testin e varianteve të URL-së në `e6384c6`.

## 2026-10-16

### `e6384c6` — Add 3 versions for testing with actual charger

- Përgatit tri prova OCPP me të njëjtin kod: hyrjen direkte në Render, Quick Tunnel brenda API-së dhe connector-in e jashtëm si alternativë të fundit. `EVCHARGING_Tunnel__Mode=direct|embedded|external` është i pavarur nga TestMode dhe ruan hyrjen direkte e autorizimin fizik në çdo variant.
- Përfshin binarin cloudflared 2026.9.0 në imazhin e API-së; nis procesin embedded vetëm pasi API-ja ka nisur, përcjell loget/linkun në Render, riprovon dështimet pa ndalur API-në dhe përfundon procesin fëmijë gjatë ndalimit të host-it. Ruan HTTPS, Host/SNI, verifikimin TLS dhe origjinën publike të njëjtë me connector-in e veçantë për krahasim të kontrolluar.
- Shton Blueprint të veçantë Quick Tunnel për provën e tretë me pagesë, dhe një gjenerues PowerShell pa akses rrjeti për konfigurimin e backend-it, simulatorit dhe URL-së fizike. Udhëzuesi dokumenton rendin e provave, rikthimin, kartën, rilidhjen dhe ndryshimin e hostname-it pas rinisjes; kalimi bëhet me variabla/deploy dhe URL të re në pajisje.
- Verifikimi: build-i .NET kaloi pa gabime/paralajmërime; kaluan 105 raste backend në ekzekutimin fillestar dhe riekzekutimin e 24 rasteve të tunelit me ekzekutuesin lokal brenda procesit. Provat përfshijnë pritjen e nisjes së API-së, riprovimin, pavarësinë nga TestMode, HTTP readiness pas dështimit të connector-it dhe ndalimin/pastrimin real të një procesi lokal fëmijë me përcjelljen e URL-së nga stderr. Kaluan 28 kontrolle PowerShell të planit të rrugëve dhe 5 teste web për konfigurimin/lidhjen e simulatorit; u verifikuan përputhja e komandës/imazhit të connector-it, CSharpier dhe `git diff --check`.
- Kufijtë: Docker engine nuk ishte i arritshëm nga ambienti lokal, ndaj imazhi me cloudflared nuk u ndërtua/nis këtu. Bash mbetet i bllokuar nga lejet Windows. Nuk u krijua tunel publik ose shërbim me pagesë dhe nuk u krye push, deploy apo provë e re me karikuesin fizik.
- Pajton commit-in `b1063ca` me historikun më poshtë.

### `b1063ca` — Add tunel on render

- Shton flag-un qendror `EVCHARGING_Features__TestMode`, OFF si parazgjedhje, dhe një endpoint të autentikuar për aftësitë e ndërfaqes. Konfigurimi vjen nga backend-i; web-i kalon në OFF nëse nuk mund ta lexojë.
- Me OFF bllokon simulatorët dhe lidhjen demo, fsheh pikat/historikun/sesionet demo nga API-ja dhe analizat, dhe heq diagnostikimin e Driver-it si nga përgjigjet HTTP ashtu edhe nga eventet live. Loget operative dhe menaxhimi normal mbeten të mbrojtura sipas roleve; loget e aplikacionit mbeten vetëm për Superadmin.
- Ruan komunikimin OCPP dhe karikimin fizik me kartë. Caktimi pa kartë lejohet vetëm në testim; caktimet e vjetra pa kartë nuk autorizojnë ose marrin pronësinë e karikimeve të reja kur flag-u është OFF. Historiku dhe pronësia ekzistuese ruhen.
- Pastron udhëzimet e testimit nga pamja normale web, kërkon kartën në formularin e caktimit dhe mbron hyrjen direkte te simulatori. Mobile merr të njëjtat kufizime nga API-ja.
- Kushtëzon seed-in demo me flag-un; flag-et e vjetra të simulatorit/seed-it nuk e riaktivizojnë testimin kur master-i është OFF. Dokumenton konfigurimin, rinisjen, provën e koordinuar dhe kufijtë e gatishmërisë për prodhim.
- Verifikimi i këtij ndryshimi: build-i .NET kaloi; 273 raste backend kaluan me ekzekutuesin lokal brenda procesit, pas korrigjimit të dy fixture-ve dhe riekzekutimit të 72 rasteve të prekura. 16 raste me varësi të jashtme u anashkaluan. Kaluan 178 teste web, 39 teste mobile, TypeScript web dhe formatimi CSharpier. Provat përfshijnë OCPP fizik me kartë kur flag-u është OFF, bllokimin e autorizimit demo dhe filtrimin HTTP/live.
- Kufizimet lokale të verifikimit: VSTest nuk komunikoi me testhost-in; build-i i plotë Next dhe nisja e Bash u bllokuan nga lejet e ambientit Windows. Testet shell të përditësuara nuk u ekzekutuan. Typecheck-u i plotë mobile nuk u krye sepse varësitë lokale mobile mungojnë. Nuk u krye provë e re me pajisjen fizike ose deploy.
- Përgatit një Blueprint opsional për cloudflared si Background Worker në Render dhe konfigurimin shembull të një tuneli të emërtuar, me të njëjtën origjinë HTTPS, Host/SNI dhe verifikim TLS si prova krahasuese.
- Dokumenton domain-in e nevojshëm, worker-in me pagesë, token-in sekret, kalimin gradual të karikuesit dhe simulatorit, si dhe verifikimin e rilidhjes dhe karikimit. Skedarët janë për konfigurim të ardhshëm; nuk aktivizojnë shërbime ose DNS.
- Verifikimi: kaluan leximi JSON, 9 raste të filtrimit të rrugëve, përputhja HTTPS/Host/SNI dhe parametrat TLS/HTTP të origjinës, si dhe `git diff --check`. Blueprint-i u rishikua kundrejt dokumentacionit zyrtar; validimi me CLI dhe nisja e container-it nuk u kryen. Nuk u krijua shërbim, domain ose deploy.
- Pajton commit-in `a24565d` me historikun më poshtë.

## 2026-10-15

### `a24565d` — Add cloudflare simulator fix 2

- Shton gjurmim te Worker-i OCPP me fazat e marrjes së kërkesës dhe përgjigjes nga Render, statusin dhe CF-Ray; përgjigjet jashtë upgrade-it shënohen me versionin dhe fazën e proxy-t për të dalluar burimin e refuzimeve.
- Ruan objektin origjinal të përgjigjes 101, socket-in dhe nënprotokollin; fushat e gjurmimit të shtuar nuk regjistrojnë kredenciale, cookies, query strings, payload-e OCPP ose përmbajtjen e gabimeve. Kjo nuk ndryshon metadatat automatike të logeve të platformës.
- Aktivizon Workers Observability në konfigurimin Wrangler dhe shton një skript për provën krahasuese Tunnel → i njëjti backend Render, me Host/SNI të përputhur dhe verifikim TLS aktiv. Skripti kërkon cloudflared ekzistues dhe nuk instalon mjete.
- Verifikimi: kaluan 8 teste të Worker-it, kontrolli i sintaksës JavaScript dhe PowerShell, si dhe `git diff --check`. Testet përdorin upstream të simuluar; nuk u krye deploy, nisje tuneli ose provë e re me karikuesin fizik. U pajtua commit-i `1407712` me historikun.

### `1407712` — Add cloudflare simulator fix

- Drejton WebSocket-in e simulatorit të integruar demo përmes Cloudflare, që prova të përdorë të njëjtën hyrje publike si karikuesi fizik. Kërkesat HTTP të aplikacionit vazhdojnë te API-ja e konfiguruar.
- Ruan lidhjet lokale të zhvillimit dhe endpoint-et e instalimeve të tjera; shton konfigurimin opsional `EVCHARGING_SIMULATOR_WS_BASE_URL`, me validim të adresës dhe pa fallback të heshtur në rast konfigurimi të gabuar.
- Shton teste për përzgjedhjen e endpoint-it dhe rrjedhën nga faqja e simulatorit deri te hapja OCPP 1.6 dhe BootNotification.
- Verifikimi: kaluan 173 teste web dhe kontrolli TypeScript; testi i lidhjes përdor një WebSocket të simuluar, jo një lidhje live. U pajtuan të 44 hyrjet e historikut me hash-et dhe titujt në Git dhe `git diff --check` kaloi.

## 2026-10-09

### `3472de8` — Add changelog history

- Shton këtë changelog me përmbledhje për të 43 commit-et ekzistuese.
- Lejon gjurmimin e `CHANGELOG.md` në Git dhe e lidh nga README-ja.

### `03d315c` — Test CloudFlare 3

- Shton Worker-in Cloudflare që përcjell lidhjet OCPP WebSocket dhe kontrollin e gatishmërisë te backend-i në Render.
- Ruajtja e përgjigjes origjinale HTTP 101 mban socket-in dhe nënprotokollin OCPP; shtohen konfigurimi Wrangler dhe teste për përcjelljen, rrugët e lejuara dhe gabimet e backend-it.

### `0f80da4` — Test CloudFlare 2

- Përcjell header-in Host si adresë lokale në Cloudflare Tunnel, që backend-i lokal ta pranojë kërkesën.
- Shton test integrimi për filtrimin e Host-it dhe pranimin e BootNotification përmes adresës lokale të origjinës.

### `86be681` — Test CloudFlare

- Shton launcher-in Windows që përgatit mjetet portative, ndërton projektin dhe nis backend-in, UI-n dhe Cloudflare Tunnel për prova lokale.
- Shton kontroll publik OCPP me BootNotification, shfaqjen e linkut WSS, ruajtjen e logeve, ndalimin e proceseve të provës dhe kontrolle offline të skripteve.

### `905b2c5` — UI refactor to support mobile use 3

- Nxjerr dokumentacionin lokal jashtë gjurmimit në Git dhe shton rregullat përkatëse të përjashtimit.
- Thjeshton README-n publike në një përshkrim bazë të projektit dhe përbërësve të tij.

## 2026-10-08

### `e8cd228` — UI refactor to support mobile use 2

- Përmirëson rrjedhat native të aplikacionit mobile: autentikimin, stacionet, karikimin, historikun, profilin dhe cilësimet; shton rifreskimin kur aplikacioni kthehet në plan të parë.
- Shton asetet dhe konfigurimin e build-it mobile, kontrolle të publikimit dhe teste për API-n e rrjedhat native.
- Përbashkon llogaritjet e historikut dhe përmirëson filtrat, grafikun dhe paraqitjen e historikut në web; përditëson kufirin e ruajtjes së logeve diagnostikuese.

### `e86c352` — UI refactor to support mobile use

- Shton stilizimin për ekranet mobile, menunë responsive dhe përshtatjen e tabelave e faqeve operative në ekran të vogël.
- Shton teste për navigimin dhe përshtat paraqitjen e menusë së përdoruesit.

## 2026-10-07

### `59664d3` — minor refactor

- Shton infrastrukturën Playwright dhe skenarë E2E për autentikimin, karikimin dhe rikuperimin e lidhjes në kohë reale.
- Përmirëson panelin e simulatorit, trajtimin e llogarisë së tij, raportin e provës dhe paraqitjen e grafikut të historikut; përditëson logimin OCPP dhe testet përkatëse.

### `045ffa6` — minor refactor

- Pasuron loget OCPP me lidhjen te mesazhi, sesioni, stacioni dhe lidhësi; ul nivelin e logimit të MeterValues në Debug.
- Përmirëson trajtimin e endpoint-eve WebSocket/SSE dhe rikuperimin e të dhënave operative pas ndërprerjeve; shton teste për diagnostikimin, privatësinë dhe rikuperimin.

### `2fafc8d` — Add UI graphs

- Shton API-n dhe llogaritjet e analitikës operative, periudhat kalendarike dhe kërkimet përmbledhëse.
- Shton grafikun e trendit dhe zgjedhjen e periudhës në dashboard; përmirëson filtrat e logeve e paraqitjen e sesioneve dhe zgjeron testet e analitikës.

### `8582c8b` — Add UI fixes

- Përshtat faqet e autentikimit për ekranet e ulëta dhe mobile, me hapësira dhe madhësi që ndjekin lartësinë e ekranit.
- Përditëson testet e migrimeve PostgreSQL.

### `2a5c442` — Add UI build fixes

- Shton krijimin e të dhënave të vjetra për testimin e migrimeve dhe teste për këtë përgatitje.
- Përshtat testet PostgreSQL të migrimeve dhe karikimit për të përdorur këto të dhëna.

### `6361c78` — Add loggings

- Shton ruajtjen e logeve të aplikacionit në databazë, procesimin në sfond dhe API-n për leximin e historikut.
- Pasuron diagnostikimin HTTP/OCPP dhe pajtueshmërinë e rrugëve të karikuesit; shton host diagnostikues dhe teste për loget e ruajtura e URL-të OCPP.
- Përmirëson filtrimin dhe leximin e logeve në dashboard.

## 2026-10-06

### `965cc91` — Add fixes 9

- Qartëson udhëzimet për ID-në që pajisja shton në URL, zbulimin automatik të lidhësve dhe lidhjen e mëvonshme me stacionin e Driver-in.
- Dokumenton që prova e lidhjes nuk kërkon regjistrim paraprak të pajisjes ose migrim të ri për zbulimin automatik.

### `908deb4` — Add fixes 8

- Shton zbulimin automatik të pajisjeve fizike dhe lidhësve të raportuar, duke lejuar shoqërimin e mëvonshëm me një stacion.
- Përshtat procesimin OCPP, menaxhimin e pajisjeve dhe testet për pajisjet e zbuluara dhe transaksionet e tyre.

### `7f174b8` — Add fixes

- Shton diagnostikimin e kërkesave HTTP dhe lidhjeve OCPP, API-n e logeve live dhe faqen e logeve në dashboard.
- Shton administrimin e identitetit të pajisjes dhe zgjerimin e lidhësve fizikë, me teste për këto rrjedha.

### `154fd13` — Add fixes

- Përmirëson diagnostikimin e endpoint-it dhe gateway-t OCPP, si dhe udhëzimet për provën me karikues fizik.

## 2026-10-05

### `611e9f5` — Add fixes

- Shton caktimin e Driver-it te karikuesi fizik, modelin e ruajtjes dhe migrimin përkatës.
- Përcjell caktimin në autorizimin OCPP, të dhënat e Driver-it dhe panelin e pajisjeve; shton teste për caktimin, privatësinë dhe rrjedhën e karikimit.

### `7a87b68` — Add fixes

- Përshtat hyrjen e karikuesve fizikë sipas identitetit në URL dhe përditëson menaxhimin e pajisjeve në API e UI.
- Përditëson modelin OCPP, testet e administrimit dhe udhëzimet e konfigurimit të karikuesit fizik.

## 2026-10-04

### `44f906f` — Add fixes

- Përmirëson sigurinë e endpoint-eve dhe audiencën e ngjarjeve operative në kohë reale.
- Shton migrimin për audiencën operative, teste për rikuperimin e outbox-it dhe udhëzimet e provës me pajisje fizike.

### `2307822` — Add admin logic

- Shton administrimin e lokacioneve, administratorëve, pajisjeve dhe lidhjes së karikuesve fizikë me stacionet.
- Shton modelet, migrimin, API-të, lejet dhe njoftimet operative në kohë reale.
- Shton seksionet përkatëse të dashboard-it dhe teste për administrimin, aksesin dhe ngjarjet operative.

### `e1cb6b4` — Add sign/up registration

- Shton regjistrimin e përdoruesve në web dhe përmirëson rrjedhën e autentikimit në web e mobile.
- Përmirëson trajtimin e ndryshimeve të njëkohshme të stacioneve dhe zgjeron testet e API-së, OCPP, push-it dhe rikuperimit të outbox-it.
- Shton workflow CI dhe skripte për mbulimin me teste të backend-it dhe web-it.

## 2026-10-03

### `56c85c0` — Add user permissions

- Përmirëson izolimin e të dhënave të karikimit sipas përdoruesit dhe sjelljen e simulatorit për llogari të ndryshme.
- Shton raportin e provës së karikimit dhe përmirëson paraqitjen e disponueshmërisë e diagnostikimit, me teste të privatësisë dhe nisjes së njëkohshme.

### `aa592e8` — Add UI changes 3

- Riorganizon paraqitjen e llogarisë, cilësimeve, navigimit dhe footer-it të Driver-it.
- Përditëson vlerësimin e kërkesave dhe dokumentacionin e statusit të implementimit.

### `f9702ec` — Add UI changes 2

- Shton skadimin e parashikimit publik të karikimit dhe përmirëson gjendjet e disponueshmërisë të stacioneve.
- Përshtat projeksionet OCPP, hartën dhe simulatorin automatik; shton migrimin e skadimit dhe teste të disponueshmërisë.

### `f22aae5` — Add UI changes

- Klikimi i stacionit në hartë ose listë e zgjedh atë dhe përditëson pamjen e hartës, në vend që të hapë automatikisht faqen e karikimit.

### `7272680` — Add simulator inside our project

- Shton faqen dhe panelin e simulatorit automatik brenda aplikacionit.
- Shton kapacitetin e baterisë në profil e sesion për vlerësimet e karikimit, migrimin përkatës dhe teste për kapacitetin, progresin dhe simulatorin.

### `6f8fc02` — Add refactoring 7

- Paraqet kohën e karikimit në minuta dhe sekonda dhe qartëson gjendjen e sesioneve të pauzuara.
- Përmirëson diagnostikimin e simulatorit për Transaction ID-në aktive dhe njësitë e pavlefshme të matjes së fuqisë.

### `56d6a55` — Add refactoring 6

- Riorganizon paraqitjen e orës së nisjes dhe kohës së kaluar të karikimit, duke përfshirë pauzat dhe formatimin e datës në kohën e Tiranës.
- Përditëson shpjegimin kur mungojnë të dhënat për vlerësimin e mbushjes së baterisë dhe testet e progresit.

### `1313d5b` — Add refactoring 5

- Përmirëson përgatitjen e nisjes së backend-it në Render dhe trajtimin e lidhjes me databazën.
- Shton teste për entrypoint-in dhe konfigurimin e lidhjes PostgreSQL.

### `9e75a67` — Add refactoring 4

- Shton skenarin demo të simulatorit, diagnostikimin e defekteve të karikuesit dhe migrimin për të dhënat diagnostikuese.
- Përmirëson matjet OCPP, projeksionet e stacioneve dhe rikuperimin e gjendjes live; zgjeron testet e regresionit të simulatorit.
- Shton paraqitjen e defekteve dhe diagnostikimit në faqen e karikimit të Driver-it.

### `deaba77` — Add refactoring 3

- Korrigjon migrimin e sesioneve të karikimit dhe zgjeron testet e migrimeve e karikimit PostgreSQL.
- Përditëson dokumentacionin e sesioneve dhe të dhënave në kohë reale.

## 2026-10-02

### `28a8f31` — Add refactoring 2

- Përmirëson ndarjen e orëve të matjes dhe marrjes së mesazheve, si dhe trajtimin e sesioneve, lidhësve e matjeve OCPP.
- Shton migrimin për orët e matjes dhe përmirëson rikuperimin e pamjes live, me teste për sesionet, PostgreSQL dhe përditësimet në kohë reale.

### `c36bdb2` — Add refactoring

- Ndan sesionet e biznesit dhe gjendjen e lidhësve nga përpunimi i mesazheve OCPP; shton monitorimin e pajisjeve dhe kontrollin e caktimit të sesionit.
- Shton migrimin e sesioneve, përmirëson ruajtjen dhe ngjarjet në kohë reale dhe shton testet përkatëse.
- Shton kontrollin e gatishmërisë së API-së gjatë autentikimit dhe rikuperimin e snapshot-it live në web.

## 2026-10-01

### `92faa6e` — Add dedicated charger page and live session tracking

- Shton faqen e dedikuar të karikimit, lidhjen e Driver-it me stacionin dhe ndjekjen live të sesionit.
- Shton API-n, modelin dhe migrimin e lidhjeve të karikimit; përshtat matjet OCPP dhe shton teste për progresin e përditësimin live.

### `ec07c4a` — Fix live charging telemetry and add monthly history charts

- Përmirëson interpretimin e matjeve OCPP dhe të dhënat live të karikimit.
- Shton grafikun mujor të historikut dhe teste për matjet e paraqitjen e grafikut.

### `0753981` — Configure Simulator

- Shton endpoint-in WebSocket OCPP 1.6, trajtimin e mesazheve, transaksioneve dhe matjeve, si dhe përgatitjen e pajisjeve demo.
- Shton ngjarjet e stacioneve dhe përditësimet live të Driver-it, panelin e karikimit dhe teste për WebSocket-in e ngjarjet.

### `7c3b1e0` — Add OCPP database schema and refactor backend

- Ndan nisjen e API-së në përbërës për konfigurimin HTTP, pipeline-in, sigurinë, komandat e nisjes dhe health checks.
- Shton përgatitjen e llogarisë Superadmin dhe teste për konfigurimin e host-it e nisjen e saj.

### `b705ac8` — Add OCPP database schema and refactor backend

- Shton skemën e telemetrisë OCPP dhe ndan konfigurimet e ruajtjes sipas përgjegjësive të tyre.
- Riorganizon API-të e llogarive e stacioneve, kontratat, trajtimin e gabimeve dhe shërbimet e njoftimeve.
- Përditëson parser-in OCPP, migrimet, host-in e provës në browser, mjetin e riprodhimit të mesazheve dhe testet përkatëse.

### `3937dc0` — Keep desktop sidebar visible and refine mobile navigation

- Mban sidebar-in të dukshëm në desktop dhe përshtat navigimin për mobile.
- Përmirëson hartën e Driver-it, zgjedhjen e koordinatave të stacionit dhe llogaritjet e gjeometrisë së hartës.

### `a149ed4` — Allow demo API to wake before web requests time out

- Zgjat pritjen e kërkesave web dhe të login-it, që API-ja demo të ketë kohë të zgjohet para skadimit të kërkesës.

### `d9f5879` — Clarify Render Docker build paths

- Qartëson në udhëzimet e deploy-it rrugët e Dockerfile-it dhe kontekstin e ndërtimit për Render.

### `1817237` — Prepare EVCharging demo for Render deployment

- Shton Dockerfile-in, entrypoint-in dhe udhëzimet e deploy-it të backend-it në Render.
- Shton përgatitjen e të dhënave demo dhe konfigurimin e lidhjes PostgreSQL, me teste për të dyja.

### `3945af6` — Initial EVCharging implementation

- Krijon backend-in .NET, databazën dhe migrimet fillestare për llogaritë, stacionet, sesionet dhe njoftimet.
- Shton aplikacionin web Next.js dhe aplikacionin mobile React Native/Expo, me autentikim, hartë stacionesh, historik dhe cilësime.
- Shton kontratat e përbashkëta, interpretimin fillestar të telemetrisë OCPP, mjetet e provës lokale dhe testet fillestare.
