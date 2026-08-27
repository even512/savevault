namespace SaveVault.Client.Ui;

/// <summary>Menschlich lesbare Byte-Größen für die Anzeige (z. B. „142 MB").</summary>
public static class ByteSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "—";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{(long)value} {Units[unit]}"
            : $"{value:0.#} {Units[unit]}";
    }
}
