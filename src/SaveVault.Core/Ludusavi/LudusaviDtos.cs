using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveVault.Core.Ludusavi;

// ---------------------------------------------------------------------------------
// ACHTUNG – am Laufzeit-Gate gegen die Realität zu verifizieren:
// Diese DTOs bilden das `ludusavi ... --api`-JSON nach bestem aktuellen Wissen nach
// (Ausgabe von `find` und `backup --preview`). Das EXAKTE Schema kann je nach
// ludusavi-Version abweichen. Deshalb ist die Deserialisierung bewusst defensiv:
//   * PropertyNameCaseInsensitive im JsonOptions (siehe LudusaviClient),
//   * jede Ebene hat [JsonExtensionData] Extra → unbekannte Felder werden ignoriert,
//   * fehlende Felder bleiben auf ihren Defaults.
// Vor dem Produktiv-Verlass MUSS ein echter Aufruf gegen die mitgelieferte Binary
// die Feldnamen/Struktur bestätigen (Bau-Plan-Schritt „Laufzeit-Gate").
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
