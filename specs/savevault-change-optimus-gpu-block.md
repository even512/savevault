# Delta-Spec: SaveVault blockiert Advanced-Optimus-Umschaltung nicht mehr

## Ziel
Startet Tim ein Spiel, versucht das Notebook automatisch per Advanced Optimus (Hardware-MUX)
auf „nur NVIDIA-GPU" umzuschalten. Der Wechsel wird aktuell **namentlich von SaveVault**
blockiert („Prozess blockiert"). SaveVault soll dabei nie mehr als blockierender Prozess
erscheinen — unabhängig davon, ob der Tray-Client gerade läuft, das Dashboard offen war oder
gerade ein Spiel synct.

## Ursache — der Weg dahin (drei widerlegte Theorien, dann der Treffer)
Mehrere Handtest-Runden bei Tim, jede mit einer plausiblen, recherche-gestützten Theorie, die
sich am echten Notebook als falsch oder unvollständig herausstellte:

1. **„WPFs Hardwarepipeline hält ein D3D-Gerät offen, sobald der Prozess läuft."** Fix:
   `RenderOptions.ProcessRenderMode = SoftwareOnly`. Handtest: **keine Wirkung.**
2. **„`MainWindow.OnClosing` hat den X-Klick bisher nur mit `Hide()` beantwortet — das
   Fenster-Handle blieb dabei am Leben."** Tim hatte das Dashboard offen gehabt und mit X
   geschlossen; das passte zu einem bekannten WPF-Verhalten
   ([dotnet/wpf#9286](https://github.com/dotnet/wpf/issues/9286)). Fix: `MainWindow` schließt
   beim X jetzt wirklich, `App` baut bei Bedarf eine frische Instanz. Handtest (Dashboard **nie**
   geöffnet, rein im Tray): **blockiert weiterhin.**
3. **Systematische Bisektion statt weiterer Theorien:** Tim bestätigte per Task-Manager
   (GPU-Spalte), dass SaveVault **keinerlei** GPU-Auslastung zeigt und im GPU-Modul gar nicht
   auftaucht — die ganze D3D-/Composition-Spur war damit falsch. Reihe von Wegwerf-Testbauten
   und Mini-Sonden (WinForms-Tray, WPF+Tray+nie gezeigtes Fenster, Netzwerk+Datei-Watcher,
   Netzwerk gegen Tims echten LAN-Server, `IsConfigured=false`-Leerlauf) — **jede einzelne** lief
   sauber durch, **außer** die echte `SaveVault.Client.exe` selbst, komplett unkonfiguriert, ganz
   ohne Agent-Aktivität. Einzige verbliebene Variable: `_window = CreateWindow();` (eager in
   `OnStartup`) — testweise entfernt: **Umschaltung klappt.**

**Bestätigte Ursache:** `MainWindow.xaml` enthält Effekte (`DropShadowEffect`) und
hochwertig skalierte Bilder. WPF bereitet das schon beim bloßen **Konstruieren** des Fensters
(`InitializeComponent()`) vor — auch ganz ohne `Show()`. Ein leeres `Window` (wie in den Sonden)
löst das nicht aus; ein reich gestaltetes wie SaveVaults Dashboard schon. Der Fix aus Runde 1
(Software-Rendering) griff hier nie, weil die Effekt-Vorbereitung unabhängig vom Render-Modus
passiert.

4. **Runde 4 — auch das reichte noch nicht ganz:** Tim öffnete das Dashboard (um durch den
   Konflikt-Vorfall unten verursachte Konflikte zu lösen), schloss es danach sauber mit X — und
   die Blockade war wieder da. Grund: WPFs interne Kompositions-Infrastruktur
   (`MediaContextNotificationWindow`, sichtbar per `EnumWindows` auf den laufenden Prozess) wird
   beim allerersten gezeigten Fenster einmalig angelegt und bleibt danach **für den Rest der
   Prozess-Laufzeit** bestehen — auch nach dessen sauberem Schließen. Es gibt keine öffentliche
   Möglichkeit, das innerhalb eines laufenden Prozesses zurückzusetzen; nur ein echter
   Prozess-Neustart hilft.

## Der Fix
- `App.xaml.cs::OnStartup` baut `MainWindow` **nicht mehr eager**. Die Instanz entsteht erst beim
  ersten echten Öffnen über den Tray (`ShowMainWindow` → `CreateWindow`). Solange niemand das
  Dashboard öffnet, existiert überhaupt kein `MainWindow`-Objekt.
- **Runde 4, Tims pragmatischer Vorschlag:** Der X-Knopf im Dashboard schließt das Fenster nicht
  mehr nur, sondern startet SaveVault **komplett neu** (`App.RestartApp`) — neue Instanz starten,
  alte sauber beenden. Das setzt die WPF-Kompositions-Altlast zuverlässig zurück, statt Tim einen
  manuellen Tray-Neustart aufzuerlegen. Normales Beenden über den Tray bleibt unverändert (kein
  Neustart-Loop).

## Umfang
- **Der eigentliche Fix:** `_window = CreateWindow();` aus `OnStartup` entfernt.
- **Notwendige Folgearbeit (nicht optional):** Die automatische 24-h-Selbst-Update-Prüfung hing
  bisher am Vorhandensein von `_window` — das war früher unkritisch (Fenster existierte immer),
  jetzt aber der Normalfall. Sauber gelöst: `App` bekommt eine eigene, vom Dashboard komplett
  unabhängige `UpdateService`-Instanz; die Prüf-und-Stempel-Logik lebt jetzt zentral in
  `UpdateService.CheckAndStampAsync` (von `App` und `MainWindow` gemeinsam genutzt, keine
  Doppelung mehr).
- **Runde 2 bleibt als Fix erhalten** (echtes Schließen des Fensters statt `Hide()`) — löst nicht
  die Advanced-Optimus-Blockade allein, ist aber selbst eine gerechtfertigte Verbesserung
  (verhindert ein separates Ressourcen-Leck).
- **Runde 1 (Software-Rendering) zurückgenommen:** jetzt erwiesenermaßen wirkungslos gegen die
  Blockade, dafür mit echtem Preis (unnötige CPU-Last, u. a. beim Wasserzeichen-Toast während des
  Zockens). `RenderOptions.ProcessRenderMode = SoftwareOnly` aus `OnStartup` entfernt. Der
  Runde-1-Fix „fester statt pulsierender Glow" bleibt trotzdem bestehen (harmlose Vereinfachung,
  kein Grund für Rückbau).

## Nicht-Umfang
- Keine Änderung an Sync-/Agent-Logik, Tray-Icon, Autostart.
- Kein Eingriff in den Applier-Zweig (`--apply-update`).
- Keine Erkennung/Reaktion auf laufende Spiele (bleibt über `FullscreenDetection` unverändert).
- Kein Erhalt von UI-Zwischenzustand über ein Fenster-Schließen hinweg (siehe Risiken,
  bereits aus Runde 2 akzeptiert).
- **Bewusst nicht weiter verfolgt** (siehe Risiken unten): Update-Banner erscheint nicht
  automatisch beim allerersten Öffnen nach einem im Hintergrund gefundenen Update; zwei
  unabhängige `UpdateService`/`ClientConfigStore`-Instanzen (App + MainWindow) statt einer
  gemeinsam injizierten.

## Betroffene Dateien
- `src/SaveVault.Client/App.xaml.cs` (kein eager `MainWindow`-Aufbau mehr; kein erzwungenes
  Software-Rendering mehr; `RestartApp()` für den Neustart-per-X; Update-Prüfung entkoppelt und
  zentralisiert)
- `src/SaveVault.Client/Services/UpdateService.cs` (`CheckAndStampAsync`, threadsicher ohne
  `ConfigureAwait(false)`)
- `src/SaveVault.Client/MainWindow.xaml.cs` (`ApplyUpdateResult`, `CheckForUpdatesAsync` nutzt
  den zentralen Helfer; `OnClosing`/`OnClosed`, `_closed`-Flag samt Nutzung in
  `LoadHistoryAsync`/`ProbeShareStatusAsync`, `_forceUploadArmTimer`-Aufräumen aus Runde 2;
  `OnCloseButtonClick` ruft jetzt `App.RestartApp()`)
- `src/SaveVault.Client/MainWindow.xaml`, `src/SaveVault.Client/Ui/Theme.xaml` (fester statt
  endlos pulsierender Glow, aus Runde 1; X-Button-Tooltip nachgezogen)

## Akzeptanz & Verifikation
- Build 0 Fehler, `dotnet test` 208/208 grün (unverändert — reines Lebenszyklus-/Timing-Verhalten,
  nicht sinnvoll automatisiert prüfbar).
- **Laufzeit-Verifikation, endgültig bestätigt (Runde 3 der Theorien, unkonfigurierter Testbau):**
  Kein je konstruiertes `MainWindow` ⇒ Umschaltung funktioniert einwandfrei. Auf Tims echter
  Hardware mehrfach reproduziert (Diagnose-Testbauten mit/ohne Watcher, mit/ohne Netzwerk,
  mit/ohne Autostart-Registry-Eintrag, mit/ohne Software-Rendering, mit/ohne Update-Check —
  **immer** blockiert, **außer** ohne `MainWindow`-Konstruktion).
- **Runde 4 real reproduziert:** Tim öffnete das Dashboard (Konflikte lösen), schloss es sauber
  mit X — Blockade war wieder da (siehe Ursache oben, `MediaContextNotificationWindow`). Mit dem
  Neustart-per-X-Fix noch nicht erneut end-zu-Ende auf der Hardware bestätigt.
- **Noch offen: End-zu-Ende-Handtest mit Tims echtem Setup auf dem finalen Code-Stand**
  (Neustart-per-X, kein Software-Rendering mehr) — insbesondere: normal starten (Dashboard nicht
  anfassen) → Spiel starten → Umschaltung sollte klappen; danach Dashboard öffnen, mit X
  schließen (löst Neustart aus) → nochmal Spiel starten → sollte weiterhin klappen.
- Kein Versions-Bump/Release vor diesem letzten Handtest.

## Risiken / Rückwärtskompatibilität
- **Bewusst nicht weiter verfolgt (mehrere Eskalationsschwellen längst überschritten,
  `/code-review high` lief auf dem Update-Checker-Rework acht Runden):**
  - Wird ein Update im Hintergrund gefunden, während das Dashboard noch nie geöffnet wurde, zeigt
    das erste Öffnen danach noch keinen Banner (die Tray-Meldung „Fenster öffnen, um zu
    aktualisieren" trifft dann nicht sofort zu) — der Nutzer muss einmal „Nach Updates suchen"
    klicken. Kein Datenverlust, reine Timing-Ungenauigkeit.
  - `App` und `MainWindow` halten weiterhin je eine eigene `UpdateService`/`ClientConfigStore`-
    Instanz statt einer gemeinsam injizierten (wie bei `_agent`) — funktioniert korrekt (die
    Config-Race wurde behoben, siehe unten), ist aber nicht so sauber wie möglich.
  - Zwei zeitlich unglücklich verschränkte Prüfungen (eine manuell im Dashboard, eine zeitgleich
    im Hintergrund) könnten theoretisch den gerade erst angezeigten Banner wieder verstecken.
    Seltener Rand, kein Datenverlust.
- **Behoben, weil echter Fehler (nicht nur Politur):** `UpdateService.CheckAndStampAsync` schrieb
  `config.json` zunächst über `ConfigureAwait(false)` auf einem Threadpool-Thread — das hätte
  mit einem gleichzeitigen, synchronen Speichern der Einstellungen im Dashboard-UI-Thread
  (`OnSaveClick`) um dieselbe Datei race können (verlorene Schreibvorgänge). Behoben durch
  Entfernen von `ConfigureAwait(false)`: läuft jetzt zuverlässig auf demselben UI-Thread wie
  jedes andere Config-Speichern.
- Aus Runde 1/2 weiterhin bestehend, bereits akzeptiert: UI-Zwischenzustand (ausgewähltes Spiel,
  Tab, offene Optionen ohne Speichern) geht über ein Fenster-Schließen hinweg verloren —
  akzeptiertes, normales „Formular ohne Speichern geschlossen"-Verhalten.
- **`/code-review high` auf Runde 4 (Neustart-per-X), Funde behoben:** (1) `RestartApp()` hätte
  kurzzeitig **zwei Instanzen gleichzeitig** laufen lassen können (neue Instanz startet, bevor die
  alte ihren Agent stoppt) — behoben, indem `_agent.StopAsync()` zuerst abgewartet wird. (2) Schlug
  der Neustart fehl (z. B. exe verschoben), blieb das Fenster ohne jede Rückmeldung einfach offen
  — behoben mit Rückfall auf normales `Close()` plus Neustart des eigenen Agents. (3) Der
  X-Button-Tooltip sagte noch „In den Infobereich schließen" — korrigiert. (4)
  `LoadHistoryAsync`/`ProbeShareStatusAsync` prüften das `_closed`-Flag aus Runde 2 nicht — jetzt
  nachgezogen.
- **Bewusst nicht behoben:** Ein Neustart-per-X (oder das normale Beenden über den Tray) kann eine
  gerade laufende Wiederherstellung/einen Upload abbrechen, statt sie abzuwarten — das war schon
  beim bisherigen „Beenden" so (derselbe Abbruch-Mechanismus), keine neue Fehlerklasse durch diese
  Runde. Ordentliches Abwarten laufender Operationen wäre ein eigenständiges, größeres Feature.
- Kein Versions-Bump vor bestätigtem End-zu-Ende-Handtest.

## Unabhängiger Vorfall während der Diagnose (nicht Teil dieses Fixes, siehe CHECKPOINT.md)
Beim probeweisen Beiseitelegen von `config.json` für einen Diagnose-Testbau hat eine einmalige
Migrationslogik (`ClientAgent.StartAsync`, `PerDeviceBucketsMigrated`-Guard) fälschlich gefeuert
und Tims lokale Sync-Status-Dateien für alle privaten Buckets gelöscht (`ResetAllState()`) — das
hat **keine** echten Spielstand-Dateien betroffen (bestätigt im Code: löscht nur die
Fortschritts-Buchführung), aber 54 falsche „Konflikt"-Meldungen erzeugt. Über die reale Server-API
(gleicher Weg wie der Dashboard-Dialog: `ResolveConflictAsync` mit `KeepDevice`, nur automatisiert
für alle Konflikte mit genau einem Teilnehmer = diesem Gerät) aufgelöst, mit Tims Freigabe nach
einem Dry-Run. Der zugrunde liegende Bug (Migrationslogik feuert bei jeder fehlenden `config.json`,
nicht nur beim echten Erstlauf) ist noch **nicht** gefixt — auf Tims Wunsch zurückgestellt, bis
der Optimus-Fix steht.
