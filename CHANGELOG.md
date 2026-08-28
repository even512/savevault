# Changelog — SaveVault Client

Alle nennenswerten Änderungen am Windows-Client. Versionen entsprechen den
`v*.*.*`-Tags, die den Client-Release bauen.

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
