using BlueprintHospital.Simulation.Core;

namespace BlueprintHospital.Simulation.Tests;

public class EventBusTests
{
    private sealed record TestEvent(int Value) : IGameEvent;

    private sealed record OtherEvent : IGameEvent;

    [Fact]
    public void Delivers_only_to_matching_subscribers()
    {
        var bus = new EventBus();
        var received = new List<int>();
        var other = 0;
        bus.Subscribe<TestEvent>(e => received.Add(e.Value));
        bus.Subscribe<OtherEvent>(_ => other++);

        bus.Publish(new TestEvent(7));

        Assert.Equal(new[] { 7 }, received);
        Assert.Equal(0, other);
    }

    [Fact]
    public void Disposing_subscription_unsubscribes()
    {
        var bus = new EventBus();
        var count = 0;
        var sub = bus.Subscribe<TestEvent>(_ => count++);
        bus.Publish(new TestEvent(1));
        sub.Dispose();
        sub.Dispose();
        bus.Publish(new TestEvent(2));

        Assert.Equal(1, count);
        Assert.Equal(0, bus.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void Handler_may_unsubscribe_during_delivery()
    {
        var bus = new EventBus();
        var calls = 0;
        IDisposable? sub = null;
        sub = bus.Subscribe<TestEvent>(_ =>
        {
            calls++;
            sub!.Dispose();
        });
        bus.Subscribe<TestEvent>(_ => calls++);

        bus.Publish(new TestEvent(1));
        bus.Publish(new TestEvent(2));

        Assert.Equal(3, calls);
    }
}
