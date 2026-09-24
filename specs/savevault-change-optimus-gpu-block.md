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

## Der Fix
`App.xaml.cs::OnStartup` baut `MainWindow` **nicht mehr eager**. Die Instanz entsteht erst beim
ersten echten Öffnen über den Tray (`ShowMainWindow` → `CreateWindow`, aus Runde 2 bereits
vorhanden). Solange niemand das Dashboard öffnet, existiert überhaupt kein `MainWindow`-Objekt —
SaveVault rührt dann nichts an, was die Umschaltung blockieren könnte.

## Umfang
- **Der eigentliche Fix:** `_window = CreateWindow();` aus `OnStartup` entfernt.
- **Notwendige Folgearbeit (nicht optional):** Die automatische 24-h-Selbst-Update-Prüfung hing
  bisher am Vorhandensein von `_window` — das war früher unkritisch (Fenster existierte immer),
  jetzt aber der Normalfall. Sauber gelöst: `App` bekommt eine eigene, vom Dashboard komplett
  unabhängige `UpdateService`-Instanz; die Prüf-und-Stempel-Logik lebt jetzt zentral in
  `UpdateService.CheckAndStampAsync` (von `App` und `MainWindow` gemeinsam genutzt, keine
  Doppelung mehr).
- **Runde 1/2 bleiben als Fixes erhalten** (Software-Rendering, echtes Schließen des Fensters,
  fester statt endlos pulsierender Glow) — sie lösen nicht die Advanced-Optimus-Blockade, sind
  aber selbst gerechtfertigte, unabhängige Verbesserungen (siehe deren eigene Historie unten).

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
- `src/SaveVault.Client/App.xaml.cs` (kein eager `MainWindow`-Aufbau mehr; Update-Prüfung
  entkoppelt und zentralisiert)
- `src/SaveVault.Client/Services/UpdateService.cs` (`CheckAndStampAsync`, threadsicher ohne
  `ConfigureAwait(false)`)
- `src/SaveVault.Client/MainWindow.xaml.cs` (`ApplyUpdateResult`, `CheckForUpdatesAsync` nutzt
  den zentralen Helfer; `OnClosing`/`OnClosed`, `_closed`-Flag, `_forceUploadArmTimer`-Aufräumen
  aus Runde 2)
- `src/SaveVault.Client/MainWindow.xaml`, `src/SaveVault.Client/Ui/Theme.xaml` (fester statt
  endlos pulsierender Glow, aus Runde 1)

## Akzeptanz & Verifikation
- Build 0 Fehler, `dotnet test` 208/208 grün (unverändert — reines Lebenszyklus-/Timing-Verhalten,
  nicht sinnvoll automatisiert prüfbar).
- **Laufzeit-Verifikation, endgültig bestätigt (Runde 3 der Theorien, unkonfigurierter Testbau):**
  Kein je konstruiertes `MainWindow` ⇒ Umschaltung funktioniert einwandfrei. Auf Tims echter
  Hardware mehrfach reproduziert (Diagnose-Testbauten mit/ohne Watcher, mit/ohne Netzwerk,
  mit/ohne Autostart-Registry-Eintrag, mit/ohne Software-Rendering, mit/ohne Update-Check —
  **immer** blockiert, **außer** ohne `MainWindow`-Konstruktion).
- **Noch offen: End-zu-Ende-Handtest mit Tims echtem, wiederhergestelltem Setup** (69 Spiele,
  echter Server, Autostart an) auf dem finalen Code-Stand (inkl. Update-Checker-Rework) — die
  bisherigen Bestätigungen liefen auf gezielt reduzierten Diagnose-Bauten, nicht auf dem
  tatsächlichen Endstand.
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
- Aus Runde 1/2 weiterhin bestehende, bereits akzeptierte Punkte: Software-Rendering bleibt
  bestehen, obwohl jetzt erwiesen ist, dass es die Optimus-Blockade nie gelöst hat (kostenlos,
  da UI dank festem statt pulsierendem Glow ohnehin kaum noch Render-Last hat); UI-Zwischenzustand
  (ausgewähltes Spiel, Tab, offene Optionen ohne Speichern) geht über ein Fenster-Schließen
  hinweg verloren — akzeptiertes, normales „Formular ohne Speichern geschlossen"-Verhalten.
- Kein Versions-Bump vor bestätigtem End-zu-Ende-Handtest.
