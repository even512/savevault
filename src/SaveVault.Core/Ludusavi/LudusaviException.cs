namespace SaveVault.Core.Ludusavi;

/// <summary>Fehler beim Aufruf oder Parsen von ludusavi.</summary>
public class LudusaviException : Exception
{
    public LudusaviException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Die ludusavi-Binary wurde am erwarteten Pfad nicht gefunden. Eigener Typ, damit der
/// Aufrufer den „nicht eingerichtet"-Zustand sauber (ohne Absturz) behandeln kann.
/// </summary>
public sealed class LudusaviNotAvailableException : LudusaviException
{
    public string ExpectedPath { get; }

    public LudusaviNotAvailableException(string expectedPath)
        : base($"ludusavi-Binary nicht gefunden: {expectedPath}")
        => ExpectedPath = expectedPath;
}
