# SaveVault — Fortschritt (fortgeschrieben 2026-08-27)

**Aktueller Stand (2026-08-27):** **SERVER-STRECKE (Schritte 1–4) + NACHRÜST-BLOCK KOMPLETT —
alle Gates grün.** Damit stehen Gerüst, Core, Server-API, Web-Dashboard und die drei echten
Anzeige-Felder. **Staffel-Halt zur Client-Strecke (5–8).**
Commits: 9847744 (Gerüst+Core+Server) · 25b9f91 (Schritt-3-Nachbesserung) · ac4b4fc (Dashboard)
· a72eddc (Nachrüst-Entscheidung) · Nachrüst-Block (dieser Commit).

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

## Nächster Schritt
**CLIENT-STRECKE, Schritt 5 (Client-Hintergrund, `bauer`):** Watcher + Entprellung + Rescan,
`ludusavi`-Erkennung, Sync-Steuerung über Core, Befehle abholen/ausführen (Restore/Resolve),
Zustandsmeldung (Heartbeat). Danach Schritt 6 (WPF-Tray, `oberflaechen-bauer`), Schritt 7
(xUnit-Tests auf Core), Schritt 8 (Laufzeit-Gate `tester`). Client-Strecke ist groß (WPF-GUI +
echte Sync-Demo) — VOR Start Budget prüfen (Woche zuletzt 62 %); ggf. Schritt 5 und 6 in
getrennten Fenstern. Am Laufzeit-Gate: echtes `ludusavi --api`-Schema gegen die Binary
bestätigen (in `Ludusavi/LudusaviDtos.cs` als „schema-to-verify" markiert).

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
