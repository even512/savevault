# SaveVault – Deployment

Zwei Teile: der **Server** läuft als Docker-Container auf Unraid, der **Client** wird auf
jedem Windows-PC installiert. Bilder/Artefakte entstehen über GitHub Actions.

## Einmal einrichten (GitHub + Docker Hub)

1. **Repo zu GitHub pushen** (falls noch kein Remote):
   ```bash
   git remote add origin git@github.com:<DEIN-GH-USER>/savevault.git
   git push -u origin main
   ```
2. **Docker-Hub-Zugang als GitHub-Secrets** hinterlegen
   (Repo → Settings → Secrets and variables → Actions → *New repository secret*):
   - `DOCKERHUB_USERNAME` – dein Docker-Hub-Benutzername
   - `DOCKERHUB_TOKEN` – ein Docker-Hub *Access Token* (Account Settings → Security)

## Server: Image bauen → Docker Hub

Der Workflow [`.github/workflows/docker-publish.yml`](../.github/workflows/docker-publish.yml)
baut das linux/amd64-Image und pusht es bei jedem Push auf `main` (Tag `latest`) und bei
`v*.*.*`-Tags (Versions-Tags) nach `docker.io/<DOCKERHUB_USERNAME>/savevault-server`.

- Auslösen: Push auf `main`, oder unter *Actions* manuell „Run workflow", oder ein Tag:
  ```bash
  git tag v1.0.0 && git push origin v1.0.0
  ```

## Server: auf Unraid installieren

1. In [`savevault-unraid.xml`](savevault-unraid.xml) **`DOCKERHUB_USER`** durch deinen
   Docker-Hub-Benutzernamen ersetzen (2 Stellen: `<Repository>` und `<Registry>`).
2. Unraid → **Docker → Add Container**, die Vorlage verwenden (Template-XML-Inhalt einfügen
   bzw. die Datei in `/boot/config/plugins/dockerMan/templates-user/` ablegen).
3. Pflichtfelder setzen:
   - **SAVEVAULT_TOKEN** – lange Zufallszeichenkette (Master-Token; ohne ihn verweigert der
     Server alle API-Aufrufe).
   - **Datenverzeichnis** – z. B. `/mnt/user/appdata/savevault` (persistenter Speicher).
   - **Port** – Standard `8420`.
4. Starten → Dashboard unter `http://<UNRAID-IP>:8420/` (mit dem Master-Token anmelden).

> Zugriff bleibt LAN-only; von unterwegs über dein bestehendes VPN ins Heimnetz.

## Client: auf einem Windows-PC installieren

Der Workflow [`.github/workflows/client-release.yml`](../.github/workflows/client-release.yml)
baut bei einem `v*.*.*`-Tag den WPF-Tray-Client **self-contained** (kein .NET auf dem PC nötig),
**bündelt ludusavi** und hängt `SaveVault-Client-<version>-win-x64.zip` an das GitHub-Release.

1. Release-ZIP herunterladen (Repo → *Releases*) und in einen festen Ordner entpacken
   (z. B. `%LOCALAPPDATA%\Programs\SaveVault`).
2. `SaveVault.Client.exe` starten – erscheint als Tray-Symbol.
3. **Einrichten** (Einstellungen im Tray-Fenster): Server-URL `http://<UNRAID-IP>:8420`,
   Gerätename, dann **Pairing-Code** aus dem Web-Dashboard (Einstellungen → Netzwerk & Pairing)
   eingeben und koppeln. Danach synchronisiert der Client automatisch.
4. Optional Autostart: eine Verknüpfung zu `SaveVault.Client.exe` in den Autostart-Ordner legen
   (`shell:startup`).

> Lokaler Build ohne CI: `dotnet publish src/SaveVault.Client -c Release -r win-x64 --self-contained`
> – solange `tools/ludusavi/ludusavi.exe` vorliegt, wird sie mitgebündelt.
