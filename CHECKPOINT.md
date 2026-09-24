# SaveVault — Fortschritt (fortgeschrieben 2026-09-24)

**Migrations-Bug gefixt: verlorene `config.json` löst `ResetAllState()` nicht mehr erneut aus
(Client 1.8.10).** Das in der Optimus-Session als „offen" zurückgestellte Thema: die einmalige
Migration auf geräte-eigene Buckets war am Config-Flag `PerDeviceBucketsMigrated` gehängt —
fehlte die `config.json`, feuerte der destruktive Reset beim nächsten Start erneut (Diagnose-
Vorfall: 54 falsche Konflikte).
- **Fix:** Die Migration läuft jetzt nur, wenn **keinerlei** Anzeichen einer abgeschlossenen
  Migration vorliegen — weder die neue persistente Marker-Datei
  (`%AppData%\SaveVault\per-device-buckets-migrated`, bewusst außerhalb des State-Verzeichnisses
  und unabhängig von `config.json`) noch das Legacy-Config-Flag. `ClientAgent.StartAsync`
  sichert die Marker-Datei bei jedem Start (no-op, falls vorhanden): bereits migrierte Geräte
  (Flag aus alter Version) werden ohne Reset „adoptiert" — danach überlebt die Migrations-
  Aufzeichnung auch den Verlust der `config.json`. Bewusst NICHT Marker-allein als Guard: das
  hätte genau die Bestandsgeräte beim Upgrade auf diese Version fälschlich re-migriert.
- **Dateien:** `ClientAgent.cs` (Guard), `SyncStateStore.cs`
  (`HasPerDeviceBucketsMigrationMarker`/`EnsurePerDeviceBucketsMigrationMarker`), `AppPaths.cs`
  (Marker-Pfad), `ClientConfig.cs` (Doku des Flags).
- **Tests:** neuer `ClientAgentMigrationTests` mit 4 Regressionstests: (1) verlorene Config nach
  abgeschlossener Migration → kein Reset; (2) echter Erstlauf mit Alt-States → Migration läuft +
  Marker; (3) Neustart danach → idempotent; (4) Legacy-Flag ohne Marker → kein Reset + Adoption.
- **Gates grün:** Build 0/0, `dotnet test` 212/212. `CHANGELOG.md` nachgezogen (v1.8.10).
- **Bekannt akzeptierte Kante:** Gerät, auf dem `config.json` UND Marker beide weg sind
  (vollständiges Löschen des SaveVault-Datenverzeichnisses) wird beim nächsten Start erneut
  migriert — das ist von einem echten Erstlauf nicht unterscheidbar und entspricht der
  ursprünglichen Migrations-Semantik („beim ersten Lauf der neuen Version").

---

**SaveVault blockierte die automatische Advanced-Optimus-Umschaltung — behoben, bestätigt,
Client 1.8.9.** Delta-Spec `specs/savevault-change-optimus-gpu-block.md`, Weg über
`/projekt-edit`. Tims Meldung: startet er auf dem Notebook ein Spiel, versucht Advanced Optimus
automatisch auf „nur NVIDIA-GPU" umzuschalten — Windows nannte SaveVault dabei namentlich als
blockierenden Prozess.
- **Zwei plausible, recherche-gestützte Theorien nacheinander durch echte Handtests widerlegt:**
  (1) Software-Rendering erzwingen (WPFs Hardwarepipeline halte ein GPU-Gerät offen) — keine
  Wirkung. (2) `MainWindow` schließt jetzt wirklich statt nur zu verstecken (`Hide()`) — Tims
  Handtest mit nie geöffnetem Dashboard zeigte: blockiert weiterhin. Task-Manager bestätigte:
  SaveVault zeigt **keinerlei** GPU-Auslastung.
- **Systematische Bisektion statt weiterer Theorien:** Reihe von Wegwerf-Sonden (WinForms-Tray,
  WPF+Tray+nie gezeigtes Fenster, Netzwerk+Datei-Watcher, Netzwerk gegen Tims echten LAN-Server,
  `IsConfigured=false`-Leerlauf) — alle liefen sauber durch, **außer** die echte
  `SaveVault.Client.exe`, komplett unkonfiguriert. Einzige verbliebene Variable:
  `_window = CreateWindow()` (eager in `OnStartup`) — entfernt: **Umschaltung klappt.**
- **Bestätigte Ursache:** `MainWindow.xaml` enthält Effekte (`DropShadowEffect`) und
  hochwertig skalierte Bilder — WPF bereitet das schon beim bloßen Konstruieren
  (`InitializeComponent()`) vor, auch ganz ohne `Show()`. Ein leeres Fenster (wie in den Sonden)
  löst das nicht aus, SaveVaults reich gestaltetes Dashboard schon.
- **Der Fix:** `App.xaml.cs::OnStartup` baut `MainWindow` nicht mehr eager — die Instanz entsteht
  erst beim ersten echten Öffnen über den Tray. Notwendige Folgearbeit: die 24-h-Selbst-Update-
  Prüfung hing bisher am Vorhandensein von `_window` (früher unkritisch, jetzt der Normalfall) —
  zentralisiert in `UpdateService.CheckAndStampAsync`, `App` bekommt eine eigene, vom Dashboard
  unabhängige `UpdateService`-Instanz. `/code-review high` lief auf diesem Rework acht Runden;
  ein echter Fund (Config-Datei-Race zwischen Update-Stempel und UI-Speichern, durch
  `ConfigureAwait(false)` auf einen Threadpool-Thread verlagert) behoben, mehrere reine
  Timing-/UX-Kanten bewusst als Trade-off akzeptiert (siehe Spec-Risiken) — Eskalationsschwelle
  war längst überschritten, keine weiteren Runden mehr gedreht.
- **Gates grün:** Delta-Gate (Spec laufend nachgezogen), Build 0/0, `dotnet test` 208/208
  unverändert grün. Kein `/security-review` (kein Auth/Pfad/Netz/Registry-Neuland).
- **Laufzeit-Verifikation, Runde 1:** Root Cause mehrfach auf Tims echter Hardware bestätigt
  (verschiedene Diagnose-Testbauten, ausschließlich der Verzicht auf `MainWindow`-Konstruktion
  macht den Unterschied).
- **Unabhängiger Vorfall während der Diagnose:** Ein Diagnose-Testbau (config.json testweise
  beiseite gelegt) hat eine einmalige Migrationslogik fälschlich erneut ausgelöst
  (`ClientAgent.StartAsync`, `PerDeviceBucketsMigrated`-Guard) und Tims lokale Sync-Status für
  alle privaten Buckets gelöscht — **keine echten Spielstand-Dateien betroffen** (nur die
  Fortschritts-Buchführung), aber 54 falsche „Konflikt"-Meldungen erzeugt. Über die reale
  Server-API automatisiert aufgelöst (Dry-Run zuerst, dann mit Tims Freigabe angewendet — alle
  54 hatten genau einen Teilnehmer = dieses Gerät, sicher). Der zugrunde liegende Bug ist noch
  **nicht** gefixt, auf Tims Wunsch zurückgestellt.
- **Laufzeit-Verifikation, Runde 2 — Handtest auf echtem Setup deckte einen weiteren Fall auf:**
  Tim öffnete das Dashboard (um die Konflikte oben zu lösen), schloss es sauber mit X — Blockade
  war wieder da. Ursache: WPFs interne Kompositions-Infrastruktur
  (`MediaContextNotificationWindow`) wird beim ersten gezeigten Fenster einmalig angelegt und
  bleibt für den Rest der Prozess-Laufzeit bestehen, auch nach sauberem Schließen — nur ein
  echter Prozess-Neustart setzt sie zurück. **Tims pragmatischer Vorschlag, umgesetzt:** der
  X-Button im Dashboard startet SaveVault jetzt komplett neu (`App.RestartApp`) statt das Fenster
  nur zu schließen. `/code-review high` fand dabei ein echtes Risiko (kurzzeitig zwei Instanzen
  gleichzeitig, falls die neue startet bevor die alte ihren Agent stoppt) — behoben, Agent stoppt
  zuerst. Dabei auch Software-Rendering (Runde 1) entfernt: jetzt erwiesen wirkungslos gegen die
  Blockade, hatte aber echten Preis (CPU-Last u. a. beim Wasserzeichen-Toast während des Zockens).
- **End-zu-Ende-Handtest auf dem finalen Stand: BESTÄTIGT.** Normal starten → Spiel starten →
  Umschaltung klappt. Dashboard geöffnet, mit X geschlossen (Neustart) → nochmal Spiel gestartet
  → Umschaltung klappt weiterhin. Tims Worte: „funktioniert jetzt exakt so wie es soll".
- **Release: Client 1.8.8 → 1.8.9**, `CHANGELOG.md` nachgezogen.
- **Rollout:** kein Server-Code betroffen, reine Client-Änderung.
- **Offen:** der in der Diagnose gefundene, zurückgestellte Migrations-Bug (`ResetAllState()`
  feuert bei jeder fehlenden `config.json`, nicht nur beim echten Erstlauf) — eigenständiger,
  unabhängiger Fix für eine spätere Sitzung.

---

**Konflikte bei geteilten Speicherständen lösen sich jetzt automatisch (Client 1.8.7).**
Delta `specs/savevault-change-shared-conflict-autoresolve.md`, Weg über `/projekt-edit`. Tims
Realtest-Log zeigte: sobald ein Gerät bei einem `shared`-Stand weiterspielte, während ein
anderes Gerät zwischenzeitlich bereits gespeichert hatte, meldete jeder Sync-Zyklus erneut
„Konflikt" und legte bei jeder weiteren lokalen Änderung eine neue Konflikt-Revision an —
bis Tim manuell im Konflikt-Dialog die richtige Fassung auswählte. Da er nie gleichzeitig auf
zwei Geräten spielt, ist das immer derselbe Fall: dieses Gerät hatte den zuletzt woanders
gespeicherten Stand nur noch nicht gezogen.
- **Fix (`SyncEngine.RunCycleAsync`):** Bei `SyncAction.Conflict` **und** `scope==Shared` läuft
  jetzt derselbe Weg wie ein gewöhnlicher Upload (`UploadAsync` auf den aktuellen Server-Head) —
  dieses Gerät gewinnt automatisch, ohne Dialog. Die überschriebene Server-Revision bleibt dabei
  unangetastet in der Revisions-Historie erhalten (Sicherheitsnetz). Für `private` bleibt der
  manuelle Konflikt-Dialog unverändert bestehen. Zusätzlich zieht das Diagnose-Log jetzt die
  tatsächlich ausgeführte Aktion nach (sonst hätte ein Auto-Resolve-Upload fälschlich als
  „Conflict" protokolliert).
- **Gates grün:** Delta-Gate, Kern-Gate (reviewer+inspekteur je zweimal, keine Drift, keine
  Regression). Build 0/0, `dotnet test` 199/199 unverändert grün.
- **Laufzeit-Gate real belegt** (kein WPF-Handtest nötig/möglich — reine Hintergrundlogik ohne
  UI-Änderung): Wegwerf-Harness (In-Process, gegen die echten Klassen `VaultStore`/`BucketKey`/
  `SyncEngine`/`SyncStateStore`, nur die HTTP-Schicht durch einen direkten Adapter ersetzt) baut
  Tims exaktes Szenario nach. Runde 1 deckte einen Fehler im Harness selbst auf (Content-Blobs
  nie gespeichert → Head blieb bei „pending" hängen, Absturz vor Erreichen des Szenarios) —
  nach Korrektur (jeder simulierte Upload speichert Content + finalisiert, wie
  `SaveVaultEndpoints.cs` es real tut) bestanden beide Läufe: `shared` löst automatisch auf
  (Aktion `Upload`, Status `Synced`, Head+Basis rücken vor, alte Revision bleibt lesbar),
  `private` bleibt wie bisher auf `Conflict` stehen.
- **Rollout:** nur Client-Update nötig (kein Server-Code geändert). Version **1.8.6 → 1.8.7**.

---

**Fix 0 real reproduziert (Client 1.8.6) — Ursache weiterhin offen, für spätere Session
aufbereitet.** Direkt nach dem v1.8.5-Release hat Tim denselben Ablauf auf echter Hardware
wiederholt und den Konflikt tatsächlich erneut ausgelöst — diesmal mit dem neuen Diagnose-Log.
**Wichtigster Fund:** das Log zeigte 5 identische „Download"-Entscheidungen in Folge
(`base=46, server=47`), OHNE dass die Basis je auf 47 vorrückte — der eigentliche Bug sitzt also
im Download-Ausführungspfad selbst (`ApplyRevisionAsync`/`DownloadAsync`), nicht in der bereits
mehrfach verifizierten Entscheidungslogik. **Datei-Sperre durch das laufende Spiel wurde von Tim
ausdrücklich widerlegt** (Spiel war zum Vorfallszeitpunkt geschlossen) — diese Hypothese ist
falsifiziert, nicht offen.

**Root-Cause des Diagnose-Log-Problems selbst gefunden und behoben:** `SyncDiagnosticsLog.Append`
wurde in `SyncEngine.RunCycleAsync` VOR der Aktionsausführung aufgerufen — das Log bewies also nur
die Absicht, nie das Ergebnis. Fix (Client 1.8.6): neue Methode `AppendOutcome(...)` protokolliert
nach der Ausführung Erfolg (+ neue Basis-Revision) oder Fehler (+ Ausnahme-Typ/-Nachricht), inkl.
neuem Sicherheitsnetz (`catch (Exception ex)`) für bislang komplett unprotokollierte Ausnahmen —
reine Sichtbarkeitserweiterung, keine Verhaltensänderung. Build 0/0, Tests 199/199 weiterhin grün.
Committet, gepusht, getaggt `v1.8.6`, released.

**Vollständige Diagnose (Log-Auszüge, verworfene Hypothesen, offene Spuren, Anleitung für den
nächsten Reproduktionsversuch) steht im Nachtrag am Ende von
`specs/savevault-change-sync-anzeige-fixes.md`** — dort ansetzen, nicht neu anfangen. Tim möchte
das Problem bewusst in einer **späteren, separaten Session** weiterverfolgen; dieser Punkt ist
absichtlich als offen/nicht abgeschlossen zurückgelassen worden, kein vergessener Rest.

---

**Sync-Anzeige-/Konflikt-Fixes released, Client 1.8.5 / Server 1.5.7.** Delta
`specs/savevault-change-sync-anzeige-fixes.md`, direkter Fortsatz des v1.8.4-Cleanups unten.
Fünf Bugs aus Tims Realtest behoben (Erstkontakt-Dialog, `RegisterConflict` hält Teilnehmer-
Revisionen aktuell, Gerätename statt ID im Konflikt-Dialog, Versionshistorie fragt aktiven Scope
ab, Zwei-Kästen-Autorefresh) + neues Diagnose-Log (`%AppData%\SaveVault\sync.log`). Alle Gates
grün (Delta-, Kern-, Oberflächen-, automatisierbarer Laufzeit-Teil), Build 0/0, Tests 199/199.
Committet (`11a2ba4`), gepusht, getaggt `v1.8.5`, CI (Client-Release + beide Docker-Publishes)
grün, GitHub-Release bestätigt.

**Bewusst nicht reproduziert, bleibt offen:** Tims schwerwiegendster Fall (Gerät zeigt
nachweislich korrekt heruntergeladenen Stand, meldet aber beim ersten neuen Speichern sofort
„Konflikt") ließ sich in einem sauberen In-Process-Nachstellversuch NICHT nachstellen —
`SyncDecider`/`ApplyRevisionAsync` als korrekt verifiziert, Ursache vermutlich umgebungsspezifisch.
Kein Fix ohne Beleg gemacht; das neue Diagnose-Log soll beim nächsten echten Auftreten die Ursache
belegbar machen (`sync.log` beider beteiligten Geräte anfordern, statt erneut zu spekulieren).

**Noch offen:** geführter Handtest mit Tim auf echten Geräten für die WPF-Interaktion selbst
(Erstkontakt-Dialog, Konflikt-Dialog-Gerätename, Kästen-Live-Update) — headless nicht prüfbar,
Tim wurde um Rückmeldung gebeten (siehe Chat). Bis dahin gilt dieser Punkt als technisch fertig,
aber nicht kundenabgenommen.

---

**Branch-Divergenz aufgeräumt + Release v1.8.4 (Client).** `/projekt-edit`-Anlauf zu Tims
neuen Konflikt-/Sync-Bugs deckte auf: seit Commit `8f2b722` waren `master` (lokal) und
`origin/master` acht bzw. sieben Commits lang unabhängig auseinandergelaufen — lokal die
Nachtrag-3/4-Fixes aus `savevault-change-shared-save-sichtbarkeit.md` (`45e724a`, `ec2a9b5`,
`cc1015e`, `f147612`), auf `origin/master` parallel dazu die Releases v1.8.0-1.8.3 (u. a. der
Geräte-Namen-Fix `6675bc7`). Tims laufender v1.8.3-Client hatte dadurch die beiden lokalen
Fixes nie bekommen — plausible Erklärung für „Konflikt-Dialog fast ohne Infos" und „zeigt nach
richtigem Umschalten trotzdem weiter Konflikt".
- Sauberer Merge (`f702a52`, einziger Konflikt in dieser Datei selbst, Code merge-clean),
  Build 0/0 + Tests 195/195 danach grün.
- Version nachgezogen (`ad50194`): Client **1.8.3 → 1.8.4** (sonst hätte der Auto-Updater keinen
  Unterschied gesehen — derselbe Fehler wie beim 1.7.0→1.8.0-Vorfall).
- Gepusht, Tag `v1.8.4` gesetzt+gepusht, CI (Client-Release + beide Docker-Publishes) grün,
  GitHub-Release `v1.8.4` mit ZIP bestätigt.
- **Noch offen (separat, nicht Teil dieses Cleanups):** `ConflictWindow.xaml.cs` zeigt für
  andere Geräte weiterhin `Gerät {ShortId}` statt eines Namens — der Geräte-Namen-Fix (`6675bc7`)
  hat nur `RevisionInfo`/`RevisionDownload` erweitert, nicht `ConflictParticipant`/den
  Konflikt-Dialog. Kandidat für die anstehende Delta-Spec.
- **Noch offen:** Tims eigentliche neue Bug-Meldungen (Konflikt beim ersten Umschalten ohne
  Nachfrage, veralteter „Geteilt"-Stand bis zum Spielwechsel, mehrere Konflikte hintereinander)
  sind noch nicht untersucht/gefixt — Grill-Prozess dazu läuft, wartet auf Tims Retest gegen
  1.8.4.

---

**Alle gepaarten Clients im Spiel-Detailpanel (Server 1.5.6), auch ohne Spielstand.** Delta-Spec
`specs/savevault-change-clients-panel-all-devices.md`, Weg über `/projekt-edit`. Reine
**Server-Dashboard-Änderung** (`app.js`/`styles.css`). Tims Auftrag: im seitlichen Spiel-Panel
sollen alle mit dem Server verbundenen (= gepaarten) Clients erscheinen, nicht nur die, die für
genau dieses Spiel bereits einen Spielstand haben — Geräte ohne Spielstand sollen das ebenfalls
sichtbar zeigen statt einfach zu fehlen.
- **Vorher:** `openGameDrawer()` bildete den `Clients`-Abschnitt ausschließlich aus den privaten
  Buckets des Spiels (`state.data.games`) — ein gepaartes Gerät, das dieses Spiel nie lokal
  erfasst hat, fehlte im Panel komplett, obwohl die volle Geräteliste (`state.data.devices`,
  bereits für die eigenständige Clients-Ansicht genutzt) längst geladen war.
- **Jetzt:** `openGameDrawer()` bildet aus `state.data.devices` + den privaten Buckets eine
  gemischte, alphabetisch nach Gerätename sortierte Liste (`clientEntries`). `clientsSection()`
  rendert sie in genau dieser Reihenfolge — Geräte mit Bucket als bekannte aufklappbare Karte
  (unverändert), Geräte ohne Bucket als neue schlanke, nicht aufklappbare Karte
  (Verbindungsstatus-Punkt via der bestehenden `clientDerivedStatus`-Logik + Text „Kein
  Spielstand für dieses Spiel"). Trenner-Label „Clients · N" zählt jetzt alle angezeigten
  Geräte. Leerzustand-Text gilt nur noch, wenn gar kein Gerät gepaart ist.
- **Gate grün:** Build **0/0**, `dotnet test` **195/0/0** (unverändert, reine
  Frontend-Änderung). `/code-review medium`: **1 Befund → behoben** (erster Wurf gruppierte
  Karten-mit-Bucket vor Karten-ohne-Bucket statt sie in der gemeinsamen alphabetischen
  Reihenfolge zu interleaven — Spec verlangte ausdrücklich reine Namenssortierung unabhängig
  vom Bucket-Status; `clientsSection()` rendert jetzt in `clientEntries`-Reihenfolge, nur das
  Akkordeon bleibt auf die Bucket-Karten beschränkt). **Laufzeit real belegt** (zwei isolierte
  lokale Testserver, echte HTTP-API-Seed-Daten, kein synthetisches Mocking): 1) 3 gepaarte
  Geräte (A+B mit privatem Bucket, A zusätzlich aktiver Teilnehmer eines geteilten Standes via
  echtem `POST .../revisions?scope=shared`, C rein gepaart ohne je einen Spielstand zu diesem
  Spiel) — Drawer zeigt „Clients · 3", A mit Sync-Icon+Glow, B mit normalem Status-Punkt, C mit
  gedämpfter Karte „Kein Spielstand für dieses Spiel" ohne Chevron; Klick auf C tut nichts,
  Akkordeon zwischen A/B funktioniert unverändert. 2) Gezielter Interleaving-Test (DeviceA/
  DeviceB mit Bucket, DeviceAB ohne Bucket, Name bewusst alphabetisch dazwischen) bestätigt nach
  dem Review-Fix die Reihenfolge A → AB → B statt fälschlich A → B → AB. Kein
  `/security-review` (keine sensible Fläche berührt).
- **Rollout:** nur Server-Image neu bauen/pushen (kein Client-Update nötig). Auf Tims
  ausdrücklichen Wunsch **direkt auf `master` committet und gepusht** (Präzedenzfall: die
  letzten beiden reinen Dashboard-Fixes liefen ebenso ohne Feature-Branch) — der
  Docker-Publish-Workflow baut/pusht das Server-Image automatisch bei jedem `master`-Push.
- **Offen:** keine Blocker. Visuelle Abnahme im echten Dashboard mit echten Daten bei Tim
  ausständig (Notebook-Testdaten waren synthetisch über die HTTP-API geseedet).

---

**Sync-Icon bei Client-Karten korrigiert (Server 1.5.5) — Rotations-Schiefstand + falsche
Sichtbarkeit.** Delta-Spec `specs/savevault-change-client-sync-icon.md`, Weg über
`/projekt-edit`. Tims Rückmeldung zum Detailpanel-Redesign (1.5.4): das rotierende Sync-Icon
bei den Geräte-Karten drehte sich schief statt sauber um die eigene Achse, und Icon +
grüner Rahmen erschienen bei JEDEM Gerät mit privatem Bucket, nicht nur bei Geräten, die den
geteilten Speicherstand tatsächlich nutzen. Reine **Server-Dashboard-Änderung** (`app.js`/
`styles.css`, kein Backend-/Client-Code).
- **Root-Cause 1 (Schiefstand):** `.client-card2__sync-icon` zentrierte das eingefügte
  `<svg>` nicht (anders als das funktionierende Vorbild `.shared-card__icon`). Per
  `getBoundingClientRect()`-Messung im echten Dashboard saß das SVG **4.49px vertikal /
  1.0px horizontal** aus der Mitte seiner eigenen Box verschoben (Inline-Baseline-Lücke) –
  die Rotation lief aber um die Box-Mitte, wodurch das sichtbare Icon exzentrisch wackelte.
  Fix: `display:flex;align-items:center;justify-content:center` auf dem Icon-Span, wie
  beim Vorbild.
- **Root-Cause 2 (falsche Sichtbarkeit):** `isSynced` prüfte bisher nur den generischen
  `bucket.status === "Synced"` des PRIVATEN Buckets – der ist auch bei einem rein lokalen,
  nie geteilten Spielstand "Synced", sobald nichts mehr aussteht. Fix: zusätzlich gegen
  `/api/game-states` geprüft (wurde schon geladen, aber bisher nirgends benutzt – laut
  Server-Kommentar extra fürs Spiel-Drawer gedacht) – nur Geräte, die aktuell gegen den
  geteilten Bucket-Schlüssel meldet, gelten als Teilnehmer und bekommen Icon + Glow.
- **Gate grün:** Build **0/0**, `dotnet test` **195/0/0** (unverändert, reine
  Frontend-Änderung). `/code-review medium`: **0 Befunde**. **Laufzeit real belegt**
  (lokaler Server, 2 gepairte Test-Geräte über echte HTTP-API: Gerät A tritt dem geteilten
  Stand bei und synct aktiv dagegen, Gerät B bleibt rein privat/lokal): vor dem Fix zeigten
  beide Karten fälschlich Icon+Glow, danach nur noch Gerät A; SVG-Offset innerhalb seiner
  Box vorher (4.49/1.0px) → nachher (0/0px), per `getBoundingClientRect()` gemessen.
  Akkordeon-Interaktion der Karte weiterhin funktionsfähig. Kein `/security-review` (keine
  sensible Fläche berührt).
- **Rollout:** nur Server-Image neu bauen/deployen (kein Client-Update nötig); danach beim
  Dashboard einmal Ctrl+F5 (neues `app.js`/`styles.css` aus dem Browser-Cache).
- **Offen:** keine Blocker. Sichtbare Abnahme im echten Dashboard bei Tim ausständig
  (Notebook-Testdaten waren synthetisch über die HTTP-API geseedet).

---

# SaveVault — Fortschritt (fortgeschrieben 2026-09-08)

**Spiel-Detailpanel neu gestaltet (Server 1.5.4).** Delta-Spec
`specs/savevault-change-detailpanel-redesign.md`, Weg über `/projekt-edit`. Reine
**Server-Dashboard-Änderung** (`app.js`/`styles.css`, kein Backend-/Client-Code) —
das ausfahrbare Spiel-Detailpanel folgt jetzt der Optik aus dem importierten
Claude-Design-Projekt `design-reference/Spiele Detailpanel.dc.html`.
- **Neue Struktur statt gestapelter Bucket-Abschnitte:** eine Karte „Geteilter
  Speicherstand" oben (Status-Pille, Herkunfts-Gerät/Zeitpunkt/Größe/Dateien,
  Standard-Pfad, eigener Versionsverlauf hinter einem Umschalter, Konflikt-Banner
  mit „Lösen") + darunter eine Liste ausklappbarer Geräte-Karten („Clients", ein
  Bucket = ein Gerät, Akkordeon — nur eine Karte gleichzeitig offen; Sync-Icon +
  dezenter Grün-Glow bei „Synced", Konflikt-Badge/-Banner+„Lösen" je Gerät).
  Leerzustand ohne geteilten Stand zeigt Hinweistext + „Über Geräte teilen".
  Drawer-Kopf (echtes Cover-Art, Titel) unverändert.
- **Konflikt-Zuordnung über Teilnehmerliste:** ein `Conflict`-Datensatz hängt am
  (geteilten) Bucket, nicht an den privaten Buckets der beteiligten Geräte — das
  Konflikt-Badge je Client-Karte matcht daher `conflict.participants[].deviceId`
  gegen `bucket.ownerDeviceId`, nicht den Bucket-Schlüssel direkt (sonst leer).
- **Konflikt-Kopien (Fork-Buckets) bekommen eine eigene, einfache Karte**
  (`forkCard`): eigener kanonischer Schlüssel server-seitig (`{key}#conflict-N`)
  → eigene Kachel/eigenes Drawer, nie Teil des Eltern-Spiels. Bewusst NICHT die
  „Geteilter Speicherstand"-Optik (kein Status-Pill/Glow/„Clients"-Abschnitt),
  sonst sähe ein eingefrorener Verlierer-Stand wie ein live synchroner Stand aus.
- **Gate grün:** Build **0/0**, `dotnet test` **195/0/0** (unverändert, reine
  Frontend-Änderung). `/code-review high`: **3 Befunde → 2 behoben** (Fork-Buckets
  öffneten anfangs fälschlich die „Geteilter Speicherstand"-Karte, weil `isFork`
  nicht geprüft wurde; der Standard-Save-Pfad ging beim Umbau zunächst verloren),
  **1 begründet abgelehnt** (neuer `kvCell()`-Helfer dupliziere `kv()` — unterschiedliche
  Optik/Aufrufer/Rückgabewert, Zusammenlegen hätte 3 bestehende Call-Sites riskiert
  für rein kosmetischen Gewinn). **Laufzeit real belegt** (lokaler Server, echte
  HTTP-API-Seed-Daten: 2 Geräte + geteilter Bucket + echter Konflikt via
  `isConflict:true`-Upload): Karte/Client-Karten zeigen korrekten Status, Akkordeon
  funktioniert, „Lösen" öffnet den bestehenden Konflikt-Dialog und löst real auf
  („Beide behalten" getestet), Standard-Pfad erscheint, Konflikt-Kopie-Karte nach
  Auflösung korrekt (nicht als „geteilt" missverstanden), Export/Wiederherstellen
  funktionieren. Kein `/security-review` (keine sensible Fläche berührt).
- **Rollout:** nur Server-Image neu bauen/deployen (kein Client-Update nötig).
- **Offen:** keine Blocker. Visuelle Abnahme im echten Dashboard mit echten Daten
  bei Tim ausständig (Notebook-Testdaten waren synthetisch über die HTTP-API geseedet).

---

**Release v1.8.0 (Client) / Server 1.5.2 — Zwei-Kästen-Ansicht Geteilt/Lokal fertig + Kern-Fixes,
Version-Bump nachgeholt.** Der Limit-Checkpoint direkt unten (2026-09-07) ist erledigt: beide dort
genannten Kern-Gate-Blocker wurden bereits mit Commit `8f2b722` behoben (Reihenfolge in
`LocalContentReplacer.Commit` umgekehrt, Sync-Flags erst nach Erfolg gesetzt), dazu der
Force-Upload-Knopf gebaut und ein Server-Fix ergänzt (Gewinner-Gerät zeigte nach einer
Dashboard-Konfliktlösung fälschlich dauerhaft weiter „Konflikt", Tims Arc-Raiders-Fall) — nur die
Version stand danach noch auf dem alten Stand.
- **Ursache für Tims Verwirrung:** Client-`<Version>` blieb nach `8f2b722` unverändert auf `1.7.0`
  stehen, obwohl seit dem `v1.7.0`-Tag 6 weitere Commits (~2556 Zeilen, u. a. komplett neues
  `MainWindow.xaml`/`.cs`) dazugekommen waren. Der installierte `v1.7.0`-Client (aus dem echten
  GitHub-Release gebaut) enthält diese Änderungen nachweislich nicht — ein Neu-Build vom
  damaligen `master` hätte sich weiter als „1.7.0" ausgegeben.
- **Jetzt nachgeholt:** Client **1.7.0 → 1.8.0**, Server **1.5.1 → 1.5.2**, `CHANGELOG.md` um
  einen v1.8.0-Eintrag ergänzt.
- **Release:** Tag `v1.8.0` gesetzt und gepusht → GitHub-Actions (`client-release.yml`) baut die
  Client-ZIP und hängt sie ans GitHub-Release; `docker-publish.yml` baut das Server-Image als
  `:latest` **und** `:v1.8.0` (läuft ohnehin bei jedem master-Push).
- **Offen (Tims Schritt):** Unraid-Server-Image ziehen/neu starten, Client auf allen Geräten
  **einmal von Hand** aktualisieren (danach greift der Selbst-Updater ab 1.6.0 automatisch für
  künftige Releases), dritter Handtest inkl. Arc-Raiders-Realtest (löst sich der Konflikt jetzt für
  beide Geräte sauber?), Force-Upload-Knopf einmal live ausprobieren. Bekannte, bewusst offen
  gelassene Nebensache aus dem Checkpoint unten (Punkt 4, `ConflictWindow`-Metadaten bei
  privat-gescopten Teilnehmern) weiterhin unverändert offen.

---

# SaveVault — Limit-Checkpoint (2026-09-07)

**Delta `savevault-change-shared-save-sichtbarkeit.md` (Phase 1) — ABGENOMMEN (2026-09-08).**
Tim: „Ja, abgeschlossen" nach Vorlage des Gesamt-Gate-Ergebnisses. Der vorherige Checkpoint-
Block (zweiter Limit-Halt, 2026-09-07) ist damit abgearbeitet.

**Phase 2 (Dashboard) — Anlauf am 2026-09-08 pausiert:** beim Versuch loszulegen zeigte
sich, dass es noch kein Mockup für die Drawer-Karten-Optik gibt und das „aktiv/inaktiv"-
Kriterium für den Dashboard-Fall (mehrere Buckets gleichzeitig sichtbar, anders als die
zwei klaren Kästen im Client) nicht eindeutig aus der Spec ableitbar ist. Tim erstellt
selbst noch ein detailliertes Mockup. **Nicht von selbst weiterbauen/-planen** — auf das
neue Mockup warten, siehe „Offene Fragen" am Ende der Spec-Datei.

## Was das ist
„Geteilter Speicherstand sichtbar & nahtlos" (Phase 1, Client). Aus der ursprünglichen
Sichtbarkeits-/Umschalt-Verbesserung wurden unterwegs (Tims Realtest deckte es auf) vier
Plan-Korrekturen nötig, die tiefer in die Sync-/Konflikt-Mechanik gingen — jede einzeln
dokumentiert, gegated und von Tim freigegeben. Nachtrag-Verlauf am Ende der Spec-Datei.

## Erreichter Stand — alles committet, alles gegated, Gesamt-Laufzeit-Gate grün

**Commits (neueste zuerst):**
- `f147612` — NoOp haelt den Konflikt-Status jetzt wirklich noch (war strukturell tot, s.u.).
- `cc1015e` — ConflictWindow-Metadaten mit korrektem (kanonischem) Bucket-Schlüssel abgefragt.
- `ec2a9b5` — Verwaisten Konflikt über „Lösen" mit Bestätigung auflösbar (Nachtrag 4).
- `45e724a` — Verwaisten Konflikt-Status nach exaktem Austausch zurücksetzen (Nachtrag 3).
- `8f2b722` — Server-Fix Gewinner-Gerät (Nachtrag 2) + Kern-Gate-Blocker behoben + Force-Upload-Knopf fertig.
- `493236f`/`1370d3f` — ältere Zwischenstände (siehe deren eigene Commit-Messages).

**Funktional bestätigt:**
- Zwei-Kästen-UI (Server/Lokal), „Sicherung deaktivieren"-Leiste, Versionshistorie-
  Flyout, `ShareCompareWindow` entfernt, echter Datei-Zeitstempel, Force-Upload-Knopf
  „Als geteilten Stand hochladen" (mit Inline-Bestätigung) — alle Gates grün.
- Exakter Bucket-Austausch (`SyncEngine.ReplaceLocalContentAsync` + `LocalContentReplacer`)
  ersetzt die additive Sync-Anwendung beim Umschalten Lokal↔Synchron — verhindert die
  fälschlichen Konflikte, die Tims Realtest zuerst aufdeckte. Sichere Reihenfolge (erst
  alle Downloads/Moves, dann erst Löschen).
- **Server-Fix ausgeliefert:** `savevault-server:1.5.10` läuft bereits auf Tims Unraid
  (gepusht + Docker-Image gebaut, von Tim bestätigt). Behebt, dass das gewinnende Gerät
  nach einer Dashboard-Konfliktlösung nie eine Bestätigung bekam.
- **Arc Raiders (Tims echter Repro-Fall) ist gelöst** — über „Lösen" mit dem neuen
  Bestätigungsdialog, von Tim live bestätigt („läuft sehr gut").
- **ConflictWindow-Metadaten-Fix (`cc1015e`):** kanonischer Schlüssel + echter Scope statt
  Doppel-Scoping — Zeit/Größe/Gerät im Konflikt-Dialog zeigen jetzt echte Werte statt „—".

**Gesamt-Laufzeit-Gate (2026-09-08, `tester`) — bestanden:**
- Build 0/0, `dotnet test` 194/194.
- Exakter Bucket-Austausch: Erfolgsfall UND erzwungener Abbruch mitten im Austausch
  real geprüft (Wegwerf-Harness) — sichere Reihenfolge bestätigt, keine Reste, Status
  korrekt (Nachtrag 3 bestätigt: Konflikt→Synced nach Austausch).
- Server-Fix Gewinner-Gerät: In-Process gegen echte `VaultStore`-Klasse — beide Geräte
  (Gewinner + Verlierer) bekommen `ApplyResolution`-Befehl.
- Nachtrag 4 (Lösen mit Bestätigung bei verwaistem Konflikt): Code-Kette geprüft,
  Bestätigung/Abbruch/echter-Konflikt-Regression alle korrekt.
- Server-Smoke (echter `dotnet run`): Konflikt-Endpunkte sauber (401/404/400/503 je nach
  Fall, keine 500er, kein Log-Fehler).

**Befund aus dem Gate, noch selbiger Session behoben:** `SyncEngine.cs` — der `NoOp`-
Schutz („Konflikt-Anzeige bleibt bei reinem No-Op-Zyklus bestehen", Zeile ~210-216) war
strukturell tot: `RunCycleAsync` (Zeile 80) setzt den Status unbedingt auf `Syncing`,
bevor `NoOp()` seine Prüfung `GetStatus==Conflict` überhaupt lesen kann. Vorbestehend
seit dem allerersten Commit (`282fba4`), **nicht** durch Nachtrag 1–4 verursacht. Fix
(`f147612`): der Status **vor** dem Zyklus wird gemerkt und an `NoOp()` durchgereicht,
statt den zwischenzeitlich überschriebenen Live-Zustand zu lesen. Build 0/0, Tests
194/194 weiterhin grün; per Wegwerf-Harness (Konflikt-Status + NoOp-Entscheidung
künstlich herbeigeführt) bestätigt: Status bleibt jetzt tatsächlich „Konflikt" statt auf
„Synced" zu kippen.

## Noch offen
1. **Prozess-Hinweis vom `inspekteur`:** für Nachtrag 2–4 wurde das Spec-Gate mit dem
   Kern-Gate zusammengelegt statt strikt getrennt (Zeitdruck bei kleinen, gut umrissenen
   Fixes) — für künftige Nachträge wieder sauber trennen.
2. **Phase 2 (Dashboard, rein visuell)** — eigener, noch nicht freigegebener Schritt.
   Erst nach Tims ausdrücklicher Freigabe angehen (siehe „Offene Fragen" in der Spec).

## Budget-Zeile (Stand Checkpoint)
Frischer Wert seit dem letzten Limit-Halt nicht neu erhoben — beim nächsten Einstieg
`/usage` neu ziehen, falls relevant.

---

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
