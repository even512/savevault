# Änderung: savevault — Sync-Anzeige- und Konflikt-Fixes rund um geteilte Speicherstände

> **Nachtrag (2026-09-15, noch während der Ausarbeitung):** Ein sechster, schwerwiegenderer Punkt
> kam während eines weiteren Realtests von Tim dazu (siehe „Fix 0" unten) — er steht bewusst **vor**
> den ursprünglichen fünf Punkten, weil er das Kernversprechen (verlässliche Synchronisation)
> betrifft, während die anderen fünf eher Anzeige-/Komfort-Bugs sind.

**Weg:** `/projekt-edit` · **Datum:** 2026-09-15

## Status
- **Freigegeben von Tim:** ja (2026-09-15)
- **Runde:** Freigegeben — Phase B′

## Bezug
- **Projekt-id / Ordner:** `savevault` (`<werkstatt>/savevault/`)
- **Basis-Spec:** `specs/savevault.md` + `specs/savevault-change-shared-save-sichtbarkeit.md`
  (Phase 1, released als Client v1.8.0–v1.8.4 / Server 1.5.2–1.5.6). Dieses Delta baut direkt
  darauf auf — Tims Realtest auf zwei echten Geräten (Notebook + Hauptrechner, Warcraft III
  Reforged) deckte sechs konkrete Folgefehler auf (fünf klar diagnostiziert, einer — Fix 0 — noch
  zu untersuchen).
- **Vorgeschichte dieser Sitzung (bereits erledigt, nicht Teil dieses Deltas):** `master` und
  `origin/master` waren seit Commit `8f2b722` acht bzw. sieben Commits lang unabhängig
  auseinandergelaufen (ein lokaler Zweig mit den Nachtrag-3/4-Fixes aus der Phase-1-Spec, ein
  gepushter Zweig mit den Releases v1.8.0–v1.8.3). Sauber gemergt (`f702a52`), Version auf
  **1.8.4** nachgezogen, getaggt und released — Build 0/0, Tests 195/195 grün. Tim testet auf
  diesem Stand weiter; die unten beschriebenen fünf Punkte sind das, was **danach** noch übrig
  blieb.
- **Betroffene Dateien/Komponenten:**
  - `src/SaveVault.Client/Services/SyncEngine.cs` (Fix 0: Untersuchung `RunCycleAsync`/
    `ApplyRevisionAsync`, ggf. Korrektur; neues Diagnose-Log)
  - `tests/SaveVault.Core.Tests/*` (Fix 0: neuer Nachstell-Versuch/Regressionstest)
  - `src/SaveVault.Server/Storage/VaultStore.cs` (`RegisterConflict`)
  - `src/SaveVault.Client/MainWindow.xaml.cs` (`LoadHistoryAsync`, `OnServerBoxClick`,
    `RefreshDetailIfNeeded`/`OnAgentStateChanged`)
  - `src/SaveVault.Client/ConflictWindow.xaml.cs`
  - `src/SaveVault.Client/Services/ClientAgent.cs` (ggf. kleine Ergänzung für den
    Opt-in-Entscheid; kein neuer Sync-Mechanismus)

## Ist-Zustand (Baseline)

**Funktioniert heute (v1.8.4):**
0. **Kein persistentes Diagnose-Log im Client.** Sync-Entscheidungen (Aktion, lokale/Basis-/
   Server-Revision je Zyklus) leben nur kurz im UI-Status (`GameRow.LastActionText`), nirgends
   dauerhaft nachvollziehbar. Nach einem abgeschlossenen Vorgang lässt sich nicht mehr
   rekonstruieren, welche Revisionsnummern zu welchem Zeitpunkt vorlagen.
0. **(Fix 0, höchste Priorität) Falscher Konflikt trotz nachweislich korrekt heruntergeladenem
   Stand.** Tims Repro (2026-09-15, nach dem Merge/Release, auf v1.8.4): Hauptrechner speichert
   einen neuen Stand für ein geteiltes Spiel (Warcraft III Reforged). Notebook zeigt danach den
   **nachweislich korrekten** Stand — nicht nur in der Kästen-Anzeige, sondern **im Spiel selbst**
   bestätigt (Tim hatte zuvor manuell gespeichert und der Spielstand war beim Laden tatsächlich da).
   Startet er das Spiel neu und speichert **einmal neu**, meldet der Client sofort **Konflikt** —
   obwohl laut `SyncDecider` (geprüft, Logik ist korrekt: `lokal geändert && Server ==
   base_revision` müsste **Upload** ergeben, nicht Conflict) genau das nicht passieren dürfte, wenn
   die lokale `SyncState.BaseRevision`/`BaseManifest` den gerade heruntergeladenen Stand korrekt
   wiedergibt. Der eigentliche Fehler liegt also vermutlich darin, **wie** `BaseRevision`/
   `BaseManifest` nach einem regulären Hintergrund-Download gesetzt werden — nicht in der
   Entscheidungslogik selbst. **Ursache noch nicht abschließend identifiziert** (siehe Kern-Delta,
   Fix 0, für den vorgesehenen Untersuchungsweg).
1. **Erstkontakt mit einem bereits existierenden geteilten Stand:** Klick auf den (inaktiven)
   Server-Kasten übernimmt den vorhandenen geteilten Stand **automatisch und ohne Rückfrage**
   (exakter Austausch, lokaler Stand bleibt als privates Backup erhalten). Wer stattdessen seinen
   lokalen Stand zum neuen geteilten Stand machen will, nutzt den separaten Knopf „Als geteilten
   Stand hochladen" (mit Kennzahlen-Vergleich + Bestätigung). Das ist eine bewusste
   Design-Entscheidung aus der Basis-Spec-Phase 1 („ohne Bestätigungsdialog, da der jeweils
   inaktive Stand als Backup erhalten bleibt").
2. **`VaultStore.RegisterConflict`** (Server): Erkennt der Server für ein Gerät, das **schon**
   Teilnehmer eines offenen Konflikts ist, erneut einen Konflikt (weitere lokale Änderung,
   Server-Revision weiterhin > Basis), wird **nichts aktualisiert** — weder die gespeicherte
   Teilnehmer-Revision dieses Geräts noch die des Gegenparts. Der Konflikt-Datensatz bleibt
   dauerhaft auf dem Stand der **erstmaligen** Erkennung eingefroren, auch wenn zwischenzeitlich
   (durch fortgesetztes Spielen) viele weitere Konflikt-Revisionen hochgeladen wurden.
3. **`ConflictWindow`** zeigt für das eigene Gerät den echten Namen, für **jedes andere** Gerät
   aber `Gerät {ShortId(DeviceId)}` (erste 8 Zeichen der rohen Geräte-ID) — unabhängig davon, ob
   der Servername bekannt ist. `RevisionInfo`/`RevisionDownload` tragen seit v1.8.1 bereits ein
   aufgelöstes `DeviceName`-Feld (Fix für die gleiche Klasse Bug bei der Versionshistorie), das
   `ConflictWindow.LoadParticipantsAsync` selbst schon lädt (`revisions.FirstOrDefault(r =>
   r.Number == participant.Revision)`), aber für das Label nicht nutzt.
4. **Zwei-Kästen-Ansicht** (`GameRow`/`MainWindow`): die Kennzahlen (Herkunft, Zeitpunkt, Größe,
   Dateien) werden nur neu abgefragt, wenn (a) ein Spiel neu ausgewählt wird (`SelectGame` →
   `ProbeShareStatusAsync`) oder (b) der Nutzer selbst eine mutierende Aktion auslöst (Kästen-Klick,
   Force-Upload). Schließt im Hintergrund ein regulärer Sync-Zyklus für das **gerade angezeigte**
   Spiel ab (z. B. weil das Spiel while die Ansicht offen ist einen neuen Spielstand geschrieben
   hat), werden die angezeigten Kästen **nicht** automatisch aktualisiert — erst ein erneutes
   Auswählen des Spiels (z. B. über Spielwechsel und zurück) löst eine frische Abfrage aus.
5. **Versionshistorie-Flyout** (`MainWindow.LoadHistoryAsync`, Zeile 315): ruft
   `_agent.GetRevisionsAsync(row.Game)` **ohne** Scope-Argument auf → nutzt den Default-Wert
   `BucketScope.Private`. Für ein Spiel, das aktuell „Synchron" ist, ist der private Bucket seit
   dem Umschalten eingefroren (i. d. R. nur eine sehr alte Revision) — die im Client angezeigte
   Versionshistorie zeigt deshalb bei geteilten Spielen **immer nur diesen einen alten Eintrag**,
   unabhängig vom tatsächlich aktiven (geteilten) Verlauf. Das Server-Dashboard fragt korrekt je
   Bucket ab und zeigt deshalb den vollständigen Verlauf — der Unterschied fiel Tim genau daran auf.
   **Reale Nebenwirkung:** Ein „Wiederherstellen"-Klick auf einen im Flyout fälschlich alt
   aussehenden Eintrag ersetzt den aktiven Ordnerinhalt tatsächlich durch diesen alten Stand —
   passt zu Tims Beobachtung „meine Speicherstände sind auf einen alten Stand zurückgesetzt".
   Nichts ist dabei endgültig verloren (der aktuelle geteilte Stand bleibt server-seitig erhalten
   und ist über das Dashboard wiederherstellbar), aber der Client führt den Nutzer aktiv in die
   falsche Entscheidung.

**Nutzungs-/Interaktionspfade heute (Regressions-Checkliste):**
- Spiel im Dropdown wechseln → Detail-Bereich rechts, Kästen + Historie laden.
- Server-Kasten klicken (Seed, wenn kein geteilter Stand existiert; sonst Übernehmen ohne Dialog).
- Lokal-Kasten klicken (zurück auf privaten Bucket).
- „Als geteilten Stand hochladen" (Force-Upload mit Kennzahlen-Vergleich + Bestätigung).
- „Sicherung deaktivieren" / „wieder aktivieren" (beide Kästen dabei ausgegraut, nicht ausgeblendet
  — von Tim auf v1.8.4 bereits bestätigt, **keine Änderung** in diesem Delta nötig).
- „Jetzt sichern", „Ordner öffnen".
- „Lösen" bei Konflikt → `ConflictWindow` (Fassung wählen oder „Beide behalten").
- „Ordner zuordnen" bei übersprungenen Spielen.
- Versionshistorie-Flyout öffnen, „Wiederherstellen" je Revision.
- Dashboard: alle fünf Ansichten, Spiel-Drawer mit Bucket-Zeilen, Konfliktlösung, Restore, Export.

**Öffentliche Schnittstellen heute:** `ISaveVaultApi` unverändert. `RevisionInfo`/
`RevisionDownload` tragen bereits `DeviceName` (seit v1.8.1). `ConflictParticipant`/`Conflict`
unverändert (`DeviceId`, `Revision` — keine Namensauflösung nötig, siehe Kern-Delta Punkt 3: löst
sich rein clientseitig über bereits vorhandene Daten).

## Änderungswunsch

Fünf konkret diagnostizierte Folgefehler aus Tims Realtest sollen behoben werden, ohne die
Grundsatzentscheidungen der Phase-1-Spec (kein automatisches Überschreiben bei echten Konflikten,
kein Bestätigungsdialog beim Umschalten zwischen bereits verbundenen Ständen) anzutasten. Zusätzlich
eine gezielte, bewusste Korrektur an genau einer Stelle: der **erste** Beitritt eines Geräts zu
einem bereits existierenden geteilten Stand (Erstkontakt) soll künftig **fragen**, welcher Stand
gelten soll (mit echten Kennzahlen beider Seiten), statt ihn stillschweigend zu übernehmen — das
spätere Hin- und Herschalten (Kästen-Klick) bleibt wie heute ohne Dialog.

## Betroffene Fläche (Right-sizing)

- [x] **Kern** (Fix 0 — höchste Priorität — `SyncEngine.cs`: Nachstell-Versuch/Untersuchung +
  Diagnose-Log + neue Regressionstests in `tests/SaveVault.Core.Tests/*`; Server:
  `VaultStore.RegisterConflict`; Client: Scope-Weitergabe in `ClientAgent`/`MainWindow`,
  Re-Probe-Hook, Kern-Ergänzung für den Erstkontakt-Entscheid) → `bauer`
- [x] **Oberfläche** (Client: Opt-in-Dialog beim Erstkontakt, Geräte-Name im Konflikt-Dialog) →
  `oberflaechen-bauer` (Kern zuerst, da der Opt-in-Entscheid eine kleine Kern-Ergänzung braucht)
- [ ] Neue/veränderte Sicherheitsfläche — **entfällt**: keine neuen Eingaben, kein neuer Endpunkt;
  alle fünf Fixes korrigieren bestehende Logik oder nutzen bereits vorhandene, bereits geladene
  Felder (`DeviceName` ist schon Teil der Antwort).
- [ ] Abhängigkeit/Stack ändert sich — entfällt.
- [ ] Öffentliche Schnittstelle ändert sich — **entfällt**: `ISaveVaultApi`, alle DTOs bleiben
  unverändert (Punkt 3 löst sich rein clientseitig aus bereits vorhandenen `RevisionInfo`-Daten).

## Delta im Detail

### Kern-Delta

- **Fix 0 — falscher Konflikt nach korrektem Download (höchste Priorität, zweistufig):**
  1. **Zuerst reproduzieren, nicht raten.** Der `bauer` baut einen gezielten Nachstell-Versuch
     (Wegwerf-Harness, wie in früheren Sessions bereits für andere Sync-Fälle genutzt — siehe
     `CHECKPOINT.md`): zwei simulierte Geräte gegen einen echten, lokal laufenden Test-Server.
     Ablauf: Gerät A lädt eine Revision hoch → Gerät B durchläuft einen **regulären**
     `RunCycleAsync`-Zyklus (Download-Fall, **nicht** den manuellen Umschalt-/Force-Pfad) und
     landet exakt auf dem neuen Stand → Gerät B macht eine **echte neue** lokale Änderung → Gerät B
     durchläuft einen weiteren `RunCycleAsync`-Zyklus. Erwartung: `SyncAction.Upload`. Lässt sich
     der von Tim gemeldete `SyncAction.Conflict` damit reproduzieren, den echten Fehler beheben
     (z. B. falls `BaseRevision`/`BaseManifest` nach `ApplyRevisionAsync` nicht das ist, was
     tatsächlich geschrieben wurde, oder ein Scope-/Timing-Problem zwischen Anzeige-Probe und
     echtem Zyklus vorliegt). Lässt es sich in diesem idealisierten Fall **nicht** reproduzieren,
     ist die Ursache wahrscheinlich umgebungsspezifisch (Datei-Locking durch das Spiel, mehrere
     Save-Wurzeln, Watcher-Timing) — dann **nicht raten**, sondern das Ergebnis so an Tim
     zurückmelden und auf das neue Diagnose-Log (Schritt 2) für den nächsten echten Vorfall setzen.
  2. **Unabhängig vom Ausgang von Schritt 1:** ein kleines, dauerhaftes Diagnose-Log im Client
     ergänzen (z. B. `%AppData%\SaveVault\sync.log`, rollierend/größenbegrenzt) — pro Sync-Zyklus
     eine Zeile mit Zeitpunkt, Spiel, Scope, gewählter `SyncAction`, lokaler/Basis-/
     Server-Revisionsnummer. Reine Beobachtung, keine Verhaltensänderung, niedriges Risiko. Zweck:
     beim nächsten Auftreten eines unerwarteten Konflikts lässt sich anhand des Logs exakt
     rekonstruieren, welche Revisionsnummern zum Zeitpunkt der Fehlentscheidung vorlagen, statt
     erneut zu rätseln.
- **Fix 1 — `VaultStore.RegisterConflict` aktualisiert Teilnehmer-Revisionen:**
  Ist ein Gerät bereits Teilnehmer eines offenen Konflikts und tritt derselbe Konflikt erneut auf
  (weitere lokale Änderung dieses Geräts, Server weiterhin > Basis), muss die gespeicherte
  `ConflictParticipant.Revision` dieses Geräts auf die **neue** `conflictRevision` aktualisiert
  werden, statt (wie heute) unverändert zu bleiben. Ebenso soll — falls der Gegenpart
  (Kopf-Revision, `currentRevision`) bereits als Teilnehmer gelistet ist und sich die
  Server-Kopf-Revision seither weiterbewegt hat — dessen Revision auf `currentRevision`
  nachgezogen werden, damit „Lösen" immer die **aktuell** abweichenden Fassungen vergleicht, nicht
  die der ursprünglichen Erst-Erkennung. Kein neuer Konflikt-Datensatz, keine neue Teilnehmerliste
  — nur die Revisionsnummern bestehender Einträge werden aktuell gehalten.
- **Fix 5 — aktiver Scope für die Versionshistorie:** `ClientAgent`/`MainWindow` müssen beim Laden
  der Versionshistorie (`LoadHistoryAsync`) den **aktiven** Scope übergeben
  (`row.IsShared ? BucketScope.Shared : BucketScope.Private`, spiegelt `ClientAgent.ActiveScope`),
  statt den Default-Parameter `BucketScope.Private` durchfallen zu lassen.
- **Fix 4 — automatische Aktualisierung der Zwei-Kästen-Anzeige:** Schließt ein regulärer
  Sync-Zyklus (`ClientAgent`, nicht der manuelle Umschalt-/Force-Pfad) für das **aktuell
  angezeigte** Spiel ab, muss die UI ohne weiteres Zutun neu abfragen (`ProbeShareStatusAsync`
  o. ä.), nicht nur bei Spielauswahl oder eigener mutierender Aktion. Naheliegender Anknüpfungspunkt:
  `MainWindow.RefreshDetailIfNeeded`/`OnAgentStateChanged` (wird bereits bei `State.Changed`
  aufgerufen und lädt dort schon die Historie neu, wenn sich `LastActionUtc` geändert hat — die
  Kästen-Probe fehlt dort bisher). Genaue Umsetzung (z. B. Drosselung, damit nicht bei jedem
  Heartbeat unnötig neu abgefragt wird) ist Ermessen des `bauer`.
- **Fix „Erstkontakt-Dialog" (Kern-Anteil):** `ClientAgent` braucht einen Weg, dem Aufrufer bei
  Erstkontakt (Spiel wird zum ersten Mal auf diesem Gerät verwaltet **und** ein geteilter Stand
  existiert bereits — unterscheidbar vom normalen späteren Hin-und-Herschalten desselben, bereits
  bekannten Spiels) mitzuteilen, dass eine Entscheidung ansteht, statt direkt zu handeln. Ob das
  über ein neues Flag in `ShareProbe`, eine eigene Methode, oder eine Prüfung auf Client-Seite
  (kein vorheriger `GameShareStore`/`SyncStateStore`-Eintrag für dieses Spiel) gelöst wird, ist
  Ermessen des `bauer` — Bedingung ist nur: **spätere** Kästen-Klicks für ein bereits bekanntes
  Spiel bleiben unverändert ohne Dialog.
- **Entferntes Verhalten:** keins.
- **Neue/geänderte Abhängigkeit:** keine.
- **Datenstruktur/Persistenz-Änderung:** keine (weder Server-Index noch Client-Config/State ändern
  ihr Format).

### Oberflächen-Delta

- **Fix 3 — Geräte-Name im Konflikt-Dialog:** `ConflictWindow.LoadParticipantsAsync` nutzt für das
  Label eines fremden Geräts, sofern die zugehörige `RevisionInfo` gefunden wurde
  (`rev is not null`) und `rev.DeviceName` gesetzt ist, diesen Namen — die
  `Gerät {ShortId(...)}`-Kurzform bleibt nur noch der Fallback für den Fall, dass zur Teilnehmer-
  Revision keine Metadaten (mehr) auffindbar sind (z. B. sehr alte, aufgeräumte Revision).
- **Fix „Erstkontakt-Dialog" (Oberflächen-Anteil):** Erkennt der Server-Kasten-Klick (oder die
  Anzeige beim ersten Öffnen eines neu erkannten, bereits geteilten Spiels — Ermessen `bauer`, wo
  genau der Haken sitzt) den Erstkontakt-Fall, zeigt der Client **vor** der Übernahme eine
  Entscheidung mit echten Kennzahlen beider Seiten (Größe, Dateien, Zeitpunkt, Herkunftsgerät) und
  zwei klaren Optionen: **„Server-Stand übernehmen"** oder **„Meinen lokalen Stand als geteilten
  Stand hochladen"**. Eine einfache Inline-Bestätigung/ein kleiner Dialog reicht (kein neuer
  Dialog-Typ nötig, ähnlich der bestehenden Force-Upload-Bestätigung) — Optik im bestehenden
  Design-Duktus (`design-reference/`), Ermessen des `oberflaechen-bauer`.
- **Fix 4 (Oberflächen-Anteil):** keine sichtbare UI-Änderung nötig, nur dass die Kästen jetzt von
  selbst aktuell bleiben.

## Was gleich bleibt (Nicht-Ziele)

- **Kein automatisches Auflösen echter Konflikte nach Zeitstempel** — Tim hat das ausdrücklich
  abgelehnt (Datenverlust-Risiko bei falscher Systemzeit eines Geräts). Echte Konflikte bleiben
  manuell entschieden, Fix 1 macht nur die **angezeigten Informationen** dazu aktuell.
  Normaler Mehrgeräte-Betrieb (nacheinander an verschiedenen Geräten spielen, kein gleichzeitiges
  Ändern) soll ohnehin **nie** einen echten Konflikt auslösen — sollte das nach diesem Delta noch
  vorkommen, ist das ein eigener, neuer Bug, kein Ziel dieses Deltas.
- **Kästen ausgrauen statt ausblenden bei „Sicherung deaktiviert"** — bereits umgesetzt und von
  Tim auf v1.8.4 bestätigt, **keine Änderung** in diesem Delta.
- **Späteres Hin- und Herschalten (Kästen-Klick bei bereits bekanntem, geteiltem Spiel) bleibt ohne
  Bestätigungsdialog** — nur der **erste** Beitritt fragt nach.
- **Kein neuer API-Endpunkt, keine DTO-Änderung** — alle fünf Fixes kommen ohne Vertragsänderung
  aus.

## Sicherheitsflächen (nur die NEU berührten ankreuzen)
- [ ] Eingaben von außen fließen neu in Logik/Ausgabe — nein.
- [ ] Neue Kommando-/Query-/Pfad-Ausführung aus Eingabewerten — nein.
- [ ] Neue ausgehende Requests an dynamische Ziele — nein, dieselben Endpunkte.
- [ ] Neue Secrets im Spiel — nein.

**Kein `security-auditor`-Einsatz** vorgesehen (keine neue Fläche); wird im Kern-Gate kurz
begründet bestätigt statt übersprungen.

## Akzeptanzkriterien der Änderung

**Neu:**
- [ ] Nachstell-Versuch für Fix 0 gebaut und ausgeführt; Ergebnis (reproduziert oder nicht) im
  Bericht dokumentiert. Falls reproduziert: der zugrunde liegende Fehler ist behoben und derselbe
  Ablauf (Download → echte neue Änderung → Sync) ergibt zuverlässig `Upload`, nie `Conflict`.
- [ ] Diagnose-Log ist aktiv und enthält für jeden Sync-Zyklus Zeitpunkt, Spiel, Scope, Aktion,
  lokale/Basis-/Server-Revision — real geprüft (Log-Datei nach ein paar Zyklen angesehen).
- [ ] Tritt ein Gerät einem Spiel mit bereits existierendem geteiltem Stand zum **ersten** Mal bei,
  erscheint eine Entscheidung mit echten Kennzahlen beider Seiten; „Server-Stand übernehmen" und
  „lokalen Stand hochladen" funktionieren beide korrekt.
- [ ] Ein Gerät, das wiederholt (z. B. bei jedem Speichern) denselben offenen Konflikt erneut
  auslöst, zeigt im „Lösen"-Dialog immer die **aktuellste** Revision dieses Geräts (nicht die der
  Erst-Erkennung) — nachgewiesen am Warcraft-3-artigen Repro (mehrfaches Speichern bei offenem,
  ignoriertem Konflikt, dann Lösen).
- [ ] Der Konflikt-Dialog zeigt für ein fremdes Gerät dessen echten Namen, sofern bekannt (keine
  rohe Geräte-ID mehr im Normalfall).
- [ ] Die Zwei-Kästen-Anzeige aktualisiert sich automatisch, wenn im Hintergrund ein Sync-Zyklus
  für das gerade angezeigte Spiel abschließt — ohne dass ein Spielwechsel nötig ist.
- [ ] Die Versionshistorie im Client zeigt für ein „Synchron"-Spiel den geteilten Verlauf
  (übereinstimmend mit dem Dashboard), für ein „Lokal"-Spiel den privaten Verlauf.

**Regression:**
- [ ] Alle Pfade aus „Ist-Zustand (Baseline)" funktionieren weiter (siehe Liste oben) — insbesondere
  bleibt das spätere Hin- und Herschalten zwischen bereits bekanntem Lokal/Synchron ohne Dialog,
  und „Sicherung deaktiviert" bleibt unverändert (ausgegraut, nicht ausgeblendet).
- [ ] Build 0/0, bestehende Tests weiter grün, `dotnet test` inkl. neuer Regressionstests für Fix 1
  (Konflikt-Revision wird bei erneutem Auftreten aktualisiert) und Fix 5 (Versionshistorie fragt
  aktiven Scope ab).
- [ ] Kein API-/DTO-Vertragsbruch — Server unverändert lauffähig mit älteren Clients (soweit noch
  im Einsatz), neuer Client bleibt kompatibel.
- [ ] Ein **echter** Konflikt (zwei Geräte ändern tatsächlich gleichzeitig) wird weiterhin korrekt
  erkannt, angehalten und muss weiterhin manuell gelöst werden (keine automatische Auflösung).

## Offene Fragen
- **Fix 0 ist die größte Unsicherheit dieser Spec:** die Ursache ist beim Schreiben dieser Spec
  noch nicht bekannt, nur die Entscheidungslogik wurde bereits als korrekt verifiziert. Findet der
  `bauer` den Fehler nicht über den Nachstell-Versuch, geht das Ergebnis (reproduziert: nein) +
  das neue Diagnose-Log als Zwischenstand an Tim, statt den Punkt als „behoben" zu melden.
- Genaue technische Umsetzung des Erstkontakt-Erkennungshakens (neues `ShareProbe`-Flag vs.
  Prüfung auf fehlenden vorherigen State) — Ermessen `bauer`, im Bericht kurz benennen, wofür er
  sich entschieden hat.
- Ob Fix 4 (automatische Kästen-Aktualisierung) eine Drosselung braucht, um nicht bei jedem
  Heartbeat unnötig oft den Server abzufragen — Ermessen `bauer`, Performance ist hier zweitrangig
  gegenüber Korrektheit, aber unnötige Last vermeiden.

## Nachtrag (2026-09-15, nach Release v1.8.5) — Fix 0 real reproduziert, Ursache weiterhin offen

**Was passierte:** Nach dem Update auf v1.8.5 hat Tim denselben Ablauf (Gerät wechseln, ins
bereits geteilte Warcraft-3-Reforged-Savegame gehen, speichern) auf echter Hardware wiederholt —
diesmal mit dem neuen Diagnose-Log. Ergebnis: **der Fehler tritt real auf**, der Nachstell-Versuch
im sauberen In-Process-Harness (siehe oben, „nicht reproduziert") deckt also nicht den echten Fall
ab.

**Echte Log-Auszüge (Gerät, das den Konflikt zeigte), chronologisch:**
```
00:49:06  NoOp      local=5a645006  base=45  server=45   Lokal unverändert und Server nicht neuer als base: nichts zu tun.
00:49:23  Upload    local=98bdd42b  base=45  server=45   Lokal geändert, Server-Revision 45 <= base 45: neue Revision hochladen.
00:49:32  NoOp      local=98bdd42b  base=46  server=46   Lokal unverändert und Server nicht neuer als base: nichts zu tun.
00:49:43  NoOp      local=98bdd42b  base=46  server=46   Lokal unverändert und Server nicht neuer als base: nichts zu tun.
00:49:45  NoOp      local=98bdd42b  base=46  server=46   Lokal unverändert und Server nicht neuer als base: nichts zu tun.
00:49:48  Download  local=98bdd42b  base=46  server=47   Lokal unverändert, Server-Revision 47 > base 46: aktuelle Revision herunterladen.
00:49:51  Download  local=98bdd42b  base=46  server=47   (identisch)
00:49:55  Download  local=98bdd42b  base=46  server=47   (identisch)
00:49:58  Download  local=98bdd42b  base=46  server=47   (identisch)
00:50:02  Download  local=98bdd42b  base=46  server=47   (identisch)
00:50:06  Conflict  local=b0fa4d0d  base=46  server=47   Lokal geändert UND Server-Revision 47 > base 46: echter Konflikt, nicht überschreiben.
00:50:08  Conflict  local=b0fa4d0d  base=46  server=47   (identisch)
00:50:22  Conflict  local=c582628f  base=46  server=47   (identisch, lokaler Hash erneut geändert)
```

**Auffälligkeit 1 — Kadenz:** die Abstände zwischen den Zyklen (3–13 Sekunden) sind viel kürzer als
ein normales Rescan-Intervall — das deutet auf `FolderWatcher`-getriebene Zyklen hin, nicht auf den
periodischen Timer. `FolderWatcher.cs` filtert **keine** `.svtmp-*`-Dateien aus den beobachteten
Ereignissen (`NotifyFilter` deckt `FileName`/`LastWrite`/`Size`/`CreationTime` pauschal ab) — ein
eigener, gescheiterter Download-Versuch (der laut `SyncEngine.ApplyRevisionAsync` `.svtmp-<guid>`-
Dateien im selben, beobachteten Ordner anlegt und bei einem Fehler wieder löscht) würde sich damit
theoretisch selbst erneut auslösen. Nicht abschließend verifiziert, ob das hier tatsächlich die
Zyklus-Quelle war oder ob z. B. `ludusavi`/das Spiel selbst wiederholt kleine Dateien berührt hat.

**Auffälligkeit 2 — Basis bewegt sich 5 Zyklen lang trotz „Download"-Entscheidung nicht:** das ist
der eigentliche Kern des Bugs. Fünfmal in Folge wird „herunterladen" entschieden, aber `base` bleibt
bei 46 stehen — das kann nur bedeuten, dass entweder (a) der Download-Versuch jedes Mal mit einer
Ausnahme abbricht, bevor `_stateStore.Save(...)` erreicht wird, oder (b) er "erfolgreich" durchläuft,
aber etwas den gespeicherten Zustand danach wieder auf 46 zurückfallen lässt (unwahrscheinlicher,
da `SyncStateStore` zustandslos direkt von der Platte liest/schreibt, keine Zwischen-Caches hat).

**Zunächst vermutet, dann verworfen:** eine Datei-Sperre durch das laufende Spiel. **Tim hat das
widerlegt** — das Spiel war zum Zeitpunkt des Vorfalls nachweislich geschlossen. Diese Theorie ist
damit **falsifiziert**, nicht nur unbestätigt — eine künftige Session sollte sie nicht wiederholen,
ohne neue Belege dafür zu haben.

**Warum das alte Diagnose-Log die eigentliche Ursache nicht zeigen konnte:** `SyncEngine.
RunCycleAsync` rief `_diagnostics.Append(...)` bisher **vor** der Ausführung der Aktion auf (direkt
nach `SyncDecider.Decide(...)`, vor dem `switch`, das `UploadAsync`/`DownloadAsync`/etc. aufruft).
Die Log-Zeile bewies also nur die **Absicht**, nie das tatsächliche Ergebnis — ob der
`DownloadAsync`-Aufruf dahinter erfolgreich war, mit einer der drei explizit behandelten Ausnahmen
(`SaveVaultApiException`/`HttpRequestException`/`SyncSecurityException`) abbrach, oder mit einer
davor **komplett unbehandelten** Ausnahme (z. B. einer rohen `IOException`, die durch keinen der
drei `catch`-Zweige gefangen wurde und bisher unprotokolliert bis zum generischen
`catch (Exception ex)` in `ClientAgent` durchgereicht wurde) scheiterte, ließ sich aus dem Log
allein **nicht** unterscheiden. Das war der eigentliche Fehler in der ersten Fix-0-Umsetzung: das
Log beobachtete die falsche Sache.

**Fix (umgesetzt, released als v1.8.5-Folgeversion — Version siehe CHECKPOINT.md/CHANGELOG.md zum
Zeitpunkt des jeweiligen Commits):**
- `SyncDiagnosticsLog` bekommt eine zweite Methode `AppendOutcome(...)`, die das TATSÄCHLICHE
  Ergebnis eines Zyklus protokolliert — bei Erfolg mit der resultierenden Basis-Revision (zeigt
  direkt, ob sie wirklich vorgerückt ist), bei einem Fehler mit Ausnahme-Typ und -Nachricht.
- `SyncEngine.RunCycleAsync` ruft diese Methode jetzt nach der Ausführung auf: im Erfolgsfall nach
  dem `switch`, in allen drei bestehenden `catch`-Zweigen, UND in einem neuen, bisher nicht
  vorhandenen `catch (Exception ex)`-Sicherheitsnetz, das jede sonst unbenannte Ausnahme zuerst
  protokolliert und dann **unverändert weiterreicht** (`throw;`, keine Verhaltensänderung
  gegenüber dem bisherigen `ClientAgent`-Fehlerpfad, nur zusätzliche Sichtbarkeit).
- Build 0/0, `dotnet test` weiterhin grün (199/199, keine neuen Tests nötig — reine
  Beobachtungserweiterung, keine neue Entscheidungslogik).

**Für die nächste Session, die das übernimmt:**
1. Tritt der Konflikt erneut auf: `%AppData%\SaveVault\sync.log` **beider** beteiligten Geräte
   anfordern (nicht nur des einen, das den Konflikt zeigte) und nach der neuen
   `ERGEBNIS(...)`-Zeile suchen, die jetzt direkt nach jeder `Download`/`Upload`/`Conflict`-Zeile
   folgen sollte — sie zeigt entweder `OK neueBasis=47` (dann war der Download technisch
   erfolgreich, und die Ursache liegt woanders, z. B. bei einer erneuten, zwischenzeitlich
   geänderten lokalen Datei) oder `FEHLER` mit einer konkreten Ausnahme (dann ist die Ursache exakt
   benannt).
2. Zusätzlich lohnt sich ein Blick auf das **andere** Gerät (das, welches Revision 47 erzeugt hat):
   Zeitstempel seines Uploads gegen die Download-Versuche des betroffenen Geräts legen, um
   auszuschließen/bestätigen, dass es sich um ein reines Timing-Fenster handelt.
3. Die `FolderWatcher`-Selbstauslösungs-Theorie (Auffälligkeit 1) ist ein guter Nebenkandidat, aber
   NICHT der Kern des Bugs (der Kern ist: Basis bewegt sich trotz „Download"-Entscheidung nicht) —
   sollte nur verfolgt werden, wenn er die eigentliche Frage (warum scheitert/verharrt der Download)
   mit erklärt, nicht als eigenständiger Fix.
4. Datei-Sperre durch das Spiel ist widerlegt — nicht erneut als erste Hypothese ansetzen.

## Nachtrag 2 (2026-09-15) — neuer, eigenständiger Vorfall: Dateisperre durchs LAUFENDE Spiel bestätigt

**Wichtig zur Abgrenzung von Nachtrag 1:** Die dort widerlegte Hypothese betraf einen Vorfall, bei
dem das Spiel nachweislich geschlossen war. Dieser Nachtrag beschreibt einen **anderen, neuen**
Vorfall — die Falsifizierung aus Nachtrag 1 bleibt für den alten Fall gültig, gilt aber nicht hier.

**Von Tim bestätigtes Muster:** Wenn Gerät A gerade spielt (Spiel offen, Speicherdatei dadurch
gesperrt) und Gerät B zwischenzeitlich eine neue Revision hochlädt, scheitert der Download auf
Gerät A dauerhaft mit `UnauthorizedAccessException` ("Access to the path is denied") — solange A
spielt. Symmetrisch: läuft das Spiel stattdessen auf B, tritt derselbe Effekt dort auf. Zusatz-
symptom: die Herkunftsgerät-Anzeige bleibt auf dem zuletzt erfolgreich heruntergeladenen Gerät
hängen, weil die neuere Revision nie tatsächlich übernommen wird.

**Mechanik (aus Code-Lesung `SyncEngine.cs` + `FolderWatcher.cs` hergeleitet):**
- `ApplyRevisionAsync` schreibt je Datei erst nach `*.svtmp-<guid>`, dann `File.Move(tmp, target,
  overwrite: true)`. Ist `target` vom laufenden Spiel offen/gesperrt, wirft `File.Move` eine rohe
  `IOException`/`UnauthorizedAccessException`; die Basis-Revision bleibt stehen (kein
  `_stateStore.Save` erreicht).
- `FolderWatcher` beobachtet den ganzen Ordner ungefiltert (`NotifyFilter` deckt alles ab) — auch
  die eigenen `*.svtmp-*`-Zwischendateien, die ein gescheiterter Download selbst anlegt und im
  `catch` wieder löscht. Das löst sofort einen neuen Zyklus aus → Kadenz von wenigen Sekunden statt
  des normalen Poll-Intervalls (im Log von Nachtrag 1 bereits als Auffälligkeit 1 vermerkt, jetzt
  als Teil-Ursache bestätigt).
- Während das Spiel weiterspielt/speichert, ändert sich der lokale Manifest-Hash real (lesend meist
  noch möglich, auch wenn Schreiben blockiert ist) → nächster Zyklus entscheidet „Conflict" →
  Shared-Auto-Resolve (v1.8.7) lädt den lokalen Stand als neue Revision hoch, **ohne** die eigentlich
  neuere Server-Revision je gesehen/gemerged zu haben → Ping-Pong zwischen den Geräten.

**Fix-Plan (umzusetzen von `bauer`):**
1. `SyncEngine.ApplyRevisionAsync`: Schreibversuch je Datei (Erstellen der Temp-Datei + `File.Move`)
   bekommt Retry-mit-kurzem-Backoff bei `IOException`/`UnauthorizedAccessException` (transiente
   Sperren, z. B. während eines Spiel-Speichervorgangs, lösen sich oft von selbst). Bleibt die Datei
   nach den Versuchen gesperrt: eigener, unterscheidbarer Exception-Typ (nicht die rohe
   `IOException` weiterreichen) — `SyncEngine.RunCycleAsync` protokolliert das dann im Diagnose-Log
   klar als „Datei gesperrt, erneuter Versuch folgt" statt als generischen Fehler; kein
   Sync-Fehler-Status bei jedem Zyklus.
2. `FolderWatcher`: `*.svtmp-*`-Namen von den beobachteten Datei-Events ausschließen (Filter auf
   `e.Name`), damit ein gescheiterter Download sich nicht selbst im Sekundentakt erneut auslöst.
3. Test-/Review-Punkt: mit 1+2 sollte der Download in der Praxis fast immer vor dem nächsten lokalen
   Speichervorgang durchgehen (kürzeres Sperrfenster + normales Poll-Intervall statt Sekundentakt).
   Bleibt dennoch ein Restfenster (Spiel hält die Datei durchgängig über Minuten offen), das im
   Bericht **offen benennen**, nicht stillschweigend als gelöst darstellen — keine implizite
   Datenverlust-Annahme.

**Akzeptanzkriterien (neu, zusätzlich zu oben):**
- [ ] Ein simulierter gesperrter Zieldatei-Schreibversuch (Test) führt nicht mehr zu einer rohen,
  unklassifizierten Exception im Diagnose-Log, sondern zu einer klar als „Datei gesperrt" erkennbaren
  Outcome-Zeile.
- [ ] `*.svtmp-*`-Dateien lösen keinen `FolderWatcher.Changed` mehr aus (Test mit Erzeugen/Löschen
  einer solchen Datei im überwachten Ordner).
- [ ] Bestehende Baseline-Pfade (Nachtrag 1, Fixes 1-5) bleiben unverändert grün.
- [ ] Build 0/0, `dotnet test` weiterhin grün.

**Kein neuer API-/DTO-Vertrag, keine neue Sicherheitsfläche** (reine Fehlerbehandlung + lokale
Datei-IO-Robustheit) — `security-auditor` bestätigt das im Gate kurz statt vollen Audit.

## Nachtrag 3 (2026-09-15) — eigentlicher Hauptfund: ManifestBuilder wirft gesperrte Dateien still aus dem Manifest

**Wichtiger als Nachtrag 2:** Im Gespräch mit Tim stellte sich heraus, dass der Access-Denied-
Download-Fall aus Nachtrag 2 nur unter künstlichem Schnell-Geräte-Wechsel auftrat (bewusster Test,
kein Alltagsfall). Tims tatsächlich beobachtetes Alltagsverhalten war ein anderes: „Während der
Hauptrechner spielt, synchronisiert das Notebook zwar immer wieder, aber erst wenn das Spiel auf
dem Hauptrechner geschlossen wird, aktualisiert sich das Herkunftsgerät korrekt auf Hauptrechner."

**Ursache gefunden in `SaveVault.Core/Hashing/ManifestBuilder.cs`, `ScanRootInto`
(Zeile 102-108):** Kann eine Datei nicht gehasht werden (`IOException`/`UnauthorizedAccessException`
— z. B. weil das Spiel sie exklusiv offen hält), wird sie per `continue` **komplett aus dem
Manifest ausgelassen** – nicht anders behandelt als eine tatsächlich gelöschte Datei. Für
`SyncDecider` sieht das identisch aus wie „Datei entfernt": der Manifest-Hash weicht vom Basis-
Stand ab → `localChanged = true` → Upload (oder Conflict) einer Revision, der genau die gerade
aktive, eigentlich unveränderte Speicherdatei fehlt.

**Warum das ernster ist als Nachtrag 2:** Diese lückenhafte Revision landet echt in der Historie.
Jede Stelle, die eine Revision **exakt** übernimmt (`ReplaceLocalContentAsync` – genutzt von
Restore, „Lokal ↔ Synchron"-Umschalten, geteilten Stand übernehmen) **löscht** dort bewusst alle
lokalen Dateien, die im Ziel-Manifest fehlen. Wird eine solche lückenhafte Revision später exakt
angewandt, kann das die aktive, tatsächlich vorhandene Speicherdatei auf einem Gerät real löschen –
ein Datenverlust-Risiko, kein reines Anzeige-Problem.

**Fix-Plan (umzusetzen von `bauer`, in dieser Reihenfolge):**
1. **Hauptfix — `ManifestBuilder.ScanRootInto`:** Schlägt `FileHasher.HashFile` mit `IOException`/
   `UnauthorizedAccessException` fehl UND existiert für denselben relativen Pfad ein Eintrag im
   `previous`-Manifest, diesen Eintrag **unverändert übernehmen** (Hash/Größe/Schreibzeit vom
   letzten erfolgreichen Scan) statt die Datei wegzulassen – die Datei bleibt damit im Manifest
   sichtbar, einfach mit zuletzt bekanntem Stand, bis sie wieder lesbar ist. Gibt es keinen
   `previous`-Eintrag (Datei ist neu, nie zuvor erfolgreich gescannt), bleibt das bisherige
   Verhalten (überspringen) – da ist nichts Sinnvolles zu bewahren.
2. **Aus Nachtrag 2 übernommen (Download-Schreibpfad, betrifft den selteneren
   Schnell-Wechsel-Fall):** `SyncEngine.ApplyRevisionAsync` – Retry-mit-Backoff bei
   `IOException`/`UnauthorizedAccessException` auf `File.Move`/`File.Create`; bleibt die Datei
   gesperrt, eigener unterscheidbarer Exception-Typ statt roher Weiterreichung, damit das
   Diagnose-Log das klar als „Datei gesperrt" statt generischen Fehler zeigt.
3. **Aus Nachtrag 2 übernommen:** `FolderWatcher` schließt `*.svtmp-*`-Namen von den beobachteten
   Events aus (verhindert Selbstauslösung bei einem gescheiterten Download-Versuch).
4. **Neu, von Tim angefragt — Variante A, Sperr-Diagnose über die Windows Restart-Manager-API:**
   Ein kleiner Wrapper um `RmStartSession`/`RmRegisterResources`/`RmGetList` (P/Invoke,
   `rstrtmgr.dll`), der zu einem gegebenen Dateipfad den/die haltenden Prozessnamen liefert –
   generisch, ohne dass der Client vorher weiß, wie das Spiel/seine exe heißt. Wird genutzt, um bei
   einer per Fix 1 erkannten gesperrten Datei eine ehrliche Statuszeile zu zeigen (z. B. „Wartet –
   `Warcraft III.exe` hält die Datei offen"), statt nur „Wartet" ohne Erklärung. Reine
   Zusatz-Diagnose, keine Verhaltensänderung der Sync-Entscheidung.

**Akzeptanzkriterien (neu, ersetzt/erweitert die aus Nachtrag 2):**
- [ ] Test: eine Datei, die zwischen zwei Scans kurzzeitig nicht lesbar ist (simulierte Sperre),
  bleibt mit ihrem letzten bekannten Hash im Manifest – `SyncDecider` wertet das NICHT als
  „lokal geändert" (kein Phantom-Upload/-Conflict wegen einer bloß gesperrten Datei).
- [ ] Test: eine Datei ohne jeden vorherigen Eintrag (nie erfolgreich gescannt) UND aktuell gesperrt
  bleibt weiterhin ausgelassen (bestehendes Verhalten, unverändert).
- [ ] Test: ein simulierter gesperrter Zieldatei-Schreibversuch beim Download führt zu einer klar
  als „Datei gesperrt" erkennbaren Outcome-Zeile statt einer rohen Exception im Diagnose-Log.
- [ ] Test: `*.svtmp-*`-Dateien lösen keinen `FolderWatcher.Changed` mehr aus.
- [ ] Der Restart-Manager-Wrapper liefert für eine bekanntermaßen gesperrte Testdatei einen
  Prozessnamen zurück (Test mit einer selbst offen gehaltenen Datei im Testprozess reicht als
  Nachweis der Mechanik) und liefert `null`/leer, wenn niemand sperrt oder die Abfrage selbst
  fehlschlägt (nie eine Exception nach außen).
- [ ] Bestehende Baseline-Pfade (Nachtrag 1+2, alle bisherigen Fixes) bleiben unverändert grün.
- [ ] Build 0/0, `dotnet test` weiterhin grün.

**Sicherheitsfläche:** Neuer P/Invoke-Aufruf gegen eine Windows-Systemkomponente
(`rstrtmgr.dll`) – keine externen/Netz-Eingaben, operiert nur auf bereits lokal bekannten
Dateipfaden dieses Geräts. `security-auditor` prüft trotzdem kurz: korrekte Freigabe der
RM-Session-Handles (kein Leak bei jedem Aufruf), keine ungeprüfte Puffergrößen-Berechnung bei der
`RmGetList`-Marshaling-Schleife.
