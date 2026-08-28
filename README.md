# SaveVault

Selbst-gehostete „Cloud" für Spielstände. Jeder Windows-PC sichert und
synchronisiert seine Savegames automatisch über den heimischen Server (Docker auf
Unraid), mit sichtbarem Status und ohne Datenverlust — als schlanker Ersatz für
Resilio Sync, beschränkt auf Savegames.

> **Stand:** In Bau (Projekt-Werkstatt, Phase B). Die verbindliche Spec liegt unter
> `../.claude/projekt-werkstatt/specs/savevault.md`.

## Bestandteile

| Teil | Projekt | Zweck |
|---|---|---|
| **Kern** | `src/SaveVault.Core` | Plattformneutraler Sync-Kern: Hashing/Manifest, Sync-Entscheidung, Konflikterkennung, Ludusavi-Wrapper, API-Client |
| **Server** | `src/SaveVault.Server` | ASP.NET Core: JSON-API + dunkles Web-Dashboard, Versions-Historie, Token/Pairing, Befehls-Warteschlange (Docker) |
| **Client** | `src/SaveVault.Client` | WPF-Tray-App: Ordner-Überwachung, Spielerkennung, Status-Fenster, Konflikt-Dialog |
| **Tests** | `tests/SaveVault.Core.Tests` | xUnit auf den Kern |

## Wie es funktioniert (Kurz)

- Der Client überwacht die Savegame-Ordner (automatisch per Ludusavi erkannt oder
  manuell gewählt). Lokale Änderung → **hochladen**; Server neuer → **herunterladen**.
- **Echter Konflikt** (beide Seiten seit dem letzten Sync geändert) wird **nicht**
  überschrieben: beide Fassungen bleiben, du entscheidest — im Web-Dashboard **oder**
  im Tray-Tool.
- Der Server behält **jede** hochgeladene Version dauerhaft (Wiederherstellen jederzeit).
- Nur im **LAN** (Fernzugriff über dein eigenes VPN ins Heimnetz).

## Bauen & Starten (Entwicklung)

Voraussetzung: .NET SDK 9.

```bash
dotnet build SaveVault.sln
dotnet test

# Server (Web-Dashboard unter http://localhost:8420)
# Beim ersten Aufruf legst du im Dashboard Benutzer + Passwort an (kein Token nötig).
dotnet run --project src/SaveVault.Server

# Client (Windows)
dotnet run --project src/SaveVault.Client
```

## Server per Docker (Zielumgebung Unraid)

```bash
cp .env.example .env      # optional: Port/Datenpfad anpassen
docker compose up -d
# danach Dashboard öffnen und Benutzer + Passwort anlegen
```

Details siehe `docker-compose.yml`. Der Storage liegt im gemounteten Volume
(`SAVEVAULT_DATA`), die Versions-Historie bleibt damit über Container-Updates erhalten.

## Ludusavi

Die automatische Spielerkennung nutzt das mitgelieferte
[`ludusavi`](https://github.com/mtkennerly/ludusavi)-CLI (MIT). Die Binary gehört nach
`tools/ludusavi/` — siehe `tools/ludusavi/README.md` (sie wird aus Größengründen nicht
mit eingecheckt).
