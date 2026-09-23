# Delta-Spec: SaveVault blockiert Advanced-Optimus-Umschaltung nicht mehr

## Ziel
Startet Tim ein Spiel, versucht das Notebook automatisch per Advanced Optimus (Hardware-MUX)
auf „nur NVIDIA-GPU" umzuschalten. Der Wechsel wird aktuell **namentlich von SaveVault**
blockiert („Prozess blockiert"). SaveVault soll dabei nie mehr als blockierender Prozess
erscheinen — unabhängig davon, ob der Tray-Client gerade läuft, minimiert ist oder ein
Wasserzeichen-Toast anzeigt.

## Ursache (Code-Recherche, noch nicht auf Hardware verifiziert)
SaveVault.Client ist eine WPF-App, die dauerhaft im Tray läuft (`App.xaml.cs`: „die App läuft
ohne sichtbares Hauptfenster weiter"). WPF komponiert jedes Fenster (auch `MainWindow` beim
Öffnen und das transparente `WatermarkWindow`-Toast bei jeder Sync-Aktivität) standardmäßig
über die DirectX-Hardwarepipeline (`wpfgfx`/Milcore). Damit hält der Prozess ein aktives
D3D-Gerät auf der aktuell aktiven GPU — genau das verhindert bei Advanced-Optimus-Laptops den
Hardware-MUX-Wechsel, solange der Prozess läuft (bekanntes Verhalten bei im Hintergrund
residenten WPF-/Overlay-Apps, nicht spezifisch für SaveVault-Logik).

## Umfang
- WPF global auf Software-Rendering umstellen (`RenderOptions.ProcessRenderMode =
  RenderMode.SoftwareOnly`), gesetzt so früh wie möglich in `App.xaml.cs::OnStartup` — vor
  dem Erzeugen von `MainWindow`. Damit fasst der Prozess nie ein Hardware-GPU-Gerät an, egal
  ob Dashboard-Fenster offen ist oder nur der Wasserzeichen-Toast erscheint.

## Nicht-Umfang
- Keine Änderung an Sync-/Agent-Logik, Tray-Icon (GDI-basiert über `NotifyIcon`, nicht
  betroffen), Autostart oder Update-Mechanik.
- Kein Eingriff in den Applier-Zweig (`--apply-update`) — der erzeugt ohnehin kein Fenster.
- Keine Erkennung/Reaktion auf laufende Spiele (das gibt es schon über
  `FullscreenDetection`, bleibt unverändert) — der Fix wirkt unabhängig davon, ob gerade ein
  Spiel läuft, weil SaveVault dann grundsätzlich kein GPU-Handle mehr hält.

## Betroffene Dateien
- `src/SaveVault.Client/App.xaml.cs` (Software-Rendering erzwingen)
- `src/SaveVault.Client/MainWindow.xaml`, `src/SaveVault.Client/Ui/Theme.xaml` (Rückarbeit nach
  `/code-review high`, siehe unten — nicht ursprünglich geplant)

## Akzeptanz & Verifikation
- Build 0 Fehler, bestehende Tests unverändert grün (reine Startup-Einstellung, keine neue
  Logik → kein neuer Test nötig/möglich).
- **Laufzeit-Verifikation nur auf echter Hardware möglich (offener Handtest bei Tim):**
  SaveVault normal laufen lassen (Tray, wie gewohnt), ein Spiel starten und beobachten, ob
  Advanced Optimus jetzt automatisch auf „nur NVIDIA-GPU" wechselt, ohne SaveVault als
  Blockierer zu nennen. Dashboard-Fenster (Öffnen über Tray) und Wasserzeichen-Toast sollen
  optisch unverändert wirken (Software-Rendering ist bei dieser kleinen, seltenen UI nicht
  wahrnehmbar).

## Risiken / Rückwärtskompatibilität
- Software-Rendering erhöht bei offenem Dashboard-Fenster die CPU-Last geringfügig (statt
  GPU) — bei einer so kleinen, selten geöffneten Oberfläche vernachlässigbar.
- **`/code-review high`, Runde 1:** Der bisherige Endlos-Puls (`RepeatBehavior="Forever"`) des
  Glow-Effekts auf aktiven Speicherstand-Kästen (`ServerBoxActive`/`LocalBoxActive` — praktisch
  jedes nicht ausgeschlossene Spiel) hätte unter Software-Rendering echte, dauerhafte CPU-Last
  erzeugt, sobald das Dashboard offen ist — nicht mehr „unwahrnehmbar", wie ursprünglich
  angenommen. Behoben: Puls-Storyboard entfernt, stattdessen fester Glow
  (`Theme.xaml`-Resource `ActiveBoxGlow`, `MainWindow.xaml`-Trigger setzen `Border.Effect`
  direkt) — optisch fast identisch (Highlight bleibt), aber ohne Endlos-Neuzeichnen.
- **`/code-review high`, Runde 2 — bewusst nicht behoben:** Software-Rendering wird
  pauschal für jede Installation erzwungen, nicht nur für Advanced-Optimus-Notebooks. Bewusst
  akzeptiert: SaveVault ist Tims eigenes Werkzeug auf bekannter Hardware, eine zuverlässige
  Advanced-Optimus-Erkennung gäbe es nicht ohne unverhältnismäßigen Aufwand, und die
  verbleibende Software-Rendering-Last ist nach obigem Fix vernachlässigbar (keine
  Endlos-Animation mehr).
- **Wenn der Handtest zeigt, dass SaveVault trotzdem noch blockiert:** Ursache liegt dann
  nicht (nur) in der WPF-Composition, sondern z. B. in einem anderen offenen
  Geräte-/API-Handle — würde eine weitere Untersuchungsrunde erfordern (siehe Gate-Regel
  „max. 2 Runden Rückarbeit, dann Eskalation").
- Kein Versions-Bump vor bestätigtem Handtest — echtes Nutzer-Verhalten (Umschalten
  funktioniert) ist die eigentliche Abnahme, nicht nur der Build.
