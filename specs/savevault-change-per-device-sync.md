# Delta-Spec: Geräte-eigene Buckets + opt-in Teilen

**Projekt:** SaveVault · **Weg:** /projekt-edit (Notebook-Werkstatt) · **Status:** Entwurf, wartet auf Tims Freigabe
**Datum:** 2026-09-02

## Ziel

Weg vom heutigen Modell „**alles synchronisiert automatisch geräteübergreifend**" (ein
globaler Bucket pro Spielname) hin zu: **jedes Gerät hat pro Spiel seinen eigenen
Bucket auf dem Server** (Backup + Historie, aber nicht geteilt). Geräteübergreifender
Sync passiert nur, wenn der Nutzer ein Spiel **aktiv auf „Teilen"** schaltet.

Damit verschwindet der akute Konflikt-Sturm, der entsteht, wenn ein zweites Gerät
(Notebook) sich koppelt und seine abweichenden lokalen Stände in denselben globalen
Verlauf schreibt.

## Kernmodell

Ein **Bucket** ist eine Revisions-/Blob-Ablage auf dem Server. Künftig drei Sorten:

| Sorte | Identität | Wer synct dagegen | Default |
|---|---|---|---|
| **Privat** | (Gerät, Spiel) | genau dieses eine Gerät | ja (jedes erkannte Spiel) |
| **Geteilt** | (Spiel) | alle Geräte, die dieses Spiel „geteilt" haben | nur nach aktivem Teilen |
| **Legacy** | (Spiel), alter globaler Bucket | niemand mehr automatisch | entsteht nur bei Migration |

- Ein Gerät synct ein Spiel gegen **genau einen aktiven Bucket**: privat (Default) oder
  geteilt (nachdem der Nutzer geteilt hat). Der jeweils andere bleibt unangetastet.
- Der **private** Bucket bleibt beim Teilen als eingefrorenes Backup erhalten.
- **Legacy**-Buckets sind lesbar (Restore/Export), werden aber von keinem Gerät mehr
  automatisch gesynct; sie lassen sich später gezielt löschen.

## Umfang

Aufgeteilt in **drei Phasen**, jede mit eigenem Gate + Checkpoint-Block. Jede Phase ist
für sich ein kohärenter, lauffähiger Zustand.

### Phase 1 — Server-Bucket-Modell + Migration (der Kern)
Nach Phase 1 sichert jedes Gerät in seinen **eigenen privaten Bucket**; nichts wird mehr
geräteübergreifend geteilt (auch bisher geteilte Spiele nicht). Der Konflikt-Sturm ist weg.

- **Bucket-Identität serverseitig:** `GameRecord` bekommt `Scope` (`Private`/`Shared`/`Legacy`)
  und `OwnerDeviceId` (gesetzt bei `Private`, sonst null). Es kann jetzt **mehrere**
  `GameRecord` mit demselben Spielschlüssel geben (je Gerät einer + evtl. ein geteilter +
  evtl. ein Legacy). Lookups gehen von „per Spielschlüssel" auf „per (Spielschlüssel, Scope,
  OwnerDeviceId)".
- **Ablagepfad** (`StoragePaths`/`PathSanitizer`): der Ordner leitet sich künftig aus
  **Scope + Owner + Spielschlüssel** ab. **Wichtig:** der Legacy-Pfad bleibt bit-genau der
  alte (`hash(gameKey)`), damit vorhandene Daten liegen bleiben und lesbar sind. Privat =
  `hash("dev/{deviceId}/{gameKey}")`, geteilt = `hash("shared/{gameKey}")`.
- **API-Scope:** die spielbezogenen Routen (`head`, `revisions`, `revision`, `content`,
  `restore`) bekommen einen Scope-Parameter (privat/geteilt). Der Server leitet den
  **Owner eines privaten Buckets immer aus dem authentifizierten Gerät ab** (ein Gerät kann
  nur seinen eigenen privaten Bucket anfassen — Sicherheits-Eigenschaft). Default ohne
  Parameter = privat.
- **Server-Migration** (Index `Version` 1→2, einmalig, idempotent): alle bestehenden
  `GameRecord` werden auf `Scope=Legacy` gesetzt (Daten bleiben, wo sie sind). Bestehende
  `DeviceGameStateRecord`s verlieren ihre Auto-Sync-Bindung an den globalen Bucket.
- **Client-Migration:** beim ersten Lauf der neuen Version wird der lokale `SyncState` je
  Spiel **zurückgesetzt** (Base-Manifest/Revision geleert), sodass der aktuelle lokale Save
  als Revision 1 in den **privaten** Bucket neu eingesät wird. Kein Spiel startet „geteilt".
- **Zielbucket beim Sync:** `SyncEngine`/`ClientAgent` sprechen für ein nicht-geteiltes
  Spiel den privaten Bucket an (Default-Scope).

### Phase 2 — Client-Umschalter Lokal/Synchron + Vergleichsdialog
- **Umschalter je Spielzeile** in der Status-Fläche: **Lokal ↔ Synchron** (Default „Lokal" =
  privater Bucket; „Synchron" = geteilter Bucket). Persistenter **Share-Set** (neuer
  `GameShareStore`, opt-in) — **zusätzlich** zur bestehenden Ausschluss-Achse, die erhalten
  bleibt und nur umbenannt wird (siehe unten).
- **Auf „Synchron" schalten (am Client):**
  1. Client fragt den Server: existiert schon ein **geteilter**, nicht-leerer Bucket für
     dieses Spiel? (neuer Endpunkt, z. B. `GET …/shared/head`).
  2. **Nein** → der lokale Stand wird als geteilte Revision 1 hochgeladen (Seed).
  3. **Ja** → **Vergleichsdialog** (Muster wie `ConflictWindow`), Titel „Es gibt bereits
     einen geteilten Speicherstand": zeigt für **beide** Seiten alle relevanten Infos —
     geteilter Head (Revision, Dateizahl, Größe, Herkunftsgerät, Stand-Zeit) vs. lokaler
     Stand (Dateizahl, Größe, letzte Änderung). Nutzer wählt:
     - **„Geteilten übernehmen"** → Download des geteilten Head (bisheriger lokaler Stand
       bleibt als privater Bucket = Backup erhalten).
     - **„Meinen lokalen hochladen & teilen"** → Upload des lokalen Stands als neue geteilte
       Head-Revision.
     - **Abbrechen** → bleibt „Lokal".
  4. Danach synct das Gerät dieses Spiel gegen den **geteilten** Bucket; die normale
     Konflikt-Mechanik gilt dort wie gehabt.
- **Vom Dashboard ausgelöstes Teilen (Beitritt am Client):** Wurde ein Spiel **im Dashboard**
  auf „synchron" geschaltet (Phase 3), erscheint auf einem noch lokalen Gerät beim nächsten
  Erkennen/Anmelden ein Dialog **„Im Dashboard wurde <Spiel> auf synchron geschaltet"** mit
  denselben Kennzahlen: **„Übernehmen"** (geteilten Stand ziehen, Gerät wird Synchron) oder
  **„Lokal weiterverwenden"** (Gerät bleibt Lokal, tritt nicht bei). Kein stilles
  Überschreiben.
- **Wichtig:** „Synchron" ist **pro Gerät** unabhängig. Ein geteilter Bucket existiert auf
  Spiel-Ebene; jedes Gerät ist für dieses Spiel entweder Synchron (beigetreten) oder Lokal.

### Umbenennung: „Sync pausieren" → „Hochladen deaktivieren"
Die bestehende Ausschluss-Achse (`GameExclusionStore`) bleibt als eigene, orthogonale
Funktion erhalten, wird aber klarer benannt: **„Hochladen deaktivieren"** = das Spiel wird
gar nicht hochgeladen (kein privater und kein geteilter Bucket, rein lokal). Mechanik
unverändert, nur Beschriftung/Anzeige. (Kann in Phase 2 miterledigt werden.)

### Phase 3 — Dashboard: Teilen-Schalter + Sichtbarkeit privat/geteilt + Legacy löschen
- Spiele-Liste zeigt Buckets sinnvoll gruppiert: **privat je Gerät** + **geteilt** +
  **Legacy** (klar markiert). Restore/Export je Bucket/Revision wie bisher.
- **Teilen-Schalter je Spiel (master-only), symmetrisch zum Client-Schalter:** Der Admin
  kann ein Spiel direkt im Dashboard auf „geteilt" schalten. Weil das Dashboard kein Gerät
  mit lokalem Save ist, wählt es den **Seed unter den vorhandenen privaten Buckets der
  Geräte**:
  1. Existiert noch **kein** geteilter Bucket:
     - **genau ein** Gerät hat einen privaten Bucket für dieses Spiel → dessen aktueller
       Stand wird der geteilte Seed (ohne Rückfrage).
     - **mehrere** Geräte haben private Buckets → **Vergleichsdialog** (dieselbe Logik wie
       am Client, nur mit N Geräte-Kandidaten): pro Gerät Revision, Dateizahl, Größe,
       Stand-Zeit; der Admin wählt, welcher Stand der geteilte wird.
  2. Existiert bereits ein geteilter Bucket → der Schalter zeigt „geteilt" an; das
     Etablieren ist erledigt.
- **Beitreten bleibt sicher am Gerät:** Das Dashboard-Teilen legt nur den geteilten Bucket
  + Seed an. Jedes **andere** Gerät wechselt **nicht** still auf den geteilten Stand —
  beim nächsten Sync bemerkt sein Client, dass das Spiel jetzt geteilt ist, und zeigt den
  **Client-Vergleichsdialog** (lokal vs. geteilter Head), sofern die Stände abweichen. So
  wird nie ohne ausdrückliche Wahl überschrieben (Phase-2-Dialog wiederverwendet).
- **Legacy-Buckets löschen:** master-only Aktion, die einen eingefrorenen Bucket samt Blobs
  entfernt (mit Bestätigung; Traversal-sicher, nur innerhalb des Datenverzeichnisses).
- **Pro-Gerät-Status sichtbar:** je Spiel ist erkennbar, welches Gerät **Synchron**
  (geteiltem Bucket beigetreten) und welches **Lokal** (eigener privater Bucket) ist —
  plus, welche Spiele überhaupt geteilt sind.

## Nicht-Umfang (v1)

- **Teilen wieder ausschalten** (geteilt → privat zurück): später, nicht in dieser Runde.
- **Automatisches Umschalten anderer Geräte** auf den geteilten Stand: bewusst nicht — jedes
  Gerät tritt selbst über seinen Client-Vergleichsdialog bei (kein stilles Überschreiben).
- Automatisches Zusammenführen/Verschmelzen zweier Stände (es bleibt bei „einer gewinnt",
  Auswahl im Vergleichsdialog). Die vorhandene Save-Merge-Semantik ändert sich nicht.

## Betroffene Dateien (beste Schätzung)

**Server (Phase 1 & 3)**
- `src/SaveVault.Server/Storage/ServerIndex.cs` — `GameRecord.Scope`/`OwnerDeviceId`, Index `Version` 2.
- `src/SaveVault.Server/Storage/VaultStore.cs` — Bucket-Lookups (Scope/Owner), Migration, Legacy-Löschen.
- `src/SaveVault.Core/Storage/StoragePaths.cs` (+ `PathSanitizer`) — scope-/owner-abhängiger Pfad, Legacy-Pfad unverändert.
- `src/SaveVault.Server/Endpoints/SaveVaultEndpoints.cs` — Scope-Parameter, `shared/head`, **Teilen-etablieren (Seed aus einem gewählten privaten Bucket, master-only + Client)**, Kandidaten-Liste der privaten Buckets je Spiel, Legacy-Delete, Dashboard-Listen.
- `src/SaveVault.Core/Api/ApiRoutes.cs` + `ApiContracts.cs` + `ISaveVaultApi.cs` + `SaveVaultApiClient.cs` — Scope/neue Endpunkte.

**Client (Phase 1 & 2)**
- `src/SaveVault.Client/Services/SyncEngine.cs` — Zielbucket-Scope, Seed-/Join-Upload.
- `src/SaveVault.Client/Services/ClientAgent.cs` — Migration (State-Reset), Teilen-Aktion.
- **Neu** `src/SaveVault.Client/Services/GameShareStore.cs` — opt-in Share-Set (Lokal/Synchron).
- `src/SaveVault.Client/Services/GameExclusionStore.cs` — bleibt; nur UI-Beschriftung „Hochladen deaktivieren".
- `src/SaveVault.Client/Services/AgentState.cs` + `Ui/GameRow.cs` — Share-Zustand, Umschalter.
- `src/SaveVault.Client/MainWindow.xaml(.cs)` — Lokal/Synchron-Umschalter je Zeile + Beitritts-Flow + Dashboard-ausgelöster Beitritts-Dialog.
- Neuer Vergleichsdialog analog `ConflictWindow.xaml(.cs)` (zwei Varianten: client- und dashboard-ausgelöst).

**Dashboard (Phase 3)**
- `src/SaveVault.Server/wwwroot/app.js` + `index.html` + `styles.css`.

**Tests**
- `tests/SaveVault.Core.Tests/*` — neue Pfad-/Scope-Tests, Migrations-Test, Bucket-Lookup.

## Akzeptanz & Verifikation

**Phase 1**
- Zwei Geräte (real oder per Harness) mit demselben Spiel schreiben in **getrennte** private
  Buckets; kein Konflikt entsteht mehr durch bloßes Koppeln.
- Migration idempotent: Alt-Index → nach Start `Version=2`, alle Alt-Buckets `Legacy`, alte
  Blobs unverändert lesbar (Export einer Legacy-Revision funktioniert).
- Ein Gerät kann den privaten Bucket eines anderen Geräts **nicht** ansprechen (Owner aus
  Auth abgeleitet — negativ verifiziert).
- Build 0/0, alle Tests grün (aktuell 88 + neue), Laufzeit-Smoke: Pairing → Save ändern →
  landet im privaten Bucket → Restore daraus.

**Phase 2**
- Teilen ohne vorhandenen geteilten Bucket → Seed als Revision 1; zweites Gerät teilt
  dasselbe Spiel → Vergleichsdialog erscheint mit korrekten Kennzahlen beider Seiten; beide
  Auswahlwege (geteilten nehmen / lokalen hochladen) real durchgespielt.
- Nach Teilen synct das Spiel geräteübergreifend; normale Konflikte greifen dort weiter.

**Phase 3**
- Dashboard listet privat/geteilt/Legacy korrekt; Legacy-Löschen entfernt Bucket + Blobs
  und ist danach nicht mehr auffindbar; Restore geteilter/privater Buckets unverändert.
- **Dashboard-Teilen:** Spiel im Dashboard auf „geteilt" schalten → bei genau einem
  Geräte-Bucket wird dieser der Seed; bei mehreren erscheint der Vergleichsdialog mit den
  Kennzahlen aller Geräte-Buckets und der gewählte Stand wird der geteilte. Danach tritt ein
  anderes Gerät beim nächsten Sync über seinen Client-Vergleichsdialog bei (kein stilles
  Überschreiben — real verifiziert).

## Risiken / Rückwärtskompatibilität

- **Migration ist der heikelste Teil.** Muss idempotent sein und darf keine Blobs bewegen
  (Legacy-Pfad = Altpfad). Vor dem Umschreiben des Index eine Sicherung (der Store schreibt
  ohnehin atomar) — Migrationsschritt gesondert verifizieren.
- **Keyspace-Aufweitung** (mehrere GameRecords je Spiel) berührt viele Lookups in
  `VaultStore` — Kern-Gate mit `/code-review high` und Augenmerk auf jeden bisherigen
  „find by key".
- **Pfad-/Sicherheitsfläche** (neuer Pfadbau aus Owner/Scope, Legacy-Delete) → `/security-review`
  auf Phase 1 und Phase 3 (Traversal, Löschen nur innerhalb Datenverzeichnis, Owner-Isolation).
- **Datenverlust-Sensibilität:** Nichts überschreiben/löschen ohne explizite Nutzerwahl;
  privater Bucket bleibt beim Teilen erhalten.

## Grundannahme

Der Server **kennt ein Spiel erst, wenn es hochgeladen wurde**. Der private Bucket eines
Geräts entsteht also beim ersten Backup-Upload; ein geteilter Bucket erst beim Etablieren
(Client-Umschalter oder Dashboard). „Hochladen deaktivieren" heißt: der Server sieht das
Spiel nie.

## Offene Punkte (Implementierungsentscheid zu Beginn von Phase 1)

1. **Bucket-Identität konkret:** eigener `BucketKey`-Typ vs. `GameKey` + Scope/Owner-Felder
   (Spec schreibt nur das Verhalten fest, nicht die Repräsentation).
