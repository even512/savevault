# SaveVault — Fortschritt (fortgeschrieben 2026-09-03)

**Client-Selbst-Updater (Client 1.6.0) — Update im laufenden Betrieb aus GitHub-Releases.**
Delta-Spec `specs/savevault-change-selbst-updater.md`, Weg über `/projekt-edit`. Reine
**Client-Änderung**; erfordert (wie frühere Client-Phasen) ein Client-Update auf allen Geräten.
**Gate grün, committet auf Branch `phase-selbst-updater` (kein Push).**
- **Was neu ist:** Der Client prüft beim Start (verzögert, ~20-h-gedämpft über `LastUpdateCheckUtc`)
  und danach **alle 24 h**, ob `even512/savevault` ein neueres Release hat (`releases/latest`, Tag
  `vX.Y.Z`, Asset `SaveVault-Client-*-win-x64.zip`; öffentliches Repo → kein Login). Fund → **Banner**
  im Fenster + einmaliger **Tray-Hinweis**; Optionen-Karte **„Über & Updates"** (installierte Version,
  „Nach Updates suchen", Schalter „Automatisch nach Updates suchen" Default an). Angewandt nur auf Klick.
- **Selbst-Austausch (im laufenden Betrieb):** ZIP → `%LocalAppData%\SaveVault\update\staging` entpackt
  → gestagte exe startet im **Applier-Modus** (`--apply-update <installDir> <pid>`, ganz früh in
  `App.OnStartup` abgefangen, ohne Tray/Agent) → alte Instanz beendet sich → Applier wartet aufs Ende,
  tauscht **transaktional** aus (jede Alt-Datei per atomarem Rename `.svold` zur Seite; bei Fehler
  **Rollback** auf den letzten guten Stand) → startet die neue exe im Installationsordner. Reste räumt
  der neue Start verzögert im Hintergrund mit Wiederholungen auf (Applier hält die Staging-exe kurz).
- **Neu:** `Services/UpdateService.cs` (Prüfen/Staging/Applier), Felder `AutoUpdateCheckEnabled` +
  `LastUpdateCheckUtc` in `ClientConfig`. Geändert: `App.xaml.cs` (Applier-Zweig, Start-/24-h-Check,
  Tray-Hinweis, verzögertes Cleanup), `MainWindow.xaml`/`.cs` (Banner, Optionen-Karte, Toggle, Logik).
- **Gate grün:** Build **0/0**, `dotnet test` **112/0/0** (unverändert). **Laufzeit real belegt** per
  Wegwerf-Harness: Versions-Parsing/-Vergleich (v1.6.0>1.5.0, ==, <, kaputt→null), Asset-Auswahl,
  **echter GitHub-Live-Check** (UA akzeptiert, Tag+Vergleich korrekt → „UpToDate" gegen 1.5.0),
  Erfolgs-Austausch (Überschreiben/Unterordner/Fremddateien bleiben, `.svold` weg) **und Rollback**
  (erzwungener Kopierfehler → app.exe auf Alt-Stand zurückgerollt, keine Reste). `/code-review high`:
  **6 Befunde → 5 behoben** (transaktionaler Austausch + Rollback, kein toter Zustand, Zeitstempel nur
  bei Erfolg, XAML-Überlappung, verzögertes Cleanup), **1 begründet zurückgestellt** (überzählige
  Alt-Dateien werden nicht gelöscht – installDir ist reiner Publish-Output, self-contained-Loader bindet
  keine überzähligen DLLs; als bekannte Grenze vermerkt). `/security-review`: **sauber** (fester
  HTTPS-Host + Repo → kein SSRF; Zip-Slip framework-abgesichert; `Process.Start` ohne Shell/Injektion;
  Applier-Args = lokal/selber Nutzer, keine Rechte-Grenze; keine Geheimnisse berührt/geloggt).
- **Bewusst akzeptiert (Spec):** Keine zusätzliche Signatur-/Hash-Prüfung über HTTPS-zu-github.com
  hinaus – gleiche Vertrauensbasis wie der bisherige manuelle Handdownload desselben ZIPs.
- **Offen (Tims Schritt):** Version **1.6.0 taggen → Release-ZIP** und **einmal noch von Hand** auf alle
  Geräte ausrollen (der laufende 1.5.0-Client hat den Updater noch nicht); danach greift die
  Automatik. **Handtest** nach dem Deploy: ein späteres Tag (z. B. 1.6.1) muss den Banner auslösen und
  „Jetzt aktualisieren" den Client live in der neuen Version hochbringen. Der volle Live-Austausch der
  echten WPF-exe ist auf dem Notebook nicht isolierbar (wie in früheren Client-Phasen).

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-03)

**Live-Dashboard Fix (Server 1.4.1) — Offline-Erkennung zeitnah + offener Client-Drawer live.**
Nach dem Deploy fiel auf: „Verbunden/Offline" reagierte träge. Ursache: der Offline-Schwellwert
in `app.js` stand noch auf **3 Minuten** (Alt-Wert des 60-s-Heartbeats), und ein geöffnetes
Client-Detail-Panel (Overlay) wurde vom Re-Render-Takt nicht erfasst.
- **Schwellwert `CLIENT_OFFLINE_AFTER_SEC = 45`** (≈3 ausgebliebene 15-s-Heartbeats, jitter-tolerant);
  `clientDerivedStatus` sekundenbasiert. Der lokale 12-s-Re-Render-Takt lässt den Übergang altern
  → geschlossener Client kippt binnen ~45–57 s auf Offline, **ohne Reload**.
- **`refreshOpenDrawer()`:** ein offener **Client**-Drawer (Marker `js-client-drawer` + Geräte-ID,
  rein synchron aus `state.data`) wird bei Live-Refresh und im Re-Render-Takt zerstörungsfrei neu
  gebaut; ein Modal/anderes Overlay leert den overlayRoot → keine Kollision.
- **Browser-Laufzeit real belegt** (lokaler Server 1.4.1, Chrome): SSE offen (Heartbeat → Nachladen
  ohne Reload, `/api/devices` before=2→after=3 in 1,5 s); Gerät kippt **Verbunden→Offline bei ~45 s
  ohne Reload**; Drawer öffnet mit korrektem Status + Live-Marker. Build 0/0, Tests 112/0/0.
- **Rollout:** Server-only (`:latest` neu). Danach **einmal Ctrl+F5** (neues `app.js` aus dem Cache).

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-03)

**Live-Dashboard Phase 2 von 2 — Client-Heartbeat entkoppelt (Client 1.5.0).**
Delta-Spec `specs/savevault-change-live-dashboard.md` (Abschnitt 6), Weg über `/projekt-edit`.
Reine **Client-Änderung**; erfordert (anders als Phase 1) ein **Client-Update auf allen Geräten**.
**Gate grün, committet auf Branch `phase-live-dashboard` (kein Push).**
- **Heartbeat vom Sync-Takt entkoppelt:** neues `ClientConfig.HeartbeatIntervalSeconds` (Default 15,
  rückwärtskompatibel – fehlt das Feld, greift 15) + berechnetes `HeartbeatInterval` (Untergrenze
  5 s, **nie langsamer als der Sync-Takt** → kein Rückschritt bei sehr kleinem Sync-Intervall).
  `ClientAgent` startet den Heartbeat-Loop jetzt mit `config.HeartbeatInterval` statt mit dem
  Sync-Intervall; Rescan-/Command-Loop bleiben am Sync-Takt. So kippt „Verbunden/Offline" im
  Dashboard binnen ~15 s statt bis zu 60 s. Das Lebenszeichen ist billig (nur Präsenz/Status aus
  lokalem Zustand, kein ludusavi/Upload) → häufiger unproblematisch.
- **Gate grün:** Build **0/0**, `dotnet test` **112/0/0** (unverändert). `HeartbeatInterval`-Arithmetik
  per Wegwerf-Harness real geprüft (6 Fälle: Default 15, nie langsamer als Sync, Untergrenze 5,
  60/60→60 = altes Verhalten). Kein `/code-review`-Fork (triviale Arithmetik+Verdrahtung, Diff selbst
  geprüft), kein `/security-review` (keine neue Fläche; bestehender authentifizierter Heartbeat).
- **Offen (Tims Schritt):** Client-Update auf alle Geräte ausrollen (Tag `v1.5.0` → Release-ZIP);
  danach live prüfen, dass Präsenz zeitnah umschlägt. WPF-Client-Laufzeit hier nicht isolierbar
  (wie in früheren Client-Phasen) → Handtest bei Tim.

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-03)

**Live-Dashboard — Echtzeit-Aktualisierung per Server-Push (Server 1.4.0), Phase 1 von 2.**
Delta-Spec `specs/savevault-change-live-dashboard.md`, Weg über `/projekt-edit`. Reine
**Server-/Dashboard-Änderung** (kein Windows-Client-Code) → Rollout = **nur Server neu deployen**.
Behebt die Trägheit: das Dashboard aktualisierte sich bisher nur beim Login/Refresh-Klick (Ursache:
kein Polling/Push; „Verbunden/Offline" wurde nur beim Rendern berechnet, das nie von selbst lief).
**Gate grün, committet auf Branch `phase-live-dashboard` (kein Push).**
- **Server-Push (SSE):** neuer In-Memory-`DashboardEventHub` (Singleton, `Realtime/`) + Endpunkt
  `GET /api/events` (`text/event-stream`, **master-only**). Nach jeder Zustandsänderung wird ein
  grobes Ereignis gepusht — verdrahtet in der **Endpunkt-Schicht** (Heartbeat→`presence`,
  Register/Finalize→`games`, Restore/Share/Delete→`games`, Resolve→`conflicts`, Pair→`devices`);
  `VaultStore` bleibt bis auf eine Rückgabe (`TryFinalizePendingAsync` meldet jetzt, **ob**
  finalisiert wurde) unangetastet. Keep-Alive-Kommentar alle 15 s; EIN gehaltenes
  `WaitToReadAsync` (SingleReader-treu), Wartezeit-Timer wird bei Ereignis freigegeben; abrupte
  Trennung (`IOException`/Cancel) sauber abgefangen (keine Fehlerflut).
- **Dashboard:** liest den Stream per **`fetch`-Streaming-Reader** (nicht `EventSource`), damit der
  Session-Token im `Authorization`-Header bleibt (nie in der URL — konsistent zur Cover-/Export-
  Linie). Jedes Ereignis → **entprelltes** `loadAll()`+Render; kommt eins während Laden/Bedienung,
  wird es als `pending` nachgeholt (kein stiller Verlust). **Lokaler Re-Render-Takt (12 s)** lässt
  Zeit-/Offline-Anzeige altern; **Interaktions-Guard** (`isInteracting`) unterbricht keine
  Sucheingabe/Slider. **Reconnect mit Backoff**; transiente Serverfehler reconnecten, nur echte
  Streaming-Unfähigkeit fällt dauerhaft auf **Polling** zurück (nie beides gleichzeitig).
- **Gate grün:** Build **0/0**, `dotnet test` **112/0/0** (+4 `DashboardEventHub`-Tests: Zustellung
  an mehrere Abonnenten, Abmeldung stoppt/vervollständigt, voller Kanal blockiert andere nicht,
  No-Sub-No-Op). **Laufzeit-Smoke** gegen echten Server (2×): `/api/events` ohne Token→401,
  Geräte-Token→403, Master→200 + korrekte Header; Push `hello`→`presence`(Heartbeat)→`devices`
  (Pairing) live; Keep-Alive-`ping` nach 15 s; keine Exceptions im Log bei abruptem Disconnect.
  `/code-review high`: **6 Befunde → alle 6 behoben** (SSE-Awaiter/Timer-Leak + SingleReader,
  `IOException`-Flut, stale-nach-Inflight-Refresh, Interaktions-Abbruch, Publish-pro-Blob→nur bei
  Finalisierung, Polling-Timer-Leak). `/security-review`: **sauber** (master-only, Token nur im
  Header, Stream trägt nur Codewort+Zeit — keine Fremddaten/PII, kein DOM-Inject).
- **Phase 2 erledigt** (Client-Heartbeat entkoppelt, Client 1.5.0 – siehe Block oben).
  Offen: visuelle Abnahme des Live-Verhaltens im echten Dashboard nach dem Deploy (Tims Schritt).
- **Verteilung:** master-Push baut Server-`:latest`; Tag (z. B. `server-v1.4.0`) für versioniertes Image.

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-03)

**Dashboard-Fix + kompletter Legacy-Neustart + Client-Reseed (Server 1.3.0 / Client 1.4.0).**
Delta-Spec `.claude/projekt-werkstatt/specs/savevault-change-dashboard-fix-legacy-neustart.md`,
Weg über `/projekt-edit`. Behebt den Dashboard-Fehler nach dem Per-Gerät-Umbau (dasselbe Spiel
mehrfach, privat klein/ohne Cover) und macht Tims „frisch anfangen" real. **Alle Gates grün,
committet + released als `v1.4.0`.**
- **Dashboard — eine Kachel pro Spiel:** `app.js` gruppiert nach kanonischem Spiel
  (`buildGameGroups`/`finalizeGroup`/`pickDisplayName`), eine Kachel je Titel (echter Name statt
  Slug, ein Cover über den kanonischen Schlüssel, aggregierter Status/Größe). Der Spiel-Drawer
  öffnet kanonisch und schlüsselt je Bucket einen Abschnitt auf (Lokal:<Gerät>/Geteilt/
  Konflikt-Kopie) mit Revisionen/States/Export/Restore/Teilen — pro Bucket geladen, nur Cover
  kanonisch. **Legacy-Button + alle `scope==="legacy"`-Zweige entfernt** (Tim-Entscheidung).
- **Cover kanonisch (Server):** der Cover-Endpunkt reduziert den Schlüssel via
  `BucketKey.Original` und übergibt `new GameKey(canonical, canonical)` — IGDB-Suche (über
  DisplayName) UND Platten-Cache (`HashKey(Value)`) treffen den kanonischen Wert → ein Cover je
  Spiel, auch für private/geteilte Buckets. Nur der Cover-Endpunkt berührt.
- **Migration v2→v3 (destruktiv, idempotent):** `MigrateIfNeeded` versioniert getrennt
  (`<2` alte Konflikt-Auflösung, `<3` Legacy-Purge) — kein erneutes Auslösen des v1→v2-Schritts
  auf heutigem v2-Index. Neuer Helfer `PurgeLegacyBucket` löscht je Legacy-Bucket
  (`!IsFork && ScopeOf==Legacy`) Verzeichnis+Blobs (traversal-sicher, `IsWithinData`-Wache) und
  Index-Nebendaten (`Games`/`GameStates`/`Conflicts`/`Commands`/`Activity`). Forks + privat +
  geteilt bleiben. `CurrentIndexVersion`=3.
- **Client-Reseed:** `SyncDecider.Decide` fängt vor den vier Fällen `serverRevision < baseRevision`
  ab → `Upload` (Server hat Bucket verloren → neu einsäen), unabhängig von localChanged.
  `SyncEngine.UploadAsync` koppelt `BasedOnRevision = head.CurrentRevision` (kein 409 beim Reseed;
  Normalfall bit-identisch, da dort head==base). +4 SyncDecider-Tests. Das war die Ursache, warum
  ein gelöschtes Spiel nie zurückkam (vorher NoOp).
- **Gates grün:** Delta-Gate (reviewer+inspekteur), Client-Kern-Gate (reviewer+inspekteur),
  Server-Kern-Gate (reviewer+**security-auditor** auf die Löschfläche: sauber, +inspekteur),
  Oberflächen-Gate (reviewer+inspekteur). **Laufzeit-Gate:** Build 0/0, `dotnet test` **108/0/0**
  (inkl. 4 Reseed-Tests), **Migrations-Smoke 16/16** (v2-Index mit Legacy+Fork+privat → Legacy weg
  inkl. Verzeichnis, Fork/privat bleiben, Version 3, idempotent), **Reseed-Smoke** (Head 0, Base 1
  → Upload/Revision 1, kein 409), Server startet sauber, Endpunkte/Fehlerfälle korrekt.
- **Rollout-Reihenfolge:** erst Server deployen (löscht Legacy, gruppiert), dann Client-Update auf
  alle Geräte (Reseed greift beim nächsten Zyklus). **Alle Geräte laufen bereits auf v1.3.0.**
- **Abgenommen (2026-09-03):** Server 1.3.0 + Client 1.4.0 auf allen Geräten deployt; visuelle
  Abnahme der Kacheln/Cover mit echten Daten durch Tim erfolgt — sieht gut aus. Delta abgeschlossen.

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-02)

**Phase 3 (von 3) fertig — Dashboard: Teilen etablieren + Legacy löschen + Scope-Sichtbarkeit
(Server 1.2.0).** Delta-Spec `specs/savevault-change-per-device-sync.md`, Weg über `/projekt-edit`.
Reine **Server-/Dashboard-Änderung** (kein Client-Code). **Gate grün, committet auf Branch
`phase3-dashboard-sharing` (kein Push).** Aufgenommen nach Limit-Abbruch bei 0 % — die im Arbeitsbaum
liegende Vorarbeit (Endpunkte + `VaultStore`-Methoden + Scope-Felder + Dashboard-Anzeige) wurde
vervollständigt, verifiziert und gehärtet.
- **Scope-Sichtbarkeit:** `GameSummary` trägt jetzt `Scope`/`OwnerDeviceId`/`CanonicalValue`/`IsFork`;
  das Dashboard labelt jede Bucket-Zeile (Lokal: <Gerät> / Geteilt / Legacy / Konflikt-Kopie) und
  gruppiert so privat je Gerät + geteilt + Legacy. Pro-Gerät-Zustände geteilter Buckets entstehen
  über die echten Revisions-Schreibvorgänge (synchronisierte Geräte erscheinen im geteilten Drawer).
- **Teilen etablieren (master-only):** neuer Endpunkt `POST /api/games/{canonical}/share`
  (`SeedSharedFromDeviceAsync`) kopiert den Stand des gewählten Geräts (privater Bucket) als geteilte
  Revision 1 — Blobs inhaltsadressiert kopiert, privater Bucket bleibt unangetastet. 409 bei
  vorhandenem geteilten Stand, 404 bei standlosem Quell-Gerät. Dashboard: „Über Geräte teilen" je
  privatem Bucket; bei **mehreren** Geräte-Kandidaten **Auswahl-/Vergleichsdialog** (Revision, Dateien,
  Größe, Zeit je Gerät), bei genau einem direkt (Spec: ohne Rückfrage). Beitritt bleibt am Gerät
  (Phase-2-Client-Vergleichsdialog beim nächsten Sync) — kein stilles Überschreiben.
- **Legacy löschen (master-only):** `DELETE /api/games/{legacyKey}` (`DeleteLegacyBucketAsync`) entfernt
  einen eingefrorenen Bucket samt Blobs; **Bestätigungsdialog** im Dashboard vorgeschaltet. Nur
  Legacy (kein Scope-Präfix) UND **kein Fork** löschbar; Verzeichnis traversal-sicher (Ordnername =
  `HashKey`, zusätzlich `IsWithinData`-Wache vor `Directory.Delete`).
- **Gate grün:** Build **0/0**, `dotnet test` **104/0/0**, app.js-Syntax ok, **Laufzeit-Smoke 22/22**
  (In-Process gegen echten `VaultStore`: 2 private Buckets + Scope/Owner/Canonical-Felder, Seed→Rev 1
  inkl. physisch kopiertem Blob + ladbarer Revision, 409/404-Fälle, Legacy-Delete-Wache privat/geteilt
  →400 + echtes Löschen + unbekannt→404, **Konflikt-Fork: IsFork-Markierung + Delete→400**).
  `/code-review high`: **6 Befunde → 5 behoben, #6 begründet abgelehnt** (Auswahl-Picker folgt dem
  bestehenden `openRestorePicker`-Idiom, `confirmModal` ist ein Ja/Nein-Dialog). Behoben: Fork-Buckets
  wurden rein präfix-basiert als Legacy/privat fehlklassifiziert und bekamen Lösch-/Teilen-Aktionen →
  jetzt Server-Wache (`IsFork→400` in Delete **und** Seed) **und** Dashboard-Markierung „Konflikt-Kopie"
  ohne Aktionen; Activity-Einträge beim Legacy-Löschen mitbereinigt; geteilte Erst-Revision
  `BasedOnRevision=null` (statt 0, konsistent zum Fork/Erst-Upload); Blob-Copy in `CopyManifestBlobs`
  zusammengeführt. **Sicherheits-Selbstprüfung** (statt Agent, wie in früheren Phasen; `/security-review`
  brauchte das Repo als cwd, das die Sitzung hier zurücksetzte): Traversal ausgeschlossen (gehashter
  Ordnername + Containment-Wache), beide Endpunkte master-only, kein neues `innerHTML`
  (XSS-frei, `textContent`), Owner beim Teilen bewusst admin-gewählt (Single-Admin), kein
  Outbound/Deserialisierung/Prozessstart.
- **Offen/Rest:** Dashboard-Interaktion (Teilen-Auswahl, Bestätigung, Beitritt) headless nicht
  bedienbar → **UI-Handtest bei Tim ausständig**. Heartbeat meldet Per-Spiel-Zustände weiter
  privat-scoped (bekannte Phase-2-Altlast); geteilte Buckets zeigen synchronisierte Geräte über die
  Revisions-Schreibvorgänge — vollständig scope-treue Heartbeat-Meldung bräuchte eine Client-Vertrags-
  änderung und ist bewusst nicht Teil dieses Dashboard-Deltas. **Nicht-Umfang v1** (unverändert):
  Teilen wieder ausschalten (geteilt→privat), automatisches Umschalten anderer Geräte, Auto-Merge.
- **Verteilung (offen, Tims Schritt):** Branch mergen → master-Push baut Server-`:latest`; Tag (z. B.
  `server-v1.2.0`/`v1.4.0`) für versioniertes Image.

---

**Phase 1+2 RELEASED als `v1.3.0` (2026-09-02).** Auf master gemerged + gepusht, Tag `v1.3.0`
gesetzt → CI grün: DockerHub `even512/savevault-server:latest` + `:1.3.0`, GitHub-Release `v1.3.0`
mit `SaveVault-Client-v1.3.0-win-x64.zip`. **Phase 3 (Dashboard) steht noch aus** — bewusst NICHT
mehr in diese Sitzung genommen (Budget), damit der Release sauber bleibt.

**Phase 2 (von 3) fertig — Client-Umschalter Lokal/Synchron + Vergleichsdialog (Client 1.3.0).**
Delta-Spec `specs/savevault-change-per-device-sync.md`, Weg über `/projekt-edit`. **Gate grün,
Teil des Release v1.3.0.** Reine Client-Änderung (kein Server).
- **Umschalter je Spielzeile „Über Geräte synchronisieren" (Lokal ↔ Synchron).** Neuer opt-in
  `GameShareStore` (Gegenstück zur Ausschluss-Achse). Sync-Scope pro Spiel: `ClientAgent.ActiveScope`
  → `SyncEngine.RunCycleAsync(scope)`; der ganze Sync-Pfad (Upload/Download/Conflict/ApplyRevision/
  Content) ist durchgefädelt.
- **Teilen-Flip:** `ProbeShareAsync` prüft den geteilten Head. Kein geteilter Stand → `SeedShareAsync`
  (lokal wird Seed, rev 1). Existiert einer → **Vergleichsdialog** `ShareCompareWindow` (lokal vs.
  geteilt: Dateien/Größe/Zeit/Herkunft) → „Geteilten übernehmen" (`JoinTakeSharedAsync`, Download,
  privater Bucket bleibt Backup) oder „Meinen lokalen teilen" (`JoinTakeLocalAsync`, Upload als neue
  geteilte Revision). Flip läuft atomar unter dem Spiel-Lock (kein Zyklus mit falschem Scope/Base).
- **Getrennter lokaler State je Bucket (Review-Fix #1/#4):** `SyncStateStore` ist jetzt nach Scope
  partitioniert (privat behält den alten Dateinamen = rückwärtskompatibel, geteilt bekommt
  „shared|"-Präfix). Privater Backup-Basis-Stand bleibt beim Teilen erhalten; privat/geteilt
  überschreiben sich nie gegenseitig.
- **Befehle scope-treu:** `CommandPoller` leitet den Scope aus dem Befehls-Bucket ab (`ScopeOf`) →
  Restore/Konfliktlösung treffen den richtigen (privaten/geteilten) Bucket.
- **„Sync pausieren" → „Hochladen deaktivieren"** (nur Beschriftung/Anzeige; Mechanik unverändert).
- **Weitere Review-Fixes:** Manifest-Build im Vergleichsdialog vom UI-Thread (`Task.Run`); Teilen-
  Button ohne Binding-Zerstörung (Busy-Guard); kein Teilen bei offenem Konflikt; toter Code entfernt.
- **Gate grün:** Build 0/0, `dotnet test` **104/0/0**, Laufzeit-Smoke **Phase 1 13/13 + Phase 2 9/9**
  (Seed/Probe/Übernehmen/Lokal-teilen→rev 2, privat/geteilt-Trennung, veraltete Basis → 409).
  `/code-review high`: 8 Befunde → 7 behoben, #6 als Nicht-Regress begründet (private Befehle zielen
  stets auf den Owner = aktuelles Gerät). Kein Server-Angriffsflächen-Zuwachs → kein separater
  `/security-review` nötig.
- **Offen/Rest:** WPF-Interaktion (Umschalter + Dialog) ist hier headless nicht ausführbar →
  **UI-Handtest bei Tim ausständig**. Heartbeat meldet Per-Spiel-Zustände weiter privat-scoped
  (Dashboard-Genauigkeit für geteilte Spiele) → wird in **Phase 3** (Dashboard) mitgezogen.
- **Phase 3 (offen):** Dashboard-Teilen (Geräte-Seed-Auswahl), Pro-Gerät-Status (Synchron/Lokal),
  dashboard-ausgelöster Beitritts-Dialog am Client, Legacy löschen.

---

**Phase 1 (von 3) fertig — Geräte-eigene Buckets + Migration (Client 1.2.0 / Server 1.1.0).**
Delta-Spec `specs/savevault-change-per-device-sync.md` (von Tim freigegeben 2026-09-02), Weg über
`/projekt-edit` (leichte Notebook-Werkstatt). **Gate grün, committet auf Branch
`phase1-per-device-buckets` (kein Push).**
Ziel: weg von „ein globaler Bucket pro Spielname" (Ursache des Konflikt-Sturms beim Koppeln
eines zweiten Geräts) hin zu **pro Gerät ein eigener privater Bucket**; geräteübergreifendes
Teilen kommt opt-in in Phase 2/3.
- **Kern-Idee (kleiner Blast-Radius):** Der ganze `VaultStore` bleibt nach `GameKey.Value`
  verschlüsselt. Die Scope-/Owner-Trennung steckt allein in einem **abgeleiteten Value-Präfix**
  (`dev|{owner}|…` privat, `shared|…` geteilt, unverändert = Legacy) — neues Core-Primitiv
  `BucketKey` (`Resolve`/`Original`/`ScopeOf`/Wire). `|` kann die Schlüssel-Normalisierung nie
  erzeugen → keine Fehlklassifikation. `GameRecord` braucht KEIN neues Feld (Scope aus Präfix
  ableitbar).
- **API-Scope:** spielbezogene Routen bekommen optionales `?scope=` (Core `ApiRoutes` +
  `ISaveVaultApi`/`SaveVaultApiClient`, Default `private`). Der Server löst den effektiven
  Bucket an der Endpunkt-Grenze auf (`ResolveGameKey`): **Owner eines privaten Buckets IMMER
  aus dem authentifizierten Gerät** (nie aus dem Query → Owner-Isolation). Default ohne Scope:
  Gerät→privat, Master/Dashboard→legacy (roher Schlüssel → Dashboard bleibt unverändert
  lauffähig, keine app.js-Änderung in Phase 1).
- **Befehle:** `CommandPoller` führt den (effektiven) Bucket-Schlüssel per `BucketKey.Original`
  auf den lokalen Originalschlüssel zurück (Registry/State/Ordner) und synct per Default-Scope
  gegen den eigenen privaten Bucket. Restore/Resolution damit korrekt.
- **Heartbeat:** Geräte-Zustände werden serverseitig auf den privaten Bucket abgebildet
  (Anzeigename + Status hängen am selben Bucket wie die Uploads dieses Geräts).
- **Migration:** Server-Index `Version` 1→2 (einmalig, idempotent): alte globale Buckets werden
  eingefroren (Legacy, keine Blobs bewegt) und **alle offenen Konflikte als gelöst markiert** →
  der Konflikt-Sturm verstummt sofort; Historie/Blobs bleiben lesbar. Client: einmaliger
  `SyncState`-Reset (`ResetAllState` + Config-Flag `PerDeviceBucketsMigrated`) → jedes Spiel wird
  als Revision 1 in den privaten Bucket **neu eingesät** (Backup), statt gegen den alten Verlauf
  zu laufen.
- **Review-Härtung (aus `/code-review high`):** (a) **Legacy-Scope ist master-only** — ein
  Geräte-Token, das `?scope=legacy` schickt, bekommt 403 (kein Gerät kann den eingefrorenen
  globalen Bucket neu beschreiben). (b) **`/api/games` ist master-only** (die Liste enthält jetzt
  effektive Bucket-Schlüssel mit fremden Geräte-IDs → nur Dashboard; der Client nutzt die Route
  nicht). (c) `CommandPoller`-Fehlerstatus unter dem kanonischen Schlüssel. (d) `BucketKey.Original`
  trennt am **letzten** `|` (robust gegen Owner-IDs mit `|`).
- **Gate grün (real verifiziert):** `dotnet build` **0/0**, `dotnet test` **104/0/0** (+16
  `BucketKey`-Fälle). **Laufzeit-Smoke 13/13** gegen den echten Server: Migration (Alt-Index
  v1+offener Konflikt → v2+resolved), Zwei-Geräte-Trennung (A lädt hoch → B head=0; B-Upload
  basedOn0 → 200 statt 409 = kein Konflikt-Sturm), 2 getrennte `dev|…`-Buckets im Index, Härtung
  (Gerät→/games 403, Gerät→legacy 403, Master→/games 200). `/security-review`: clean (Owner-Isolation
  server-seitig aus dem Token; Pfade gehasht; Scope enum-validiert). Umgebung: .NET 9 SDK (9.0.317)
  per winget nachinstalliert (Notebook hatte nur SDK 8).
- **Bewusst zurückgestellt (nicht Phase-1-Flow):** Restore einer **Legacy**-Revision auf ein Gerät
  (Befund #2) läuft heute ins Leere/Fehler — kein Phase-1-Ziel; sauberes Scope-Threading der
  Befehle kommt mit dem Dashboard-Umbau in Phase 3. Master + `?scope=private` → 400 (Befund #5) ist
  latent (kein Master-C#-Aufrufer; Dashboard ist JS). **Phase 2 (Client-Umschalter Lokal/Synchron +
  Vergleichsdialog) und Phase 3 (Dashboard-Teilen + Pro-Gerät-Status + Legacy löschen) stehen aus.**

---

# SaveVault — Fortschritt (fortgeschrieben 2026-08-28)

**Client v1.0.5 — Autostart + eigenes exe-Icon.** Delta-Spec
`specs/savevault-change-autostart-icon.md` (von Tim freigegeben 2026-08-28), Weg über
`/projekt-edit`.
- **Autostart:** neues Feld `ClientConfig.AutostartEnabled` (Default `true`, config.json nur
  erweitert, rückwärtskompatibel). Neuer `AutostartService` kapselt den HKCU-Run-Key
  `…\Run\SaveVault` (nur HKCU, kein Admin): `IsEnabled/Enable/Disable/Apply`, Pfad aus
  `Environment.ProcessPath` quotiert, alle Registry-Zugriffe fehlertolerant, `Disable`
  idempotent. `App.OnStartup` gleicht best-effort ab (`SyncAutostart`), sodass „Standard AN"
  schon beim ersten Lauf greift. Einstellungen: Checkbox „Automatisch mit Windows starten"
  (neuer `DarkCheckBox`-Stil), lädt aus der Config, schreibt + wendet beim Speichern an.
- **exe-Icon:** mehrauflösende `Assets/SaveVault.ico` (16/32/48/256) aus der Tray-Zeichnung
  erzeugt (`TrayIconFactory` minimal auf `Create(int size=32)` + `RenderBitmap` refaktoriert,
  Tray bleibt pixelgleich); `<ApplicationIcon>` gesetzt. Einmal-Generator war ein Wegwerf-
  Werkzeug, nicht im Laufzeit-Code.
- **Gates:** Delta-, Kern- und Oberflächen-Gate grün (reviewer + inspekteur; security-auditor
  auf die Registry-Fläche: sauber). Laufzeit: Build 0/0, **88 Tests grün**, Icon in der exe
  belegt (RT_GROUP_ICON/RT_ICON). Autostart-Mechanik per isoliertem Harness real geprüft
  (Enable→quotierter Pfad, idempotent, Disable→weg). Offen (Umgebung, nicht Code): der echte
  App-Start des neuen Builds gegen Tims reale Config ließ sich auf diesem Rechner nicht
  gefahrlos isolieren (.NET 9 ignoriert die APPDATA-Env-Var; Tims echter Client lief) →
  App-Start-Abgleich + Checkbox live per Handtest ausständig.
- **Bekannte Altlast (unverändert):** GDI-HICON in `TrayIconFactory` ohne `DestroyIcon` —
  vorbestehend, vom Delta nicht berührt.

**Server 1.0.5 — Dashboard-Login statt Master-Token.** Das `SAVEVAULT_TOKEN` als Dashboard-
Zugang ist komplett raus. Neu: ein im Dashboard eingerichtetes Admin-Konto (Benutzer + Passwort).
- **Ersteinrichtung:** `POST /api/setup {username,password}` legt das EINZIGE Admin-Konto an (nur
  solange keins existiert → sonst 409) und meldet direkt an. Passwort nur als PBKDF2-Hash
  (`Secrets.HashPassword`, 100k Iterationen, Zufalls-Salt) im Index; nie Klartext.
- **Login:** `POST /api/login` → Session-Token (30 Tage; nur Hash + Ablauf im Index, restart-fest),
  ratenbegrenzt (10 Fehlversuche/5 min → 429). `POST /api/logout` beendet die Sitzung.
- **Middleware:** `/setup`+`/login` token-frei; ohne Admin → 503 (Dashboard zeigt Ersteinrichtung);
  Session-Token = Master, sonst Geräte-Token. Master-Token-Vergleich entfernt. `ServerConfig`:
  `MasterToken`/`IsConfigured` raus; `/health` liefert jetzt `needsSetup`.
- **Clients unberührt** (Pairing-Code + Geräte-Token wie bisher); `/api/pair` weiter token-frei,
  verlangt aber ein eingerichtetes Konto.
- **Dashboard:** Token-Eingabe ersetzt durch Setup-/Login-Screen (Benutzer+Passwort, bei Setup mit
  Bestätigung); Session in sessionStorage; „Abmelden" in den Einstellungen. Header-Kommentar/Texte
  angepasst.
- **Docs:** `SAVEVAULT_TOKEN` aus `.env.example`, Unraid-Template, docker-compose (erzwang die Var!),
  README, deploy/README, Dockerfile entfernt.
- **Verifiziert (Laufzeit-Smoke):** needsSetup→503→setup→Session→409-Re-Setup→login(falsch=401,
  richtig, case-insensitiver Benutzer)→master-Endpunkt 200→logout→401; Pairing weiter ok. Build 0/0,
  88 Tests grün. Server-Version → 1.0.5 (in Einstellungen sichtbar). Verteilung: master-Push → neues
  Server-`:latest`-Image. **security-auditor auf die Auth-Fläche noch offen (Angebot an Tim).**



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
