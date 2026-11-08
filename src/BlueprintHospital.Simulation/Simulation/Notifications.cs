using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Medical;

namespace BlueprintHospital.Simulation.Simulation;

public enum NotificationLevel
{
    Info,
    Warning,
    Critical,
}

/// <summary>A message for the player, linked to the patient or room it is about.</summary>
public sealed record Notification(
    int Id,
    DateTime Time,
    NotificationLevel Level,
    string Text,
    int? PatientId = null,
    string? CareSpaceId = null,
    TriageCategory? Triage = null) : IGameEvent;

/// <summary>Keeps the most recent notifications and publishes each one on the event bus.</summary>
public sealed class NotificationLog(EventBus events, int capacity = 200)
{
    private readonly LinkedList<Notification> _items = new();
    private int _nextId = 1;

    public IEnumerable<Notification> Recent => _items;

    public int Count => _items.Count;

    public Notification Add(DateTime time, NotificationLevel level, string text, int? patientId = null, string? spaceId = null, TriageCategory? triage = null)
    {
        var n = new Notification(_nextId++, time, level, text, patientId, spaceId, triage);
        _items.AddFirst(n);
        while (_items.Count > capacity)
        {
            _items.RemoveLast();
        }

        events.Publish(n);
        return n;
    }
}
