# Kontrolli i pamjes në celular — 8 tetor 2026

Screenshot-i i raportuar vjen nga iPhone 16 Pro Max. Përmasat e synuara për kontrollin vizual janë 440 × 956 CSS px, bashkë me gjerësi më të vogla 320, 360 dhe 390 px. **Nuk u krye kontroll vizual në shfletues ose në një iPhone fizik.** Pas bllokimit nga një preferencë e ruajtur e lejeve të shfletuesit, përdoruesi kërkoi të vazhdohej me kodin dhe testet.

## Problemi dhe ndryshimet

Menuja administrative vendoste 8 lidhje për Admin dhe 11 për Superadmin në një grid me 4 kolona. Në celular krijoheshin 2–3 rreshta të fiksuar, ndërsa përmbajtja rezervonte vend vetëm për një rresht. Kjo mbulonte përmbajtjen, si në screenshot-in e logeve.

- Në celular, Admin dhe Superadmin kanë një rresht me **Paneli, Pikat, Karikimet, Njoftime dhe Më shumë**. Vendndodhjet, çmimet, auditimi, llogaria dhe seksionet e Superadmin-it hapen në panelin modal “Më shumë”. Lidhjet e desktop-it ruajnë paraqitjen e mëparshme.
- Paneli përdor `dialog.showModal()` për fokusin modal, Escape dhe izolimin e përmbajtjes pas tij. Mbyllet me butonin, prekjen jashtë panelit, zgjedhjen e faqes, ndryshimin e rrugës ose kalimin në desktop. Scroll-i rikthehet pas mbylljes. Një seksion dytësor aktiv shënon “Më shumë” dhe lidhjen përkatëse.
- Kreu në telefon ka logon dhe avatarin e llogarisë në një rresht. Emri i plotë ruhet në etiketën e aksesueshme të butonit dhe në panelin e llogarisë.
- Grid-et, formularët dhe filtrat përshtaten në një kolonë; të dhënat e gjata mbështillen. Fushat përdorin 16 px dhe kontrollet kryesore kanë të paktën 44 px lartësi.
- Tabelat e gjera kanë scroll brenda kontejnerit, gjerësi të lexueshme, etiketë të aksesueshme dhe akses me tastierë. Në telefon shfaqet udhëzimi për rrëshqitjen horizontale. Kolonat ruhen.
- Harta, historiku, parametrat e navigimit, karikimi, diagnostikimi dhe simulatori kanë rregulla për gjerësitë e vogla. Butonat e hartës janë 44 px; butonat e simulatorit vendosen në rreshta më vete.
- Hapësira e poshtme përputhet me lartësinë e menusë dhe `safe-area-inset-bottom`. Elementet që marrin fokus kanë hapësirë scroll-i larg kreut dhe menusë. Telefoni horizontal mbulohet me media query për ekran me pointer coarse, deri në 1000 px gjerësi dhe 500 px lartësi.

Rregullat janë në `web/src/app/mobile.css`, me breakpoint 700 px për telefonin vertikal. Jashtë tyre, elementet e reja të celularit janë të fshehura dhe titulli i kreut të Driver-it ruan grid-in dhe hapësirën ekzistuese.

## Faqet e shqyrtuara në kod

| Faqja | Roli | Pjesët e shqyrtuara |
| --- | --- | --- |
| `/`, `/register` | Publik | Logo/gjuhë, emër/mbiemër, fjalëkalim, fusha dhe footer |
| `/dashboard` | Admin, Superadmin | KPI, grafikë, periudha/kalendar, renditje, menu dhe kreu |
| `/dashboard/stations` | Admin, Superadmin | Listë/kërkim, formular, përzgjedhje harte, pajisje dhe caktim Driver |
| `/dashboard/locations` | Admin, Superadmin | Listë dhe formular; lejet ekzistuese të redaktimit |
| `/dashboard/sessions` | Admin, Superadmin | Filtra, datat, tabelë me 9 kolona dhe faqezim |
| `/dashboard/prices`, `/dashboard/audit` | Admin, Superadmin | Tabela, të dhëna të gjata dhe detajet JSON |
| `/dashboard/notifications` | Admin, Superadmin | Tekst dhe data që mbështillen në kartë |
| `/dashboard/account` | Admin, Superadmin | Profil, siguri dhe formularë |
| `/dashboard/administrators` | Superadmin | Listë, profil, lejet dhe checkbox-et |
| `/dashboard/logs` | Superadmin | Filtra, veprime, tabelë, payload dhe faqezim |
| `/dashboard/drivers` | Superadmin | Tabelë me emër, email, telefon, gjendje dhe datë |
| `/driver` | Driver | Harta, filtrat, gjendjet, specifikimet dhe lista e pikave |
| `/driver/history` | Driver | Grafik mujor, listë, vlera dhe detaje; rreshtet në 320 px |
| `/driver/account` | Driver | Profil/makinë, fjalëkalim dhe parametrat e navigimit |
| `/driver/settings` | Driver | Ridrejtimi te seksioni i parametrave në profil |
| `/driver/stations/[id]` | Driver | Navigim, gjendje pritjeje/karikimi, matje dhe diagnostikim |
| `/driver/simulator` | Driver, kur lejohet nga API | Parametra, veprime, gjendje dhe mesazhet OCPP |
| Gjendjet bosh/gabim dhe footer-at | Sipas hapësirës | Tekst, lidhje dhe hapësira nga navigimi i fiksuar |

## Verifikimi dhe kufijtë

Pas këtij kontrolli, grafiku dhe lista e Driver-it u ridizajnuan me filtra të
përbashkët dhe faqezim edhe në desktop. [Ridizajnimi dhe verifikimi i historikut](driver-history-redesign.md)
dokumenton ndryshimin dhe zëvendësimin e stileve të vjetra të grafikut.

- Kaluan **156 teste Node**, duke përfshirë dy teste të reja për navigimin dhe ciklin e hapjes/mbylljes së panelit. Testet ekzistuese mbulojnë ndër të tjera formularët, grafiqet, karikimin dhe kufizimet e roleve. Këto janë teste kodi/renderimi React, jo teste të layout-it në shfletues.
- Kaloi `npm run typecheck` dhe `git diff --check`.
- `npm run build` me konfigurimin origjinal nuk përfundoi: SWC në mjedisin e kufizuar Windows nuk mund të canonicalize `jsc.baseUrl`, me `Access is denied (os error 5)`. Provimi me konfigurim të përkohshëm Babel u pengua gjithashtu nga gabime EPERM gjatë krijimit të output-it. Konfigurimet e përkohshme u hoqën; `next.config.ts` u rikthye pa ndryshime. Nuk konfirmohet një build production i suksesshëm.
- Host-i lokal me fixture në memorie u nis dhe u ndal. Nuk u kryen karikime, veprime në databazën e prodhimit ose publikim.
- Mbetet kontrolli vizual në 320/360/390/440 px, në Safari të iPhone-it, me tastierën e hapur dhe në orientim horizontal. Për desktop rekomandohet krahasim në 1280/1440 px. Pamja e përfunduar nuk është verifikuar me screenshot-e.

## Referencat

- [Material Design — navigation](https://m2.material.io/design/navigation/): 3–5 destinacione kryesore në navigimin e poshtëm.
- [W3C — Reflow](https://www.w3.org/WAI/WCAG21/Understanding/reflow): përshtatje në 320 CSS px dhe scroll lokal për tabela/harta.
- [W3C — Target Size Minimum](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum): madhësia dhe distanca e objektivave të prekjes.
- [MDN — env()](https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Values/env): hapësirat e sigurta në pajisje me home indicator dhe skaje të veçanta.
