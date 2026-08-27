# SaveVault — Fortschritt (fortgeschrieben 2026-08-27)

**Aktueller Stand (2026-08-27):** **SCHRITTE 1–6 + NACHRÜST-BLOCK + M2-FIX KOMPLETT.** Es stehen
Gerüst, Core, Server-API, Web-Dashboard, die drei echten Anzeige-Felder, der M2-Fix, die
WPF-freie Client-Hintergrund-Logik (Schritt 5) und die WPF-Tray-Oberfläche (Schritt 6). Alle
Gates grün. **Offen: Schritt 7 (xUnit-Tests) + Schritt 8 (Laufzeit-Gate `tester`).**
Commits: 9847744 · 25b9f91 · ac4b4fc · a72eddc · 132350b (Nachrüst-Block) · 282fba4 (Schritt 5)
· 23dec23 (M2-Fix) · Schritt-6 (dieser Commit).

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

## Nächster Schritt
1. **Schritt 7 (Tests, `bauer`):** xUnit auf Core (Sync-Entscheidung `SyncDecider`,
   Konflikterkennung, Hashing/Manifest, Pfad-Sanitisierung `PathSanitizer.TryResolveWithin`).
   Test-Projekt `tests/SaveVault.Core.Tests/` besteht schon aus dem Gerüst. Client-Services sind
   DI-fähig — optional auch SyncEngine-Fälle testbar. Danach Gate (reviewer) + Commit.
2. **Schritt 8 (Laufzeit-Gate, `tester`):** GROSSER Schritt, eigenes frisches Fenster einplanen.
   Alles baut/testet; Server (`docker build`/`docker compose up` ODER `dotnet run --project
   src/SaveVault.Server`, Port 8420) starten; Web-Dashboard im Browser (Chrome-Automation)
   bedienen; Sync/Konflikt/Restore mit ZWEI lokalen Save-Ordnern als zwei „Geräte" durchspielen;
   Client-Tray starten. **Zwingend am Gate:** echtes `ludusavi --api`-Schema gegen die
   mitgelieferte Binary bestätigen (`Ludusavi/LudusaviDtos.cs` „schema-to-verify"; `GameDiscovery`
   leitet den Save-Ordner als gemeinsame Wurzel der Datei-Keys ab). `tools/ludusavi/ludusavi.exe`
   muss vorliegen; Port 8420 frei.

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
