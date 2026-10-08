# Historiku i Driver-it — ridizajnimi dhe filtrat, 8 tetor 2026

Grafiku i raportuar tregonte gjashtë muaj, shumë muaj bosh, vija të ndërprera
për secilën kolonë dhe vlera të përsëritura në disa karta. Lista poshtë shfaqte
çdo seancë të ngarkuar, pa lidhje me përzgjedhjen në grafik.

## Sjellja e re

- Filtrat e përbashkët: **7 ditë**, **30 ditë**, **6 muaj**, interval datash,
  pika e karikimit, energjia për seancë **≤ 10**, **> 10–30**, **> 30 kWh**.
  Monedha shfaqet si filtër kur historiku përmban disa monedha.
- Parazgjedhja është shtatë ditë deri te përfundimi më i fundit i ngarkuar.
  Datat shfaqen qartë; periudhat nuk pretendojnë të jenë java aktuale kur
  përdoruesi nuk ka karikuar së fundi. Datat dhe filtrimi përdorin `Europe/Tirane`.
- Grafiku tregon energjinë, koston ose numrin e seancave. Periudhat deri në
  31 ditë grupohen sipas ditës, deri në 93 sipas javës, më gjatë sipas muajit.
  Preseti gjashtëmujor përdor muajt. Dita e parë dhe e fundit janë përfshirëse.
- Kolonat kanë një bosht të përbashkët me bazë zero, shkallë numerike dhe njësi.
  Një total zero ka lartësi zero. Vijat e rrjetës janë të vazhdueshme.
  Paneli anësor tregon periudhën me totalin më të lartë ose periudhën e zgjedhur.
- Prekja e kolonës filtron KPI-të dhe listën në atë ditë/javë/muaj. Grafiku
  ruan kontekstin e periudhës së gjerë dhe shënon kolonën e zgjedhur.
  Prekja përsëri ose **Shfaq gjithë periudhën** heq këtë përzgjedhje.
- Lista ka **8 seanca për faqe**, numër rezultatesh dhe Para/Tjetër.
  Ndryshimi i filtrave rikthen faqen e parë. Detajet i përkasin një seance
  në faqen e dukshme; një seancë e përjashtuar nuk mbetet te detajet.
- Kostot ruhen të ndara sipas monedhës. Kalimi te matja e kostos zgjedh një
  monedhë dhe e zbaton edhe te lista/KPI-të. Rikthimi te të gjitha monedhat
  përdor matjen e energjisë, pa mbledhur EUR me ALL.
- Kontrollet kanë lartësi së paku 44 px, etiketa dhe fokus të dukshëm.
  Kolonat hapen me Enter/Space dhe navigohen me shigjeta, Home/End.
  **Shiko vlerat e grafikut** hap tabelën tekstuale me vlerat e sakta.
- Në telefon kontrollet vendosen në rreshta, grafiku ka lartësi 180 px dhe
  paneli i detajeve vendoset poshtë tij. Periudhat e dendura kanë scroll lokal
  dhe udhëzim; faqja nuk zgjerohet nga numri i kolonave.

Historiku i API-së vazhdon të kthejë vetëm **100 seancat më të fundit**.
Filtrat punojnë mbi të dhënat e ngarkuara, dhe kjo thuhet në faqe. Nuk është
shtuar kërkim mbi historikun më të vjetër në server. Kontrata e historikut nuk
ka fuqi të matur në kW; filtri i ri përdor energjinë reale të ruajtur në kWh.

## Kërkimi që udhëhoqi vendimet

- [Datawrapper — column charts](https://www.datawrapper.de/academy/customizing-your-column-chart):
  bazë zero, shkallë dhe etiketa të qarta, kontroll i hapësirës ndërmjet kolonave.
- [Carbon — simple charts](https://v10.carbondesignsystem.com/data-visualization/simple-charts/):
  kolonat përshtaten për krahasimin e totaleve të periudhave diskrete.
- [Department for Education — charts](https://design.education.gov.uk/design-system/patterns/charts):
  kontekst kohor, alternativë tekstuale, tastierë dhe të dhëna të kuptueshme
  edhe pa dalluar ngjyrat.
- [GOV.UK — pagination](https://design-system.service.gov.uk/components/pagination/):
  ndarje e listave të gjata në faqe dhe shmangie e scroll-it të pafund.

## Verifikimi

- Kaluan **169 teste Node** dhe `npm run typecheck`. Përfshihen filtrat e
  përbashkët, përzgjedhja e kolonave, faqezimi, datat/DST, monedhat, periudhat
  bosh, tastiera dhe etiketat shqip. Këto janë teste kodi/renderimi React.
- Kaloi `git diff --check`.
- `npm run build` bllokohet nga problemi ekzistues i mjedisit Windows:
  SWC nuk mund të canonicalize `jsc.baseUrl`, me `Access is denied (os error 5)`.
  Nuk është konfirmuar build production.
- Nuk u krye kontroll vizual në shfletues, sipas kërkesës për të vazhduar pa të
  pas bllokimit të lejeve. Mbetet krahasimi në desktop dhe 320/360/390/440 px,
  veçanërisht Safari në iPhone 16 Pro Max.

## Loget teknike

Default-i i `DiagnosticOptions.RetentionDays` dhe shembulli i konfigurimit
u ndryshuan nga 90 në **30 ditë**. Worker-i ekzistues fshin gradualisht deri
në 2 000 ngjarje të skaduara për kalim, afërsisht çdo minutë. Nuk u ekzekutua
pastrim në prodhim. Politika prek vetëm `application_logs`.

Backend-i u kompilua me zero gabime. Host-i standard VSTest dështoi të lidhej
me testhost-in brenda 90 sekondave. **6 testet e `PersistentApplicationLogTests`
kaluan me thirrje të drejtpërdrejtë**, duke përdorur databazën e testit në memorie
dhe gjenerimin e SQL-së pa lidhje PostgreSQL. Testi i ri kontrollon ngjarjen
më të vjetër se 30 ditë, ngjarjen saktësisht në kufi dhe ngjarjen më të re.
Kaloi edhe kontrolli CSharpier për dy skedarët C# të ndryshuar.

Nëse mjedisi i shërbimit ka një override të vjetër, vendos
`EVCHARGING_Diagnostics__RetentionDays=30`; ai ka përparësi ndaj default-it.
Ndryshimet kërkojnë commit/push dhe deploy nga përdoruesi.
