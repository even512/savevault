# design-reference — verbindliche Optik-Vorlage

Diese Dateien stammen aus Tims Claude-Design-Projekt „SaveVault Webinterface
Design" und sind die **verbindliche visuelle Vorlage** für das Server-Web-Dashboard
(Bau-Plan-Schritt 4). Der `oberflaechen-bauer` reproduziert diese Optik in **echtem
HTML/CSS/JS**, das die JSON-API des Servers konsumiert — die Dateien hier sind
Referenz, kein Liefercode.

## Dateien
- **`SaveVault.dc.html`** — Haupt-Layout mit allen fünf Ansichten (Dashboard, Spiele,
  Clients, Verlauf, Einstellungen), beiden Detail-Drawern (Spiel/Client), dem
  Konflikt-Modal, allen Farben (oklch), Icons (inline-SVG) und Beispiel-Daten.
- **`GameCard.dc.html`**, **`ClientRow.dc.html`** — die zwei wiederkehrenden Bausteine.

## So liest man die Vorlage
Das `.dc.html`-Format ist Claude Designs Komponenten-Format: `{{ … }}` sind
Platzhalter, `<sc-if>`/`<sc-for>` sind Bedingungen/Schleifen, `<dc-import>` bindet die
Bausteine ein. **Ignorieren** für die Umsetzung — relevant sind die **Style-Werte,
Struktur und Farben**. Das begleitende `support.js` (Design-Runtime) wurde bewusst
**nicht** übernommen: es ist die Rendering-Engine der Design-Umgebung und braucht
React aus jener Umgebung, ist also standalone nicht lauffähig und für uns kein Code.

## Kern der Design-Sprache (für die Umsetzung)
- **Theme:** dark-only. Hintergrund `oklch(0.17 0.014 280)`, Flächen/Karten
  `oklch(0.20–0.23 …)`, Text `oklch(0.94 0.006 280)`.
- **Akzent:** Teal `oklch(0.68 0.075 195)`.
- **Statusfarben:** synced grün `oklch(0.75 0.16 150)`, syncing teal (Akzent),
  Konflikt orange `oklch(0.75 0.18 55)`, pending gelb `oklch(0.82 0.15 95)`, offline
  grau `oklch(0.58 0.02 280)`, error rot `oklch(0.65 0.22 25)`.
- **Formen:** Karten mit 12–16 px Radius, 1 px Rand `oklch(0.29 0.02 280 / 0.6)`,
  Sidebar links, Suchfeld oben rechts, Filter-Pills, dezente Animationen (Puls/Shimmer).

## Bewusste Abweichung vom Mockup (von Tim entschieden)
Im **Konflikt-Modal** werden **Spielzeit** und **Fortschritt %** NICHT umgesetzt (aus
Savegame-Dateien nicht ableitbar). Stattdessen echte Felder: Zeitpunkt, Größe,
Dateianzahl, Gerät, Prüfsumme. Sonst so nah wie möglich am Mockup.
