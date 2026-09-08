# Änderung: savevault — Geteilter Speicherstand sichtbar & nahtlos

**Weg:** `/projekt-edit` · **Datum:** 2026-09-07

## Status
- **Freigegeben von Tim:** ja (2026-09-07, Phase 1)
- **Runde:** Freigegeben (nach `/grill-me`, inkl. Design-Referenz
  `design-reference/SpielstandSyncRedesign.dc.html` — lokale Kopie aus Tims
  Claude-Design-Projekt „Spielstand-Sync Redesign", siehe dortige README)

## Bezug
- **Projekt-id / Ordner:** `savevault` (`<werkstatt>/savevault/`)
- **Basis-Spec:** `specs/savevault-change-per-device-sync.md` (Phase 1-3, released bis
  Client 1.6.0 / Server 1.4.1) — führt das Privat-/Geteilt-Bucket-Modell ein, auf dem
  dieses Delta aufbaut.
- **Betroffene Dateien/Komponenten (Phase 1 — Client):**
  - `src/SaveVault.Client/Ui/GameRow.cs` (neue Properties für Zwei-Kästen-Zustand)
  - `src/SaveVault.Client/Services/ClientAgent.cs` (neue `SwitchToLocalAsync`, Probe
    wird nicht mehr nur beim Umschalt-Klick, sondern bei Detail-Anzeige geladen)
  - `src/SaveVault.Client/Services/GameShareStore.cs` (unverändert, `Remove()` wird
    jetzt tatsächlich genutzt)
  - `src/SaveVault.Client/MainWindow.xaml` + `.cs` (Detail-Bereich: Zwei-Kästen-UI,
    „Sicherung deaktivieren"-Leiste, Versionshistorie als Flyout statt Inline-Liste)
  - `src/SaveVault.Client/ShareCompareWindow.xaml(.cs)` — **entfällt** (durch die
    dauerhafte Zwei-Kästen-Ansicht ersetzt)
- **Betroffene Dateien/Komponenten (Phase 2 — Dashboard, spätere Freigabe):**
  - `src/SaveVault.Server/wwwroot/app.js` + `index.html` + `styles.css` (nur der
    Spiel-Drawer, rein visuell)

## Ist-Zustand (Baseline)

**Funktioniert heute** (Phase 1-3 aus `savevault-change-per-device-sync.md`):
- Jedes Spiel hat einen **privaten** Bucket je Gerät (Default) und optional einen
  **geteilten** Bucket, sobald ein Gerät „Über Geräte synchronisieren" aktiviert hat.
- Client-Umschalter „Über Geräte synchronisieren" in der Spielzeile: nur
  **Lokal → Synchron**, einmalig. `CanShare` ist `false`, sobald `IsShared` — es gibt
  **keine UI zum Zurückschalten**, obwohl `GameShareStore.Remove()` als Methode
  existiert.
- Beim erstmaligen Umschalten auf „Synchron": `ProbeShareAsync` prüft, ob schon ein
  geteilter Stand existiert. Keiner vorhanden → stiller Seed (Revision 1). Vorhanden →
  `ShareCompareWindow`-Dialog (lokal vs. geteilt: Größe/Dateien/Zeit/Herkunftsgerät),
  Nutzer wählt „Geteilten übernehmen" oder „Meinen lokalen teilen". Dieser Vergleich
  ist **nur in diesem Moment** sichtbar, danach verschwindet er.
- Ist ein Spiel „Synchron", zeigt die Zeile nur noch das Label „Geteilt (synchron)" —
  keine Kennzahlen mehr (kein Herkunftsgerät/Zeitpunkt/Größe des aktiven Stands), kein
  sichtbarer lokaler (eingefrorener) Stand.
- Ein Client, der ein Spiel **neu erkennt** (frische Installation, neuer Save-Ordner
  gefunden), fragt **nicht** aktiv nach, ob für dieses Spiel schon ein geteilter Bucket
  existiert — er bleibt stumm „Lokal", bis jemand manuell den Schalter betätigt.
- „Hochladen deaktivieren" (vormals „Sync pausieren", `GameExclusionStore`) ist eine
  orthogonale dritte Achse: ein deaktiviertes Spiel wird weder privat noch geteilt
  hochgeladen. Heute ein Chip-Button „Hochladen deaktivieren" / „Hochladen wieder
  aktivieren".
- Konflikt-Zustand (`SyncStatus.Conflict`) blendet den Teilen-Umschalter aus
  (`ShareVisibility=Collapsed`); übersprungene Spiele (`IsSkipped`) zeigen eine eigene
  Hinweiszeile mit „Ordner zuordnen". Beide Zustände sind laut Tim in der Praxis
  inzwischen selten/nicht mehr auftretend (Konflikt-Sturm durch das Bucket-Modell
  behoben, Skip-Fälle durch ein früheres Release aufgelöst) — bleiben aber als
  Fallback im Code bestehen.
- Dashboard-Spiel-Drawer zeigt Bucket-Zeilen (Lokal:<Gerät>/Geteilt/Legacy/
  Konflikt-Kopie) mit Revisionen/States/Export/Restore/Teilen-Aktion (aus
  `savevault-change-dashboard-fix-legacy-neustart.md` + `per-device-sync.md`
  Phase 3), rein listenbasiert.
- `MainWindow` hat bereits ein zweispaltiges Spiel-Detail (linke Spalte: „Spiel
  wechseln"-Dropdown + Cover/Kurzinfo; rechte Spalte: Fehler-Banner, Ordnerpfad,
  Aktions-`WrapPanel`, Versionshistorie **fest sichtbar** als Liste mit
  In-Fenster-Wiederherstellen).

**Nutzungs-/Interaktionspfade heute:**
- Tray → Fenster öffnen → Spiel per Dropdown wählen → Detail rechts sehen.
- Klick „Über Geräte synchronisieren" → Seed oder Vergleichsdialog → Synchron.
- Klick „Hochladen deaktivieren" / „wieder aktivieren".
- Klick „Ordner öffnen", „Jetzt sichern", „Lösen" (Konflikt), „Ordner zuordnen" (Skip).
- Versionshistorie-Liste direkt unter den Aktionen, „Wiederherstellen" je Revision.
- Dashboard: Spiel-Drawer öffnen, Bucket-Zeilen ansehen, Teilen etablieren
  (master-only), Legacy löschen, Export/Restore je Revision.

**Öffentliche Schnittstellen heute:** keine Änderung an `ISaveVaultApi`/`ApiRoutes`
vorgesehen — Phase 1 ist eine reine Client-Änderung, die bestehende Endpunkte
(`GetHeadAsync`/`GetRevisionAsync` mit `BucketScope`) wiederverwendet.

## Änderungswunsch

Der geteilte Speicherstand ist heute funktional und visuell zu unauffällig: neue
Clients merken nicht, dass für ein erkanntes Spiel schon ein geteilter Stand
existiert, und sobald ein Spiel „Synchron" ist, verschwindet jede Anzeige davon, wer
zuletzt gespeichert hat, wann und wie groß der Stand ist — der lokale (eingefrorene)
Stand ist unsichtbar und es gibt keinen Weg zurück. Ziel: im Client unter jedem Spiel
**dauerhaft** sichtbar machen, welcher Stand (Server/geteilt oder Lokal) gerade aktiv
ist, mit echten Kennzahlen für beide Seiten, und **nahtlos per Klick** zwischen beiden
wechseln können — ohne Bestätigungsdialog, da der jeweils inaktive Stand als Backup
erhalten bleibt. Das Dashboard bekommt eine passend übersichtlichere, aber rein
visuelle Überarbeitung des Spiel-Drawers, gleiche Funktionalität wie heute. Optik
verspielter, mit Animationen/Piktogrammen, innerhalb der bestehenden Design-Sprache
aus `design-reference/`.

Tim hat dafür ein konkretes visuelles Referenz-Mockup in Claude Design gebaut, das
als `design-reference/SpielstandSyncRedesign.dc.html` im Projekt liegt — verbindlich
für **Aufbau, Kästen, Verbindung zueinander und Buttons** des Spiel-Detailbereichs im
Client (nicht für die genauen Farbwerte — die bleiben aus `Ui/Theme.xaml`/
`design-reference/`). Siehe die Datei-Beschreibung in `design-reference/README.md`.

## Betroffene Fläche (Right-sizing)

**Phase 1 (jetzt zur Freigabe):**
- [x] **Oberfläche** (Client-UI) → `oberflaechen-bauer`
- [x] **Kern** (Client-seitige Logik: neue `SwitchToLocalAsync`, Probe-Timing) → `bauer`
  (Kern zuerst, dann Oberfläche)
- [ ] Neue/veränderte Sicherheitsfläche — **entfällt**: keine neuen Endpunkte, keine
  neue Fremddaten-/Eingabefläche; die zwei Kästen zeigen dieselben Felder, die
  `ShareCompareWindow` heute schon über dieselben Endpunkte anzeigt.
- [ ] Abhängigkeit/Stack ändert sich — entfällt.
- [ ] Öffentliche Schnittstelle ändert sich — entfällt (kein API-Vertragsbruch,
  `ISaveVaultApi` unverändert).

**Phase 2 (separate Freigabe nach Phase 1, hier nur grob geplant):**
- [x] **Oberfläche** (Dashboard `app.js`/`styles.css`) → `oberflaechen-bauer`
- Rein visuell, keine neue Server-Logik, kein neuer Endpunkt.

## Delta im Detail

### Kern-Delta (Phase 1)

- **Neues Verhalten „Zurück zu Lokal":** `ClientAgent` bekommt eine neue Methode
  (z. B. `SwitchToLocalAsync(GameKey game)`), die spiegelbildlich zu
  `ShareAndSyncAsync` arbeitet: `_shares.Remove(game)`, `State.SetShared(game,
  false)`, danach synct das Gerät wieder gegen seinen **eigenen, seit dem Teilen
  eingefrorenen privaten Bucket** — **keine Datenbewegung nötig**, da
  `SyncStateStore` privat/geteilt schon getrennt hält (Phase 2 des Ursprungs-Deltas).
  Das ist die Umsetzung des in `per-device-sync.md` bewusst zurückgestellten Punkts
  „Teilen wieder ausschalten" — jetzt **pro Gerät**, nicht global (der geteilte
  Bucket selbst bleibt für andere Geräte unangetastet bestehen).
- **Probe wird zum Dauerzustand statt Einmal-Klick:** `ProbeShareAsync` (oder ein
  leichtgewichtiger Ableger) wird aufgerufen, **sobald der Detail-Bereich eines
  Spiels sichtbar ist** (Auswahl im Dropdown), nicht mehr nur beim Klick auf den
  Teilen-Button — damit der Server-Kasten immer aktuelle Kennzahlen zeigt, auch wenn
  das Spiel schon „Synchron" ist oder noch gar nicht geteilt wurde. Ergebnis wird in
  neuen `GameRow`-Properties gehalten (Herkunftsgerät/Zeitpunkt/Größe/Dateien des
  geteilten Stands; Zeitpunkt/Größe/Dateien des lokalen Stands). Muss tolerant sein,
  wenn der Server offline ist (Kasten zeigt „—"/letzten bekannten Stand, kein Absturz).
- **`ShareCompareWindow` entfällt** vollständig — sowohl die Datei als auch ihr
  Aufruf in `MainWindow.xaml.cs` (`OnToggleShareClick`). Die Erstteilen-Entscheidung
  „wessen Stand wird der geteilte" passiert jetzt **innerhalb** der Zwei-Kästen-
  Ansicht: existiert noch kein geteilter Bucket, zeigt der Server-Kasten einen
  Leer-/CTA-Zustand („Noch kein geteilter Stand"); Klick darauf seedet den lokalen
  Stand ohne Rückfrage (wie heute beim „Nein"-Zweig der Probe).
- **Kein Dialog mehr bei Erst-Erkennung eines schon geteilten Spiels:** wird ein
  Spiel neu erkannt (bisher nicht verwaltet) und es existiert bereits ein geteilter
  Bucket, zeigt die Zwei-Kästen-Ansicht sofort die echten Kennzahlen des geteilten
  Stands (Server-Kasten) neben dem frisch erkannten lokalen Stand (Lokal-Kasten,
  aktiv per Default) — der Nutzer entscheidet **manuell** per Klick. Kein Popup, kein
  Toast (das ist ausdrücklich auf ein späteres Release vertagt, siehe „Nicht-Ziele").
- **Entferntes Verhalten:** der einmalige Vergleichsdialog beim Umschalten
  (`ShareCompareWindow`) fällt weg — ersetzt durch die dauerhafte Ansicht.
  Kein Bruch für Aufrufer, da rein clientinterne UI.
- **Keine Datenstruktur-/Persistenz-Änderung, keine Migration** — `GameShareStore`
  bleibt wie es ist (`Add`/`Remove`/`IsShared` existieren schon vollständig).

### Oberflächen-Delta (Phase 1 — Client, nach `design-reference/SpielstandSyncRedesign.dc.html`)

- **Zwei-Kästen-Ansicht** im Detail-Bereich, unterhalb des Ordnerpfads, oberhalb der
  Aktionsreihe: links **„Geteilter Speicherstand"**, rechts **„Lokaler
  Speicherstand"** (Reihenfolge exakt wie im Mockup: Server links, Lokal rechts —
  weicht von der Ursprungsfrage in diesem Grill-Prozess leicht ab, ist aber Tims
  ausdrückliche Design-Vorgabe aus dem Mockup und hat Vorrang).
  - **Aktiver Kasten:** grüner Rahmen (Akzentfarbe aus `design-reference/`, nicht das
    Mockup-Grün wörtlich), dezenter Puls/Glow (passt zu „dezente Animationen
    Puls/Shimmer" aus der bestehenden Design-Sprache), Pille „✓ Aktiv".
  - **Inaktiver Kasten:** deutlich ausgegraut (reduzierte Deckkraft/Sättigung), Pille
    „Inaktiv" — Kennzahlen bleiben **lesbar**, nur optisch zurückgenommen.
  - **Klick auf den inaktiven Kasten** löst den Wechsel aus (`SwitchToLocalAsync`
    bzw. den bestehenden Seed-/Join-Pfad) — **sofort, ohne Bestätigungsdialog**.
  - **Felder Server-Kasten:** Herkunfts-Gerät, Zeitpunkt (absolut + relativ „vor
    X Tagen"), Größe, Dateizahl.
  - **Felder Lokal-Kasten:** Zeitpunkt der letzten lokalen Änderung (absolut +
    relativ), Größe, Dateizahl — kein Herkunftsgerät (ist dieses Gerät selbst).
  - **Piktogramme** je Kasten: Server-Kasten bekommt ein kleines Server-/Wolken-Symbol
    (Mockup: stilisierte zwei-drei überlappende Kreise/Blöcke, sinngemäß „entfernt/
    Netzwerk"), Lokal-Kasten ein PC-/Monitor-Symbol (Mockup: umrandetes Rechteck mit
    Standfuß, sinngemäß „dieses Gerät") — als Vektor-Icon in der bestehenden
    Symbolsprache des Clients (`Ui/`-Icons, gleiche Strichstärke/Stil wie die
    vorhandenen Status-Punkte/Chevrons), keine Fotos/Bitmaps. Exakte Pfad-Geometrie
    ist Ermessen des `oberflaechen-bauer`, solange die Bedeutung (Server vs. dieses
    Gerät) auf einen Blick erkennbar bleibt.
- **„Sicherung deaktivieren"-Leiste** unterhalb der zwei Kästen: ersetzt den
  heutigen Chip-Button „Hochladen deaktivieren"/„wieder aktivieren" durch eine
  eigene horizontale, klickbare Leiste mit demselben Aktiv-/Inaktiv-Kontrast wie die
  Kästen (gedämpft, wenn nicht aktiv; deutlich rot/warnend + „klicken zum
  Reaktivieren", wenn aktiv). Funktional **unverändert** (`GameExclusionStore`).
- **Aktionsreihe:** „Jetzt sichern" (deaktiviert/grau, wenn „Sicherung deaktiviert"
  aktiv ist), „Ordner öffnen", dann rechtsbündig **„Versionshistorie"** als
  Toggle-Button mit Chevron.
- **Versionshistorie wird Flyout:** die heute fest sichtbare Historie-Liste wird ein
  **Flyout-Panel** (WPF `Popup`, `StaysOpen="False"`, analog dem bestehenden
  `GameDropdownPopup`-Muster in `MainWindow.xaml`), das per Klick auf
  „Versionshistorie" seitlich neben dem Hauptfenster aufklappt und beim nächsten Klick
  außerhalb oder erneutem Button-Klick wieder schließt. Öffnen-Transition: kurzer
  Fade+leichter Slide aus Richtung des Buttons (≈150–200ms, ease-out) — dieselbe
  Größenordnung wie die bereits im Client vorhandenen kurzen UI-Übergänge, kein
  auffälliges/langsames Einfliegen. Inhalt/Funktion (Liste, „Wiederherstellen" je
  Revision) bleibt unverändert — nur die Präsentation ändert sich von „immer
  sichtbar" zu „auf Abruf".
- **Konflikt/Skip bleiben wie heute:** die Zwei-Kästen-Ansicht wird bei offenem
  Konflikt ausgeblendet (analog heutigem `ShareVisibility=Collapsed`); übersprungene
  Spiele zeigen weiter die einfache Hinweiszeile mit „Ordner zuordnen"-Chip. Kein
  Redesign dieser beiden seltenen Zustände in diesem Delta.
- **Visuelle Absicht:** Farben/Radien/Grundsprache bleiben, wie in
  `design-reference/README.md` festgelegt (dark-only, Teal-Akzent, 12–16px-Radius,
  dezente Puls-/Shimmer-Animationen) — das Mockup liefert **Struktur, Kästen,
  Verbindung, Buttons**, nicht die exakte Farbpalette. Client ist ohnehin
  dark-only (kein Hell-Modus-Erfordernis).

### Oberflächen-Delta (Phase 2 — Dashboard, spätere Freigabe, hier nur grob geplant)
- Spiel-Drawer im Dashboard bekommt eine der Client-Ansicht **ähnliche** Karten-Optik
  (aktiv/inaktiv-Kontrast, Piktogramme, dezente Transitions) für die **bestehenden**
  Aktionen (Teilen etablieren, Restore, Export, Legacy löschen) — **keine neue
  Funktion**. Genaues Layout wird zu Beginn von Phase 2 mit einem eigenen kurzen
  Grill-Durchgang/Mockup-Abgleich präzisiert, falls nötig.

## Was gleich bleibt (Nicht-Ziele)

- **Server-/API-Vertrag unverändert** in Phase 1 — keine neuen Endpunkte, keine
  geänderten DTOs, `ISaveVaultApi` bleibt stabil.
- **Kein Auto-Merge, keine automatische Übernahme** — der Wechsel Synchron ↔ Lokal
  bleibt eine **bewusste, manuelle** Handlung des Nutzers (Klick auf den Kasten).
  Kein automatisches Umschalten anderer Geräte.
- **Kein Toast/Popup bei Erst-Erkennung** eines schon geteilten Spiels — bewusst auf
  ein späteres Release vertagt (Tims Entscheidung in diesem Grill-Durchgang).
  Bleibt „manuelle Entscheidung im Client" für jetzt.
- **„Teilen" für alle Geräte rückgängig machen** (geteilten Bucket komplett
  auflösen) bleibt außerhalb des Umfangs — nur der **Beitritt/Austritt** eines
  einzelnen Geräts wird nahtlos.
- **Dashboard-Funktionalität unverändert** — Admin kann weiterhin nur „Teilen
  etablieren"/Legacy löschen/Export/Restore; **kein** Admin-Umschalter, der ein
  fremdes Gerät zwischen Synchron/Lokal hin- und herschaltet (bleibt Client-Hoheit,
  Tims ausdrückliche Entscheidung).
- **Konflikt-Mechanik und Skip-Erkennung unverändert** — keine funktionale
  Änderung, kein visuelles Redesign dieser Zustände.
- **Kein Hell-Modus-Erfordernis** (Client ist dark-only, anders als das Web-Dashboard).

## Sicherheitsflächen (nur die NEU berührten ankreuzen)
- [ ] Eingaben von außen fließen neu in Logik/Ausgabe — nein, dieselben Felder wie
  heute (`ShareSide`), nur häufiger abgefragt und dauerhaft angezeigt statt einmalig.
- [ ] Neue Kommando-/Query-/Pfad-Ausführung aus Eingabewerten — nein.
- [ ] Neue ausgehende Requests an dynamische Ziele — nein, dieselben Endpunkte
  (`GetHeadAsync`/`GetRevisionAsync`), nur öfter aufgerufen (Performance-Aspekt,
  keine Sicherheitsfläche — Rate/Timing gegen den eigenen, vertrauten Server).
- [ ] Neue Secrets im Spiel — nein.

**Kein `security-auditor`-Einsatz in Phase 1** vorgesehen (keine neue Fläche); wird
im Kern-Gate kurz begründet bestätigt statt übersprungen.

## Akzeptanzkriterien der Änderung

**Neu (Phase 1):**
- [ ] Ein Spiel mit geteiltem Bucket zeigt dauerhaft zwei Kästen (Server/Lokal) mit
  den vereinbarten Kennzahlen; der aktive ist eindeutig optisch hervorgehoben, der
  inaktive lesbar, aber klar ausgegraut.
- [ ] Klick auf den inaktiven Kasten wechselt **sofort** (ohne Dialog) den aktiven
  Stand; ein Wechsel Synchron→Lokal verliert keine Daten (privater Bucket bleibt als
  Backup) und ein späterer Rückwechsel Lokal→Synchron funktioniert weiterhin über
  denselben Mechanismus.
- [ ] Ein neu erkanntes Spiel mit bereits existierendem geteiltem Bucket zeigt sofort
  die echten Kennzahlen im Server-Kasten, ohne dass der Nutzer manuell etwas
  anstoßen muss, um sie zu sehen.
- [ ] „Sicherung deaktivieren" funktioniert wie heute (`GameExclusionStore`
  unverändert), nur in neuer Leisten-Optik.
- [ ] Versionshistorie ist per „Versionshistorie"-Button als Flyout erreichbar,
  Inhalt/Wiederherstellen-Funktion unverändert.
- [ ] `ShareCompareWindow` ist entfernt, keine toten Verweise/Referenzen mehr im Code.

**Regression:**
- [ ] Alle Pfade aus „Ist-Zustand (Baseline)" funktionieren weiter: Spiel wechseln,
  Ordner öffnen, Jetzt sichern, Konflikt lösen, Ordner zuordnen (Skip), Restore aus
  der Historie.
- [ ] Build 0/0, bestehende Tests weiter grün, Client startet und der Detail-Bereich
  rendert für Spiele in allen Zuständen (Lokal, Synchron, Deaktiviert, Konflikt,
  Skip) ohne Absturz und ohne Konsolenfehler.
- [ ] Kein API-/DTO-Vertragsbruch — Server unverändert lauffähig mit dem neuen
  Client, alte Clients bleiben mit dem unveränderten Server kompatibel.

## Plan-Korrektur (2026-09-07, Nachtrag 2) — Gewinner-Gerät bleibt nach Konfliktlösung fälschlich „Konflikt"

**Was sich am echten Test zeigte:** Tim löste den Arc-Raiders-Konflikt über das
Dashboard („Gerät behalten"). Danach zeigte sein Client weiterhin „Konflikt" für Arc
Raiders — weder Warten noch „Jetzt sichern" änderte etwas.

**Ursache (vorbestehender Bug, nicht durch die heutigen Änderungen verursacht):**
`VaultStore.ResolveKeepDevice` und `ResolveKeepBoth`
(`src/SaveVault.Server/Storage/VaultStore.cs`) reihen einen `ApplyResolution`-Befehl
nur für die **verlierenden** Geräte in die Warteschlange ein
(`if (p.DeviceId == winnerDevice) continue;` bzw. das Pendant in `ResolveKeepBoth`).
Das **gewinnende** Gerät bekommt nie einen Befehl — nur eine reine Server-Buchhaltung
(`SetDeviceGameState(winnerDevice, ...)`). Sein lokaler `SyncStateStore`
(Base-Revision/-Manifest) und die Konflikt-Marke (`ConflictHash`) bleiben auf dem
Stand von vor dem Konflikt eingefroren. Jeder folgende Sync-Zyklus vergleicht den
aktuellen lokalen Inhalt weiterhin gegen diesen alten, eingefrorenen Stand und die
neue (höhere) Server-Revision — `SyncDecider` liefert dadurch dauerhaft `Conflict`,
obwohl Server und alle anderen Geräte längst konvergiert sind.

**Fix (klein, gezielt, serverseitig):** In beiden Methoden auch dem **Gewinner-Gerät**
einen `ApplyResolution`-Befehl einreihen (gleicher Befehlstyp, den die Verlierer schon
bekommen; `CommandPoller.cs` verarbeitet ihn bereits korrekt: lädt die aktuelle
Head-Revision, schreibt sie über `SyncEngine.ApplyRevisionAsync` — idempotent, da
Inhalt beim Gewinner bereits identisch ist —, zieht `SyncState` nach und löscht die
Konflikt-Marke). Kein neuer Befehlstyp, keine neue API, keine neue Sicherheitsfläche —
reine Erweiterung des bestehenden, bereits genutzten Mechanismus auf den bisher
übersehenen Teilnehmer.

**Betroffene Datei (neu, nicht vorher Teil dieses Deltas):**
`src/SaveVault.Server/Storage/VaultStore.cs` (`ResolveKeepDevice`, `ResolveKeepBoth`).

**Akzeptanzkriterium (Nachtrag 2):**
- [ ] Nach einer über das Dashboard gelösten „Gerät behalten"- oder „Beide
  behalten"-Konfliktlösung zeigt **auch das gewinnende Gerät** nach dem nächsten Sync
  (oder „Jetzt sichern") wieder „Synchronisiert" statt weiter „Konflikt".
- [ ] Arc Raiders (Tims echter, aktuell hängender Fall) löst sich nach dem Fix beim
  nächsten Sync/„Jetzt sichern" auf.
- [ ] Regression: das Verhalten für die Verlierer-Geräte (die schon vorher korrekt
  einen Befehl bekamen) bleibt unverändert.

## Plan-Korrektur (2026-09-07, Nachtrag 3) — Verwaister Konflikt-Status bei Arc Raiders lässt sich nicht mehr lösen

**Was sich zeigte:** Nachtrag 2 ist deployt (Server 1.5.10), aber Arc Raiders zeigt
weiterhin „Konflikt", auch nach Client-Neustart und „Jetzt sichern". Ursache: der
Konflikt wurde von Tim **vor** dem Deploy des Nachtrag-2-Fixes über das Dashboard
aufgelöst — für dieses eine, bereits vergangene Ereignis wurde nie ein
`ApplyResolution`-Befehl für das Gewinner-Gerät erzeugt (der Fix wirkt erst auf
**künftige** Auflösungen). Der Konflikt-Datensatz selbst ist server-seitig bereits
`Resolved=true` — „Lösen" findet ihn deshalb korrekterweise nicht mehr.

**Zusätzlich gefunden:** `SyncEngine.NoOp` (Zeile ~210-216) hält den Anzeige-Status
absichtlich auf `Conflict`, solange `_state.GetStatus(game) == SyncStatus.Conflict`
ist — unabhängig davon, was die aktuelle `SyncDecider`-Entscheidung sagt. Der
**einzige** Ort im gesamten Client, der diesen Status je explizit auf `Synced`
zurücksetzt, ist `CommandPoller.ApplyResolution` (Zeile 161-162). Weder
`SwitchToLocalAsync` noch `JoinTakeSharedAsync`/`ReplaceLocalContentAsync` setzen
nach einem erfolgreichen, garantiert exakten Ordner-Austausch den Anzeige-Status
explizit zurück — ein Nutzer hat also **keinen** eigenständigen Weg, einen
verwaisten Konflikt-Status loszuwerden, außer über einen (u.U. nie kommenden)
Server-Befehl.

**Fix:** `ReplaceLocalContentAsync` (`SyncEngine.cs`) ruft nach erfolgreichem
Austausch zusätzlich `_state.SetStatus(game, SyncStatus.Synced, ...)` auf — ein
erfolgreicher, exakter Austausch ist per Definition ein garantiert sauberer,
bekannter Zustand, unabhängig davon, ob vorher „Konflikt" angezeigt wurde. Das gibt
dem Nutzer über die neue Lokal/Synchron-Umschaltung einen sofortigen,
selbstständigen Weg aus einem verwaisten Konflikt-Status heraus — ohne die
bestehende `NoOp`-Absicht zu ändern (ein bloßer No-Op-Zyklus soll weiterhin nichts
verschweigen).

**Für Arc Raiders konkret:** nach diesem Fix (+ Neustart des Clients mit dem
aktualisierten Build) einmal auf den inaktiven Kasten klicken (Lokal↔Synchron
umschalten) — das räumt den verwaisten Status auf.

**Betroffene Datei:** `src/SaveVault.Client/Services/SyncEngine.cs`
(`ReplaceLocalContentAsync`).

**Akzeptanzkriterium (Nachtrag 3):**
- [ ] Nach einem erfolgreichen Umschalten (Lokal↔Synchron) zeigt das Spiel
  „Synchronisiert", auch wenn vorher „Konflikt" angezeigt wurde.
- [ ] Arc Raiders lässt sich darüber freischalten.
- [ ] Regression: `NoOp`-Verhalten (Konflikt bleibt sichtbar, solange nichts
  Aktives passiert) bleibt für alle anderen Fälle unverändert.

## Plan-Korrektur (2026-09-07, Nachtrag 4) — Nachtrag 3 ist über die UI nicht erreichbar

**Was sich zeigte:** Der Fix aus Nachtrag 3 (Status-Reset in `ReplaceLocalContentAsync`)
ist zwar korrekt, aber für den verwaisten Arc-Raiders-Fall **unerreichbar**: die
Zwei-Kästen-Ansicht — der einzige Weg, `SwitchToLocalAsync`/`JoinTakeSharedAsync`
auszulösen — wird bei `SyncStatus.Conflict` bewusst ausgeblendet
(`DetailBoxesVisibility=Collapsed`, siehe Ist-Zustand/Oberflächen-Delta oben). Für ein
Spiel mit verwaistem Konflikt-Status bleibt also **einzig** der „Lösen"-Knopf sichtbar
— der aber (korrekt) keinen Server-Konflikt findet und dann nur eine Info-Meldung
zeigt, ohne den lokalen Status zu berühren. Ein Teufelskreis ohne Ausweg über die UI.

**Fix:** `OnResolveConflictClick` (`MainWindow.xaml.cs`) wird im „kein Konflikt
gefunden"-Zweig erweitert: findet „Lösen" keinen passenden, offenen Server-Konflikt
für ein Spiel, das lokal trotzdem als „Konflikt" markiert ist, ist das selbst das
Signal, dass die lokale Marke verwaist ist (der Server hat nichts mehr offen). Statt
nur einer Info-Meldung wird jetzt eine neue, kleine `ClientAgent`-Methode
(Arbeitstitel `ClearOrphanedConflictAsync(GameKey game)`) aufgerufen. **Wichtig,
Korrektur gegenüber dem ersten Entwurf:** bloßes Löschen der Konflikt-Marke reicht
nicht — die eigentlich eingefrorene `SyncState` (Basis-Revision/-Manifest) bleibt
davon unberührt, ein danach angestoßener regulärer Sync-Zyklus würde sofort wieder
„Konflikt" erkennen. Die Methode übernimmt daher stattdessen den **aktuellen
Server-Head des aktiven Scopes als neue, verbindliche Basis** — über dieselbe exakte
Austausch-Funktion (`SyncEngine.ReplaceLocalContentAsync`) wie beim Umschalten
(Nachtrag 1/2), die dank Nachtrag 3 auch den Anzeige-Status korrekt auf „Synced"
zurücksetzt. Inhaltlich richtig, weil der Server-Konflikt ja bereits (über das
Dashboard) aufgelöst wurde — sein aktueller Head ist die gewollte Fassung.

**Ergänzung nach Rückfrage bei Tim:** Da diese Aktion den **aktiven** lokalen
Ordnerinhalt sofort ersetzt (anders als der Kästen-Klick, wo der jeweils inaktive
Stand als Backup erhalten bleibt), bekommt sie — analog zum Force-Upload-Knopf —
**vorher** einen kurzen Kennzahlen-Vergleich (Lokal vs. Server-Head des aktiven
Scopes: Größe, Dateizahl, Zeitpunkt) und eine echte Bestätigung, statt sofort ohne
Rückfrage zu übernehmen. Keine neue Dialog-Infrastruktur nötig — ein einfacher
`MessageBox`-artiger Ja/Nein-Hinweis mit den Zahlen reicht. Dafür braucht
`ClientAgent` einen zusätzlichen, kleinen Abfrage-Schritt (Vorschau der beiden
Seiten) **vor** dem bereits fertigen `ClearOrphanedConflictAsync` (das bleibt der
Ausführungs-Schritt nach Bestätigung).

**Betroffene Dateien (neu):**
`src/SaveVault.Client/Services/ClientAgent.cs` (neue Methode),
`src/SaveVault.Client/MainWindow.xaml.cs` (`OnResolveConflictClick` erweitert).

**Akzeptanzkriterium (Nachtrag 4):**
- [ ] Klick auf „Lösen" bei einem Spiel mit verwaistem (server-seitig nicht mehr
  existierendem) Konflikt-Status zeigt zuerst Lokal- vs. Server-Kennzahlen und eine
  Bestätigung; erst nach „Ja" wird der Status sichtbar zurückgesetzt (nicht mehr
  „Konflikt"), ohne dass der Nutzer die Zwei-Kästen-Ansicht braucht.
- [ ] „Nein"/Abbrechen ändert nichts (Ordner und Status bleiben wie sie waren).
- [ ] Arc Raiders lässt sich darüber freischalten.
- [ ] Ein **echter**, noch offener Server-Konflikt wird weiterhin korrekt gefunden
  und öffnet wie bisher den `ConflictWindow`-Dialog (keine Regression am
  eigentlichen Lösen-Weg).

## Offene Fragen
- Phase 2 (Dashboard) wird erst nach Abschluss und Abnahme von Phase 1 im Detail
  ausgeplant und braucht eine eigene Freigabe, bevor daran gebaut wird.
- **Stand 2026-09-08:** Beim Anlauf von Phase 2 zeigte sich, dass es noch kein
  Mockup für die Drawer-Karten-Optik gibt (das vorhandene `SpielstandSyncRedesign`-
  Mockup deckt nur den Client ab) und das „aktiv/inaktiv"-Kriterium im Drawer nicht
  eindeutig ist, da ein Spiel dort mehrere Buckets gleichzeitig zeigen kann (Geteilt +
  je Gerät ein Lokal-Bucket + ggf. Konflikt-Kopie) — anders als die klaren zwei Kästen
  im Client. Tim erstellt dafür noch ein eigenes, detailliertes Mockup. **Phase 2 ruht
  bis dahin** — nicht von selbst weiterplanen/bauen, auf Tims neues Mockup warten.

## Plan-Korrektur (2026-09-07) — Umschalten muss ein exakter Austausch sein, nicht additiver Sync

**Was galt:** Die ursprüngliche Kern-Delta-Annahme war „keine Datenbewegung nötig, die
bestehende Teilen-Mechanik (`SeedShareAsync`/`JoinTakeSharedAsync`/`JoinTakeLocalAsync`,
neu `SwitchToLocalAsync`) reicht, weil `SyncStateStore` privat/geteilt schon getrennt
hält". Das war zu optimistisch für den jetzt tatsächlich gewünschten Nutzungsablauf
(wiederholtes, freies Hin- und Herschalten).

**Was sich am echten Test zeigte (Tim, 2026-09-07):** Ein Spiel geriet nach mehrfachem
Umschalten Lokal↔Synchron in einen Konflikt-Zustand, der über „Lösen" nicht auflösbar war
(„es gäbe keinen Konflikt", obwohl die Zeile Konflikt zeigte) — reproduzierbar, betrifft
mindestens „Arc Raiders". Ursache identifiziert: `SyncEngine.ApplyRevisionAsync` (in
`src/SaveVault.Client/Services/SyncEngine.cs`) schreibt beim Anwenden eines Standes nur
die Dateien des **Ziel**-Manifests in den Ordner — sie **löscht nie** Dateien, die zum
vorherigen Stand gehörten, aber nicht im neuen Manifest enthalten sind. Nach einem
Umschalten entspricht der Ordnerinhalt damit keinem der beiden Manifeste mehr exakt. Der
reguläre `SyncDecider` (gedacht für „mehrere Geräte ändern nebenläufig", Fall 3: „lokal
geändert UND Server-Revision > base") interpretiert diese Restdifferenz als echte lokale
Änderung und kann daraus fälschlich `SyncAction.Conflict` ableiten — obwohl der Nutzer nur
bewusst zwischen zwei vollständigen, in sich konsistenten Ständen hin- und hergeschaltet
hat. Zusätzlich nutzt `OnResolveConflictClick`/`GetConflictsAsync` eine ältere, gegen den
Spiel-Schlüssel abgefragte Konfliktliste, die nach der Bucket-Umstellung (Scope-Präfixe)
den betroffenen Konflikt-Datensatz nicht zuverlässig wiederfindet — daher die Fehlmeldung
„kein Konflikt" trotz sichtbarem Konflikt-Status.

**Tims Korrektur des Modells (verbindlich ab jetzt):** Das Umschalten Lokal↔Synchron ist
eine **bewusste, exklusive Einzelentscheidung des Nutzers** — kein Mehrgeräte-Sync-Fall
und darf **nie** einen Konflikt erzeugen können:
- Kein geteilter Stand vorhanden → lokaler Stand wird hochgeladen (Seed, wie bisher).
- Geteilter Stand vorhanden → wird heruntergeladen und **ersetzt exakt** den Ordnerinhalt;
  der lokale Stand bleibt unangetastet in seinem eigenen privaten Bucket erhalten
  (vorausgesetzt, er wurde vorher dorthin hochgeladen — siehe Akzeptanzkriterium unten).
- Umschalten ist technisch ein **reiner Austausch** des Ordnerinhalts gegen den jeweils
  anderen bekannten Stand (Ziel-Bucket-Inhalt rein, alles was nicht zum Ziel-Manifest
  gehört raus) — **nie** über die reguläre Mehrgeräte-Konflikterkennung.
- Der einzige denkbare Sonderfall — der lokale Stand ist neuer als der aktuell geteilte
  und der Nutzer möchte ihn bewusst als neuen geteilten Stand hochladen, obwohl er gerade
  „Synchron" ist oder war — bekommt einen **eigenen, expliziten Knopf** mit
  Metadaten-Vergleich zum aktuell geteilten Stand (nicht das normale Umschalten).

**Erweiterter Umfang (Teil von Phase 1, nicht Phase 2):**

*Kern-Delta (Nachtrag):*
- Neue **exakte Austausch-Funktion** in `SyncEngine` (Arbeitstitel
  `ReplaceLocalContentAsync` oder vergleichbar) für den Umschalt-Pfad. **Sichere
  Reihenfolge, verbindlich (nicht Ermessen des Bauers):** erst wie im bestehenden
  `ApplyRevisionAsync` **alle** Ziel-Dateien vollständig herunterladen und nach
  `*.svtmp-…` schreiben, validieren; **danach erst**, in einem letzten kurzen Schritt,
  die überzähligen Alt-Dateien (im lokalen Ordner vorhanden, aber nicht im
  Ziel-Manifest) löschen und die Temp-Dateien per `File.Move(..., overwrite: true)`
  an ihren Platz verschieben. **Nie zuerst löschen und danach laden** — bricht der
  Download mittendrin ab (Server offline, Verbindungsabbruch), darf der lokale
  Save-Ordner nie in einem Zustand landen, der weder dem alten noch dem neuen Stand
  entspricht (Datenverlust-Risiko bei einem Backup-Tool, siehe `CLAUDE.md` →
  „Fehlerzustände abfangen"). Gleiche Pfad-Sicherheits-Validierung wie
  `ApplyRevisionAsync` (`PathSanitizer`, Alles-oder-nichts vor dem ersten Schreiben).
  `ApplyRevisionAsync` selbst bleibt für seine bisherigen Aufrufer (`CommandPoller.cs`
  Restore/ApplyResolution, beide scope-intern, kein Scope-Wechsel) **unverändert**.
- `SeedShareAsync`/`JoinTakeSharedAsync`/`JoinTakeLocalAsync`/`SwitchToLocalAsync`
  nutzen für das Schreiben in den Ordner ab jetzt die neue exakte Austausch-Funktion
  statt (indirekt) der additiven `ApplyRevisionAsync`, wo sie den Ordner auf einen
  anderen Bucket-Stand umstellen.
- Nach jedem Umschalten müssen **beide** betroffenen `SyncState`-Einträge (privat und
  geteilt) so gesetzt sein, dass unmittelbar danach `LocalChanged=false` für den jetzt
  aktiven Scope gilt — kein Folge-Sync-Zyklus darf direkt nach dem Umschalten fälschlich
  Upload/Conflict auslösen.
- **Neuer expliziter Knopf „Als geteilten Stand hochladen"** (Name Vorschlag, endgültige
  Beschriftung Ermessen des `oberflaechen-bauer`): nur sichtbar/aktiv, wenn der lokale
  Stand gerade aktiv ist UND bereits ein geteilter Stand existiert (sonst ist der normale
  Server-Kasten-Klick der Seed-Weg). Zeigt vorher einen Metadaten-Vergleich (Größe,
  Dateizahl, Zeitpunkt beider Seiten — dieselben Felder wie die Kästen selbst, kein neuer
  Dialog-Typ nötig, ggf. reicht eine Inline-Bestätigung direkt am Knopf) und lädt nach
  Bestätigung den lokalen Stand als neue geteilte Revision hoch (bestehender
  Upload-Mechanismus, analog `JoinTakeLocalAsync`). Fehlerfrei auch dann, wenn der lokale
  Stand **nicht** neuer ist (Nutzer entscheidet selbst, keine erzwungene Aktualitätsprüfung).
- **Verbindlicher Fix, nicht optional:** `OnResolveConflictClick`/`ClientAgent.
  GetConflictsAsync` vergleichen einen Konflikt-Datensatz heute über
  `c.Game.Equals(row.Game)` — `c.Game` trägt aber seit der Bucket-Umstellung den
  **gescopten** Schlüssel (`dev|{owner}|{value}` bzw. `shared|{value}`), `row.Game`
  den **kanonischen** Wert. Diese beiden können nie gleich sein (`GameKey.Equals`
  vergleicht nur den rohen String) — „Lösen" ist dadurch **strukturell für praktisch
  jeden Konflikt kaputt**, nicht nur für Arc Raiders, und das ist unabhängig vom
  eigentlichen additiven-Schreiben-Bug. Muss behoben werden (z. B. Abgleich über
  `BucketKey.Original(c.Game)` gegen `row.Game`, oder äquivalente Normalisierung in
  `GetConflictsAsync`) — **Teil dieses Nachtrags**, kein optionaler Nebeneffekt.
- **Arc Raiders (und jeder ähnlich betroffene Bestand):** einmalige Prüfung/Bereinigung
  des aktuell hängenden Konflikt-Zustands, nachdem beide obigen Fixes stehen (additive
  Schreiben-Ursache + Schlüssel-Abgleich) — konkret am Kern-Gate nachgewiesen und im
  Bericht benannt.

*Oberflächen-Delta (Nachtrag):*
- Neuer Knopf „Als geteilten Stand hochladen" in der Aktionsreihe oder direkt am
  Server-Kasten (Ermessen `oberflaechen-bauer`, orientiert an der bestehenden
  Optik-Sprache).
- Nach jedem Seed/Übernehmen/Umschalten/**Hochladen (neuer Force-Knopf)** wird die
  Zwei-Kästen-Anzeige **sofort neu abgefragt** (erneuter `TryProbeShareAsync`-Aufruf),
  damit z. B. die CTA-Meldung „Noch kein geteilter Stand" nach dem Hochladen durch die
  echten Kennzahlen ersetzt wird (bisher blieb sie stehen — gemeldeter Bug aus dem
  Handtest).

**Betroffene Dateien (Nachtrag):** `src/SaveVault.Client/Services/SyncEngine.cs` (neue
Funktion), `src/SaveVault.Client/Services/ClientAgent.cs` (Umschalt-Methoden auf die neue
Funktion umstellen, neue Upload-Methode für den Force-Knopf), `src/SaveVault.Client/
Ui/GameRow.cs` + `MainWindow.xaml(.cs)` (neuer Knopf, Probe-Refresh nach Aktion).
`tests/SaveVault.Core.Tests/*` — neue Tests für die exakte Austausch-Logik, falls sie
testbare Kern-Anteile enthält (z. B. „welche Dateien werden zum Löschen vorgemerkt").

**Akzeptanzkriterien (Nachtrag):**
- [ ] Mehrfaches Umschalten Lokal→Synchron→Lokal→Synchron (min. 3 Zyklen) am selben
  Spiel erzeugt **keinen** Konflikt.
- [ ] Nach dem Umschalten entspricht der Ordnerinhalt exakt dem Ziel-Manifest (keine
  Restdateien des vorherigen Standes).
- [ ] Der private Bucket eines Spiels bleibt beim Umschalten auf „Synchron" als exakter
  Snapshot des Standes zum Zeitpunkt des Umschaltens erhalten (kein Vermischen mit
  Inhalten aus dem geteilten Bucket).
- [ ] „Als geteilten Stand hochladen" funktioniert unabhängig davon, ob der lokale Stand
  älter oder neuer als der aktuell geteilte ist; zeigt vorher die Kennzahlen beider Seiten.
- [ ] Nach Seed/Übernehmen/Umschalten aktualisiert sich die Zwei-Kästen-Anzeige sofort
  mit den echten Daten (keine veraltete CTA-Meldung).
- [ ] `OnResolveConflictClick` findet einen bestehenden, unresolved Konflikt für ein
  Spiel zuverlässig, unabhängig davon, ob der zugrundeliegende Bucket privat oder
  geteilt ist (Schlüssel-Abgleich über den gescopten Wert korrekt aufgelöst).
- [ ] Arc Raiders (oder ein vergleichbar präparierter Testfall) lässt sich nach dem Fix
  wieder normal bedienen — konkreter Nachweis am Kern-/Laufzeit-Gate.
- [ ] Bricht der Austausch-Vorgang mitten im Download ab (simulierter Verbindungsfehler),
  bleibt der lokale Save-Ordner **unverändert im alten Zustand** (kein Datenverlust,
  kein halb ausgetauschter Ordner).
- [ ] Bestehende, von diesem Nachtrag **nicht** betroffene Aufrufer von
  `ApplyRevisionAsync` (normales Revisions-Restore) verhalten sich unverändert
  (Regression).

Dieser Nachtrag läuft **erneut durchs Spec-Gate** (`reviewer` + `inspekteur`), bevor am
Kern weitergebaut wird.
