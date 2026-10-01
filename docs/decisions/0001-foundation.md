# Vendimet bazë të projektimit

Data: 2026-09-28. Gjendja: **Proposed**. Këto vendime janë rekomandime të discovery, jo implementim i përfunduar.

## Monolit modular dhe një PostgreSQL

Kërkesat mbulojnë një produkt të vetëm me ndryshime që duhen kryer atomikisht: gjendje, sesione, çmime dhe audit. Një backend me module dhe një DB mbështet këto transaksione me më pak operim. Inbox/outbox në të njëjtën DB ruajnë eventet; nuk shtohet broker i jashtëm.

## HOUSS si referencë vetëm për lexim

Përdoret ndarja .NET/Next/Expo dhe mësimet nga auth, API, konfigurimet dhe hartat. Produkti ka emra, DB, cookies, keys, bundle identifiers dhe deploy të veçantë. Nuk importohet kodi i HOUSS dhe nuk kopjohen sekretet ose rregullat e domenit të pronave.

## Statusi i pajisjes ndahet nga kufizimi administrativ

Ruhet gjendja e raportuar dhe veçmas `Maintenance`/`OutOfService` administrative. Ngjarjet e pajisjes nuk heqin vendimin e administratorit. Sesioni fizik vazhdon të regjistrojë matje edhe kur disponueshmëria administrative është e bllokuar.

## Çmimi ruhet te sesioni

Sesioni ruan revisionin dhe vlerën e çmimit në nisje; ndryshimi i mëvonshëm nuk ndryshon koston historike. Decimal/numeric përdoret nga backend-i. Monedha, momenti i fiksimit dhe rrumbullakimi mbeten politika për konfirmim përpara implementimit të pricing.

## Harta sipas platformës

Kontrata e marker-ëve dhe viewport-it është e pavarur nga SDK. Rekomandohet Leaflet në web, adapter Android dhe MapKit native në iOS, sipas mësimit nga HOUSS. SDK/versionet dhe qëndrueshmëria duhen provuar në fazën teknike. Harta funksionale në iOS është kusht pranimi i MVP-së; fallback listë trajton gabime, por nuk e përmbush këtë kërkesë.

## Kufiri i dorëzimit të parë

Brief-i kërkon discovery/arkitekturë si detyrë të parë. Krijohen dokumente dhe dosje të pavarura. Funksionet e produktit, instalimet, lidhja live me karikues dhe publikimi i përkasin fazave pasuese.
