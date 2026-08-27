using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveVault.Core.Serialization;

/// <summary>
/// Gemeinsame JSON-Optionen für den API-Vertrag, damit Server und Client identisch
/// serialisieren. Enums als Strings (stabiler Vertrag), camelCase (Web-Default),
/// null-Werte beim Schreiben ausgelassen.
/// </summary>
public static class SaveVaultJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Vorkonfigurierte, gemeinsam nutzbare Instanz (thread-safe für Lesen).</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();
}
