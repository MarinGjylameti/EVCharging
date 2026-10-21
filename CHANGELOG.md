# Changelog

Historiku i ndryshimeve sipas commit-it, me hyrjet më të reja në fillim.
Datat, hash-et dhe titujt vijnë nga Git; përmbledhjet bazohen në ndryshimet e skedarëve.
Përmendja e testeve të shtuara nuk nënkupton që ato janë ekzekutuar.
Ndryshimet në panele të jashtme nuk regjistrohen si commit-e.

## Unreleased

### 2026-10-21

- Plotëson pikat 4–6 të kontrollit: ndan dosjen e paketimit EAS nga burimi i projektit, me kontroll të rrugës reale edhe për junction/symlink dhe gabim përpara instalimit ose build-it kur ajo është brenda burimit. Skedarët e përkohshëm të compiler-it mbeten lokalë. Dokumenton konfigurimin opsional `EVCHARGING_EAS_TEMP_DIR`; nuk është nisur build i ri në cloud ose krijuar APK/IPA e nënshkruar.
- Shton `/driver/history/search` me filtra, përmbledhje dhe faqëzim në server për të gjithë historikun e përdoruesit. Përfshin periudhën/datat, pikën, energjinë, monedhën dhe zgjedhjen e kolonës së grafikut; izolon pronarin dhe të dhënat demo, ruan historikun e pikave të arkivuara dhe i llogarit kufijtë ditorë në Europe/Tirane me DST. Përmbledhja, grafiku dhe faqja lexohen në të njëjtën pamje transaksionale në PostgreSQL; grupimi bëhet në SQL dhe numri i kolonave mbetet i kufizuar edhe për intervale të gjata. Nuk ndryshon skemën.
- Lidh web-in dhe iOS/Android me historikun e faqëzuar: tetë seanca për faqe, totalet e plota sipas filtrit, anulim kërkesash, refuzim përgjigjesh të vonuara dhe riprovim pas gabimit. Historiku mobile ngarkohet kur hapet dhe rifreskohet vetëm në plan të parë, veçmas nga seksionet e tjera. Endpoint-i i mëparshëm ruhet për klientët ekzistues; karta e hartës web e emërton qartë pamjen e kufizuar në 100 seancat e fundit.
- Shton job të veçantë CI për instalimin e varësive mobile nga lockfile, TypeScript, testet dhe eksportet iOS/Android, me ruajtje të bundle-ve shtatë ditë. Përditëson dokumentimin e historikut, EAS, OCPP/SSE dhe kufijve të publikimit. Korrigjon paralajmërimin CSS për `align-items`. Pika 7, modeli i kartës/përdoruesit për karikuesit fizikë, shtyhet me kërkesë të përdoruesit dhe modeli aktual ruhet.
- Verifikimi i pikave 4–6: kaluan 201 teste web, 115 teste mobile, TypeScript-i në të dyja, build-i .NET pa gabime/paralajmërime dhe 79 raste backend me ekzekutuesin lokal brenda procesit. Përfshihen 13 raste të reja për historikun me HTTP TestServer, mbi 100 seanca, privatësi, faqe të qëndrueshme, filtra, DST dhe përkthim SQL nga Npgsql. Kaluan build-i production Next.js në një dosje të re verifikimi dhe eksportet Hermes iOS/Android. Dosja e zakonshme `.next` hasi gabim lejesh EPERM; konfigurimi i përkohshëm u rikthye pas build-it.
- Kufijtë e këtij verifikimi: 17 prova PostgreSQL u anashkaluan lokalisht, përfshirë provën e re të historikut; janë përfshirë në CI, i cili nuk u nis këtu. Profili i brendshëm UI kalon kontrollin e konfigurimit; profilet për dyqanet kërkojnë konfigurimin real të API-së dhe publikimit. Nuk u provua pajisje fizike, nënshkrim ose dorëzim në dyqane dhe nuk u krye deploy, commit ose push.
- Përditëson Next.js nga 15.2.8 në 15.5.28 dhe lockfile-in me sharp 0.35.5 për korrigjimet e sigurisë. Kufizon override-in te PostCSS i Next.js në 8.5.30; përditëson nanoid 3.3.20 dhe source-map-js 1.2.2. Raporti përfundimtar online `npm audit` i web-it kthen zero dobësi.
- Ndan kufizimin e hyrjeve në 15 tentativa/minutë për email të normalizuar dhe 300 kërkesa/minutë për IP lidhjeje, të konfigurueshme me `AuthLimits`. Login/regjistrimi ndajnë kuotën e email-it edhe kur ndryshon IP-ja; ndryshimi i fjalëkalimit përdor llogarinë e autentikuar. Përdoruesit e ndryshëm pas serverit web nuk ndajnë më kuotën e ulët prej 15 tentativash. Header-at Forwarded të klientit nuk anashkalojnë kufirin; përgjigjja 429 përfshin kodin dhe Retry-After. Dalja mbetet e disponueshme për revokimin e seancës. Kufijtë ruhen në proces, për secilën instancë API.
- Ngarkon veçmas pikat, historikun, njoftimet dhe seancat në iOS/Android. Seksionet e gatshme shfaqen menjëherë pas kontrollit të identitetit; një gabim ruan të dhënat e fundit të atij seksioni dhe ofron riprovim. Ngarkimi fillestar i dështuar nuk shfaqet si listë bosh. Shënon pikat/seancat e vjetruara, pastron të dhënat në ndërrimin e llogarisë dhe refuzon përgjigjet pas anulimit ose skadimit të seancës.
- Verifikimi: kaluan build-i .NET pa paralajmërime/gabime dhe 66 raste backend me ekzekutuesin lokal brenda procesit, përfshirë 8 raste të reja kufizimi me pipeline/endpoints HTTP në TestServer dhe validim konfigurimi. Kaluan 195 teste web, 113 teste mobile, TypeScript-i në të dyja, build-i production Next.js dhe eksportet Hermes iOS/Android. Build-i web ka një paralajmërim CSS ekzistues për align-items: end. Prova HTTP lokale e web-it u bllokua me EACCES; testet mobile simulojnë modulet native dhe nuk u provua PostgreSQL ose pajisje fizike. Nuk u krye deploy, commit ose push.
- Pajton commit-in `44261c5` me historikun dhe dokumenton konfigurimin e kufijve, override-in e sigurisë dhe ngarkimin e pjesshëm mobile.

## 2026-10-18

### `44261c5` — Fixes for ios and android map

- Qartëson bllokimin e lidhjes demo nga një pritje/seancë ekzistuese: API-ja kthen vetëm pronarit pikën/lidhësin përkatës. Web, iOS dhe Android ofrojnë Shko te pritja ose Shko te seanca; anulimi mbetet vetëm për pritjet pa nisje. Një konflikt i zbuluar ndërmjet rifreskimeve ngarkon destinacionin aktual. Pritjet e skaduara dhe seancat e përfunduara nuk mbajnë kartën e bllokimit. Hapja e kartës nuk nis/ndal karikim ose ndryshon pronësinë.
- Korrigjon riprovimin OCPP pas humbjes së përgjigjes së nisjes: një StartTransaction i ruajtur me sukses dhe i lidhur me transaksionin e vet kthen përgjigjen origjinale edhe pas rilidhjes mbi 24 orë. Ruan përputhjen e pajisjes, CALL ID dhe përmbajtjes; nuk ruan përgjithmonë refuzimet ose krijon seancë të dytë nga riprovimi i një nisjeje të përfunduar.
- Simulatori përpiqet të dërgojë StopTransaction edhe kur matjet finale, vlerësimi ose statusi Finishing dështojnë. Ruhet në memorie i njëjti ID, regjistër final, moment dhe arsye gjatë rilidhjes/riprovimit; rifillimi dhe nisja e re bllokohen sa kohë ndalimi pret konfirmim. Mbyllja/rifreskimi i faqes ende kërkon rikuperimin ekzistues demo nga diagnostikimi i serverit.
- Dokumenton arkitekturën, ndarjen mes lidhjes/statusit/seancës/pronësisë, rrjedhën e rikuperimit dhe kushtet e prodhimit. Ndalimi i munguar përgjithmonë nga pajisja kërkon procedurë operatori me evidencë; nuk shtohet mbyllje me hamendje, pagesë, remote stop ose regjistër i ri kartash. Konfigurimi aktual me një Driver/kartë të caktuar për pajisje mbetet për konfirmim me klientin.
- Në web, përfundimi/ndryshimi i pronësisë në pikën e mëparshme rifreskon pikën që po shikohet, pa futur aty seancën e një pike tjetër. Korrigjon rastin kur stream-i mbetej i lidhur por karta e bllokimit nuk hiqej pas ndalimit/anulimit nga një pajisje tjetër.
- Verifikimi i rikuperimit: kaluan 125 teste backend me ekzekutuesin lokal brenda procesit: 44 OCPP/API (përfshirë 4 regresione të reja) dhe 81 raste arkitekture, administrimi e rilidhjeje. Kaluan build-i serial .NET pa gabime/paralajmërime, 100 teste mobile, 195 teste web, TypeScript-i në të dyja dhe eksportet Hermes iOS/Android. Mbulohen edhe dy regresione për ndryshimet live nga pika tjetër; shtatë prova të reja mbulojnë dështimin e matjeve finale dhe humbjen/riprovimin e përgjigjes së ndalimit. VSTest ngeci pas zbulimit të testeve pa rezultate; PostgreSQL, pajisja reale dhe Android fizik nuk u provuan. Nuk u ndryshuan rekorde live, nuk u shtuan migrime/varësi dhe nuk u bë deploy, commit ose push.
- Përmirëson lëvizjen native të hartës në iOS/Android: pllakat dhe shenjat zhvendosen bashkë me transformim Animated, pa rindërtuar pemën React për çdo lëvizje brenda të njëjtit grup pllakash. Ngarkon pllakat e reja të dukshme sipas frame-it; përdor koordinatat e ekranit për të shmangur kërcimin nga matja e vonuar dhe ruan vazhdimësinë kur shtohet/hiqet gishti. Pinch-i mbetet i vazhdueshëm; lëshimi/anulimi ruan pamjen dhe rikthen scroll-in.
- Shton fokusin fillestar sipas vendndodhjes me `expo-location` 57.0.21, modul për versionet native të publikuara dhe Expo Go. Përfshin përdoruesin dhe pikën më të afërt kur nuk futen në pamjen lokale, edhe nga jashtë vendit. Shenja blu tregon vendndodhjen në hapje. Pas ndërveprimit, përgjigjet e vonuara, rifreskimet, filtrat dhe rikthimi nga ekranet e tjera nuk e ndryshojnë pamjen. Butoni i inkuadrimit përfshin të gjitha pikat e shfaqura dhe vendndodhjen.
- Kërkon vetëm leje vendndodhjeje gjatë përdorimit; një lexim ruhet vetëm në memorien e seancës së aplikacionit. Refuzimi, shërbimet e fikura, gabimi ose pritja mbi 12 sekonda lënë hartën e pikave funksionale. Konfiguron tekstin iOS dhe lejet Android, pa gjurmim në sfond ose dërgim koordinatash te API-ja. README shpjegon edhe varësinë nga pllakat e jashtme dhe deklarimin përkatës të privatësisë. Instalimi u përfundua nga terminali i përdoruesit pasi ambienti i agentit nuk arriti npm.
- Forcon ngjyrat e statuseve në mobile, zgjeron shenjat dhe shton legjendën kompakte; gjendjet e vjetruara ruhen gri. Riformaton menunë e poshtme me ikona të vizatuara, formë të rrumbullakosur, hije të lehtë dhe zgjedhje jeshile, duke ruajtur tre destinacionet dhe safe area në të dyja platformat.
- Shfaq kohën publike të mbetur te detajet e pikës së zgjedhur në mobile, përfshirë lidhësin dhe burimin e vlerësimit. Në listë shfaqet kur karta është e hapur. Përbashkon kontrollin e skadimit me web-in; nuk nxjerr të dhëna private të përdoruesit tjetër dhe nuk shpik minutazh kur matjet mungojnë ose janë të vjetruara. Sipas provës vizuale në iPhone, heq etiketat e minutave nga shenjat si në web ashtu edhe në mobile, që pikat e afërta të mos mbulohen.
- Verifikimi: kaluan TypeScript-i mobile/web, 97 teste mobile, 37 teste të fokusuara web dhe eksportet Hermes iOS/Android. Introspektimi native konfirmoi lejen iOS When In Use, mungesën e Always/background modes dhe lejet Android foreground me bllokim background/service location. Testet mbulojnë lëvizjen pa render për çdo hap, ndërrimin e gishtave, fokusin vetëm fillestar, rikthimin e kamerës, refuzimin/timeout-in e vendndodhjes dhe skadimin e vlerësimeve. Modulet native janë të simuluara; FPS dhe rrjedha përfundimtare e lejeve nuk u testuan në pajisje fizike Android. Nuk u krijua APK, deploy, commit ose push.
- Pajton commit-in `ae0d3bf` me historikun më poshtë.

### `ae0d3bf` — Fixes for ios and android map

- Në pamjen Lista të Pikave, kartat ruajnë detajet të dukshme dhe fshehin Hap karikimin/Hap në hartë kur ka disa rezultate. Prekja hap butonat vetëm te karta e zgjedhur; prekja e një karte tjetër mbyll të parën, ndërsa prekja e së njëjtës i fsheh përsëri. Me vetëm një rezultat butonat janë menjëherë të dukshëm, edhe pas filtrimit. Shton treguesin e hapjes dhe gjendjen e aksesueshme; veprimet mbeten jashtë zonës që hap/mbyll kartën.
- Përmirëson pinch-in native në iOS/Android: heq rrumbullakosjen në nivele të plota, ruan pikën gjeografike nën gishta dhe bashkon lëvizjet në një përditësim për frame. Pllakat e hartës shkallëzohen në mënyrë të vazhdueshme, por URL-të përdorin vetëm nivele të plota. Çaktivizon fade-in e imazheve në Android; përfundimi/anulimi i gjestit ruan pozicionin final dhe rikthen scroll-in e faqes. Sjellja e parazgjedhur e helper-it në web ruhet.
- Verifikimi: kaluan TypeScript-i mobile/web, 82 teste mobile, 8 teste të gjeometrisë dhe eksportet Hermes iOS/Android. Provat kontrollojnë zmadhime të vogla, pikën nën gishta, kufijtë e zoom-it, bashkimin e frame-ve dhe pozicionin pas lëshimit. Nuk u mat frame-rate në pajisje fizike; ngarkimi i pllakave mbetet i varur nga rrjeti. Nuk u shtuan varësi, APK, deploy, commit ose push.
- Pajton commit-in `d3a22eb` me historikun më poshtë.

### `d3a22eb` — Fixes for ios and android

- Riorganizon ekranin Pikat në iOS/Android: titull kompakt, kalim Harta/Lista, kontrolle mbi hartë dhe karta të përbashkëta me statusin, adresën, fuqinë, lidhësit e tarifën. Qyteti, fuqia dhe Vetëm të lira janë të fshehura fillimisht te Filtrat; zgjedhjet ruhen kur paneli mbyllet dhe mund të pastrohen.
- Prekja e një pike zgjedh/shënon pikën dhe zbulon detajet poshtë hartës. Navigimi i jashtëm bëhet vetëm me Hap në hartë, sipas preferencës së ruajtur. Hap karikimin çon te rrjedha ekzistuese e lidhjes/seancës. Kur ka vetëm një pikë, detajet hapen fillimisht; karta mund të mbyllet. Rifreskimi i statusit nuk detyron zhvendosje të faqes.
- Shton Shko te karikimi sipër navigimit mobile, vetëm për seancat e përdoruesit që raportohen Në karikim. Hap pikën dhe lidhësin e saktë; fshihet kur përdoruesi ndodhet aty, gjatë pauzës/shkëputjes/defektit, kur të dhënat nuk rifreskohen dhe pas përfundimit. Rikthehet me konfirmimin e rifillimit. Konfirmimi i ndalimit në ekranin e karikimit e heq menjëherë; një përgjigje më e vjetër e listës nuk e rikthen. Dalja/ndërrimi i llogarisë pastron seancat dhe refuzon përgjigjet e vjetra.
- Qartëson shkëputjen e karikuesit në web/mobile: ruan përqindjen e fundit të vlefshme, e shënon si të vjetruar me shirit gri dhe shfaq orën 24-orëshe të matjes së fundit të seancës në Tiranë. Fuqia aktuale dhe vlerësimi deri në mbushje presin matje të reja. Koha e seancës përfshin pritjen/pauzat dhe nuk provon transferim energjie. Nuk ndryshon protokollin OCPP apo mbyll seanca pa konfirmim nga pajisja.
- Zëvendëson butonin e gjerë Kthehu te pikat me shigjetë/tekst kompakt Pikat, duke ruajtur zonën minimale të prekjes 44 pikë në iOS/Android.
- Dokumenton kërkesën për Dynamic Island/Live Activity si të papërfunduar: Expo Go nuk e mbështet; nevojiten build iOS i nënshkruar dhe cikli i përditësimeve ActivityKit/APNs në sfond. Llogaria Apple Developer ende mungon. Nuk shton implementim native ose pretendon verifikim të kësaj veçorie.
- Verifikimi: kaluan TypeScript-i mobile/web, 82 teste mobile, 32 teste të fokusuara web dhe eksportet Hermes iOS/Android. Testet mbulojnë filtrat e fshehur, zgjedhjen e pikës pa navigim të menjëhershëm, lidhësin e saktë, statuset e shënimit, mbylljen/rikthimin e seancës, përgjigjet e vonuara të llogarive dhe përqindjen e vjetruar. Modulet native janë të simuluara në teste; prova fizike Android mbetet e pakryer. Nuk u nis APK, nuk u instalua varësi dhe nuk u bë deploy, migrim, commit ose push.
- Pajton commit-in `380153d` me historikun më poshtë.

### `380153d` — Refactor for apple and android versions 3

- Plotëson ekranin e karikimit mobile për iOS dhe Android me të njëjtin komponent: banner i gjelbër, përqindje e madhe e baterisë, shirit progresi i lexueshëm nga teknologjitë ndihmëse dhe katër matje të organizuara për energjinë, fuqinë, koston dhe kohëzgjatjen. Përmbajtja përshtatet me ekranin/tekstin; karikimi shfaqet përpara specifikimeve të pikës.
- Shton kohën e vlerësuar deri në 100%, duke përbashkuar llogaritjen dhe arsyet e mungesës me web-in. Përdor kapacitetin e profilit dhe matjet e freskëta të baterisë/fuqisë totale nga API-ja ekzistuese. Mungesa e kapacitetit hap profilin me një veprim; pauza, defekti, humbja e lidhjes, matjet e vjetruara dhe fuqia e pjesshme nuk prodhojnë kohë të sajuar.
- Përqindja e panjohur shfaqet pa vlerë numerike. Seanca e përfunduar ruan përqindjen e fundit të raportuar, pa e detyruar në 100%, dhe heq kohën e mbetur. Matja finale në verifikim nuk paraqet kosto të konfirmuar. Tekstet dhe sjellja janë të njëjta në të dyja platformat.
- Verifikimi: kaluan TypeScript-i mobile/web, 70 teste mobile, 31 teste të fokusuara të karikimit web dhe eksportet Hermes iOS/Android. Mbulimi përfshin ndryshimet e matjeve, veprimin e profilit, skadimin e matjeve me kalimin e kohës, gabimin/rikthimin e polling-ut, privatësinë e seancës, përfundimin dhe progresin e aksesueshëm. Kaloi edhe kontrolli i diff-it. Modulet native në teste janë të simuluara; eksportet nuk zëvendësojnë provën fizike Android.
- Dokumenton shtyrjen e APK-së dhe vazhdimin e provave në iPhone me Expo Go, duke ruajtur mbulimin e Android-it. Prova EAS kishte krijuar projektin/keystore-in, por paketimi dështoi me EINVAL sepse dosja e përkohshme ishte brenda burimit; nuk u krijua APK dhe nuk u rinis build-i. Nuk u shtua varësi, migrim, deploy, commit ose push.
- Ruan lidhjen publike të projektit EAS në konfigurimin mobile dhe pajton commit-in `32f7a1c` me historikun.

### `32f7a1c` — Refactor for apple and android versions

- Përditëson aplikacionin nga Expo SDK 54 në SDK 57 për përputhje me Expo Go në iPhone. Komanda `upgrade:expo-go` kalon gradualisht në 55/56/57, ruan kopje të manifestit/lockfile-it, kontrollon varësitë, TypeScript-in dhe testet pas çdo hapi, eksporton të dyja platformat dhe ndalon në dështimin e parë.
- Shton `start:tunnel` që zgjedh shprehimisht Expo Go dhe pastron cache-in, ngrok si varësi lokale zhvillimi dhe lockfile-in mobile. Dokumenton dallimin mes QR-së së preview-t, API-së së arritshme dhe APK-së së nënshkruar.
- Korrigjon ndalimin pas instalimit të SDK 55: regjistron shprehimisht plugin-in e kalendarit në konfigurimin dinamik. Rifillimi përfundon varësitë dhe kontrollon SDK-në aktuale përpara kalimit më tej. Opsioni `--start` hap tunelin vetëm pas suksesit të të gjithë hapave, duke shmangur nisjen aksidentale nga komandat e ngjitura veçmas në PowerShell.
- Përshtat SDK 56/TypeScript 6: deklaron tipet Node për konfigurimin, përdor `StyleSheet.absoluteFill` dhe shënon saktë eksportet default të moduleve të simuluara në teste. Nuk fsheh gabime me përjashtime të compiler-it. Përcakton Node 20.19.4 si minimum dhe mban skedarët e përkohshëm të compiler-it brenda projektit për të shmangur gabimin e shkrimit të Hermes në Windows.
- Shton `build:apk` dhe profilin e brendshëm `preview-ui`: përdor API-në demo, instalon EAS CLI lokalisht sipas nevojës, krijon/lidh projektin Expo, ruan UUID-në reale për ripërdorim, kontrollon konfigurimin, eksporton Android dhe nis build-in APK në EAS. Refuzon identitet kontradiktor ose slug të papritur dhe ndalon pas gabimeve; nuk kryen dorëzim në dyqane, commit ose push. `.easignore` kufizon arkivin te burimet e nevojshme dhe përjashton konfigurimet lokale, kredencialet dhe historikun Git.
- Vetëm profili `preview-ui` çaktivizon push për të lejuar provën e pamjes pa konfigurim Firebase. Cilësimet shpjegojnë kufizimin; preferenca e ruajtur në llogari nuk ndryshon nga ky kufizim. Profilet e tjera ruajnë push dhe kontrollet ekzistuese të publikimit.
- Shton hartën brenda aplikacionit mobile, kalimin Harta/Lista, lëvizjen/zmadhimin dhe hapjen e navigimit nga pika. Filtrat janë të njëjtë në të dyja pamjet; ndryshimi i statusit nuk e rikthen pozicionin e hartës. Përbashkon llogaritjet e hartës dhe URL-të e navigimit me web-in. Përdor OpenStreetMap me atribuim, kërkesa vetëm për pamjen e dukshme dhe identifikim aplikacioni, pa varësi të reja ose leje vendndodhjeje.
- Shton te cilësimet zgjedhjen Google Maps/Apple Maps, të ruajtur në telefon. Hap koordinatat e sakta sipas preferencës dhe përdor faqen Google kur aplikacioni mungon. Gabimi i ruajtjes nuk shfaqet si sukses; prekjet e përsëritura të navigimit bashkohen.
- Organizon njoftimet nga më të rejat, me grupim/filtër sipas ditës dhe nga tetë karta për faqe. Ndryshimi i ditës rikthen faqen e parë; kalimi i faqes rikthen listën në krye. Ora është 24-orëshe në `Europe/Tirane`, përfshirë ndryshimet verë/dimër; edhe koha e nisjes së karikimit nuk shfaq më shënimin 12-orësh.
- Korrigjon parazgjedhjen e simulatorit web që ende dërgonte API-në demo te Worker-i i vjetër Cloudflare: tani përdor origjinën e API-së, ndërsa tuneli kërkon konfigurim të shprehur të web-it. Ruan refuzimin e URL-ve të pavlefshme dhe nuk bën fallback të fshehtë. Logu `ConnectionAttempt` regjistron endpoint-in e validuar dhe protokollin përpara lidhjes. Ndryshimi lokal nuk është deploy ose konfirmim i shkakut të refuzimit live.
- Verifikimi: kaluan TypeScript-i mobile/web, 63 teste mobile në SDK 57, 183 teste web, konfigurimi prebuild dhe eksportet Hermes iOS/Android. Kaluan edhe 15 kontrolle të fokusuara pas plotësimit të logut të lidhjes. Testet mbulojnë hartën/preferencën, navigimin, prekjet/zmadhimin, ditën/orën shqiptare me DST, filtrimin/faqëzimin dhe rrugën direkte të simulatorit me WebSocket të simuluar. Përfshihen migrimi, rrjedha APK me EAS të simuluar, kontrollet e paketimit, sintaksa, manifest/lockfile dhe `git diff --check`. Kontrolli offline i varësive raportoi përputhje me kufizimin e vet të besueshmërisë.
- Kufijtë: ambienti i agentit nuk arrin npm/Expo (`ENOTFOUND`); instalimet u kryen nga terminali i përdoruesit. Krijimi real i projektit EAS dhe build-i i nënshkruar APK mbeten për ekzekutim nga terminali me rrjet. Përdoruesi konfirmoi hapjen e SDK 57 në iPhone përpara shtimit të hartës/njoftimeve; këto ndryshime mbeten për provë në pajisje. Eksportet/testet e simuluara nuk janë APK/IPA, push delivery, lidhje live e simulatorit ose testim fizik. Nuk u krye deploy, commit ose push.
- Pajton commit-in `b1d1a66` me historikun më poshtë.

### `b1d1a66` — Refactor for apple and android versions

- Përmirëson përdorimin mobile të funksioneve ekzistuese: admini hap detajet e pikës veçmas nga lista, kthehet me një veprim dhe konfirmon braktisjen e ndryshimeve të paruajtura. Rifreskimet e serverit përditësojnë formularin e paprekur; konfliktet ofrojnë ngarkimin e versionit aktual.
- Karikimet e adminit paraqiten si karta me etiketa në telefon; filtrat hapen sipas nevojës. Refuzon intervalet e pavlefshme dhe nuk lejon përgjigjet e vjetra të zëvendësojnë rezultatet e filtrit të ri. Formulari i kartës mund të riprovojë ngarkimin dhe bllokon dërgimet e dyfishta.
- Përbashkon udhëzimin Driver në web/native për pikë offline, gjendje të vjetruar, mirëmbajtje, lidhës të zënë/rezervuar dhe kartë të lidhur. Nuk krijon pronësi nga hapja e pikës. Ruhet shfaqja e defekteve të pajisjes; udhëzimet teknike kufizohen në testim.
- Mobile shfaq gabimin dhe riprovimin pas dështimit të ngarkimit, lejon pastrimin e filtrave bosh, përdor tarifën historike të seancës dhe nuk e ndërpret monitorimin me gabim lejeje kur përdoruesi ka refuzuar push. Përmirëson reagimin dhe tekstin e butonave.
- Përgatit URL-të publike të privatësisë dhe kërkesës së fshirjes: privatësia para hyrjes dhe në cilësime; fshirja në cilësime. Konfigurimi production dhe komandat e reja të kontrollit për dyqanet refuzojnë URL të munguara/të pavlefshme. Faqet e klientit, rregullat e ruajtjes dhe procesi real i fshirjes mbeten kërkesa të paplotësuara; preview i brendshëm nuk bllokohet nga mungesa e tyre.
- Dokumenton kufijtë e QR/kartës, caktimin aktual një Driver/kartë për karikues, provat fizike dhe kushtet e shqyrtimit Apple/Google. Nuk shton pagesa, portofol, nisje/ndalim nga larg ose regjistër të ri kartash. Shënon edhe mungesën ekzistuese të hartës së integruar native.
- Pajton commit-in `e206009` me historikun më poshtë.
- Verifikimi: kaluan 183 teste web, 45 teste mobile me module native të simuluara, TypeScript web dhe `git diff --check`. Përfshihen udhëzimet e karikimit, formulari mobile i adminit, përgjigjet e vonuara, gabimet e ngarkimit, tarifa e seancës, push i refuzuar dhe kontrollet/veprimet e privatësisë. Kontrolli store iOS refuzoi konfigurimin lokal të paplotësuar, siç pritet.
- Kufijtë: Next.js nuk u nis për shkak të gabimit Windows `Access is denied` gjatë zgjidhjes së rrugës së projektit. Kontrolli vizual në browser nuk u krye për shkak të refuzimit të aksesit localhost. Varësitë Expo nuk janë të instaluara; typecheck i plotë mobile, export Metro, build native, prova me pajisje/karikues dhe dorëzimi në dyqane nuk janë kryer. Backend-i dhe skema nuk ndryshojnë; nuk u krye migrim, commit, push ose deploy.

### `e206009` — Add 3 versions for testing with actual charger 2

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
