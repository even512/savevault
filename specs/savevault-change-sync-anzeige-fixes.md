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
