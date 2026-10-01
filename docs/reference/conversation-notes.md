# Saktësime nga biseda

Data: 2026-09-28.

- Përdoruesi kërkoi të përdoret HOUSS lokal si referencë dhe projekti të fillojë nga zero brenda `Apps`.
- Në përgjigje të pyetjes për prodhuesin/API/OCPP dhe identifikimin, përdoruesi tha se informacionin mund ta dërgojë më vonë dhe pyeti nëse duhet menjëherë.
- U sqarua se nuk nevojitet për të vazhduar discovery/arkitekturën; nevojitet për integrimin real.
- Përdoruesi sugjeroi: kur drejtuesi shkon te karikuesi, mendon se do të skanojë QR për t’u lidhur me pajisjen.
- QR është regjistruar si propozim i përdoruesit. Formati, provider-i dhe verifikimi i pronësisë së sesionit mbeten të hapur. Nuk është interpretuar si autorizim për pagesa ose start/stop remote.

Këto saktësime plotësojnë materialet e bashkëngjitura. Nuk ndryshojnë ndalimin e aksesit në rrjet pa leje ose kufirin e dorëzimit të parë të brief-it.

## Implementimi dhe prioritetet e konfirmuara

- Përdoruesi kërkoi të fillojë implementimi dhe do të sjellë informacion shtesë gjatë punës. Kufiri vetëm-discovery i dorëzimit të parë nuk zbatohet për këtë hap.
- Drejtuesi regjistrohet vetë.
- Njoftimet dërgohen push në telefon dhe përdorin shqip/anglisht sipas gjuhës së zgjedhur.
- U dha projekti universal_ocpp16_csms i kolegut për lexim dhe përdorim të logjikës. Kodi aktual gjendet në një nënfolder me të njëjtin emër.
- Fotografia e bisedës e përshkruan si prototip me tabela për t’u rishikuar dhe kalim në C#. Kjo është evidencë/kontekst, jo leje për ndërhyrje në pajisje.
- Përdoruesi kërkoi të mos shpenzohet më kohë te mobile deri në stabilizimin e web-it.
- Testimi i web/API vetëm në localhost u autorizua shprehimisht. Kjo nuk autorizon shkarkime nga interneti, publikim ose ndryshime globale.