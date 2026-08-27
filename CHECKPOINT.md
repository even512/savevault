# SaveVault — Fortschritt (fortgeschrieben 2026-08-27)

**Aktueller Stand (2026-08-27):** **SERVER-STRECKE (Schritte 1–4) KOMPLETT — alle Gates grün.**
Schritt 4 (Web-Dashboard) Gate: **reviewer GRÜN + security-auditor GRÜN**. Damit stehen
Gerüst, Core, Server-API und Web-Dashboard. **Staffel-Halt zur Client-Strecke (5–8).**
Commits: 9847744 (Gerüst+Core+Server) · 25b9f91 (Schritt-3-Nachbesserung) · ac4b4fc (Dashboard).

**Offener Entscheidungspunkt vor Schritt 5 (Anzeige-Kompromisse des Dashboards):** Drei
Felder aus Spec/Mockup sind datengetrieben (noch) nicht darstellbar, weil das Server-Modell
sie nicht führt — Entscheidung, ob in Schritt 5 (Client meldet sie) + kleiner Server-Nachtrag
mitgenommen oder als MVP-Kompromiss belassen:
- **Speicher je Client** (Spec Z.121): braucht per-Gerät-Bytes (+ ggf. IP) in `DeviceInfo` +
  Heartbeat-Meldung durch den Client (Schritt 5).
- **per-Spiel-Geräte-Status** (echte Syncing/Error-Zustände): braucht einen Server-Endpunkt,
  der die schon gemeldeten `DeviceGameState` je Spiel ausliefert. Aktuell nur Synced/Pending/
  Conflict aus Revisionshistorie ableitbar.
- Server-Info in Einstellungen (Container/Port/Storage-Pfad): optional über `/health`-Erweiterung.

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
Schritt 4 (Web-Dashboard, `oberflaechen-bauer`, dark-SPA nach `design-reference/`-Mockup,
konsumiert die API) + XSS-Gate. Großer Schritt — VOR Start frischen 5h-Stand von Tim holen
(budgetverwalter: passt plausibel in ein frisches Fenster, aber ohne großen Puffer).

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
