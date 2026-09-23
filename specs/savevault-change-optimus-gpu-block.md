# Delta-Spec: SaveVault blockiert Advanced-Optimus-Umschaltung nicht mehr

## Ziel
Startet Tim ein Spiel, versucht das Notebook automatisch per Advanced Optimus (Hardware-MUX)
auf „nur NVIDIA-GPU" umzuschalten. Der Wechsel wird aktuell **namentlich von SaveVault**
blockiert („Prozess blockiert"). SaveVault soll dabei nie mehr als blockierender Prozess
erscheinen — unabhängig davon, ob der Tray-Client gerade läuft, minimiert ist oder ein
Wasserzeichen-Toast anzeigt.

## Ursache
**Runde 1 (widerlegt durch echten Handtest):** Vermutet wurde, WPFs Standard-Hardwarepipeline
(`wpfgfx`/Milcore) halte allein durchs Rendern ein aktives D3D-Gerät offen. Fix
(`RenderOptions.ProcessRenderMode = SoftwareOnly`) gebaut und deployt — Tims Handtest zeigte:
**SaveVault blockiert weiterhin, kein Unterschied.** Treiber war bereits aktuell (555+, damit
scheidet auch eine reine Treiber-Alt-Version als Ursache aus).

**Tatsächliche Ursache (durch Recherche + Rückfrage bei Tim bestätigt):** `MainWindow.OnClosing`
hat den X-Klick bisher abgefangen und nur `Hide()` aufgerufen („nicht schließen, sondern in den
Tray zurückziehen"). Tim hatte das Dashboard mindestens einmal offen und mit X geschlossen —
das Fenster-Handle blieb dabei die ganze Zeit am Leben, nur unsichtbar. Ein WPF-Fenster hält,
sobald es einmal gezeigt wurde, sein Composition-Handle bis zum echten `Close()`, unabhängig vom
Render-Modus (bestätigt durch [dotnet/wpf#9286](https://github.com/dotnet/wpf/issues/9286) —
„even a minimal WPF application" blockiert — sowie einen vergleichbaren, ungelösten
PowerToys-Fall). Genau dieses dauerhaft offene, nur versteckte Handle blockiert die
MUX-Umschaltung — unabhängig davon, ob gerade ein Spiel läuft.

## Umfang
- **Runde 1 (behalten als Zusatzmaßnahme, siehe Risiken):** WPF global auf Software-Rendering
  umstellen (`RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly`), gesetzt so früh wie
  möglich in `App.xaml.cs::OnStartup` — vor dem Erzeugen von `MainWindow`.
- **Runde 3 (der eigentliche Fix):** `MainWindow.OnClosing` versteckt das Fenster beim X-Klick
  nicht mehr, sondern lässt es wirklich schließen (Handle wird zerstört); `OnClosed` meldet die
  Anbindung an `_agent.State.Changed` ab. `App.xaml.cs` hält `_window` dafür nullable und baut
  beim nächsten Öffnen über den Tray (`ShowMainWindow`) eine frische Instanz — **nicht** sofort
  im `Closed`-Handler, damit beim „Beenden" (das ein offenes Fenster mitschließt) nicht kurz vor
  dem Herunterfahren noch einmal unnötig eine neue, am Agent-Zustand hängende Instanz entsteht.

## Nicht-Umfang
- Keine Änderung an Sync-/Agent-Logik, Tray-Icon (GDI-basiert über `NotifyIcon`, nicht
  betroffen), Autostart oder Update-Mechanik.
- Kein Eingriff in den Applier-Zweig (`--apply-update`) — der erzeugt ohnehin kein Fenster.
- Keine Erkennung/Reaktion auf laufende Spiele (das gibt es schon über
  `FullscreenDetection`, bleibt unverändert).
- Kein Erhalt unsauber unsicherer UI-Zwischenzustände über ein Schließen hinweg (siehe Risiken) —
  bewusst nicht nachgebaut, das ist normales „Formular ohne Speichern geschlossen"-Verhalten.

## Betroffene Dateien
- `src/SaveVault.Client/App.xaml.cs` (Software-Rendering erzwingen; Fenster-Lebenszyklus:
  `CreateWindow()`/`ShowMainWindow()` mit nullable `_window`)
- `src/SaveVault.Client/MainWindow.xaml.cs` (`OnClosing`/`OnClosed`: echtes Schließen statt
  Hide-in-den-Tray)
- `src/SaveVault.Client/MainWindow.xaml`, `src/SaveVault.Client/Ui/Theme.xaml` (Rückarbeit nach
  `/code-review high` Runde 1 — Endlos-Puls-Glow durch festen Glow ersetzt)

## Akzeptanz & Verifikation
- Build 0 Fehler, bestehende Tests unverändert grün (kein neuer Test möglich — reines
  Fenster-Lebenszyklus-/Startup-Verhalten, nicht sinnvoll ohne echtes WPF-Fenster/GPU-Treiber
  automatisiert prüfbar).
- **Laufzeit-Verifikation nur auf echter Hardware möglich (offener Handtest bei Tim, Runde 2):**
  Dashboard einmal öffnen und mit X schließen (genau Tims Ausgangslage), dann ein Spiel starten
  und beobachten, ob Advanced Optimus jetzt automatisch auf „nur NVIDIA-GPU" wechselt, ohne
  SaveVault als Blockierer zu nennen.

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
- **`/code-review high`, Runde 3, Fund 1 (behoben):** Der ursprüngliche Entwurf hat die neue
  Fenster-Instanz sofort im `Closed`-Handler nachgebaut. Beim „Beenden" (Shutdown schließt ein
  offenes Dashboard mit) wäre dabei kurz vor `_agent.DisposeAsync()` noch eine zusätzliche,
  am Agent-Zustand hängende Instanz entstanden — meldet `DisposeAsync()` dabei noch eine späte
  `State.Changed`, könnte `Dispatcher.BeginInvoke` auf einem bereits herunterfahrenden
  Dispatcher eine `InvalidOperationException` werfen. Behoben: `_window` wird beim echten
  Schließen nur noch auf `null` gesetzt, der Neuaufbau passiert erst faul beim nächsten
  `ShowMainWindow()` — während des Beendens wird also gar keine neue Instanz mehr gebaut.
- **`/code-review high`, Runde 3, Fund 2 — bewusst nicht behoben:** Vorher überlebte
  unsaved UI-Zustand (offene Optionen-Eingaben ohne „Speichern", ausgewähltes Spiel, aktiver
  Tab) ein Schließen, weil dieselbe Fenster-Instanz nur versteckt wurde. Jetzt beginnt jedes
  erneute Öffnen frisch. Bewusst akzeptiert: das entspricht normalem „Formular ohne Speichern
  geschlossen"-Verhalten, wie es die meisten Apps zeigen — Zustand über ein komplettes
  Fenster-Schließen hinweg zu erhalten wäre selbst mit dem alten Hide()-Verhalten schon
  ungewöhnlich gewesen und keine bewusst gewollte Eigenschaft.
- **`/code-review high`, Runde 4, Fund (behoben):** Die Lazy-Neuaufbau-Lösung aus Runde 3
  hatte einen Nebeneffekt übersehen: `RunAutoUpdateCheckAsync` (24-h-Selbst-Update-Prüfung)
  brach bisher früh ab, wenn `_window` gerade `null` war (`if (_window is null) return;`) —
  vorher tote Absicherung (das Fenster existierte immer), jetzt aber der Normalfall, sobald
  das Dashboard einmal geschlossen wurde. Die automatische Update-Prüfung wäre damit für den
  Rest der Laufzeit stillschweigend tot gewesen. Behoben: baut sich bei Bedarf genau wie
  `ShowMainWindow` selbst eine (ungezeigte) Instanz.
- **`/code-review high`, Runde 5 — Eskalationsschwelle erreicht (Gate-Regel „max. 2 Runden"):**
  Runde 4s Fix hatte selbst ein Leck: `RunAutoUpdateCheckAsync` baute sich bei Bedarf ein
  komplettes `MainWindow` nur für den Update-Check auf, das aber nie gezeigt/geschlossen wird —
  damit nie über `Closed` aufgeräumt, hängt für immer am `_agent.State.Changed`, und jede
  weitere Zustandsänderung hätte über `Refresh()`/`SelectGame()` erneut echte Netzwerk-Aufrufe
  auf diesem unsichtbaren Geister-Fenster ausgelöst. **Zurückgerollt** auf den einfachen,
  sicheren Guard von vorher (`if (_window is null) return;`) — die 24h-Update-Prüfung setzt
  bewusst aus, solange das Dashboard geschlossen ist, statt eine neue Fehlerquelle zu riskieren.
  Zusätzlich zwei kleinere, risikoarme Funde aus derselben Runde behoben: `_forceUploadArmTimer`
  wird jetzt in `OnClosed` gestoppt (sonst hätte ein 5-s-Bestätigungs-Timer das geschlossene
  Fenster überlebt), und ein bei Schließen bereits über `Dispatcher.BeginInvoke` eingereihtes
  `Refresh()` bricht jetzt über ein `_closed`-Flag früh ab, statt auf dem toten Fenster
  weiterzulaufen.
- **Bewusst NICHT weiter verfolgt (Eskalation an Tim statt weiterer Runden):**
  - `_pendingUpdate` (die „Update verfügbar"-Bannerinfo) lebt auf der Fenster-Instanz und geht
    beim Schließen verloren — dieselbe Kategorie Trade-off wie der bereits gebilligte
    UI-Zustandsverlust (ausgewähltes Spiel, Tab), nur eben auch fürs Update-Banner.
  - Ob `RenderOptions.ProcessRenderMode = SoftwareOnly` (Runde 1) überhaupt noch etwas bringt,
    ist unklar: Tims Handtest hat sie nicht isoliert geprüft (der Hide()-Bug war zur gleichen
    Zeit noch aktiv). Bleibt vorerst drin (möglicher Zusatzschutz, siehe EarTrumpet-Präzedenzfall
    in der Recherche), könnte sich aber nach einem erfolgreichen Handtest als überflüssiger
    Ballast herausstellen — dann bräuchte auch der feste Glow (Runde 1) keinen Grund mehr.
  - Kleinere Code-Doppelungen (verwaiste `Glow`-Ressource in `MainWindow.xaml`, `AccentColor`
    vs. hartkodierte Farbe in `ActiveBoxGlow`, `CreateWindow`/`ShowWatermark` folgen demselben
    Muster zweimal) — kosmetisch, keine Korrektheitsfunde.
- **Wenn der Handtest (Runde 2) zeigt, dass SaveVault trotzdem noch blockiert:** Ursache liegt
  dann nicht (nur) im Fenster-Handle, sondern z. B. im `WatermarkWindow`-Toast selbst (zeigt
  sich nur während eines laufenden Spiels, schließt sich aber nach ~2,5 s selbst) oder an etwas
  außerhalb von SaveVault — würde eine weitere Untersuchungsrunde erfordern (siehe Gate-Regel
  „max. 2 Runden Rückarbeit, dann Eskalation" — diese Runde wäre dann die Eskalations-Schwelle).
- Kein Versions-Bump vor bestätigtem Handtest — echtes Nutzer-Verhalten (Umschalten
  funktioniert) ist die eigentliche Abnahme, nicht nur der Build.
