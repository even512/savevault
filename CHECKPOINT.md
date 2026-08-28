# SaveVault — Fortschritt (fortgeschrieben 2026-08-28)

**v1.0.4 — Übersprungene Spiele bleiben im Client sichtbar (manuell zuordnen).** Bisher
tauchten rausgefallene Spiele (mehrdeutiger/kollabierter Ordner, zu großes Save-Set) nur einmalig
im Hinweis-Dialog nach der Erkennung auf. Jetzt bleiben sie dauerhaft als Zeile in der Status-
Fläche, amber markiert („Nicht automatisch erfasst" + Grund) mit eigenem Button „Ordner zuordnen".
- Erkennung liefert strukturierte Skips: neue Typen `SkipReason`/`SkippedGame`; `DiscoveryResult.
  Skipped` (die alten `SkippedAmbiguous`/`SkippedTooLarge` bleiben als abgeleitete Anzeige-Helfer,
  Dialog unverändert).
- `AgentState`: `GameStatusView.IsSkipped/SkipReason`; neue `ReplaceSkipped(...)` (ersetzt die
  Skip-Menge, entfernt veraltete Skips, lässt echt verwaltete Spiele unangetastet); `EnsureGame`/
  `SetStatus` löschen den Skip-Marker (echter Zustand hat Vorrang). `ClientAgent.RefreshDiscovery`
  füllt die Skips nur bei erfolgreicher Erkennung (kein Löschen bei ludusavi-Aussetzer).
- GUI: `GameRow` zeigt Skip-Zeile + „Ordner zuordnen"; `OnAssignFolderClick` ordnet den gewählten
  Ordner GENAU diesem Spiel zu (`AddManualFolder(row.Game, path)`) → danach regulär synchronisiert.
  Neuer Amber-Brush `StatusVisuals.Attention`.
- Build 0/0, 88 Tests grün. Client-Version → 1.0.4. (Reine Client-Änderung; Server unberührt.)



**v1.0.3 — Server-Export + Box-Art (IGDB).** Zwei neue Features (Delta-Spec
`specs/savevault-change-export-boxart.md`):
- **Revision-Export als ZIP** (master-only): `GET /api/games/{key}/revisions/{n}/export`
  rekonstruiert aus Manifest + Blobs die Originalstruktur der Savegames und legt eine
  `SaveVault-Info.txt` bei (Spiel, Revision, Quell-Gerät, Zeit, **Standard-Save-Pfad**). Pfad-
  Sicherheit über `PathSanitizer.SafeZipEntryName` (kein `..`, nicht rooted, Segmente saniert) –
  **live verifiziert**: ein manipulierter Manifest-Eintrag `../../evil.txt` landet als `evil.txt`
  im ZIP. Streaming mit gezieltem `AllowSynchronousIO` nur für diesen Endpunkt (ZipArchive
  schreibt Central Directory synchron). Dashboard: Export-Button je Revision (Blob-Download mit
  Bearer-Header), Anzeige des Standard-Pfads im Drawer.
- **Standard-Save-Pfad** durchgereicht: `UploadRevisionRequest.SaveRoot` (Client sendet den
  ohnehin bekannten Save-Ordner) → an `Revision`/`RevisionDownload`/`RevisionInfo` persistiert.
- **Box-Art via IGDB** (wie dashsharp „game-releases"): `CoverService` (Twitch-OAuth →
  IGDB-Namenssuche → Cover von `images.igdb.com`, Platten-Cache `dataRoot/covers`, Negativ-Cache).
  `GET /api/games/{key}/cover` (master-only) → image/jpeg oder 404. Konfiguration
  `SAVEVAULT_IGDB_CLIENT_ID/SECRET`; **ohne Keys sauber deaktiviert** (live: 404, kein Crash).
  Strikte Outbound-Allowlist (id.twitch.tv, api.igdb.com, images.igdb.com), image_id auf
  `[a-z0-9_]` gefiltert, Bildgröße/Timeout begrenzt, Secrets nie geloggt. Dashboard: echtes Cover
  per Blob-Fetch, Fallback auf die farbige `coverColor`-Kachel.
- **Grün:** Build 0/0, `dotnet test` **88/0/0** (+11 für `SafeZipEntryName`). Laufzeit-Smoke
  end-to-end (Pairing→Upload→Export) belegt. `.env.example` dokumentiert die IGDB-Keys.
- **Verteilung:** master-Push → neues Server-`:latest`-Image (Docker-Workflow); Tag `v1.0.3` →
  Client-Release-ZIP + versioniertes Server-Image. Client-Version → 1.0.3 (sendet SaveRoot).
- **Sicherheits-Selbstprüfung** am Gate statt security-auditor-Agent (Budget): Traversal live
  entschärft, SSRF durch feste Hosts ausgeschlossen, Auth master-only, keine Secret-Leaks.



**v1.0.2 — Pfad-Härtung fertiggestellt (2026-08-28).** Der vorherige Worker war beim Bau von
1.02 mitten in `GameDiscovery` gekappt worden: er hatte den Aufruf `FolderMuchLargerThanSaves`
(Street-Fighter-/Steam-Root-Kollaps) geschrieben, die Methode aber nie definiert → genau **ein**
CS0103-Compile-Fehler, Client baute nicht. Zusätzlich war die UI-Info für die **zu großen**
Spiele (Project Zomboid) nicht verdrahtet.
- **Fix 1 (Compile):** `FolderMuchLargerThanSaves(folder, saveFileCount, ct)` in `GameDiscovery.cs`
  implementiert. Zählt die Dateien im abgeleiteten Ordner **beschränkt** (bricht bei
  `max(saveFileCount*4, saveFileCount+100)` ab → enumeriert NIE einen Riesenbaum, ist selbst kein
  Show-Stopper), `EnumerationOptions{RecurseSubdirectories, IgnoreInaccessible, AttributesToSkip=
  ReparsePoint}` (keine Symlink-Verfolgung). Unlesbarer Ordner ⇒ sicherer Default „zu weit gefasst".
- **Fix 2 (UI-Info):** `MainWindow.OnRediscoverClick` zeigt jetzt auch `SkippedTooLarge` an —
  der Anwender erfährt, welche Spiele wegen zu großem Save-Ordner ausgelassen wurden und über
  »Ordner hinzufügen« einen kleineren Unterordner nachtragen kann. `SkippedAmbiguous` (zu breit /
  kollabiert) wurde schon gemeldet.
- **Version** auf 1.0.2 (`SaveVault.Client.csproj`: Version/AssemblyVersion/FileVersion) → wird als
  AgentVersion an den Server gemeldet.
- **Grün:** `dotnet build SaveVault.sln` 0/0, `dotnet test` **77/0/0**.
- **Offen/noch nicht getan:** Gate (reviewer + security-auditor auf die neue Disk-Enumerations-
  Fläche + tester) und Commit stehen noch aus — Entscheidung bei Tim (Limit-Lage).

---

# SaveVault — Fortschritt (fortgeschrieben 2026-08-27)

**Aktueller Stand (2026-08-27): ✅ MVP FERTIG — ALLE 8 BAU-PLAN-SCHRITTE + LAUFZEIT-GATE GRÜN.**
Gerüst, Core, Server-API, Web-Dashboard, drei echte Anzeige-Felder, M2-Fix, Client-Hintergrund
(Schritt 5), WPF-Tray (Schritt 6), Core-Tests (Schritt 7, 61 grün), ludusavi-Fix, und das
**Laufzeit-Gate (Schritt 8) GRÜN** (tester).
Commits: 9847744 · 25b9f91 · ac4b4fc · a72eddc · 132350b (Nachrüst) · 282fba4 (S5) · 23dec23
(M2) · d8bc68a (S6) · be1b0d1 (S7) · f345c0b (ludusavi-Fix) · Abschluss (dieser Commit).

**LAUFZEIT-GATE (Schritt 8) — GRÜN, vom `tester` belegt (Server via `dotnet run` auf :8420):**
Build 0/0 + 61 Tests grün; Dashboard rendert alle fünf Ansichten dark mit ECHTEN Werten
(Server-Info Port/Storage/Container/Version, Pairing-Code + Erneuern), Leerzustand ohne Absturz,
keine Konsolenfehler; komplette Kette end-to-end korrekt: Pairing (2 Geräte) → Upload (M2-Head-
Semantik verifiziert: Head rückt erst nach vollständigem Content vor) → Download (Bytes identisch)
→ Konflikt (nichts überschrieben, beide Fassungen erhalten, `/api/conflicts` listet) → Lösung
(KeepDevice, ApplyResolution-Befehl in Verlierer-Queue) → Restore (Restore-Befehl in Queue);
Fehlerfälle sauber 401/403 statt 500; ludusavi real (95 Spiele); Tray startet stabil.

**Noch offen (KEINE Blocker, Handtest/Umgebung):**
- **Docker-Image**: hier kein Docker im PATH — `docker build`/`compose up` auf Unraid verifizieren.
- **Tray-GUI-Tiefe**: nur fehlerfreier Start geprüft; Fenster/Dialoge per Handtest bedienen.
- **„Server ohne Token"**-Startverhalten: gegen den Token-Server nicht geprüft (einmal ohne
  `SAVEVAULT_TOKEN` starten → muss laufen + API mit klarer Meldung verweigern).
- Backlog-Punkte unten (security H2/H5/H6, L1/L2, Waisen-Pending, App.OnExit) — alle low.

**ERLEDIGT — Schritt 7 (Core-Tests), selbst-geprüft grün.** 61 xUnit-Tests, keine Core-Bugs
aufgedeckt: `SyncDeciderTests` (4 Fälle + LocalChanged/IsConflict, echte `FileManifest.Create`-
Manifeste), `ManifestBuilderTests` (FileHasher-Determinismus, Build/Diff, Vorfilter-Äquivalenz,
verschachtelte Ordner, RelativePath-Normalisierung), `PathSanitizerTests` (Traversal-Abwehr:
dotdot/absolut/UNC/Präfix-Trick/Elternverzeichnis, HashKey sicherer Dateiname). `TempDirectory`-
Helfer. `dotnet test` → 61/0/0. src/ unangetastet.

**ERLEDIGT — Schritt 6 (WPF-Tray), reviewer GRÜN.** `System.Windows.Forms.NotifyIcon` (eingebaut,
kein NuGet, `UseWindowsForms`), Status-Fenster + Einstellungen/Pairing + modaler Konflikt-Dialog
(echte Felder je Teilnehmer-Revision: Zeit/Größe/Dateien/Gerät/Prüfsumme, keine erfundenen),
dark-only Theme (`Ui/Theme.xaml`), Threading über `Dispatcher`. Dünne durchreichende
`ClientAgent`-Methoden ergänzt (`GetConflictsAsync`, `GetRevisionsAsync`, `ResolveConflictAsync`,
`CurrentDeviceId/Name`). Token nie angezeigt; Fremddaten über WPF-Text-Bindings. Build 0/0.
Nachrangig (Backlog): `App.OnExit` async-void Cleanup nur best-effort; einmaliger GDI-HICON;
Fremdgerät im Konflikt als Kurz-ID (nur `DeviceId` verfügbar).

**ERLEDIGT — Schritt 5 (Client-Hintergrund), Gate grün.** security-auditor GRÜN (Pfad-Traversal-
Chokepoint `SyncEngine.ApplyRevisionAsync` mit Zwei-Pass-Validierung via `PathSanitizer.
TryResolveWithin`; ludusavi fester-Binary-Aufruf; Token nur in config.json). reviewer nach
Nachbesserung grün: **B1 behoben** (Befehls-Anwendung Restore/Resolve jetzt über gemeinsames
Pro-Spiel-Gate `GameSerializer` wie der Sync-Zyklus → kein Upload halb geschriebener Ordner,
deadlockfrei da `ApplyRevisionAsync` das Gate nicht selbst nimmt), **M1 behoben** (Konflikt-
Revision nur noch bei geändertem Manifest-Hash, persistierte Konflikt-Marke `*.conflict.json`).
Services unter `src/SaveVault.Client/Services/` (ClientAgent, SyncEngine, CommandPoller,
HeartbeatReporter, PairingService, FolderWatcher, GameDiscovery, SaveFolderRegistry,
SyncStateStore, GameSerializer, AgentState, ClientConfig, AppPaths, JsonFileStore,
DeviceIdentity). Status-Fläche `AgentState`/`GameStatusView` steht für die Schritt-6-GUI bereit.
Build 0/0. **KEIN reviewer-Re-Agent** für B1/M1 — Fixes vom Orchestrator selbst verifiziert
(Budget 82 %).

**ERLEDIGT — M2 (serverseitig) behoben, Re-Gate reviewer GRÜN.** Head (`GameRecord.
CurrentRevision`) rückt bei Nicht-Konflikt-Revisionen erst vor, wenn ALLE Blobs vorliegen:
`RegisterRevisionAsync` nimmt eine Revision mit fehlenden Blobs in `GameRecord.PendingRevisions`
(Status `Syncing`, kein Head-Sprung, keine "upload"-Activity); der Content-PUT-Endpunkt ruft
nach `StoreContentAsync` das neue `TryFinalizePendingAsync`, das vollständige Pending-Revisionen
entlang der Kette (`BasedOnRevision ?? CurrentRevision == CurrentRevision`) finalisiert (Head +
Metadaten + `Synced` + genau eine "upload"-Activity). Dedup-Fall (`missing==0`) finalisiert
sofort wie früher; Konflikt-Zweig unverändert. Kein Contract-/Client-Eingriff. Build 0/0.
Restfolge (Backlog): Waisen-Pending bei wiederholtem Upload-Abbruch (kein Head, kein
Datenverlust) — Aufräumen offen.

**ERLEDIGT — ALLE DREI Anzeige-Felder nachgerüstet** (Re-Gate reviewer GRÜN + security-auditor
GRÜN, Build 0/0, `node --check` ok). Umsetzung bewusst über NEUE DTOs statt `DeviceInfo` zu
erweitern (Client-Vertrag bleibt stabil):
- **Speicher je Client + IP** (Spec Z.121): neues `DeviceView`-DTO
  (`id,name,os,agentVersion,lastSeenUtc,ipAddress,storageBytes,gameCount`); `/api/devices`
  liefert es jetzt. IP serverseitig aus `ctx.Connection.RemoteIpAddress` am Heartbeat
  (`DeviceRecord.LastIpAddress`, nicht client-gemeldet, kein `X-Forwarded-For`). StorageBytes/
  GameCount = Summe/Anzahl über Spiele mit `BaseRevision > 0`. Dashboard: Client-Karte + Drawer.
- **per-Spiel-Geräte-Status**: `GET /api/game-states` (master-only) → `GameStatesResponse`
  (`states[]` mit `deviceId,game,baseRevision,status`); Spiel-Drawer nutzt echten Status statt
  Ableitung aus Revisionshistorie.
- **Server-Info**: `GET /api/server-info` (master-only) → `port,dataRoot,configured,container,
  version` (kein Secret); Einstellungen zeigen echte Werte.
- Berührt: `ApiContracts.cs`, `ApiRoutes.cs`, `ServerIndex.cs`, `VaultStore.cs`,
  `SaveVaultEndpoints.cs`, `wwwroot/app.js`. `DeviceInfo.cs` unverändert.

**Erledigt Schritt 3-Nachbesserung:** KeepBoth-Konvergenz-Befehle; Anzeigename/Store aus
Heartbeat; ResolveKeepDevice-Validierung; H1/H3/H4.

## Backlog (später, kein Blocker im Ein-Nutzer-LAN hinter VPN)
- security H2: Restore/Resolve auf Master-Token beschränken.
- security H5: Upload-Größenlimit (Speicher-DoS durch gekoppeltes Gerät).
- security H6: Timing-Leak der Master-Token-Länge.
- security H4-Rest: Rate-Limit an Anfrage-Quelle koppeln (aktuell global → theoret. Selbst-DoS
  der seltenen Pairing-Aktion).
- reviewer minor: bei >2 Konflikt-Teilnehmern wird nur der erste Nicht-Head-Stand als Fork-Bucket
  abgelegt (Rest bleibt verlustfrei in der Historie); MVP = 2 Geräte, daher unkritisch.
- reviewer minor: Anzeige-Artefakt — `BaseRevision`/Status der Nicht-Gewinner kurz `Synced`
  statt `Pending` bis zum nächsten Client-Heartbeat (kein Konvergenz-/Datenproblem).
- **M2 behoben** (siehe Stand-Block). Restfolge: Waisen-Pending-Revisionen aufräumen (kein
  Head, kein Datenverlust) — offen, low.
- security L1 (Schritt 5, low): Symlink/Junction-Following beim Restore-Schreiben — `Path.
  GetFullPath` löst keine Reparse-Points; Angriff braucht vorab existierenden Symlink im
  Save-Ordner. Optional: Real-Pfad-/Reparse-Prüfung.
- security L2 (Schritt 5, low): heruntergeladener Inhalt wird nicht gegen den angefragten
  SHA-256 verifiziert (Server ist per Design vertraut). Defense-in-depth: Hash beim Schreiben
  mitrechnen und vergleichen.

**ERLEDIGT vor Schritt 8 — ludusavi `--api`-Bug behoben + Schema verifiziert (0.31.0).**
Tim hat `tools/ludusavi/ludusavi.exe` (0.31.0) abgelegt. Beim Verifizieren fiel ein echter
Core-Bug auf: `LudusaviClient` rief `--api find` / `--api backup --preview` — in 0.31.0 gehört
`--api` aber HINTER den Subbefehl (`find --api`, `backup --preview --api`), sonst lehnt die CLI
mit „unexpected argument" ab → Erkennung hätte NIE funktioniert. Fix: Argument-Reihenfolge in
`LudusaviClient.FindAsync`/`BackupPreviewAsync`. DTO-Schema (`LudusaviDtos.cs`) deckt sich mit der
echten Ausgabe (verifiziert per Wegwerf-Integrationscheck: `BackupPreviewAsync` parst real 95
Spiele, `overall.totalGames`/`files[].bytes`/`change` korrekt). Build 0/0, 61 Tests grün.

## Nächster Schritt
**Der MVP ist fertig.** Optional/später:
1. Auf Unraid deployen: `docker build`/`docker compose up`, echten Datenpfad-Volume + Token setzen,
   dann realen Mehrgeräte-Betrieb (echte Windows-PCs, ludusavi-Erkennung, Tray-Pairing).
2. Handtest der Tray-GUI (Status-Fenster, Einstellungen/Pairing, Konflikt-Dialog).
3. „Server ohne Token"-Start einmal prüfen.
4. Backlog-Härtungen (security H2/H5/H6, L1/L2, Waisen-Pending, App.OnExit) nach Bedarf.

## Backlog Client (Schritt 6, low)
- `App.OnExit` async-void: Netz-Schleifen werden beim Beenden nur best-effort gestoppt.
- einmaliger GDI-HICON in `TrayIconFactory` (vernachlässigbar).
- Konflikt-Dialog zeigt Fremdgerät als Kurz-ID (nur `DeviceId` im `ConflictParticipant`).

---

# SaveVault — Limit-Checkpoint (2026-08-26, historisch)

**Grund:** 5-Stunden-Nutzungslimit auf 100 % erreicht. Halt an der Schritt-Grenze
gemäß RULES → „Limit-Checkpoint". Nichts geht verloren — Dateien liegen auf Platte,
Gerüst ist in git erfasst (noch **kein** Commit, wie vorgesehen).

## Erreichter Stand
- **Phase A:** abgeschlossen, Spec `.claude/projekt-werkstatt/specs/savevault.md`
  von Tim freigegeben (2026-08-26).
- **Spec-Gate:** grün (reviewer + inspekteur).
- **Bau-Plan-Schritt 1 (Gerüst):** FERTIG. .NET-9-Solution (Core/Server/Client/Tests),
  `.gitignore`, `.env.example`, README, Dockerfile, docker-compose,
  `design-reference/` (Mockup + Bausteine + Leseanleitung), `tools/ludusavi/README`.
  Baut mit 0 Fehlern. `git init` + `git add -A` erfolgt, Index sauber (keine Secrets).
- **Bau-Plan-Schritt 2 (Core-Bibliothek):** **FERTIG** — der bauer-Lauf ist vor dem
  Kappen noch sauber durchgelaufen. `SaveVault.Core` mit `Models/ Hashing/ Sync/
  Storage/ Ludusavi/ Api/ Serialization/`; `Class1.cs` entfernt. Solution baut mit
  **0 Fehlern**; 19/19 Eigenprüfungen des bauer grün. Gemeinsamer API-Vertrag
  (`ISaveVaultApi`, `ApiRoutes`, `ApiContracts`) steht. Noch **nicht gated** und noch
  **kein** git-Commit.

## Nächster Schritt nach dem 5h-Reset
1. **Komponenten-Gate Core** (Schritt 2 ist gebaut, aber ungated): reviewer +
   **security-auditor** (Subprozess-/Pfad-Fläche: `Ludusavi/LudusaviClient.cs`,
   `Storage/PathSanitizer.cs`) + inspekteur + budgetverwalter. Optional vorab
   `dotnet build SaveVault.sln` als Sanity.
2. Nach grünem Core-Gate: **Staffel-Halt-Entscheidung** für Schritt 3 (Server-API)
   gegen frischen `/usage`-Stand.
3. Danach Server-Strecke weiter: Schritt 3 (Server-API + Docker) → Schritt 4
   (Web-Dashboard, `design-reference/` als Vorlage).

## Am Laufzeit-Gate noch gegen die Realität zu prüfen (aus Core)
- Echtes `ludusavi --api`-Schema (`find`, `backup --preview`) gegen die mitgelieferte
  Binary bestätigen — in `Ludusavi/LudusaviDtos.cs` als „schema-to-verify" markiert.

## Staffelung (vom budgetverwalter, weiterhin gültig)
- Server-Strecke = Schritte 1–4 (Gerüst, Core, Server-API+Docker, Web-Dashboard),
  Halt, dann Client-Strecke = Schritte 5–8 (Client-Hintergrund, WPF-Tray, Tests,
  Laufzeit-Gate). Woche stand zuletzt bei 42 % (Reset in ~3d21h) — komfortabel;
  eng ist nur das 5h-Fenster.

## Budget-Zeile (Stand Checkpoint)
5h **100 %** (erschöpft, Reset abwarten) · Woche **~42 %** (Reset ~3d21h).
