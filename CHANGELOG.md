# Changelog — SaveVault Client

Alle nennenswerten Änderungen am Windows-Client. Versionen entsprechen den
`v*.*.*`-Tags, die den Client-Release bauen.

## v1.3.0 — 2026-09-02

- **Pro Spiel wählbar: „Über Geräte synchronisieren" (Lokal ↔ Synchron).** Jedes
  Spiel bleibt standardmäßig lokal (eigener Bereich je Gerät). Über den neuen
  Schalter in der Spielzeile machst du es geräteübergreifend synchron. Gibt es für
  das Spiel bereits einen geteilten Stand, erscheint ein **Vergleichsdialog**
  (Dateien, Größe, Zeit, Herkunftsgerät) und du wählst, ob du den vorhandenen
  geteilten Stand übernimmst oder deinen lokalen als neuen geteilten hochlädst –
  es wird nie ohne deine Wahl überschrieben. Dein lokaler Stand bleibt beim
  Übernehmen als privates Backup erhalten.
- **„Sync pausieren" heißt jetzt „Hochladen deaktivieren".** Gleiche Funktion
  (das Spiel wird gar nicht hochgeladen, bleibt rein lokal), klarere Beschriftung.

## v1.2.0 — 2026-09-02

- **Jedes Gerät sichert in seinen eigenen Bereich.** Bisher teilten sich alle PCs
  denselben Verlauf pro Spiel — koppelte man einen zweiten PC mit abweichenden
  Ständen, gab es sofort viele Konflikte. Ab jetzt hat **jedes Gerät pro Spiel
  seinen eigenen privaten Bucket** auf dem Server (Backup + Historie). Ein frisch
  gekoppeltes Gerät löst damit keinen Konflikt-Sturm mehr aus. Geräteübergreifendes
  Teilen kommt als **opt-in** in einem der nächsten Releases.
- **Einmalige Umstellung beim ersten Start.** Der lokale Basis-Stand wird einmalig
  zurückgesetzt, sodass jedes Spiel als erste Revision in den privaten Bucket neu
  gesichert wird. Der alte gemeinsame Verlauf bleibt auf dem Server als Archiv
  erhalten (nur lesbar, wird nicht mehr automatisch synchronisiert).

## v1.0.5 — 2026-08-28

- **Autostart mit Windows.** Neue Einstellung „Automatisch mit Windows starten"
  (Einstellungen → Gerät & Sync), standardmäßig aktiv. Der Client trägt sich pro
  Benutzer in den Windows-Autostart ein (Registry-Run-Key, kein Admin nötig) und
  startet weiterhin still im Infobereich. Abhaken entfernt den Eintrag wieder; der
  Client gleicht den Zustand bei jedem Start ab.
- **Eigenes Programm-Icon.** Die `SaveVault.Client.exe` trägt jetzt dasselbe Symbol
  wie das Infobereich-Icon (mehrauflösend: 16/32/48/256 px), abgeleitet aus dem
  bestehenden Tray-Design. Das Tray-Symbol selbst bleibt unverändert.

## v1.0.4 — 2026-08-28

- Übersprungene Spiele bleiben dauerhaft in der Statusfläche sichtbar (amber
  markiert, mit Grund) und lassen sich per „Ordner zuordnen" manuell nachtragen,
  statt nur einmalig im Hinweis-Dialog aufzutauchen.

## v1.0.3 — 2026-08-28

- Client meldet zusätzlich den Standard-Save-Pfad (`SaveRoot`) an den Server —
  Grundlage für den serverseitigen Revision-Export und die Pfad-Anzeige im
  Dashboard. (Der größere Teil dieses Release lag serverseitig: ZIP-Export und
  Box-Art via IGDB.)

## v1.0.2 — 2026-08-28

- Pfad-Härtung bei der Spielerkennung: zu weit gefasste oder kollabierte Ordner
  (Steam-Root, mehrdeutige Zuordnung) und zu große Save-Ordner werden zuverlässig
  erkannt, beschränkt gezählt (kein Durchlaufen riesiger Bäume) und dem Anwender
  gemeldet.

## v1.0.1

- Frühe Korrekturen nach dem ersten Release.

## v1.0.0

- Erster Client-Release: WPF-Tray-Client mit Pairing, Hintergrund-Sync über
  ludusavi-Spielerkennung, Status-Fenster, Einstellungen und Konflikt-Dialog.
