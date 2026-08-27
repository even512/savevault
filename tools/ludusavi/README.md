# tools/ludusavi

Hier gehört die `ludusavi`-Binary hinein, die der Client für die automatische
Spielerkennung als Subprozess aufruft (im `--api`-JSON-Modus).

- Quelle: https://github.com/mtkennerly/ludusavi/releases (MIT-Lizenz)
- Windows: `ludusavi.exe` in diesen Ordner legen.
- Die Binary wird per `.gitignore` **nicht** eingecheckt (Größe); der Build kopiert
  sie in die Client-Ausgabe bzw. der Client sucht sie zur Laufzeit hier.

> Der Aufruf erfolgt mit **fester** Binary und **festen** Argumenten — nie mit aus
> Spiel-/Ordnernamen zusammengesetzten Shell-Strings (siehe Sicherheitsflächen der Spec).
