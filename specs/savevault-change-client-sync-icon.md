# SaveVault — Delta-Spec: Sync-Icon bei Client-Karten (Rotation + Sichtbarkeit)

## Ziel
Zwei Korrekturen am Sync-Icon der Geräte-Karten im Spiel-Detailpanel (`Clients`-Abschnitt,
Server 1.5.4-Redesign): (1) das rotierende Icon dreht sich sauber um die eigene Achse statt
schief zu wackeln, (2) Icon + grüner Rahmen/Glow erscheinen nur noch bei Geräten, die den
geteilten Speicherstand tatsächlich nutzen — alle anderen Geräte-Karten bleiben normal
(Status-Punkt statt Icon, kein Glow).

## Root-Cause (per Laufzeit-Messung im Notebook verifiziert)

**1. Rotations-Schiefstand:** `.client-card2__sync-icon` (styles.css:461) setzt Breite/Höhe
13px auf dem `<span>`, rotiert aber via `.spin` (styles.css:462) direkt diesen Span. Das per
`iconEl()` eingefügte `<svg>` bekommt KEINEN eigenen zentrierenden Wrapper (anders als beim
funktionierenden Vorbild `.shared-card__icon`, styles.css:415-418, das per
`display:flex;align-items:center;justify-content:center` zentriert). Ohne diese Zentrierung
sitzt das SVG als Inline-Replaced-Element mit Baseline-Lücke NICHT mittig im 13×13-Box: per
`getBoundingClientRect()`-Messung im echten Dashboard lag das `<svg>` um **+4.49px vertikal /
+1.0px horizontal** aus der Mitte seines eigenen `<span>` verschoben. Da die Rotation um die
Mitte der `<span>`-Box läuft, dreht sich das sichtbare Icon dadurch sichtbar exzentrisch
("schief") statt um seine eigene optische Achse.
**Fix:** `.client-card2__sync-icon` bekommt dieselbe Zentrierungstechnik wie
`.shared-card__icon` (`display:flex;align-items:center;justify-content:center`), sodass das
SVG unabhängig von seiner Inline-Baseline exakt mittig sitzt.

**2. Icon/Glow unabhängig von echter Teilnahme:** `buildClientCard()` (app.js:1625-1663)
berechnet `isSynced` bisher nur aus `bucket.status === "Synced"` — dem generischen
Sync-Status des PRIVATEN Buckets dieses Geräts. Ein Gerät, das dieses Spiel nie geteilt hat
(reiner lokaler Spielstand, nie über `JoinTakeSharedAsync` beigetreten), bekommt für seinen
privaten Bucket ebenfalls `"Synced"`, sobald keine Änderung mehr aussteht — unabhängig davon,
ob es je den geteilten Stand nutzt. Per Laufzeit-Test (2 Geräte, 1 kanonisches Spiel, Gerät A
tritt dem geteilten Stand bei, Gerät B bleibt rein lokal) zeigten **beide** Karten Icon + Glow,
obwohl nur Gerät A den geteilten Stand nutzt.
**Datengrundlage für den Fix:** `/api/game-states` (`state.data.gameStates`, app.js:448) wird
bereits bei jedem Laden geholt, aktuell aber nirgends verwendet — laut Server-Doc-Kommentar
(`VaultStore.GetGameStatesAsync`) exakt "fürs Spiel-Drawer des Dashboards" gedacht. Jeder
Eintrag trägt `deviceId` + den rohen Bucket-Schlüssel (`game.value`), den das Gerät zuletzt per
Heartbeat/Upload gemeldet hat. Ein Gerät, das aktiv den geteilten Stand nutzt, meldet dort
`game.value === sharedBucket.game.value` (verifiziert: nach `ReplaceLocalContentAsync`/Upload
unter `scope=shared` entsteht genau so ein Eintrag). Reine Lokal-Geräte haben KEINEN Eintrag
gegen den Shared-Schlüssel.
**Fix:** In `openGameDrawer()` die Menge der Geräte-IDs bilden, die laut `gameStates` aktuell
gegen `sharedBucket.game.value` melden, und an `clientsSection`/`buildClientCard`
durchreichen. `isSynced` wird zusätzlich an `sharedParticipants.has(bucket.ownerDeviceId)`
geknüpft. Ohne geteilten Stand (`sharedBucket` undefined) ist die Menge leer — alle
Geräte-Karten zeigen dann normal den Status-Punkt, wie bisher im Leerzustand.

## Umfang
- **Betrifft:** `src/SaveVault.Server/wwwroot/app.js` (Teilnehmer-Ermittlung + Weiterreichen),
  `src/SaveVault.Server/wwwroot/styles.css` (`.client-card2__sync-icon`-Zentrierung).
- **Nicht-Umfang:** Kein Backend-/API-Vertragswechsel (die genutzte `/api/game-states`-Route
  existiert bereits unverändert). Keine Änderung an `.shared-card__icon` (Karte oben, bereits
  korrekt). Keine Änderung am Client (`SaveVault.Client`).

## Betroffene Dateien (Schätzung)
- `src/SaveVault.Server/wwwroot/app.js` — `openGameDrawer`, `clientsSection`, `buildClientCard`.
- `src/SaveVault.Server/wwwroot/styles.css` — `.client-card2__sync-icon`.

## Akzeptanz & Verifikation
- Rotierendes Icon bei einer Client-Karte mit echtem geteilten Stand dreht sich sichtbar sauber
  um seine eigene Mitte (per `getBoundingClientRect()`-Messung: SVG-Offset ≈ 0,0 relativ zu
  seiner `<span>`-Box, statt zuvor +4.49/+1.0px).
- Laufzeit-Test wie im Notebook durchgeführt (2 gepairte Geräte, 1 kanonisches Spiel, Gerät A
  tritt dem geteilten Stand bei und synct gegen ihn, Gerät B bleibt rein privat/lokal): nur
  Gerät A zeigt Icon + grünen Rahmen/Glow, Gerät B zeigt den normalen Status-Punkt.
- Randfall Leerzustand (kein geteilter Stand für das Spiel): alle Client-Karten zeigen normal
  den Status-Punkt (unverändert zum Ist-Zustand).
- `dotnet build` 0 Fehler, `dotnet test` unverändert grün (reine Frontend-Änderung, keine
  neuen Core-Tests nötig).

## Risiken / Rückwärtskompatibilität
- Rein kosmetisch/Frontend, kein Migrations-/Datenrisiko. Einziges Risiko: falls ein Gerät die
  App-alte Version nutzt und daher nie `scope=shared` meldet, obwohl es im Client als "geteilt"
  markiert ist — dann zeigt die Karte fälschlich "normal" statt Icon. Das ist der bestehenden
  Server-Sicht angemessen (der Server kennt den Freigabe-Zustand ausschließlich über tatsächlich
  gemeldete Bucket-Zugriffe, nicht über einen expliziten "ist geteilt"-Flag) und kein Rückschritt
  gegenüber dem Ist-Zustand.
