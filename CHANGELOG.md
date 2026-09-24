# Changelog — SaveVault Client

Alle nennenswerten Änderungen am Windows-Client. Versionen entsprechen den
`v*.*.*`-Tags, die den Client-Release bauen.

## v1.8.10 — 2026-09-24

- **Fix: Eine verlorene `config.json` löst die einmalige Per-Device-Buckets-Migration nicht
  mehr erneut aus.** Der Migrations-Guard hing bisher allein am Config-Flag
  `PerDeviceBucketsMigrated` — fehlte die `config.json` (z. B. weil sie bei einem
  Diagnose-Testbau beiseitegelegt wurde), lief beim nächsten Start der destruktive
  `ResetAllState()` erneut und löschte den lokalen Sync-Fortschritt aller Spiele; in einem
  realen Vorfall (2026-09-24) entstanden daraus 54 falsche „Konflikt“-Meldungen. Die Migration
  läuft jetzt nur, wenn **keinerlei** Anzeichen einer abgeschlossenen Migration vorliegen:
  weder die neue persistente Marker-Datei (`%AppData%\SaveVault\per-device-buckets-migrated`,
  unabhängig von `config.json`) noch das Legacy-Config-Flag. Bereits migrierte Geräte (Flag aus
  der alten Version) bekommen beim ersten Start der neuen Version die Marker-Datei gesetzt,
  **ohne** Reset — und danach kann auch ein späterer `config.json`-Verlust den Reset nicht mehr
  auslösen. Vier Regressionstests decken die Fälle ab (verlorene Config, echter Erstlauf,
  Neustart, Legacy-Flag ohne Marker).

## v1.8.9 — 2026-09-24

- **SaveVault blockierte auf Advanced-Optimus-Notebooks (Hardware-MUX) die automatische
  Umschaltung auf „nur NVIDIA-GPU" beim Spielstart — behoben.** Ursache: das Dashboard-Fenster
  (`MainWindow`) wurde bisher beim Programmstart immer im Hintergrund aufgebaut, auch wenn es
  nie geöffnet wurde; WPF bereitet dabei allein durchs Konstruieren (nicht erst beim Anzeigen)
  Effekte vor, die Windows als GPU-Nutzung wertet. Der Client baut das Fenster jetzt erst beim
  tatsächlichen Öffnen über den Tray. Da WPFs interne Kompositions-Infrastruktur, einmal
  angelegt, für den Rest der Programmlaufzeit bestehen bleibt, startet ein Klick auf das X im
  Dashboard SaveVault jetzt außerdem komplett neu (statt das Fenster nur zu schließen) — so
  bleibt die Umschaltung auch nach einem Blick ins Dashboard zuverlässig frei.
- Software-Rendering (seit v1.8.8-Vorstufe testweise erzwungen) wieder entfernt: hat die
  Blockade oben nicht gelöst, kostete aber unnötig CPU, u. a. beim Wasserzeichen-Toast während
  des Spielens.

## v1.8.7 — 2026-09-15

- **Konflikte bei geteilten (shared) Speicherständen lösen sich jetzt automatisch, ohne
  Dialog.** Tims Reallog zeigte: sobald ein Gerät bei einem geteilten Stand weiterspielte,
  während ein anderes Gerät zwischenzeitlich schon gespeichert hatte, meldete der Client
  jeden Sync-Zyklus erneut „Konflikt" und legte bei jeder weiteren lokalen Änderung eine
  neue Konflikt-Revision an — bis Tim manuell im Konflikt-Dialog die richtige Fassung
  auswählte. Da er nie gleichzeitig auf zwei Geräten spielt, ist das immer derselbe Fall:
  dieses Gerät hatte den zuletzt woanders gespeicherten Stand nur noch nicht gezogen. Bei
  `shared`-Speicherständen gewinnt jetzt automatisch dieses Gerät (normaler Upload auf den
  aktuellen Server-Stand) — die überschriebene Server-Revision bleibt dabei unangetastet in
  der Versions-Historie erhalten. Private Speicherstände zeigen den manuellen Konflikt-Dialog
  weiterhin wie bisher.

## v1.8.6 — 2026-09-15

- **Diagnose-Log erfasst jetzt auch das tatsächliche Ergebnis eines Sync-Zyklus, nicht nur die
  Entscheidung.** Tim reproduzierte den in v1.8.5 noch offenen Konflikt-Fall real (Basis-Revision
  rückte trotz „Download"-Entscheidung fünf Zyklen lang nicht vor) — das bisherige Log zeigte aber
  nur, was der Client vorhatte, nicht ob es gelang. `sync.log` bekommt jetzt zusätzlich eine
  `ERGEBNIS(...)`-Zeile je Zyklus (Erfolg + neue Basis-Revision, oder Fehler + genaue Ausnahme).
  Zusätzlich fängt ein neues Sicherheitsnetz jede zuvor unprotokollierte Ausnahme ab, bevor sie wie
  gehabt weitergereicht wird — reine Sichtbarkeit, kein Verhaltensunterschied. Ursache des
  eigentlichen Konflikt-Falls ist damit noch nicht gefunden, aber beim nächsten Auftreten jetzt
  belegbar statt zu erraten.

## v1.8.5 — 2026-09-15

- **Nachfrage beim ersten Beitritt zu einem bereits geteilten Stand.** Trifft ein Gerät zum ersten
  Mal auf ein Spiel, das anderswo bereits geteilt wird, erscheint jetzt eine Entscheidung mit
  echten Kennzahlen beider Seiten („Server-Stand übernehmen" oder „meinen lokalen Stand als
  geteilten Stand hochladen"), statt den Server-Stand automatisch und ohne Rückfrage zu
  übernehmen. Späteres Hin- und Herschalten eines bereits bekannten Spiels bleibt unverändert ohne
  Nachfrage.
- **Fix: Konflikt-Dialog zeigte bei einem wiederholt auftretenden Konflikt weiterhin den
  allerersten Erkennungszeitpunkt.** Speicherte ein Gerät bei offenem, ungelöstem Konflikt mehrfach
  weiter (z. B. beim Weiterspielen), zeigte „Lösen" bis zuletzt nur die Fassung vom ersten
  Auftreten, nicht die aktuelle. Der Server hält die im Konflikt verzeichneten Revisionsnummern
  jetzt aktuell (serverseitiger Fix, Server 1.5.7 erforderlich).
- **Fix: Geräte-Name im Konflikt-Dialog.** Für ein fremdes Gerät stand dort weiterhin eine rohe
  Geräte-Kennung statt des Namens (der Namens-Fix aus v1.8.1 deckte nur die Versionshistorie/den
  geteilten Stand ab, nicht den Konflikt-Dialog selbst).
- **Fix: Versionshistorie im Client zeigte bei einem „Synchron"-Spiel den falschen (privaten,
  eingefrorenen) Verlauf** statt des tatsächlich aktiven geteilten Verlaufs — dadurch wirkte die
  Historie unvollständig/veraltet im Vergleich zum Dashboard, und ein „Wiederherstellen" auf einen
  vermeintlich aktuellen, tatsächlich alten Eintrag konnte den aktiven Ordnerinhalt ungewollt
  zurücksetzen.
- **Fix: die Geteilt/Lokal-Kästen aktualisierten sich nicht automatisch**, wenn im Hintergrund ein
  Sync-Zyklus für das gerade angezeigte Spiel abschloss — erst ein erneutes Auswählen des Spiels
  zeigte den aktuellen Stand. Aktualisiert sich jetzt von selbst.
- **Neu: dauerhaftes Diagnose-Log** (`%AppData%\SaveVault\sync.log`) protokolliert ab jetzt jeden
  Sync-Zyklus (Zeitpunkt, Spiel, Scope, Aktion, Revisionsstände) — hilft, einen weiteren,
  bislang nicht reproduzierbaren Einzelfall (ein Gerät zeigte nach nachweislich korrektem Download
  trotzdem beim nächsten Speichern „Konflikt") beim nächsten Auftreten mit echten Daten statt
  Vermutungen einzugrenzen.

## v1.8.4 — 2026-09-15

- **Nachgezogene Fixes aus einem zweiten, zuvor nicht veröffentlichten Entwicklungszweig**
  (Ursache: nach Commit `8f2b722` liefen zwei unabhängige Fortsetzungen auseinander — der
  Zweig, der zu v1.8.0-1.8.3 führte, und ein separater lokaler Zweig mit den folgenden
  beiden Fixes; jetzt zusammengeführt).
- **Fix: Konflikt-Dialog zeigte fast keine Informationen.** Zeit/Größe/Prüfsumme der
  beteiligten Fassungen blieben oft bei „—" stehen. Ursache: die Metadaten-Abfrage nutzte
  den bereits gescopten Konflikt-Schlüssel ein zweites Mal als wäre er der kanonische
  Spiel-Schlüssel und fand serverseitig nichts. Fragt jetzt den kanonischen Schlüssel +
  echten Scope ab.
- **Fix: Nach richtig gewähltem Stand blieb „Konflikt" bei jedem Speichern erneut sichtbar.**
  Ein erfolgreicher, exakter Wechsel (Lokal↔Synchron) oder ein über „Lösen" bestätigter
  verwaister Konflikt setzten den Anzeige-Status bisher nicht zurück, wenn zuvor „Konflikt"
  stand. Der Zustand kippte danach nie mehr eigenständig zurück auf „Synchronisiert".

## v1.8.3 — 2026-09-08

- **Nachbesserung zu v1.8.2:** Der Umbruch allein reichte noch nicht aus, die Zeile blieb
  bei mehreren gleichzeitig sichtbaren Buttons eng. Die Aktions-Buttons im Spiel-Detail
  (Jetzt sichern, Als geteilten Stand hochladen, Ordner öffnen, Lösen, Ordner zuordnen)
  sind jetzt insgesamt kompakter (kleinere Schrift/Abstände) und brechen bei Platzmangel
  in eine zweite Zeile um, statt sich zu überlappen. „Versionshistorie" bleibt weiterhin
  fest am rechten Rand.

## v1.8.2 — 2026-09-08

- **Fix: Layout im Spiel-Detail.** Der Knopf „Als geteilten Stand hochladen" konnte durch seinen
  Text sehr breit werden und drängte dabei „Ordner öffnen" fast komplett aus der Zeile. Der
  Button-Text bricht jetzt um statt in voller Breite auf einer Zeile zu stehen.

## v1.8.1 — 2026-09-08

- **Fix: „Herkunftsgerät" zeigte eine Geräte-ID statt eines Namens.** Sowohl beim geteilten
  Speicherstand (Zwei-Kästen-Ansicht) als auch in der Versionshistorie stand dort bisher die
  rohe, kryptische Geräte-Kennung. Der Server liefert jetzt den beim Pairing vergebenen
  Gerätenamen (standardmäßig der Rechner-Hostname) mit, der Client zeigt ihn unverkürzt an.

## v1.8.0 — 2026-09-08

- **Geteilt/Lokal auf einen Blick im Spiel-Detail.** Neue Zwei-Kästen-Ansicht zeigt für jedes
  synchrone Spiel den geteilten und den lokalen Stand nebeneinander (Zeit/Größe/Herkunft), inkl.
  „Sicherung deaktivieren"-Leiste und einem Versionshistorie-Flyout mit dem echten Datei-Zeitstempel.
- **Neuer Knopf „Als geteilten Stand hochladen".** Lädt deinen lokalen Stand bewusst als neue
  geteilte Revision hoch, ohne das Gerät auf Synchron umzuschalten (mit Zwei-Klick-Bestätigung und
  Metadaten-Vergleich).
- **Sicherer beim Umschalten Lokal ↔ Synchron.** Ein interner Fix stellt sicher, dass beim
  Ordner-Austausch nie alte Dateien gelöscht werden, bevor die neuen vollständig da sind, und dass
  der Client den Wechsel erst nach erfolgreichem Austausch als vollzogen meldet – schützt vor
  Datenverlust/Fehlanzeige, falls der Vorgang mittendrin abbricht (z. B. Server offline).
- **Konflikt-Anzeige korrigiert.** Nach einer im Dashboard gelösten Konfliktsituation zeigte das
  gewinnende Gerät fälschlich dauerhaft weiter „Konflikt" (Server-seitiger Fix).

## v1.6.0 — 2026-09-03

- **Der Client aktualisiert sich selbst.** Kein manuelles ZIP-Herunterladen und Ordner-Austauschen
  mehr: Der Client prüft beim Start und danach täglich, ob auf GitHub ein neueres Release vorliegt.
  Wird eine neuere Version gefunden, erscheint ein **Banner** im Fenster (und ein kurzer Tray-Hinweis).
  Auf **„Jetzt aktualisieren & neu starten"** zieht der Client das neue Release, tauscht sich im
  laufenden Betrieb aus und startet in der neuen Version neu. Der Austausch ist **transaktional**:
  scheitert er zwischendurch, wird auf den vorherigen Stand zurückgerollt – kein halb-aktualisierter
  Client. In den **Optionen → „Über & Updates"**: installierte Version, Knopf **„Nach Updates suchen"**
  und der Schalter **„Automatisch nach Updates suchen"** (Standard an). Angewandt wird ein Update nie
  ohne Bestätigung.
- **Hinweis:** Diese automatische Aktualisierung greift ab dieser Version. Der Schritt von 1.5.0 auf
  1.6.0 wird noch einmal wie gewohnt von Hand ausgerollt; danach übernimmt der Updater.

## v1.5.0 — 2026-09-03

- **Dashboard zeigt „Verbunden/Offline" schneller.** Der Client sendet sein Lebenszeichen
  (Präsenz) jetzt in einem eigenen, kürzeren Takt (Standard alle 15 Sekunden) statt nur im
  vollen Sync-Intervall (Standard 60 Sekunden). Ein Gerät, das online kommt oder wegfällt,
  erscheint im Dashboard dadurch deutlich zeitnaher. Der eigentliche Sync-Zyklus
  (Erkennung/Upload) läuft unverändert weiter – nur das billige Lebenszeichen wurde
  entkoppelt. Das Intervall ist über `heartbeatIntervalSeconds` in der `config.json`
  einstellbar (Untergrenze 5 s; nie langsamer als das Sync-Intervall). Zusammen mit dem
  Live-Dashboard (Server 1.4.0) fühlt sich die Übersicht damit „live" an.

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
