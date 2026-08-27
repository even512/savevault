namespace SaveVault.Client.Ui;

/// <summary>
/// Formatiert UTC-Zeitpunkte als kurze, deutsche Relativangabe („vor 3 Min.", „gestern")
/// für die Statusanzeige. Robust gegen zukünftige/ungültige Werte.
/// </summary>
public static class RelativeTime
{
    /// <summary>Relative Angabe zu einem UTC-Zeitpunkt (oder „—", wenn keiner vorliegt).</summary>
    public static string Format(DateTime? utc, DateTime? nowUtc = null)
    {
        if (utc is null)
            return "—";

        var now = nowUtc ?? DateTime.UtcNow;
        var delta = now - utc.Value;

        if (delta < TimeSpan.Zero)
            return "gerade eben";
        if (delta.TotalSeconds < 45)
            return "gerade eben";
        if (delta.TotalMinutes < 60)
        {
            var m = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
            return $"vor {m} Min.";
        }
        if (delta.TotalHours < 24)
        {
            var h = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return h == 1 ? "vor 1 Std." : $"vor {h} Std.";
        }
        if (delta.TotalDays < 2)
            return "gestern";
        if (delta.TotalDays < 7)
            return $"vor {(int)delta.TotalDays} Tagen";

        // Ältere Zeitpunkte als lokales Datum anzeigen.
        return utc.Value.ToLocalTime().ToString("dd.MM.yyyy");
    }
}
