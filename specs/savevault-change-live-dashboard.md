# Delta-Spec: Live-Dashboard (Echtzeit-Aktualisierung per Server-Push)

**Projekt:** SaveVault · **Weg:** `/projekt-edit` · **Datum:** 2026-09-03
**Betrifft:** Server (neuer Endpunkt + Event-Hub) + Dashboard-Frontend (`wwwroot`).
**Kein** Windows-Client-Code in Phase 1 → reiner Server-Deploy, kein Client-Update nötig.

> **Phasen:** **Phase 1 (unten, Umfang 1–7)** = Live-Dashboard per Server-Push, reine
> Server-/Dashboard-Änderung. **Phase 2 (Abschnitt 6)** = Client-Heartbeat-Frequenz senken
> für schnellere Präsenz — **eigenständige Phase, nur bei ausreichend 5h-Budget** nach
> Phase 1; erfordert ein Windows-Client-Update auf allen Geräten.

---

## 1. Ziel

Das Web-Dashboard aktualisiert seine Daten **von selbst und nahezu verzögerungsfrei**.
Heute lädt es nur einmal beim Login bzw. beim Klick auf „Aktualisieren"; Statusänderungen
(Client verbunden/offline, neuer Upload, Konflikt, Wiederherstellung …) werden erst nach
einem manuellen Reload (STRG+F5) sichtbar. Künftig schiebt der Server jede relevante
Änderung aktiv ans Dashboard, das daraufhin die betroffenen Daten neu lädt und rendert —
ohne Zutun des Nutzers.

## 2. Umfang / Nicht-Umfang

### Umfang
1. **Server-Event-Hub (in-memory, Singleton):** `DashboardEventHub` mit `Publish(...)`
   und Subscribe/Unsubscribe pro offener Verbindung (thread-sicher; ein bounded
   `Channel<…>` je Abonnent). Reiner Prozess-Speicher — passt zum Ein-Prozess-Container
   und Ein-Admin-Betrieb.
2. **SSE-Endpunkt `GET /api/events`** (master-only): liefert `text/event-stream`,
   registriert einen Abonnenten und streamt Ereignisse (`event: <typ>\ndata: <json>\n\n`).
   Sende-Absicherung: Keep-Alive-Kommentar (`: ping`) alle ~15 s gegen Idle-Timeouts von
   Proxys; sauberes Abmelden bei Verbindungsabbruch (`RequestAborted`).
3. **Verdrahtung der Mutationspunkte:** nach jeder zustandsändernden Operation wird ein
   **grobes** Ereignis veröffentlicht (Typ als Hinweis, kein sensibler Payload nötig):
   - Heartbeat empfangen → `presence`
   - Revision finalisiert / Upload vollständig → `games`
   - Konflikt entstanden bzw. gelöst → `conflicts`
   - Restore ausgelöst → `games`
   - Pairing eingelöst → `devices`
   - Teilen etabliert / Legacy gelöscht → `games`
   Veröffentlicht wird **aus der Endpunkt-Schicht** (nach dem `VaultStore`-Aufruf), damit
   der `VaultStore` unangetastet bleibt (kleiner Blast-Radius).
4. **Frontend — Live-Verbindung:** das Dashboard öffnet `/api/events` per **`fetch` mit
   Streaming-Reader** (`response.body.getReader()`), nicht per `EventSource` — so bleibt
   der Session-Token im `Authorization`-Header (nie in der URL, konsistent zur bestehenden
   Cover-/Export-Logik). Auf jedes Ereignis → **entprelltes** `loadAll()` + Re-Render
   (kein Doppellauf, wenn gerade ein manueller Refresh läuft). Automatischer
   **Reconnect mit Backoff** bei Abbruch/Fehler.
5. **Frontend — lokaler Re-Render-Takt:** ein Timer (~10–15 s) rendert die aktuelle Ansicht
   neu, damit **zeitabhängige** Anzeigen ohne Server-Ereignis „altern": relative Zeiten
   („vor X Min.") und der aus `lastSeenUtc` abgeleitete **Offline-Status** kippen live,
   sobald ein Client keine Heartbeats mehr sendet (Offline ist per Definition ein
   Ausbleiben — kein Server-Ereignis, daher client-seitig getaktet).
6. **Frontend — Fallback:** schlägt der Stream grundsätzlich fehl (alter Browser/Proxy ohne
   Streaming-`fetch`), fällt das Dashboard automatisch auf **periodisches Polling**
   (`loadAll()` alle ~8 s) zurück, damit es sich trotzdem selbst aktualisiert.
7. Der **manuelle „Aktualisieren"-Button bleibt** unverändert erhalten.

### Nicht-Umfang von Phase 1 (ausdrücklich)
- **Kein** Ändern der Client-Heartbeat-Frequenz in Phase 1 (Default 60 s). „Verbunden"
  erscheint in Phase 1 also weiterhin frühestens beim nächsten Heartbeat des Clients — das
  ist die Grundgranularität der Präsenz. Die schnellere Präsenz kommt in **Phase 2**
  (Abschnitt 6), weil sie ein Windows-Client-Update auf allen Geräten erfordert.
- **Kein** feingranulares Delta-Protokoll (der Server schickt „etwas in Kategorie X hat sich
  geändert", das Dashboard lädt die betroffenen Endpunkte neu — kein Diff-Streaming der
  einzelnen Datensätze). Reicht für „fühlt sich live an", hält die Fläche klein.
- **Kein** WebSocket (SSE/Streaming-`fetch` genügt für reinen Server→Client-Push).
- **Kein** Persistieren/Nachliefern verpasster Ereignisse (bei Reconnect wird einfach einmal
  voll nachgeladen — der `loadAll()`-Vollstand ist die Wahrheit).
- **Kein** Zugriff für Geräte-Token (der Stream ist master-only wie `/devices`, `/activity`).

## 3. Betroffene Dateien (Schätzung)

**Server**
- `src/SaveVault.Server/Realtime/DashboardEventHub.cs` — **neu** (Hub + Abonnenten-Kanäle).
- `src/SaveVault.Server/Endpoints/SaveVaultEndpoints.cs` — SSE-Endpunkt `GET /api/events`;
  `Publish(...)`-Aufrufe nach den Mutationen (Heartbeat, Register/Finalize, Restore,
  Resolve, Pair, Share, Delete-Legacy).
- `src/SaveVault.Server/Program.cs` — `DashboardEventHub` als Singleton registrieren.
- `src/SaveVault.Server/SaveVault.Server.csproj` — Version 1.3.0 → **1.4.0**.
- ggf. `src/SaveVault.Core/Api/ApiRoutes.cs` — Route-Konstante `/api/events` (Stil-Konsistenz).

**Frontend**
- `src/SaveVault.Server/wwwroot/app.js` — Live-Verbindung (Streaming-`fetch`), Reconnect,
  entprellter Refresh, lokaler Re-Render-Timer, Polling-Fallback; Start beim `startApp()`,
  Stopp bei Logout/Auth-Fehler.

**Doku**
- `CHANGELOG.md` (nutzergerichtete Änderung), `CHECKPOINT.md` (neuer Block).

## 4. Akzeptanz & Verifikation

**Funktional (Laufzeit-Smoke gegen den echten Server):**
1. Dashboard offen lassen (kein Reload). Ein zweiter Client sendet einen Heartbeat →
   der Client erscheint **innerhalb ~1–2 s** als „Verbunden", ohne STRG+F5.
2. Ein Upload/eine neue Revision erscheint live in Spiele-Kachel/Verlauf ohne Reload.
3. Ein Client sendet keine Heartbeats mehr → er kippt binnen des Re-Render-Takts
   (~≤15 s) sichtbar auf „Offline"; die relativen Zeiten altern mit.
4. `GET /api/events` mit **Geräte-Token → 403**, ohne Token → 401, mit Master → 200 +
   `text/event-stream` (Header korrekt, Stream bleibt offen, Keep-Alive kommt).
5. Verbindung hart trennen (Server-Neustart / Netz weg) → das Dashboard verbindet sich
   automatisch neu und lädt einmal voll nach; keine Fehlerflut, keine Endlosschleife.
6. Zwei Tabs gleichzeitig offen → beide bekommen Ereignisse; Schließen eines Tabs meldet
   dessen Abonnenten sauber ab (kein Leak).

**Technisch:**
- `dotnet build SaveVault.sln` → 0 Fehler.
- `dotnet test` → grün (Bestand hält; neue Server-Logik im Hub bekommt gezielte Unit-Tests:
  Publish erreicht mehrere Abonnenten, Unsubscribe stoppt die Zustellung, ein voller/
  geschlossener Abonnenten-Kanal blockiert die anderen nicht).
- `node --check` (bzw. Syntaxprüfung) für `app.js`.

## 5. Risiken / Rückwärtskompatibilität

- **Neue Netz-/Streaming-Fläche → `/security-review` erforderlich.** Prüfpunkte:
  master-only durchgesetzt; Token nur im Header (nie in URL/Log); langlebige Verbindung
  ressourcenbeschränkt (bounded Channel je Abonnent, sauberes Abmelden bei Disconnect,
  Keep-Alive statt unbegrenztem Puffer); kein Fremddaten-Payload, der XSS ermöglicht
  (Dashboard rendert weiter ausschließlich über die bestehende `loadAll()`+`textContent`-
  Pipeline, nie direkt aus dem Ereignis-Payload).
- **Kompatibilität:** rein additiv. Ältere Clients unberührt (kein Client-Vertrag geändert).
  Ein Dashboard ohne Streaming-Unterstützung nutzt den Polling-Fallback. Rollout =
  **nur Server neu deployen** (Dashboard liegt in `wwwroot`); kein Windows-Client-Update.
- **Last:** Ein Ereignis je Heartbeat (≈ 1×/60 s pro Gerät) + wenige Event-getriebene Fälle;
  Payloads winzig, Ein-Admin-Betrieb → vernachlässigbar. Grober Refresh statt Diff bewusst
  gewählt (Einfachheit vor Mikro-Optimierung).
- **Reihenfolge/Robustheit:** Entprellung verhindert Refresh-Stürme bei Ereignis-Bursts;
  Backoff verhindert Reconnect-Sturm bei Server-Ausfall.

---

## 6. Phase 2 — Client-Heartbeat-Frequenz (separat, budget-abhängig)

**Nur bauen, wenn nach Phase 1 genug 5h-Budget übrig ist.** Sonst als offener Folgeschritt
im Checkpoint vermerken.

**Ziel:** „Verbunden/Offline" im Dashboard reagiert schneller, weil der Client häufiger ein
Lebenszeichen sendet — heute an das Sync-Intervall (Default 60 s) gekoppelt.

**Ansatz (minimal-invasiv):** den **Heartbeat vom Sync-Intervall entkoppeln** und mit einem
eigenen, kürzeren Takt versehen (Zielwert ~10–15 s), statt den ganzen Sync-Zyklus (ludusavi,
Manifest, Upload) zu beschleunigen. Heartbeat ist eine schlanke Statusmeldung — häufig ist
billig; ein häufigerer Voll-Sync wäre teuer und unnötig.

**Betroffene Dateien (Schätzung):**
- `src/SaveVault.Client/Services/ClientConfig.cs` — neues Feld `HeartbeatIntervalSeconds`
  (Default ~15, Untergrenze absichern; `SyncIntervalSeconds` bleibt unberührt,
  rückwärtskompatibel — fehlt das Feld, greift der Default).
- `src/SaveVault.Client/Services/ClientAgent.cs` — den Heartbeat-Loop mit dem neuen,
  separaten Intervall starten (heute läuft er im Sync-Intervall).
- `src/SaveVault.Client/SaveVault.Client.csproj` — Client-Version-Bump.
- ggf. Einstellungen-UI, falls der Wert dort sichtbar/änderbar sein soll (sonst nur Config).

**Akzeptanz:** ein zwischen zwei Sync-Zyklen offline gegangener/zurückgekehrter Client wird
im Dashboard binnen ~15 s (statt bis 60 s) korrekt gezeigt; Voll-Sync-Verhalten/-Frequenz
unverändert; Build 0/0, Tests grün.

**Rollout:** erfordert (anders als Phase 1) ein **Client-Update auf allen Geräten**.

**Risiko:** gering — mehr, aber sehr kleine Heartbeat-Requests; keine neue Angriffsfläche
(bestehender authentifizierter Endpunkt). Kein `/security-review` nötig.
