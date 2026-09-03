using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SaveVault.Server.Realtime;

/// <summary>
/// In-Memory-Vermittler für Live-Aktualisierungen des Web-Dashboards. Jede offene
/// SSE-Verbindung (<c>GET /api/events</c>) meldet sich mit einem <see cref="Subscribe"/>
/// an und bekommt einen eigenen, <b>begrenzten</b> Kanal; zustandsändernde Endpunkte rufen
/// <see cref="Publish"/> und verteilen so eine grobe „hat sich geändert"-Nachricht an alle
/// Abonnenten. Bewusst prozess-lokal: SaveVault läuft als Ein-Prozess-Container mit einem
/// Admin-Dashboard – kein verteilter Bus nötig.
///
/// Robustheit: Ein voller oder geschlossener Abonnenten-Kanal darf die anderen nie
/// blockieren. Deshalb ist jeder Kanal begrenzt (<see cref="BoundedChannelFullMode.DropOldest"/>)
/// und das Schreiben nicht-blockierend (<see cref="ChannelWriter{T}.TryWrite"/>) – im Zweifel
/// verliert ein langsamer Abonnent das älteste Ereignis, holt aber beim nächsten Voll-Refresh
/// ohnehin den ganzen Stand nach.
/// </summary>
public sealed class DashboardEventHub
{
    /// <summary>Ein einzelnes Live-Ereignis: grobe Kategorie + Serverzeit (UTC, ISO-8601).</summary>
    public readonly record struct DashboardEvent(string Type, string TimestampUtc);

    // Abonnenten-Kanäle; der Schlüssel ist die zurückgegebene Subscription (Identität zum Abmelden).
    private readonly ConcurrentDictionary<Subscription, byte> _subscribers = new();

    /// <summary>
    /// Meldet einen neuen Abonnenten an und liefert dessen Handle. Der Aufrufer liest
    /// <see cref="Subscription.Reader"/> und ruft am Ende (auch im Fehlerfall)
    /// <see cref="Subscription.Dispose"/>, um sich abzumelden.
    /// </summary>
    public Subscription Subscribe()
    {
        var channel = Channel.CreateBounded<DashboardEvent>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var sub = new Subscription(this, channel);
        _subscribers[sub] = 0;
        return sub;
    }

    /// <summary>
    /// Veröffentlicht ein Ereignis an alle Abonnenten (nicht-blockierend). <paramref name="type"/>
    /// ist eine grobe Kategorie (z. B. <c>presence</c>, <c>games</c>, <c>conflicts</c>,
    /// <c>devices</c>) – das Dashboard lädt daraufhin den betroffenen Stand neu.
    /// </summary>
    public void Publish(string type)
    {
        if (_subscribers.IsEmpty) return;
        var evt = new DashboardEvent(type, DateTime.UtcNow.ToString("o"));
        foreach (var sub in _subscribers.Keys)
            sub.Writer.TryWrite(evt);
    }

    /// <summary>Anzahl aktuell offener Abonnenten (für Tests/Diagnose).</summary>
    public int SubscriberCount => _subscribers.Count;

    private void Remove(Subscription sub)
    {
        if (_subscribers.TryRemove(sub, out _))
            sub.Writer.TryComplete();
    }

    /// <summary>
    /// Handle eines Abonnenten. <see cref="Reader"/> liefert die Ereignisse; <see cref="Dispose"/>
    /// meldet ab und schließt den Kanal.
    /// </summary>
    public sealed class Subscription : IDisposable
    {
        private readonly DashboardEventHub _hub;
        private readonly Channel<DashboardEvent> _channel;

        internal Subscription(DashboardEventHub hub, Channel<DashboardEvent> channel)
        {
            _hub = hub;
            _channel = channel;
        }

        internal ChannelWriter<DashboardEvent> Writer => _channel.Writer;

        /// <summary>Die eingehenden Ereignisse dieses Abonnenten.</summary>
        public ChannelReader<DashboardEvent> Reader => _channel.Reader;

        public void Dispose() => _hub.Remove(this);
    }
}
