using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveVault.Core.Ludusavi;

// ---------------------------------------------------------------------------------
// Schema VERIFIZIERT gegen ludusavi 0.31.0 (Laufzeit-Gate, 2026-08-27):
//   `find --api`            → { "games": { "<name>": { "score": null } } }
//   `backup --preview --api`→ { "overall": {...}, "games": { "<name>": {
//                                 "decision", "change", "files": {
//                                   "<absoluter Pfad>": { "change", "bytes" } } } } }
//   (Preview liefert je Datei nur `change`+`bytes`; `hash`/`failed` erst beim echten
//    Backup → hier Defaults.) Feldnamen/Struktur decken sich mit den DTOs unten.
// Deserialisierung bleibt bewusst defensiv (versions-tolerant):
//   * PropertyNameCaseInsensitive im JsonOptions (siehe LudusaviClient),
//   * jede Ebene hat [JsonExtensionData] Extra → unbekannte Felder werden ignoriert
//     (z. B. `change`/`processedGames`/`changedGames`),
//   * fehlende Felder bleiben auf ihren Defaults.
// ---------------------------------------------------------------------------------

/// <summary>Ergebnis von <c>ludusavi --api find</c>: die gefundenen Spiele (Key = Name).</summary>
public sealed class LudusaviFindResult
{
    [JsonPropertyName("games")]
    public Dictionary<string, LudusaviFoundGame> Games { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Details zu einem gefundenen Spiel (find liefert i. d. R. nur den Namen als Key).</summary>
public sealed class LudusaviFoundGame
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Ergebnis von <c>ludusavi --api backup --preview</c>.</summary>
public sealed class LudusaviBackupPreview
{
    [JsonPropertyName("overall")]
    public LudusaviOverall? Overall { get; set; }

    [JsonPropertyName("games")]
    public Dictionary<string, LudusaviGameBackup> Games { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LudusaviOverall
{
    [JsonPropertyName("totalGames")]
    public int TotalGames { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LudusaviGameBackup
{
    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("files")]
    public Dictionary<string, LudusaviFile> Files { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LudusaviFile
{
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("failed")]
    public bool Failed { get; set; }

    [JsonPropertyName("change")]
    public string? Change { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
