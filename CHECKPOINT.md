# SaveVault — Fortschritt (fortgeschrieben 2026-08-27)

**Aktueller Stand (2026-08-27):** **HALT an der Schritt-3-Grenze** (Tim-Entscheidung).
Budget bei Halt: **5h = 80 % (Reset in ~4h16m), Woche = 53 %.** Core-Gate GRÜN
abgeschlossen. Schritt 3 (Server-API) gebaut + HTTP-selbstverifiziert + **committet**
(9847744, Gerüst+Core+Server).

**Fahrplan (Tim, Option 2):** Nichts mehr im laufenden 5h-Fenster. Nach dem 5h-Reset
frischen `/usage` holen, dann im frischen Fenster: (1) bauer-Nachbesserungs-Lauf für die
Punkte unten, (2) right-sized Re-Gate, (3) Schritt 4 (Web-Dashboard). Woche mit 53 % im
Blick behalten — die Reststrecke (4–8) inkl. Laufzeit-Gate ist noch groß.

**Schritt-3-Gate gelaufen — Ergebnis ROT (bedingt):** inspekteur GRÜN, security-auditor
GRÜN (6 Härtungen), budgetverwalter „Halt vor Schritt 4", **reviewer ROT (1 blockierender
Punkt)**. Schritt 3 ist erst abgeschlossen, wenn die Nacharbeit unten grün nachgeprüft ist.

## Offene Nacharbeit an Schritt 3 (bevor Schritt 4 startet)
**Blockierend (reviewer):**
- KeepBoth reiht KEINEN Download-Befehl für das Verlierer-Gerät ein → Geräte divergent,
  Akzeptanzkriterium Z.188 nicht erfüllt. `VaultStore.cs:534-608`. Klären, welcher Download
  (Gewinner-Head oder Fork) fürs Verlierer-Gerät gewünscht ist, und einreihen.

**Vor Schritt 4 relevant (Dashboard-Optik / Korrektheit):**
- reviewer [mittel]: Anzeigename/Store-Metadaten gehen verloren (`GameKey(routeValue,
  routeValue)`), Dashboard zeigt „the witcher 3" statt „The Witcher 3".
  `SaveVaultEndpoints.cs:134-139` + `VaultStore.cs:172-176, 710-724`. Anzeigename beim Upload
  mitführen oder aus Heartbeat-`GameKey` übernehmen.
- reviewer [niedrig]: ResolveKeepDevice — Gewinner-Validierung fehlt (`winnerDevice=""` möglich).
  `VaultStore.cs:473-490`.
- security H1 [mittel]: Revisions-Upload prüft `CanActAsDevice(req.Device.Id)` nicht →
  Attributions-Spoofing. `SaveVaultEndpoints.cs:53`.
- security H3 [mittel]: `/devices`, `/activity`, `/pairing-code`, `regenerate` per `IsMaster`
  absichern (nicht jeder Geräte-Token). `SaveVaultEndpoints.cs:111-127`.
- security H4 [mittel]: Pairing-Code single-use ODER kurzlebig + Rate-Limit auf `/api/pair`.
  `VaultStore.cs:110`.

**Backlog (später, kein Blocker im Ein-Nutzer-LAN):**
- security H2 (Restore/Resolve nur Master), H5 (Upload-Größenlimit), H6 (Timing-Länge Master-Token).

## Nächster Schritt
Bauer-Nachbesserungs-Lauf (blockierend + „vor Schritt 4"-Punkte gebündelt) → right-sized
Re-Gate (reviewer auf berührte Sync-Semantik, security-auditor auf H1/H3/H4) → dann erst
Schritt 4. Halt-/Weiterlauf-Entscheidung liegt bei Tim (Budget).

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
