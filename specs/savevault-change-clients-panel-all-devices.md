# SaveVault — Delta-Spec: Alle Clients im Spiel-Detailpanel (auch ohne Spielstand)

## Ziel
Im ausfahrbaren Spiel-Detailpanel (Drawer) zeigt der `Clients`-Abschnitt künftig **jedes mit
dem Server gekoppelte Gerät**, nicht nur die, die für genau dieses Spiel bereits einen privaten
Bucket (= einen erfassten Spielstand) haben. Geräte ohne Spielstand zu diesem Spiel bekommen
eine eigene, klar erkennbare Karte mit dem Hinweis „Kein Spielstand für dieses Spiel" statt
einfach zu fehlen.

## Ist-Zustand (Root-Cause)
`openGameDrawer()` (app.js:1404) bildet `privateBuckets` ausschließlich aus
`buckets = state.data.games.filter(g => canonicalOf(g) === canonical)` — ein privater Bucket
existiert serverseitig nur für Geräte, die dieses Spiel je lokal erfasst (gesynct/hochgeladen)
haben. Ein Gerät, das zwar gepaart ist und mit dem Server heartbeatet, dieses Spiel aber nie
hatte, taucht in `buckets` gar nicht auf und fehlt dadurch komplett im `Clients`-Abschnitt
(`clientsSection`, app.js:1618). Die vollständige Geräteliste ist bereits geladen
(`state.data.devices`, genutzt u. a. in der eigenständigen Clients-Ansicht `viewClients()`,
app.js:1071) — sie wird im Spiel-Drawer bisher nur nicht herangezogen.

## Umfang
- `openGameDrawer()`: zusätzlich zu `privateBuckets` die volle `state.data.devices`-Liste
  heranziehen; pro Gerät den passenden privaten Bucket zuordnen (`bucket.ownerDeviceId === device.id`),
  falls vorhanden.
- `clientsSection()`/`buildClientCard()`: auf eine gemischte Liste `{ device, bucket }` umstellen.
  - Gerät **mit** Bucket → bestehende Karte/Verhalten unverändert (Status-Punkt/Sync-Icon,
    Konflikt-Badge, aufklappbar mit Größe/Dateien/Versionsverlauf).
  - Gerät **ohne** Bucket → neue, schlanke Karte: Geräte-Icon, Name, Verbindungsstatus-Punkt
    (wiederverwendet `clientDerivedStatus`/`statusMeta`, dieselbe Verbunden/Offline-Logik wie in
    `viewClients()`), Text „Kein Spielstand für dieses Spiel" statt der Aktivitäts-Zeitangabe,
    kein Chevron, nicht aufklappbar (kein Akkordeon-Eintrag, kein Klick-Handler).
- Sortierung der gemischten Liste: alphabetisch nach Gerätename (bisher implizite Reihenfolge
  der `buckets`, ungeordnet) — stabil und für Nutzer vorhersehbar, unabhängig davon ob mit/ohne
  Bucket.
- Trenner-Label „Clients · N" (app.js:1453): N = Anzahl **aller** angezeigten Geräte (bisher nur
  `privateBuckets.length`).
- Leerzustand-Text (app.js:1621, bisher „Noch kein Gerät hat diesen Spielstand erfasst.") gilt
  nur noch, wenn **gar kein** Gerät gepaart ist (`state.data.devices.length === 0`) — Text
  angepasst analog `viewClients()` („Noch kein Client gekoppelt…").
- Neue CSS-Klasse für die schlanke „ohne Bucket"-Karte (styles.css, angelehnt an
  `.client-card2`/`.client-card2__head`, aber ohne Cursor/Hover-Feedback und mit gedämpfter
  Deckkraft, damit sie sich sichtbar von den aufklappbaren Karten abhebt).

## Nicht-Umfang
- Keine Änderung an der „Geteilter Speicherstand"-Karte (`sharedCard`), an Fork-Karten
  (`forkCard`) oder an der eigenständigen Clients-Ansicht (`viewClients()`/`clientCard()`).
- Kein Backend-/API-Vertragswechsel — `/api/devices` liefert bereits alle gepaarten Geräte,
  wird im Drawer nur zusätzlich verwendet.
- Kein Client-Update (`SaveVault.Client`) nötig — reine Server-Dashboard-Änderung.

## Betroffene Dateien (Schätzung)
- `src/SaveVault.Server/wwwroot/app.js` — `openGameDrawer`, `clientsSection`, `buildClientCard`
  (neue Variante für Geräte ohne Bucket).
- `src/SaveVault.Server/wwwroot/styles.css` — neue Karten-Variante für Geräte ohne Bucket.
- `src/SaveVault.Server/SaveVault.Server.csproj` — Version-Bump (1.5.5 → 1.5.6).

## Akzeptanz & Verifikation
- Laufzeit-Test (lokaler Server, mind. 3 gepaarte Test-Geräte: A hat einen Spielstand zu Spiel X
  und nutzt den geteilten Stand, B hat einen rein lokalen Spielstand zu Spiel X, C ist gepaart,
  hat aber nie einen Spielstand zu Spiel X gemeldet): Drawer von Spiel X zeigt alle drei Geräte;
  A/B wie bisher (Status/Sync-Icon/aufklappbar), C mit neuer schlanker Karte „Kein Spielstand für
  dieses Spiel", Verbindungsstatus-Punkt korrekt (verbunden/offline je nach Heartbeat-Alter).
- Randfall: Spiel, das nur ein einziges der gepaarten Geräte je erfasst hat → alle übrigen
  gepaarten Geräte erscheinen mit der neuen Karte.
- Randfall: kein Gerät überhaupt gepaart → weiterhin der (angepasste) Leerzustand-Text, keine
  Karten.
- Trenner zeigt die korrekte Gesamtzahl aller angezeigten Geräte.
- `dotnet build` 0 Fehler, `dotnet test` unverändert grün (reine Frontend-Änderung).
- `/code-review medium` auf den Diff, Befunde beheben oder begründet ablehnen.

## Risiken / Rückwärtskompatibilität
- Rein kosmetisch/Frontend, kein Migrations-/Datenrisiko.
- Bei sehr vielen gepaarten, aber für dieses Spiel irrelevanten Geräten wird die Liste länger als
  bisher (z. B. ein Testgerät, das nie für dieses Spiel genutzt wurde, erscheint jetzt in jedem
  Spiel-Drawer). Das ist der ausdrückliche Zweck der Änderung, aber bei sehr vielen Geräten könnte
  das Panel dadurch lang werden — akzeptiertes Trade-off laut Auftrag, kein Paging/Scroll-Umbau in
  diesem Delta.
- Rollout: nur Server-Image neu bauen/pushen (kein Client-Update nötig).
