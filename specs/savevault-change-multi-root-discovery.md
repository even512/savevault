# Änderung: savevault — Mehr-Ordner-Erkennung (Split-Save-Spiele automatisch abdecken)

> Delta-Spec (Phase A′). Beschreibt nur den **Delta**; der bestehende Projektstand
> und `specs/savevault.md` bleiben für alles Unveränderte die Wahrheit.

## Status
- **Freigegeben von Tim:** ja (2026-09-03)
- **Runde:** **ABGENOMMEN (2026-09-04).** Etappe 1 + 2a + 2b gebaut, getestet (187 grün, Build 0/0),
  Security-Gate bestanden, als **v1.7.0** released und von Tim auf echter Hardware bestätigt
  („läuft gut, alles synchronisiert") — visueller Zwei-Ordner-Handtest bestanden.
- **Datum:** 2026-09-04
- **Fahrweise (Budget):** zwei Etappen mit Checkpoint am Kern-Gate — Etappe 1 = Delta-Gate
  + Kern-Bau (GameDiscovery-Gruppierung + SaveFolderSafety) + Kern-Gate; danach Halt vor dem
  Sync-Plumbing bis frischer `/usage`-Stand vorliegt.
- **Etappe-1-Stand (2026-09-04):**
  - `SaveFolderSafety.IsContainerRoot` (lexikalische Container-Erkennung: Steam-Install/
    `steamapps`/`common`/`userdata\<id>`, Ubisoft `savegames\<guid>`, Launcher-Bibliotheken) —
    gebaut.
  - `SaveFolderSafety.IsBroadUserStructure` (NEU, rein lexikalisch: Benutzerprofil, `AppData`,
    `AppData\{Local,LocalLow,Roaming}`) — ergänzt die laufzeit-ermittelten Sammelwurzeln
    **maschinenunabhängig**, damit die Gruppierung auch fern von `timse`s Profil (Tests/CI,
    Zweitprofile) korrekt durch sie hindurchsteigt. Kein reales Verhalten verschlechtert.
  - `SaveRootGrouping.Group` (NEU, `SaveVault.Core.Storage`, rein rechnend) — rekursiver
    Mehr-Ordner-Algorithmus. Ergebnis: enge `Roots` + `Unresolved`.
  - Der Größen-/Container-Disk-Heuristik-Check (`FolderMuchLargerThanSaves`) in `GameDiscovery`
    ist **noch unangetastet** (gehört zur Verdrahtung in Etappe 2).
  - **Bewusster Schnitt (Checkpoint-Seam):** Etappe 1 baut nur den **Algorithmus** + Tests +
    Headless-Beweis. Die **Verdrahtung** (`GameDiscovery` emittiert mehrere Roots je Spiel →
    `SaveFolderRegistry`/`SyncEngine`/`ManifestBuilder`/`ApplyRevisionAsync`/`ClientAgent`/
    Root-Key) ist **Etappe 2** — dadurch bleibt der Build am Checkpoint grün und `DiscoveredGame`
    unverändert (keine ungetestete halbe Ripple-Änderung im Repo).
  - **Verifikation Etappe 1:** Solution-Build **0 Warnungen / 0 Fehler**; `dotnet test`
    **155 grün** (inkl. neuer Tests: Gruppierung, `IsContainerRoot`, `IsBroadUserStructure`);
    Headless-Lauf des Algorithmus über Tims **echte** ludusavi-`backup --preview`-Daten (95
    Spiele, davon 1 ohne Files): **76 einfach-Root · 18 mehr-Root · 0 ungelöst/zu breit** — die
    18 sind exakt die AAA-Titel der Spec (Cyberpunk, AC Mirage/Valhalla/Black Flag, Star Wars
    Outlaws, Street Fighter 6, No Man's Sky, The Outer Worlds, RE Village mit 3 Ordnern über
    zwei Laufwerke, CS2, PoE 2, u. a.), jeweils in enge, korrekte Ordner aufgelöst.
  - **Offen für Etappe 2:** Root-Key (Punkt 2), Manifest-Präfix (Punkt 3), Schreib-Chokepoint
    `ApplyRevisionAsync` mehr-Root + `security-auditor` (Punkt 4), Registry-Liste + Migration
    (Punkt 5), Verdrahtung (Punkt 6), GUI „mehrere Ordner". Unabhängiges Kern-Gate
    (`reviewer`/`inspekteur`) auf Wunsch vor Etappe 2.
- **Etappe-2a-Stand (2026-09-04, committet `367d5f9`) — Fundament fertig + bewiesen:**
  - `SaveRootKey.Derive` (NEU, Core, rein lexikalisch): stabiles, geräteübergreifendes
    Root-Kennzeichen. Konto-verankerte Orte (Steam `userdata\<id>`, Ubisoft `savegames\<guid>`,
    `AppData\{Local,LocalLow,Roaming}`, `Saved Games`, `Documents`) → Key **ohne** Laufwerk/Profil
    (geräteübergreifend stabil). Installations-Orte (`steamapps\common`, GOG/Epic) → **Laufwerk im
    Key** (RE Village: identische `config.ini` auf C: **und** D: → sonst Kollision).
  - `SaveRoot` + `SaveRootLayout` (NEU, Core): Präfix-Konvention. Eine Wurzel → **unpräfixiert**
    (bit-identisch, kein Reseed); mehrere → je Datei `"<key>/<sub>"`. `TryResolve` bildet einen
    Manifest-Pfad auf die richtige lokale Wurzel ab; **unbekannter Key → nicht abgebildet** (kein
    Blindschreiben) — Traversal-Abwehr danach über `PathSanitizer.TryResolveWithin`.
  - `ManifestBuilder.BuildCombined` (NEU, Core): EIN Manifest über mehrere Wurzeln; Einzel-Wurzel
    bleibt bit-identisch zu `Build`.
  - **Verifikation:** Build 0/0; `dotnet test` **182 grün** (+27: `SaveRootKey`, `SaveRootLayout`,
    `ManifestBuilderCombined`). Headless-Key-Ableitung über Tims echte 18 Mehr-Root-Spiele:
    **0 Kollisionen** innerhalb eines Spiels.
- **Etappe 2b (Verdrahtung) — OFFEN, ein zusammenhängender Block:** `SaveFolderRegistry` auf
  **Liste je Spiel** + idempotente Migration (Alt-Einzelordner → ein Root mit abgeleitetem Key;
  Handzuordnung ersetzen wo sicher, Nischen-Override behalten); `GameDiscovery` nutzt
  `SaveRootGrouping`+`SaveRootKey` und emittiert mehrere Roots je Spiel (Größen-/Container-Disk-
  Heuristik `FolderMuchLargerThanSaves` dabei ablösen, Größen-Schutz pro Spiel = Summe erhalten);
  `SyncEngine.RunCycleAsync`/`ApplyRevisionAsync`/`UploadMissingContents` über Roots
  (`BuildCombined` + `TryResolve`+`TryResolveWithin` je Eintrag); Verdrahtung in `ClientAgent`
  (Rescan/Sync/`ShareAndSync`/`JoinTakeShared`), `CommandPoller` (Restore/Resolve),
  `HeartbeatReporter` (Storage = Summe über Roots), **je Root ein `FolderWatcher`**;
  `AgentState.EnsureGame`/GUI-Zeile „mehrere Ordner"; Version → **1.7.0**, Release-ZIP Tag `v1.7.0`.
  **Sicherheits-Gate Pflicht:** `security-auditor` über den mehr-Root-`ApplyRevisionAsync` (kein
  Ausbrechen je Root, unbekannter Key schreibt nirgends, Alles-oder-nichts bei Traversal). Danach
  Laufzeit-Gate/`tester` + Tims visueller Zwei-Ordner-Handtest nach Release.
- **Etappe-2b-Stand (2026-09-04) — FERTIG + Security-Gate bestanden:**
  - Verdrahtung komplett (committet `fec98af`): `SaveFolderRegistry` (Liste je Spiel + Migration +
    „Handzuordnung ersetzen wo sicher"), `GameDiscovery` (Mehr-Root über `SaveRootGrouping`+
    `SaveRootKey`, Disk-Heuristik abgelöst), `SyncEngine` (RunCycle/Upload/Download/Conflict/
    ApplyRevision/UploadMissing über Roots, `BuildCombined`), Verdrahtung in `ClientAgent`/
    `CommandPoller`/`HeartbeatReporter`, **je Ordner ein `FolderWatcher`**, GUI `RootCount`→„N Ordner",
    Version **1.7.0**.
  - **Security-Gate** (`security-auditor`, 2026-09-04): kein kritischer Ausbruch; ein **MITTEL**-Fund
    behoben (committet `5c0cac5`): Manifest-Pfad `"."`/`"<key>/."` kollabierte aufs Wurzelverzeichnis
    → Temp-Datei landete im Elternordner; Fix in `PathSanitizer.TryResolveWithin` (Ziel == Wurzel →
    ungültig) + `ResolveRootsSafe` für defekte Roots. Regressionstests ergänzt. GERING-Punkte
    (Key-Kollision distinkter Ordner – strukturell ausgeschlossen; Symlink-in-Wurzel – vorbestehende
    Grenze) bewusst offen.
  - **Verifikation:** Build 0/0, `dotnet test` **187 grün**. **Offen:** Push + Release `v1.7.0`
    (Tim entscheidet) und Tims visueller Zwei-Ordner-Handtest nach dem Release.

## Bezug
- **Projekt-id / Ordner:** `savevault` (`<werkstatt>/savevault/`)
- **Basis-Spec:** `.claude/projekt-werkstatt/specs/savevault.md` (in der Werkstatt, vorhanden: ja);
  die Regressions-Baseline steht zusätzlich inline unten (Abschnitt „Ist-Zustand").
- **Betroffene Dateien/Komponenten (nur Client + Core, KEIN Server):**
  - `src/SaveVault.Core/Storage/SaveFolderSafety.cs` — Container-Wurzel-Erkennung (neu)
  - `src/SaveVault.Client/Services/GameDiscovery.cs` — Mehr-Ordner-Gruppierung (Kern des Deltas)
  - `src/SaveVault.Client/Services/SaveFolderRegistry.cs` — mehrere Ordner je Spiel + Migration
  - `src/SaveVault.Client/Services/SyncEngine.cs` — Zyklus/Manifest/Restore über mehrere Roots
  - `src/SaveVault.Core/Hashing/ManifestBuilder.cs` — Scan über mehrere Roots, Root-Präfix
  - `src/SaveVault.Client/Services/ClientAgent.cs` — Verdrahtung (Discovery→Registry→Sync→Watcher)
  - `src/SaveVault.Client/Services/CommandPoller.cs` — Restore/Resolve auf die richtigen Roots
  - `src/SaveVault.Client/Services/FolderWatcher.cs` — je Root ein Watcher (Verdrahtung in ClientAgent)
  - `src/SaveVault.Client/Services/HeartbeatReporter.cs` — Storage/Ordner-Meldung mehr-Root-tauglich
  - `src/SaveVault.Client/Ui/GameRow.cs` + `MainWindow.xaml.cs` — Anzeige „mehrere Ordner"
  - `src/SaveVault.Client/SaveVault.Client.csproj` — Version → 1.7.0
  - Tests unter `tests/SaveVault.Core.Tests/` — Gruppierung + Root-Key + Manifest-Präfix

## Ist-Zustand (Baseline)
- **Funktioniert heute:** ludusavi-Erkennung → **ein** Save-Ordner je Spiel; periodischer
  Rescan + Watcher + Sync (Upload/Download/Konflikt) je Ordner; manuelles Zuordnen eines
  Ordners; per-Gerät/geteilte Buckets; Restore/Konfliktlösung; Dashboard „eine Kachel/Spiel".
- **Nutzungs-/Interaktionspfade heute (Regressions-Checkliste):**
  - Erkennung übernimmt Spiele mit **einem** klaren Save-Ordner automatisch.
  - Rescan/Watcher/„Jetzt synchronisieren" syncen jeden bekannten Ordner.
  - Upload einer lokalen Änderung → neue Revision; Download eines Server-Stands in den Ordner.
  - Konflikt: nichts überschrieben, lokale Fassung als Konflikt-Revision gesichert.
  - Restore einer Revision + Konfliktlösung schreiben in den richtigen Ordner.
  - „Ordner manuell zuordnen" für nicht erkannte Spiele; manuelle Ordner haben Vorrang.
  - Zu große Save-Sets (>5000 Dateien / >2 GB, z. B. Project Zomboid) bleiben übersprungen.
  - „Sync pausieren"/„Wieder einschließen"; Teilen (Lokal↔Synchron) inkl. Vergleichsdialog.
- **Öffentliche Schnittstellen heute:** Der **Server-API-Vertrag** (`ISaveVaultApi`, Manifest-
  Format `FileManifest`/`FileEntry` mit `RelativePath`) — bleibt formal **unverändert**;
  `RelativePath` bleibt ein string. Kein Client ↔ Server Vertragsbruch.

## Änderungswunsch
Spiele, deren Saves ludusavi über **mehrere getrennte Wurzeln** meldet (echte AAA-Titel:
Cyberpunk, Assassin's Creed, Star Wars Outlaws, Street Fighter 6, No Man's Sky, The Outer
Worlds u. a.), fallen heute auf „manuell", weil SaveVault pro Spiel **einen** gemeinsamen
Ordner bildet — der auf eine zu breite Ahnen-Wurzel (`C:\Users\…`, `C:\`) kollabiert und
verworfen wird. ludusavi liefert **präzise Pfade**; das Problem ist allein die Reduktion auf
einen Ordner. **Ziel:** ~99 % der Spiele automatisch abdecken, indem ein Spiel künftig
**mehrere Save-Ordner** haben kann (alle Orte, die ludusavi meldet). Nur echte Nischentitel,
die ludusavi **gar nicht** kennt, bleiben als „muss manuell zugeordnet werden".

**Datengrundlage (auf Tims Rechner gemessen, ludusavi 0.31.0):** 96 Spiele mit Saves; 78
schon heute einfach-Root, 18 mehr-Root. Der neue Gruppierungs-Algorithmus (unten) löst
**alle 96 sauber** auf — 0 ungelöste/zu breite Roots.

## Betroffene Fläche (Right-sizing)
- [x] **Kern** (Logik, Daten, Client-Sync) → `bauer`
- [x] **Oberfläche** (Client-GUI: Anzeige „mehrere Ordner") → `oberflaechen-bauer` (klein)
- [x] **Neue/veränderte Sicherheitsfläche** → `security-auditor` prüft mit (Pfad-Schreib-
      Chokepoint `ApplyRevisionAsync` wird mehr-Root-fähig → Traversal-Abwehr neu prüfen)
- [ ] Abhängigkeit/Stack ändert sich — nein (ludusavi/DTOs unverändert)
- [ ] Öffentliche Server-Schnittstelle ändert sich — nein (Manifest-Format formal gleich)

## Delta im Detail

### Kern-Delta

**1. Mehr-Ordner-Gruppierung (`GameDiscovery`).** Statt eines gemeinsamen Nenners über ALLE
Dateien werden ludusavis Dateipfade rekursiv in ihre natürlichen Save-Wurzeln gruppiert:
- Gemeinsamen Ordner der Pfadmenge bilden. Ist er **akzeptabel** (nicht zu breit, kein
  bekannter Container) → **ein Root**.
- Sonst an der nächsten Pfad-Verzweigung aufsplitten (nach dem Segment direkt unter dem
  gemeinsamen Ordner; bei unterschiedlichen Laufwerken nach Laufwerk) und je Gruppe
  **rekursiv** wiederholen, bis jede Gruppe einen engen, spielspezifischen Ordner hat.
- **Akzeptabel = nicht `IsTooBroad` UND kein bekannter Container-Root.** „Container" sind
  Wurzeln, deren Kinder je Spiel/Konto getrennt sind und die man daher **eine Ebene tiefer**
  betreten muss (deterministisch, kein Platten-Scan): Steam-Install (`X:\Steam`,
  `…\Program Files*\Steam`, `…\SteamLibrary`), `…\steamapps`, `…\steamapps\common`,
  `…\Steam\userdata`, `…\userdata\<steamid>`, `…\Ubisoft Game Launcher`, `…\savegames`,
  `…\savegames\<accountGuid>`, `…\GOG Games`, `…\Epic Games` u. ä. (pflegbare Liste in
  `SaveFolderSafety`).
- Der bestehende Disk-Heuristik-Check `FolderMuchLargerThanSaves` **treibt das Splitten
  nicht mehr** (er erzeugte Fehlalarme, wenn neben Saves noch Cache im Ordner liegt); er
  bleibt höchstens als letzter Sicherheits-Sockel für den Einfach-Root-Pfad erhalten oder
  wird entfernt, wenn die Container-Regeln ihn überflüssig machen (Entscheidung des `bauer`
  mit Beleg gegen die 96-Spiele-Daten; **kein** Verhalten still verschlechtern).
- Größen-Schutz **bleibt pro Spiel** (Summe über alle Roots > 5000 Dateien / > 2 GB →
  übersprungen wie heute).

**2. Stabiles Root-Kennzeichen (Restore-Zielort, geräteübergreifend).** Jeder Root bekommt
einen **stabilen Key**, mit dem ein Restore/Download auf einem anderen Gerät die Dateien in
den richtigen lokalen Ordner zurückschreibt. Der Key = **semantischer Anker + der
app-definierte Unterpfad darunter** (nicht nur das grobe Tag — echte Daten zeigen Spiele mit
**zwei** Ordnern unter demselben groben Tag, z. B. zwei `AppData\Local`-Ordner). Anker
werden aus bekannten Basen abgeleitet: `SavedGames`, `AppData\Local`, `AppData\Roaming`,
`Documents`, `Steam\userdata\<id>`, `steamapps\common`, `Ubisoft…\savegames\<guid>` usw. Der
maschinenabhängige Präfix (Laufwerk, Profilpfad) wird durch den Anker abstrahiert; die
konto-spezifischen Segmente (Steam-ID, Ubisoft-GUID) sind für **einen** Nutzer über seine
Geräte hinweg **konstant** (gleiche Accounts) → der Key ist geräteübergreifend konsistent.

**3. Manifest mit Root-Präfix (Server bleibt unangetastet).** `ManifestBuilder.Build` scannt
künftig **mehrere Roots** und legt je Datei den relativen Pfad als `"<rootKey>/<subpfad>"`
ab. **Rückwärtskompatibilität/Kein Churn:** hat ein Spiel nur **einen** Root, bleibt das
Schema **exakt wie heute** (kein Präfix) → die 78 Einfach-Root-Spiele erzeugen bit-identische
Manifeste, kein Reseed. Nur echte Mehr-Root-Spiele bekommen Präfixe.

**4. Schreib-Chokepoint mehr-Root-fähig (`ApplyRevisionAsync`, SICHERHEIT).** Beim Anwenden
eines Server-Manifests wird je Eintrag der `rootKey` abgetrennt, auf den **lokalen** Ordner
dieses Roots abgebildet und der Rest-Pfad **strikt gegen genau diesen Ordner** validiert
(`PathSanitizer.TryResolveWithin`, wie heute — kein `..`, kein absoluter Pfad, kein
Ausbrechen). Unbekannter/nicht abbildbarer `rootKey` → dieser Eintrag wird **nicht**
geschrieben (kein Blindschreiben außerhalb bekannter Roots). Validierung komplett **vor** dem
ersten Schreibzugriff (Alles-oder-nichts bleibt).

**5. Registry: mehrere Ordner je Spiel (Persistenz-Änderung + Migration).**
`SaveFolderRegistry` führt je Spiel eine **Liste** von Ordnern (je mit `rootKey`, `Manual`).
- **Migration (idempotent):** bestehende Einfach-Ordner-Einträge werden als **ein** Root mit
  abgeleitetem Key übernommen (kein Datenverlust). Altes Registry-Format bleibt lesbar.
- **Handzuordnung ersetzen, wo sicher** (Tim-Entscheidung): liefert die Erkennung für ein
  Spiel ein zuverlässiges Mehr-Root-Ergebnis **und** ist die alte manuelle Zuordnung einer
  der neu erkannten Roots (liegt darin/gleicht ihm), übernimmt Auto. Eine manuelle Zuordnung,
  die zu **nichts** aus der Erkennung passt (echter Nischen-Override), bleibt unangetastet.

**6. Verdrahtung (`ClientAgent`, `CommandPoller`, `FolderWatcher`, `HeartbeatReporter`).**
Rescan/Watcher/Sync iterieren die Root-Liste je Spiel; der Sync eines Spiels bleibt pro Spiel
serialisiert (ein Gate übers ganze Spiel, alle seine Roots — keine Parallel-Schreiber). Je
Root ein Watcher. Restore/Resolve treffen über den `rootKey` den richtigen Ordner. Heartbeat
meldet Storage/Ordner als Summe über die Roots.

- **Entferntes Verhalten:** keines beabsichtigt. Das Kollaps-auf-einen-Ordner-Verhalten wird
  durch die Gruppierung ersetzt; Einfach-Root-Spiele verhalten sich **identisch** zu heute.
- **Datenstruktur/Persistenz:** Registry-Format erweitert (Liste statt Einzelordner) **mit**
  Migration; Sync-State/Manifest-Format des Servers **unverändert** (nur Client-seitige
  Präfix-Konvention in `RelativePath`).

### Oberflächen-Delta (Client-GUI, klein)
- Eine Spielzeile mit mehreren Save-Ordnern zeigt das an (z. B. „3 Ordner" bzw. die Liste im
  Detail), statt nur einen Pfad. Kein neues Bedienkonzept; „Ordner manuell zuordnen" bleibt
  für echte Nischentitel. UI-Texte Deutsch.

## Was gleich bleibt (Nicht-Ziele)
- **Server/Dashboard/API-Vertrag** — kein Eingriff (Manifest-Format formal unverändert).
- **Einfach-Root-Spiele** (78 von 96) — bit-identisches Verhalten, kein Reseed, kein Churn.
- Größen-Schutz (>5000/2 GB), Teilen (Lokal↔Synchron), per-Gerät/geteilte Buckets,
  Konflikt-/Restore-Semantik, „Sync pausieren" — alles unverändert.
- Kein eigenes Filtern von ludusavis Dateiliste (Cache o. Ä. wird mitsynct — bewusst; ludusavi
  ist die kuratierte DB).

## Sicherheitsflächen (NEU berührt)
- [x] **Neue Pfad-Schreib-Logik:** `ApplyRevisionAsync` bildet `rootKey`→lokaler Ordner ab.
      → `security-auditor` prüft: kein Ausbrechen aus dem Zielordner, unbekannter/manipulierter
      `rootKey` schreibt **nirgends**, Traversal weiter über `TryResolveWithin` abgewehrt,
      Alles-oder-nichts-Semantik erhalten.
- [ ] Keine neuen ausgehenden Requests, keine neuen Secrets, keine neue Kommando-Ausführung.

## Akzeptanzkriterien der Änderung
- [ ] **Neu:** Die 18 Mehr-Root-Spiele aus Tims echter ludusavi-Ausgabe werden **automatisch**
      mit ihren korrekten, engen Ordnern erfasst (nicht mehr „manuell"). Headless gegen die
      echten 96-Spiele-Daten belegt: alle sauber aufgelöst, 0 zu breite/ungelöste Roots.
- [ ] **Neu:** Ein Mehr-Root-Spiel synct alle seine Ordner (Upload/Download), und ein
      Download/Restore schreibt jede Datei über den `rootKey` in den **richtigen** lokalen
      Ordner (Traversal-sicher).
- [ ] **Neu:** Nur Spiele ohne ludusavi-Treffer bleiben „manuell zuzuordnen".
- [ ] **Regression:** Alle Baseline-Pfade funktionieren weiter; Einfach-Root-Spiele erzeugen
      bit-identische Manifeste (kein Reseed); Build 0/0, `dotnet test` grün (bestehende + neue),
      Server/Dashboard unberührt lauffähig.

## Verifikation
- **Headless beweisbar (Kern):** Unit-Tests der Gruppierung + Root-Key gegen reale Pfadmuster;
  ein Wegwerf-/Integrationscheck lässt den Gruppierungs-Algorithmus über Tims echte
  `backup --preview`-Ausgabe laufen (Erwartung: 78 einfach / 18 mehr / 0 Probleme).
  Manifest-Bau + `ApplyRevisionAsync`-Routing mit mehreren Roots per Harness real geprüft
  (inkl. Traversal-Abwehr je Root und unbekanntem `rootKey`).
- **WPF-Client-Laufzeit** ist auf dem Notebook nicht isolierbar (bekannte Grenze früherer
  Client-Phasen) → GUI-Anzeige „mehrere Ordner" + realer Zwei-Ordner-Sync per **Handtest bei
  Tim** nach dem Release.
- **Rollout:** reine Client-Änderung → Version **1.7.0**, Release-ZIP (Tag `v1.7.0`), einmal
  auf alle Geräte; danach greift der Selbst-Updater (1.6.0).

## Offene Fragen
- Keine offen — Kern-Entscheidungen (alle Orte; Handzuordnung ersetzen wo sicher) geklärt.
