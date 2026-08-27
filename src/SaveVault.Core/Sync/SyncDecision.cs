namespace SaveVault.Core.Sync;

/// <summary>Die vier möglichen Ausgänge der Sync-Entscheidung.</summary>
public enum SyncAction
{
    /// <summary>Nichts zu tun.</summary>
    NoOp,

    /// <summary>Lokale Änderung hochladen (neue Revision).</summary>
    Upload,

    /// <summary>Neuere Server-Revision herunterladen.</summary>
    Download,

    /// <summary>Echter Konflikt: beide Seiten geändert – nicht überschreiben.</summary>
    Conflict
}

/// <summary>Entscheidung der <see cref="SyncDecider"/> samt kurzer Begründung.</summary>
public sealed record SyncDecision(SyncAction Action, string Reason);
