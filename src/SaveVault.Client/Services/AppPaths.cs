using System.IO;

namespace SaveVault.Client.Services;

/// <summary>
/// Leitet die lokalen Ablagepfade des Clients unter <c>%AppData%\SaveVault</c> ab.
/// Das Wurzelverzeichnis ist injizierbar, damit die Services in Schritt 7 gegen ein
/// temporäres Verzeichnis getestet werden können (keine echte %AppData%-Abhängigkeit).
/// </summary>
public sealed class AppPaths
{
    /// <summary>Wurzelverzeichnis aller lokalen Client-Daten.</summary>
    public string Root { get; }

    public AppPaths(string? root = null)
    {
        Root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveVault")
            : Path.GetFullPath(root);
    }

    /// <summary>Datei mit der Client-Konfiguration (Server-URL, Token, Gerätename, Intervall).</summary>
    public string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>Verzeichnis mit je-Save-Set-Sync-State-Dateien.</summary>
    public string StateDirectory => Path.Combine(Root, "state");

    /// <summary>Datei mit der Ordner-Zuordnung (Spiel → lokaler Ordner).</summary>
    public string FolderRegistryFile => Path.Combine(Root, "folders.json");

    /// <summary>Datei mit den vom Sync ausgeschlossenen Spielen (Menge von <c>GameKey.Value</c>).</summary>
    public string ExclusionsFile => Path.Combine(Root, "excluded.json");

    /// <summary>Datei mit den geräteübergreifend geteilten Spielen („Synchron"; Menge von <c>GameKey.Value</c>).</summary>
    public string SharedGamesFile => Path.Combine(Root, "shared.json");

    /// <summary>Verzeichnis für den lokalen Box-Art-/Cover-Cache (verwerfbar).</summary>
    public string CoverCacheDirectory => Path.Combine(Root, "covers");

    /// <summary>Dauerhaftes, rollierendes Diagnose-Log der Sync-Entscheidungen (siehe <see cref="SyncDiagnosticsLog"/>).</summary>
    public string SyncLogFile => Path.Combine(Root, "sync.log");

    /// <summary>
    /// Marker-Datei der einmaligen Migration auf geräte-eigene Buckets (siehe
    /// <see cref="SyncStateStore.MigrateToPerDeviceBucketsIfNeeded"/>). Bewusst
    /// <b>unabhängig</b> von <see cref="ConfigFile"/> und außerhalb des State-Verzeichnisses:
    /// Die Migrations-Entscheidung muss den Verlust einer <c>config.json</c> überleben —
    /// das Config-Flag allein hat früher den destruktiven Reset fälschlich erneut
    /// ausgelöst (Regression, CHECKPOINT.md 2026-09-24).
    /// </summary>
    public string PerDeviceBucketsMigrationMarker => Path.Combine(Root, "per-device-buckets-migrated");
}
