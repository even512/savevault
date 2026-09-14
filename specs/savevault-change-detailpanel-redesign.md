# SaveVault — Delta-Spec: Spiel-Detailpanel neu gestalten (Server-Dashboard)

## Ziel
Das ausfahrbare Spiel-Detailpanel (rechter Drawer, öffnet aus der Spiele-Liste/-Kachel)
bekommt die Struktur/Optik aus `design-reference/Spiele Detailpanel.dc.html`: statt
gestapelter, textlastiger Bucket-Abschnitte eine **Karte „Geteilter Speicherstand"**
oben, darunter eine **Liste ausklappbarer Geräte-Karten** („Clients"). Reine
**Server-Dashboard-Änderung** (kein Windows-Client-Code, kein Rollout auf Geräten
nötig — nur der Server neu deployen).

## Umfang
- **Nur der Drawer-Körper** (`app.js#openGameDrawer` und die vormaligen
  `bucketSection`/`fillBucketSection`, komplett ersetzt). Der
  Drawer-Kopf (Cover, Titel, Meta-Zeile, Schließen-Button) bleibt wie er ist — er
  nutzt bereits echtes Cover-Art (IGDB), das ist besser als die Initialen-Kachel im
  Mockup und wird **nicht** durch sie ersetzt.
- **Geteilter-Speicherstand-Karte** ersetzt den bisherigen „Geteilt"-Bucket-Abschnitt:
  Icon + Status-Pille (wiederverwendet die bestehende `STATUS`-Zuordnung
  Synced/Syncing/Conflict/Pending/Offline/Error — **keine** neuen Status erfunden,
  d. h. das Mockup-Tristate „aktiv/inaktiv/keiner" wird auf **zwei** reale Zustände
  reduziert: *existiert* (Status/Kennzahlen/Versionsverlauf wie gehabt) und
  *existiert nicht* (Hinweistext + „Über Geräte teilen"-Button, wenn mind. ein
  privater Bucket zum Einsäen vorhanden ist — sonst kein Button). Felder:
  Herkunfts-Gerät (Gerät der ältesten/Erst-Revision), Zeitpunkt (neueste Revision),
  Größe/Dateien (aggregiert, wie bisher). Versionsverlauf wie im Mockup **komplett**
  hinter dem Umschalter versteckt (auch die neueste Version), nicht wie bisher mit
  stets sichtbarer neuester Version — das ist eine bewusste Abweichung vom
  bisherigen Verhalten, siehe „Risiken" unten.
  Konflikt auf dem geteilten Bucket → Banner **innerhalb** dieser Karte mit
  „Lösen"-Button (bestehender `openConflictModal`-Fluss, unverändert).
- **Clients-Liste** ersetzt die bisherigen „Lokal: <Gerät>"-Bucket-Abschnitte: eine
  ausklappbare Karte je **privatem Bucket** (`scope === "private"`, ein Bucket = ein
  Gerät). Kopf: Geräte-Icon (immer PC — SaveVault-Clients sind aktuell ausschließlich
  Windows, keine Konsolen-Unterscheidung), Name, Status-Punkt (Sync-Icon + dezenter
  Akzent-Glow, wenn `bucket.status === "Synced"`; sonst normaler Farbpunkt),
  Konflikt-Badge, letzte Aktivität, Chevron. **Nur eine Karte gleichzeitig
  ausgeklappt** (Akkordeon, wie im Mockup — Öffnen einer Karte klappt eine zuvor
  offene automatisch zu). Ausgeklappt: Status/Größe/Dateien/letzte-Änderung-Grid,
  bei Konflikt zusätzlich das Warnbanner **mit** „Lösen"-Button (Mockup zeigt nur
  Text — Button ergänzt, sonst wäre Konfliktlösung aus diesem Panel nicht mehr
  möglich), eigener Versionsverlauf (Export/Wiederherstellen unverändert).
- **Konflikt-Kopien** (Fork-Buckets, `isFork === true`): **Korrektur nach Laufzeit-Test**
  (siehe „Risiken") — Fork-Buckets bekommen server-seitig einen EIGENEN kanonischen
  Schlüssel (`{key}#conflict-{revision}`, `VaultStore.ResolveKeepBoth`) und erscheinen
  daher **nie** im `buckets`-Array eines anderen Spiels, sondern öffnen ihr **eigenes**
  Drawer (eigene Kachel in der Spiele-Liste, `scope==="shared"`, `isFork===true`, immer
  der einzige Bucket dieses kanonischen Schlüssels). Die ursprünglich geplante
  „dezente Zeile unterhalb der Client-Liste" war auf einer falschen Annahme aufgebaut
  und wurde **nicht gebaut** (wäre toter Code gewesen). Stattdessen eine eigene,
  einfache **Konflikt-Kopie-Karte** (`forkCard`, gleiches Karten-Layout wie die
  geteilte Karte, aber ohne Status-Pille/Glow/„Clients"-Abschnitt — sonst sähe ein
  eingefrorener Verlierer-Stand wie ein live synchroner Stand aus): Herkunfts-Gerät/
  Zeitpunkt/Größe/Dateien, Standard-Pfad, Versionsverlauf (Export/Wiederherstellen
  unverändert). Vom `/code-review high`-Pass gefunden, siehe „Risiken".
- **Betroffene Dateien:** `src/SaveVault.Server/wwwroot/app.js` (Drawer-Aufbau),
  `src/SaveVault.Server/wwwroot/styles.css` (neue/angepasste Klassen für Karte,
  Client-Karten, Trennlinie „Clients · N"). Farb-Tokens (`--accent`, `--synced`,
  `--conflict` usw.) werden **wiederverwendet**, keine neuen oklch-Werte — passt
  bereits zum Mockup-Akzent (Teal).

## Nicht-Umfang
- Kein Server-/API-Code (`SaveVault.Server` C#), keine neuen Endpunkte oder
  DTO-Felder — alle benötigten Daten (`GameSummary.status/totalBytes/fileCount/
  ownerDeviceId/isFork`, `RevisionInfo.deviceId/timestampUtc`, `conflicts`) liegen
  bereits vor.
- Kein Client-Code (`SaveVault.Client`, WPF) — die dortige Zwei-Kästen-Ansicht
  (`SpielstandSyncRedesign.dc.html`) ist bereits umgesetzt und unberührt.
- Keine neue Funktion „Teilen wieder ausschalten" oder „geteilt pausieren" — die
  Mockup-Zustände „inaktiv" entfallen ersatzlos (siehe oben), da es diese Funktion
  im Produkt nicht gibt.
- Die Variante `Spiele Detailpanel - Timeline-Konzept.dc.html` wird nicht angefasst.
- Keine Änderung an Spiele-Kacheln/-Liste, Client-Drawer, Konflikt-Modal,
  Sortierung/Filter — nur der Spiel-Detail-Drawer-Körper.

## Akzeptanz & Verifikation
- Build 0/0, bestehende Tests unverändert grün (reine Frontend-Änderung, keine
  C#-Logik berührt).
- **Laufzeit-Smoke** (lokaler Server, Browser) mit echten Testdaten:
  - Spiel mit geteiltem Stand + 2 Geräten, eines synced, eines im Konflikt: Karte
    zeigt korrekten Status/Kennzahlen, Versionsverlauf klappt auf/zu; Client-Liste
    zeigt beide Karten, Konflikt-Badge + Warnbanner + „Lösen" öffnet den
    bestehenden Konflikt-Dialog und löst real auf.
  - Spiel **ohne** geteilten Stand, mit genau einem privaten Bucket: leere Karte +
    „Über Geräte teilen"-Button funktioniert (bestehender Seed-Fluss).
  - Spiel mit einer Konflikt-Kopie (Fork): eigene Zeile erscheint, Export/
    Wiederherstellen funktionieren.
  - Öffnen einer zweiten Client-Karte klappt die erste zu (Akkordeon); Reload/
    erneutes Öffnen des Drawers setzt den Ausklapp-Zustand zurück (kein
    persistenter State — wie bisher beim „Ältere Versionen"-Toggle).
  - Responsives Verhalten unverändert (Drawer bleibt 460 px/92vw, wie bisher).
- `/code-review high` auf den Diff (Kernänderung): **3 Befunde → 2 behoben, 1 begründet
  abgelehnt.** Behoben: (1) Fork-Buckets öffneten fälschlich die „Geteilter
  Speicherstand"-Optik (Karte matchte auf `scope==="shared"`, ohne `isFork` zu
  prüfen) → eigene `forkCard`, real verifiziert. (2) Der Standard-Save-Pfad
  (`RevisionInfo.saveRoot`) ging beim Umbau ersatzlos verloren → wieder ergänzt
  (geteilte Karte, Client-Karten, Konflikt-Kopie-Karte), real verifiziert.
  Abgelehnt: neuer `kvCell()`-Helfer dupliziere den bestehenden `kv()` — beide
  haben aber unterschiedliche Optik (eigene, großgeschriebene Label-Zeile fürs
  Mockup-Layout vs. `kv()`s Fließtext-Label fürs Geräte-Info-Raster) und
  unterschiedliche Aufrufer/Rückgabewert (`kvCell` liefert zusätzlich `valueEl`
  fürs asynchrone Nachfüllen); ein Zusammenlegen hätte den bestehenden, an drei
  Stellen verwendeten `kv()` riskant verändert für einen rein kosmetischen Gewinn.
  Kein `/security-review` nötig (reine UI-Restrukturierung, keine sensible Fläche).

## Risiken / Rückwärtskompatibilität
- **Verhaltensänderung „Versionsverlauf initial komplett verborgen"** (auch die
  neueste Version) statt bisher „neueste Version immer sichtbar, nur Ältere
  eingeklappt" — folgt bewusst dem Mockup. Falls das im Handtest unpraktisch wirkt,
  ist die Umkehr (neueste Version weiterhin außerhalb des Toggles) eine kleine,
  risikoarme Anpassung im selben Delta.
- **Konflikt-Zuordnung je Client-Karte über Teilnehmerliste, nicht Bucket-Schlüssel.**
  Beim Laufzeit-Test (echter Server, 2 Geräte + Konflikt via `isConflict:true`-Upload
  gegen den geteilten Bucket) zeigte sich: ein `Conflict`-Datensatz hängt am
  (geteilten) Bucket, in dem er entdeckt wurde — NICHT an den privaten Buckets der
  beteiligten Geräte. Die ursprünglich geplante Zuordnung „Konflikt = exakter
  Bucket-Schlüssel-Treffer" hätte das Konflikt-Badge auf keiner Client-Karte je
  angezeigt. Behoben: Zuordnung über `conflict.participants[].deviceId` gegen
  `bucket.ownerDeviceId`, real verifiziert (beide Geräte zeigen „⚠ Konflikt",
  „Lösen" öffnet den bestehenden Konflikt-Dialog korrekt, Auflösen setzt beide
  Client-Karten und die geteilte Karte zurück auf „Synchronisiert").
- Kein Backend-Risiko, da rein deklarative Neuzusammensetzung bereits geladener
  Daten (`state.data.games/gameStates/conflicts`, gecachte Revisionen).
- Rollout: nur Server-Image neu bauen/deployen (kein Client-Update nötig).
