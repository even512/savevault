using SaveVault.Server.Realtime;

namespace SaveVault.Core.Tests;

/// <summary>
/// Prüft den Live-Aktualisierungs-Vermittler des Dashboards: ein Ereignis erreicht alle
/// Abonnenten, ein abgemeldeter bekommt nichts mehr, und ein langsamer/voller Abonnent
/// blockiert die anderen nicht (begrenzter Kanal, DropOldest).
/// </summary>
public class DashboardEventHubTests
{
    [Fact]
    public void Publish_reaches_all_subscribers()
    {
        var hub = new DashboardEventHub();
        using var a = hub.Subscribe();
        using var b = hub.Subscribe();

        hub.Publish("presence");

        Assert.True(a.Reader.TryRead(out var ea));
        Assert.True(b.Reader.TryRead(out var eb));
        Assert.Equal("presence", ea.Type);
        Assert.Equal("presence", eb.Type);
        Assert.False(string.IsNullOrWhiteSpace(ea.TimestampUtc));
    }

    [Fact]
    public void Unsubscribe_stops_delivery_and_completes_reader()
    {
        var hub = new DashboardEventHub();
        var sub = hub.Subscribe();
        Assert.Equal(1, hub.SubscriberCount);

        sub.Dispose();
        Assert.Equal(0, hub.SubscriberCount);

        hub.Publish("games"); // darf den abgemeldeten Kanal nicht mehr erreichen
        Assert.False(sub.Reader.TryRead(out _));
        Assert.True(sub.Reader.Completion.IsCompleted);
    }

    [Fact]
    public void Publish_without_subscribers_is_noop()
    {
        var hub = new DashboardEventHub();
        var ex = Record.Exception(() => hub.Publish("devices"));
        Assert.Null(ex);
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public void Full_subscriber_does_not_block_others()
    {
        var hub = new DashboardEventHub();
        using var slow = hub.Subscribe(); // liest NIE → läuft in die Kapazitätsgrenze (DropOldest)
        using var fast = hub.Subscribe();

        // Weit mehr Ereignisse als die Kanalkapazität – darf weder werfen noch blockieren.
        for (int i = 0; i < 500; i++)
            hub.Publish("games");

        // Der schnelle Abonnent bekommt weiterhin Ereignisse (die jüngsten).
        Assert.True(fast.Reader.TryRead(out var e));
        Assert.Equal("games", e.Type);
        // Beide Abonnenten leben noch (kein Abwurf durch Überlauf).
        Assert.Equal(2, hub.SubscriberCount);
    }
}
