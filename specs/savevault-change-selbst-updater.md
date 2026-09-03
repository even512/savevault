# Delta-Spec — Client-Selbst-Updater (GitHub-Releases)

## 1. Ziel
Der Windows-Client aktualisiert sich künftig selbst: Er erkennt neuere GitHub-Releases,
lädt das passende ZIP, tauscht seine eigenen Dateien aus und startet neu — **ohne** dass Tim
manuell ein ZIP herunterlädt und den Ordnerinhalt ersetzt. Anstoß per **Banner** (bei
automatischem Fund) oder per Knopf in den **Optionen**; angewandt wird erst nach Bestätigung.

## 2. Umfang / Nicht-Umfang

**Umfang**
- **Update-Prüfung** gegen die GitHub-API: neuestes Release von `even512/savevault`
  (`releases/latest`), Tag `vX.Y.Z`, Asset `SaveVault-Client-*-win-x64.zip`. Öffentliches Repo
  → keine Anmeldung nötig. Vergleich Tag-Version ↔ laufende Assembly-Version (`System.Version`).
- **Automatischer Check**: einmal beim Start (kurz verzögert) **und danach alle 24 h**, solange
  der Client läuft. Zu häufige Aufrufe werden über einen persistierten Zeitstempel
  (`LastUpdateCheckUtc`, „nicht öfter als ~alle 20 h beim Start") gedämpft. Abschaltbar über
  eine Option **„Automatisch nach Updates suchen"** (Default an).
- **Melden**: Wird eine neuere Version gefunden, erscheint im Fenster ein **Banner**
  („Neue Version X.Y.Z verfügbar — Jetzt aktualisieren & neu starten") und einmalig ein
  **Tray-Hinweis** (das Fenster ist meist ins Tray minimiert). In den **Optionen** eine Karte
  **„Über & Updates"**: aktuelle Version, Status, Knopf **„Nach Updates suchen"**, bei Fund
  **„Jetzt aktualisieren & neu starten"**.
- **Anwenden (Selbst-Update)**: ZIP nach `%LocalAppData%\SaveVault\update\staging` entpacken,
  die gestagte `SaveVault.Client.exe` mit Sonder-Argument `--apply-update "<installDir>" <pid>`
  starten, laufende App beenden. Die gestagte Instanz wartet auf das Ende der alten Instanz,
  kopiert Staging → Installationsordner (mit kurzen Wiederholungen gegen transiente Sperren),
  startet die aktualisierte `SaveVault.Client.exe` im Installationsordner und beendet sich. Der
  frisch gestartete Client räumt beim nächsten Start das Staging-Verzeichnis auf (best-effort).
- **Kein Vollbild-Guard** (Tims Entscheidung): Der Neustart erfolgt sofort auf Bestätigung, auch
  wenn ein Spiel läuft — der Vorgang ist kurz.

**Nicht-Umfang**
- Keine Server-/Dashboard-Änderung, keine Änderung an der Release-Pipeline (das bestehende
  `client-release.yml` produziert bereits das erwartete ZIP-Asset).
- Kein Delta-/Patch-Update, keine Signatur-/Hash-Prüfung über das hinaus, was HTTPS zu
  github.com liefert (gleiche Vertrauensbasis wie Tims heutiger Handdownload desselben ZIPs).
- Kein Downgrade, keine Pre-Release-/Draft-Berücksichtigung, kein automatisches Anwenden ohne
  Bestätigung (nur „melden + bestätigen").
- Keine Änderung an der Kern-Sync-Logik oder am Server-API-Client.

## 3. Betroffene Dateien (Schätzung)
- **Neu** `src/SaveVault.Client/Services/UpdateService.cs` — Prüfen (GitHub-API), Herunterladen +
  Entpacken ins Staging, Anwenden (Prozess-Start + Beenden) sowie der Applier-Zweig
  (Kopieren Staging→Install + Neustart). Reine Client-Logik (Windows: Prozess/Pfade/Netz).
- **Neu** `src/SaveVault.Client/Services/UpdateModels.cs` — kleine Ergebnis-Typen
  (`UpdateCheckResult` etc.), falls nicht in `UpdateService.cs` gebündelt.
- `src/SaveVault.Client/App.xaml.cs` — ganz früh in `OnStartup` den `--apply-update`-Modus
  abfangen (Applier ausführen, dann `Shutdown`); Staging-Aufräumen; Start-/24-h-Check anstoßen;
  Tray-Hinweis bei Fund.
- `src/SaveVault.Client/MainWindow.xaml` (+`.cs`) — Banner-Zeile oben im Inhaltsbereich; Karte
  „Über & Updates" in den Optionen; Toggle „Automatisch nach Updates suchen"; Status-/Knopf-Logik.
- `src/SaveVault.Client/Services/ClientConfig.cs` — Felder `AutoUpdateCheckEnabled` (Default `true`)
  und `LastUpdateCheckUtc` (nullable), rückwärtskompatibel (fehlt = Default).
- Ggf. `tests/SaveVault.Core.Tests/` — nur falls testbare reine Logik in den Kern wandert
  (Versionsvergleich/Asset-Auswahl). Sonst per Wegwerf-Harness verifiziert (WPF/Client ist hier
  nicht als Unit-Test isolierbar, wie in früheren Client-Phasen).

## 4. Akzeptanz & Verifikation
- **Versionsvergleich**: „1.5.0" gegen Tag „v1.6.0" → Update verfügbar; gegen „v1.5.0" → aktuell;
  gegen „v1.4.0" → kein Update. Kaputter/fehlender Tag oder fehlendes Asset → sauberes
  „kein Update / Fehler", nie Absturz. (Wegwerf-Harness, mehrere Fälle.)
- **Check-Pfad real**: gegen die echte GitHub-API von `even512/savevault` einmal `releases/latest`
  ziehen, Tag + Asset-URL korrekt herauslesen (User-Agent-Header gesetzt). Beleg im Checkpoint.
- **Staging/Apply-Argumentzweig**: `--apply-update` wird in `OnStartup` erkannt und verzweigt,
  ohne Agent/Tray zu starten (per Log/Beleg). Der vollständige Selbst-Austausch (Kopieren +
  Neustart der echten WPF-exe) ist auf dem Notebook nicht sicher isolierbar → **offener Handtest
  bei Tim** nach dem Deploy (im Checkpoint vermerkt): neues Release taggen, Client meldet Banner,
  „Jetzt aktualisieren" → Client kommt in neuer Version hoch.
- **Build grün** (0 Fehler), **`dotnet test` grün** (bestehende 112 unverändert; neue reine Logik
  → neue Tests).
- **Gate**: `/code-review high` (Kernänderung); `/security-review` (sensible Fläche: Netz-Outbound,
  Prozess-Start, Datei-Überschreiben).

## 5. Risiken / Rückwärtskompatibilität
- **Teil-Überschreiben bei Abbruch**: Bricht das Kopieren Staging→Install mitten ab (z. B. gesperrte
  Datei, kein Schreibrecht), kann der Installationsordner gemischt sein. Gegenmaßnahme: kurze
  Wiederholungen pro Datei; bei endgültigem Fehler bricht der Applier ab und startet die
  **vorhandene** (alte) exe wieder — kein „toter" Zustand. Für ein persönliches LAN-Tool
  akzeptabel; bewusst kein transaktionaler Ordner-Swap (Aufwand vs. Nutzen).
- **Schreibrechte**: Liegt der Client in `Program Files`, scheitert das In-Place-Überschreiben ohne
  Admin. Erwartete Installation ist ein Nutzerordner (ZIP entpackt). Fehler wird gemeldet, alte
  Version läuft weiter.
- **GitHub-Rate-Limit** (unauthentifiziert 60/h): Start- + 24-h-Check plus gelegentlich manuell
  liegen weit darunter; der 20-h-Dämpfer beim Start schützt zusätzlich.
- **Config-Migration**: Neue Felder sind additiv; alte `config.json` ohne die Felder liest Defaults
  (Auto-Check an), kein Bruch.
- **Vertrauen**: Bezug ausschließlich über HTTPS von `github.com`/`api.github.com`, fester
  Repo-Pfad. Gleiche Vertrauensbasis wie der bisherige manuelle Download. Kein Token, keine
  Ausführung fremden Codes außer der neuen SaveVault-exe (die Tim heute ohnehin manuell startet).
