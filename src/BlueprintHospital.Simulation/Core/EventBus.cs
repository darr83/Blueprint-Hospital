namespace BlueprintHospital.Simulation.Core;

/// <summary>Marker interface for anything sent through the <see cref="EventBus"/>.</summary>
public interface IGameEvent;

/// <summary>
/// Typed publish/subscribe hub. Managers talk to each other through events instead of
/// holding references to one another, which keeps systems loosely coupled.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    /// <summary>Subscribes to events of type <typeparamref name="T"/>. Dispose the result to unsubscribe.</summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            list = new List<Delegate>();
            _handlers[typeof(T)] = list;
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    /// <summary>Delivers an event synchronously to every current subscriber of its exact type.</summary>
    public void Publish<T>(T gameEvent) where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
        {
            return;
        }

        // Copy so handlers may subscribe or unsubscribe while the event is being delivered.
        foreach (var handler in list.ToArray())
        {
            ((Action<T>)handler)(gameEvent);
        }
    }

    public int SubscriberCount<T>() where T : IGameEvent =>
        _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}

public sealed record SimulationTicked(DateTime Now, long TickCount) : IGameEvent;

public sealed record NewDayStarted(DateTime Date) : IGameEvent;

public sealed record SimulationSpeedChanged(SimulationSpeed Previous, SimulationSpeed Current) : IGameEvent;
