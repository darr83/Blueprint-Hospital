using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Core;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Audio;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Transport;
using BlueprintHospital.UI;
using Godot;

namespace BlueprintHospital.Audio;

/// <summary>
/// All the hospital's sound. It watches the simulation and the camera each frame and plays:
/// background ambience; monitor beeps and alarms, X-ray and CT machines, and gibberish chatter
/// that get louder as you zoom in near them; a siren when an ambulance is on its way; a sad
/// sting when a patient dies; and interface sounds. It only reads the game, so it can be added
/// or removed without changing anything else.
/// </summary>
public partial class HospitalAudio : Node2D
{
    private const int MaxMonitorVoices = 5;
    private const int MaxOneShots = 14;
    private const double FadeSpeed = 3.0;

    private static readonly string[] Gibberish = { "gibberish_question", "gibberish_complaint", "gibberish_thanks", "gibberish_laugh" };
    private static readonly string[] Coughs = { "cough_1", "cough_2", "cough_3" };

    private readonly Dictionary<string, Loop> _loops = new();
    private readonly List<AudioStreamPlayer2D> _oneShots = new();
    private readonly Dictionary<string, double> _nextBeat = new();
    private readonly Dictionary<string, double> _nextAlarm = new();
    private readonly Dictionary<string, (int PatientId, double NextShot)> _xrayUse = new();
    private readonly HashSet<int> _redPatients = new();
    private readonly HashSet<int> _sirenPlayed = new();
    private readonly Dictionary<int, AmbulanceState> _ambulanceStates = new();
    private readonly Dictionary<string, double> _cooldowns = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Random _random = new();

    /// <summary>Prints every sound as it starts, when the game is run with -- --log-sounds.</summary>
    private static readonly bool LogSounds = OS.GetCmdlineUserArgs().Contains("--log-sounds");

    private GameSession _session = null!;
    private WorldLayer _world = null!;
    private AudioStreamPlayer _siren = null!;
    private double _time;
    private double _nextChatter = 3;
    private int _roomCount = -1;
    private int _furnitureCount = -1;
    private int _staffCount = -1;
    private long _lastBalance = long.MinValue;

    public void Initialise(GameSession session, WorldLayer world)
    {
        _session = session;
        _world = world;
        AudioSettings.Current.Apply();

        _siren = new AudioStreamPlayer { Bus = AudioSettings.EffectsBus };
        AddChild(_siren);

        var announcer = new Announcer();
        AddChild(announcer);
        _subscriptions.Add(session.Events.Subscribe<Announcement>(announcer.Enqueue));

        _subscriptions.Add(session.Events.Subscribe<PatientLeft>(OnPatientLeft));
        _subscriptions.Add(session.Events.Subscribe<Notification>(OnNotification));
        _subscriptions.Add(session.Events.Subscribe<NewDayStarted>(_ => PlayUi("new_day", 0.6)));

        // Ambulances already on the road when a save loads shouldn't all sound their sirens.
        foreach (var ambulance in session.Simulation.Ambulances)
        {
            _sirenPlayed.Add(ambulance.Id);
            _ambulanceStates[ambulance.Id] = ambulance.State;
        }

        // Every button in the game clicks, and every panel whooshes open.
        GetTree().NodeAdded += OnNodeAdded;
        HookButtons(GetTree().Root);
    }

    public override void _ExitTree()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        if (IsInstanceValid(this) && GetTree() is { } tree)
        {
            tree.NodeAdded -= OnNodeAdded;
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null)
        {
            return;
        }

        _time += delta;
        var zoom = GetViewport().GetCanvasTransform().X.X;

        UpdateAmbience(zoom);
        UpdateMonitors(zoom);
        UpdateRooms(zoom);
        UpdateMovement(zoom);
        UpdateAmbulances();
        UpdateRandomVoices(zoom);
        UpdateBuildingAndMoney();
        UpdateLoops(delta);
        _oneShots.RemoveAll(p => !IsInstanceValid(p));
    }

    // ---------------------------------------------------------------- ambience

    private void UpdateAmbience(float zoom)
    {
        var hour = _session.Clock.Now.Hour;
        var night = hour >= 22 || hour < 7;

        // Inside: steady, a little louder as you get closer; quieter overnight.
        var inside = (0.55 + (0.45 * SoundFalloff.ZoomGain(zoom, 0.3, 1.0))) * (night ? 0.55 : 1.0);
        WantFlat("amb_hospital_day", inside, AudioSettings.AmbienceBus);

        // Outside: the car park, mainly when you're zoomed right out.
        var outside = 0.6 * (1 - SoundFalloff.ZoomGain(zoom, 0.25, 0.7));
        WantFlat("amb_outside", outside, AudioSettings.AmbienceBus);
    }

    // ---------------------------------------------------------------- monitors

    private void UpdateMonitors(float zoom)
    {
        var candidates = new List<(string Key, Vector2 Pos, Patient Patient, int News2, double Gain)>();
        foreach (var monitor in _world.Monitors)
        {
            if (_world.ConnectedPatient(monitor.Space) is not { } patient)
            {
                continue;
            }

            var news2 = patient.LatestObservations?.News2.Total ?? 0;
            var pos = IsoDraw.ToScreen(monitor.X, monitor.Y);

            // Red alarms carry further: faintly audible even zoomed out.
            var gain = news2 >= 7
                ? Spatial(pos, zoom, 0.3, 0.9, floor: 0.3)
                : Spatial(pos, zoom, 0.45, 1.3);
            if (gain > 0.02)
            {
                candidates.Add(($"{monitor.Space.Id}:{monitor.X:0.0}:{monitor.Y:0.0}", pos, patient, news2, gain));
            }

            // A crash call the moment someone first becomes critically unwell.
            if (news2 >= 7 && _redPatients.Add(patient.Id))
            {
                if (OffCooldown("crash_call", 90))
                {
                    PlayAt("crash_call", pos, Math.Max(0.35, gain), AudioSettings.EffectsBus);
                }
            }
            else if (news2 < 7)
            {
                _redPatients.Remove(patient.Id);
            }
        }

        // Only the loudest few monitors play, alarms first, so a full ward doesn't become noise.
        foreach (var c in candidates.OrderByDescending(c => c.News2 >= 7).ThenByDescending(c => c.Gain).Take(MaxMonitorVoices))
        {
            var heartRate = c.Patient.CurrentObservations().HeartRate;
            var beatInterval = 60.0 / Math.Clamp(heartRate, 30, 180);
            if (!_nextBeat.TryGetValue(c.Key, out var nextBeat) || nextBeat < _time - 1)
            {
                nextBeat = _time + (_random.NextDouble() * beatInterval);
            }

            if (_time >= nextBeat)
            {
                PlayAt("monitor_beep", c.Pos, c.Gain * (c.News2 >= 7 ? 0.5 : 0.7), AudioSettings.EffectsBus);
                nextBeat += beatInterval;
            }

            _nextBeat[c.Key] = nextBeat;

            if (c.News2 >= 5)
            {
                var red = c.News2 >= 7;
                if (!_nextAlarm.TryGetValue(c.Key, out var nextAlarm) || nextAlarm < _time - 10)
                {
                    nextAlarm = _time;
                }

                if (_time >= nextAlarm)
                {
                    PlayAt(red ? "alarm_red" : "alarm_amber", c.Pos, c.Gain * (red ? 1.0 : 0.75), AudioSettings.EffectsBus);
                    nextAlarm = _time + (red ? 4.0 : 6.5);
                }

                _nextAlarm[c.Key] = nextAlarm;
            }
        }
    }

    // ---------------------------------------------------------------- rooms

    private void UpdateRooms(float zoom)
    {
        var sim = _session.Simulation;
        foreach (var space in sim.Spaces)
        {
            var r = space.Rect;
            var centre = IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0));
            var atSpot = sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == space.PatientSpot);
            var kind = space.Kind.ToString();

            switch (space.Kind)
            {
                case CareSpaceKind.XRay:
                    if (atSpot is not null)
                    {
                        var gain = Spatial(centre, zoom, 0.4, 1.1);
                        Want($"xray:{space.Id}", "xray_hum", centre, gain * 0.7, AudioSettings.EffectsBus);

                        // An exposure shortly after each patient is positioned, then every so often.
                        if (!_xrayUse.TryGetValue(space.Id, out var use) || use.PatientId != atSpot.Id)
                        {
                            use = (atSpot.Id, _time + 2.5);
                        }

                        if (_time >= use.NextShot)
                        {
                            PlayAt("xray_exposure", centre, gain, AudioSettings.EffectsBus);
                            use.NextShot = _time + 12 + (_random.NextDouble() * 6);
                        }

                        _xrayUse[space.Id] = use;
                    }
                    else
                    {
                        _xrayUse.Remove(space.Id);
                    }

                    break;

                case CareSpaceKind.CT when atSpot is not null:
                    Want($"ct:{space.Id}", "ct_scanner", centre, Spatial(centre, zoom, 0.4, 1.1) * 0.8, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Theatre when atSpot is not null || space.OccupantId is not null:
                    Want($"theatre:{space.Id}", "amb_theatre", centre, Spatial(centre, zoom, 0.5, 1.2) * 0.7, AudioSettings.AmbienceBus);
                    break;

                case CareSpaceKind.Ward when space.Beds.Any(b => b.OccupantId is not null):
                    Want($"ward:{space.Id}", "amb_ward", centre, Spatial(centre, zoom, 0.5, 1.2) * 0.6, AudioSettings.AmbienceBus);
                    break;

                case CareSpaceKind.Triage when atSpot is not null:
                    Want($"chat:{space.Id}", "chatter_triage", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.7, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Waiting:
                {
                    var waiting = sim.Patients.Count(p => !p.IsMoving && r.Contains(p.Tile));
                    if (waiting > 0)
                    {
                        // A few people: reception desk chat. A crowd: the waiting room murmur.
                        var sound = waiting >= 4 ? "chatter_waiting" : "chatter_reception";
                        Want($"chat:{space.Id}", sound, centre, Spatial(centre, zoom, 0.8, 1.4) * 0.65, AudioSettings.EffectsBus);
                    }

                    break;
                }

                default:
                    // A reception room, when the game has one.
                    if (kind.Contains("Reception", StringComparison.Ordinal) && sim.Patients.Any(p => r.Contains(p.Tile)))
                    {
                        Want($"chat:{space.Id}", "chatter_reception", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.7, AudioSettings.EffectsBus);
                    }

                    break;
            }
        }
    }

    // ---------------------------------------------------------------- people moving

    private void UpdateMovement(float zoom)
    {
        if (zoom < 1.0f)
        {
            return;
        }

        var sim = _session.Simulation;
        var rolling = sim.Patients
            .Where(p => p.IsMoving && _world.IsOnMap(p) && (_world.InWheelchair(p) || _world.OnTrolley(p)))
            .Select(p => (Patient: p, Pos: _world.FootPosition(p)))
            .Select(x => (x.Patient, x.Pos, Gain: Spatial(x.Pos, zoom, 1.0, 1.6)))
            .Where(x => x.Gain > 0.05)
            .OrderByDescending(x => x.Gain)
            .Take(2);
        foreach (var (patient, pos, gain) in rolling)
        {
            Want($"roll:{patient.Id}", _world.InWheelchair(patient) ? "wheelchair" : "trolley", pos, gain * 0.5, AudioSettings.EffectsBus);
        }

        var walker = sim.Staff
            .Where(s => s.IsMoving)
            .Select(s => (Staff: s, Pos: _world.FootPosition(s)))
            .Select(x => (x.Staff, x.Pos, Gain: Spatial(x.Pos, zoom, 1.3, 2.0)))
            .Where(x => x.Gain > 0.05)
            .OrderByDescending(x => x.Gain)
            .FirstOrDefault();
        if (walker.Staff is not null)
        {
            Want("footsteps", "footsteps", walker.Pos, walker.Gain * 0.35, AudioSettings.EffectsBus);
        }
    }

    // ---------------------------------------------------------------- ambulances

    private void UpdateAmbulances()
    {
        foreach (var ambulance in _session.Simulation.Ambulances)
        {
            // A siren for each ambulance on its way in with a patient.
            if (ambulance.Purpose == AmbulancePurpose.Arrival && ambulance.State == AmbulanceState.Driving && _sirenPlayed.Add(ambulance.Id))
            {
                if (!_siren.Playing && SoundLibrary.Get("siren") is { } siren)
                {
                    _siren.Stream = siren;
                    _siren.VolumeDb = (float)SoundFalloff.ToDecibels(0.55);
                    _siren.Play();
                    if (LogSounds)
                    {
                        GD.Print("[sound] siren");
                    }
                }
            }

            _ambulanceStates.TryGetValue(ambulance.Id, out var previous);
            if (ambulance.State == AmbulanceState.AtBay && previous != AmbulanceState.AtBay)
            {
                var pos = _world.FootPosition(ambulance);
                var zoom = GetViewport().GetCanvasTransform().X.X;
                var gain = Spatial(pos, zoom, 0.35, 1.0);
                PlayAt("ambulance_arrive", pos, gain * 0.7, AudioSettings.EffectsBus);
                if (ambulance.Purpose == AmbulancePurpose.Arrival)
                {
                    PlayAt("ambulance_doors", pos, gain * 0.8, AudioSettings.EffectsBus, delay: 3.5);
                }
            }

            _ambulanceStates[ambulance.Id] = ambulance.State;
        }
    }

    // ---------------------------------------------------------------- voices

    private void UpdateRandomVoices(float zoom)
    {
        if (_time < _nextChatter)
        {
            return;
        }

        _nextChatter = _time + 3 + (_random.NextDouble() * 5);
        if (zoom < 0.9f || _session.Clock.IsPaused)
        {
            return;
        }

        var nearby = _session.Simulation.Patients
            .Where(_world.IsOnMap)
            .Select(p => (Patient: p, Pos: _world.FootPosition(p)))
            .Select(x => (x.Patient, x.Pos, Gain: Spatial(x.Pos, zoom, 0.9, 1.5)))
            .Where(x => x.Gain > 0.15)
            .ToList();
        if (nearby.Count == 0)
        {
            return;
        }

        var (patient, pos, gain) = nearby[_random.Next(nearby.Count)];
        var news2 = patient.LatestObservations?.News2.Total ?? 0;
        string? sound;
        if (patient.Age < 2)
        {
            sound = "baby_cry";
        }
        else if (_world.IsLying(patient))
        {
            sound = news2 >= 5 && _random.NextDouble() < 0.5 ? "groan" : null;
        }
        else
        {
            var roll = _random.NextDouble();
            sound = roll < 0.3 ? Coughs[_random.Next(Coughs.Length)]
                : roll < 0.75 ? (patient.Age < 12 ? "gibberish_child" : news2 >= 5 ? "gibberish_ouch" : Gibberish[_random.Next(Gibberish.Length)])
                : null;
        }

        if (sound is not null)
        {
            PlayAt(sound, pos, gain * 0.6, AudioSettings.EffectsBus);
        }
    }

    // ---------------------------------------------------------------- building, staff, money

    private void UpdateBuildingAndMoney()
    {
        var sim = _session.Simulation;
        var rooms = sim.Spaces.Count(s => s.Kind != CareSpaceKind.Corridor);
        var furniture = sim.Spaces.Sum(s => s.Furniture.Count);
        var staff = sim.Staff.Count;

        if (_roomCount >= 0)
        {
            if (rooms > _roomCount)
            {
                PlayUi("room_built", 0.8);
            }
            else if (rooms < _roomCount)
            {
                PlayUi("demolish", 0.8);
            }
            else if (furniture > _furnitureCount)
            {
                PlayUi("furniture_placed", 0.8);
            }

            if (staff > _staffCount)
            {
                PlayUi("staff_hired", 0.7);
            }
            else if (staff < _staffCount)
            {
                PlayUi("staff_let_go", 0.7);
            }
        }

        (_roomCount, _furnitureCount, _staffCount) = (rooms, furniture, staff);

        var balance = sim.Balance;
        if (_lastBalance != long.MinValue && !sim.UnlimitedMoney)
        {
            if (balance - _lastBalance >= 1000 && OffCooldown("money_in", 4))
            {
                PlayUi("money_in", 0.4);
            }

            const long lowMoney = 50_000;
            if (balance < lowMoney && _lastBalance >= lowMoney)
            {
                PlayUi("money_low", 0.8);
            }
        }

        _lastBalance = balance;
    }

    // ---------------------------------------------------------------- events

    private void OnPatientLeft(PatientLeft e)
    {
        switch (e.Outcome)
        {
            case PatientOutcome.Died:
            {
                // The monitor flatlines if they were on one, then a gentle sting.
                var monitor = _world.Monitors.FirstOrDefault(m => _world.ConnectedPatient(m.Space)?.Id == e.Patient.Id);
                if (monitor is not null)
                {
                    var pos = IsoDraw.ToScreen(monitor.X, monitor.Y);
                    var zoom = GetViewport().GetCanvasTransform().X.X;
                    PlayAt("flatline", pos, Spatial(pos, zoom, 0.3, 1.0, floor: 0.3) * 0.6, AudioSettings.EffectsBus, maxSeconds: 2.5);
                }

                PlayFlat("death_sting", 0.9, AudioSettings.MusicBus, delay: monitor is null ? 0 : 1.5);
                break;
            }

            case PatientOutcome.Discharged when OffCooldown("discharged", 2.5):
                PlayUi("discharged", 0.35);
                break;

            case PatientOutcome.Transferred when OffCooldown("transferred", 2.5):
                PlayUi("transferred", 0.4);
                break;
        }
    }

    private void OnNotification(Notification n)
    {
        if (n.Level == NotificationLevel.Critical && OffCooldown("notify_urgent", 3))
        {
            PlayUi("notify_urgent", 0.7);
        }
        else if (n.Level == NotificationLevel.Warning && OffCooldown("notify", 2))
        {
            PlayUi("notify", 0.5);
        }
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton button)
        {
            HookButton(button);
        }
        else if (node is ModalPanel && OffCooldown("panel", 0.3))
        {
            PlayUi("ui_panel_open", 0.6);
        }
    }

    private void HookButtons(Node node)
    {
        if (node is BaseButton button)
        {
            HookButton(button);
        }

        foreach (var child in node.GetChildren())
        {
            HookButtons(child);
        }
    }

    private void HookButton(BaseButton button)
    {
        if (button.HasMeta("click_sound"))
        {
            return;
        }

        button.SetMeta("click_sound", true);
        button.Pressed += () =>
        {
            if (IsInstanceValid(this) && OffCooldown("click", 0.05))
            {
                PlayUi("ui_click", 0.6);
            }
        };
    }

    // ---------------------------------------------------------------- playback helpers

    /// <summary>Loudness for a sound at a world position: zoom-in level times distance from the middle of the screen.</summary>
    private double Spatial(Vector2 worldPos, float zoom, double silentAt, double fullAt, double floor = 0)
    {
        var viewport = GetViewportRect().Size;
        var screen = GetViewport().GetCanvasTransform() * worldPos;
        var distance = screen.DistanceTo(viewport / 2);
        var radius = viewport.Length() / 2;
        return SoundFalloff.ZoomGain(zoom, silentAt, fullAt, floor) * SoundFalloff.DistanceGain(distance, radius * 0.45, radius * 1.3);
    }

    private bool OffCooldown(string key, double seconds)
    {
        if (_cooldowns.TryGetValue(key, out var until) && _time < until)
        {
            return false;
        }

        _cooldowns[key] = _time + seconds;
        return true;
    }

    private void PlayUi(string sound, double gain) => PlayFlat(sound, gain, AudioSettings.InterfaceBus);

    private void PlayFlat(string sound, double gain, string bus, double delay = 0) =>
        PlayAt(sound, null, gain, bus, delay);

    /// <summary>Plays a sound once, panned to <paramref name="worldPos"/> (or centred when null).</summary>
    private void PlayAt(string sound, Vector2? worldPos, double gain, string bus, double delay = 0, double maxSeconds = 0)
    {
        if (gain <= 0.02 || _oneShots.Count >= MaxOneShots || SoundLibrary.Get(sound) is not { } stream)
        {
            return;
        }

        if (LogSounds)
        {
            GD.Print($"[sound] {sound} gain {gain:0.00}");
        }

        var player = MakePlayer(stream, worldPos, bus);
        player.VolumeDb = (float)SoundFalloff.ToDecibels(gain);
        player.Finished += player.QueueFree;
        _oneShots.Add(player);

        if (delay > 0)
        {
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (IsInstanceValid(player))
                {
                    player.Play();
                }
            };
        }
        else
        {
            player.Play();
        }

        if (maxSeconds > 0)
        {
            GetTree().CreateTimer(delay + maxSeconds).Timeout += () =>
            {
                if (IsInstanceValid(player))
                {
                    player.QueueFree();
                }
            };
        }
    }

    private AudioStreamPlayer2D MakePlayer(AudioStream stream, Vector2? worldPos, string bus)
    {
        // Distance fading is done here (from zoom and screen position), so the engine only pans.
        var player = new AudioStreamPlayer2D
        {
            Stream = stream,
            Bus = bus,
            Attenuation = 0.0001f,
            MaxDistance = 1_000_000,
            PanningStrength = worldPos is null ? 0 : 0.6f,
        };
        AddChild(player);
        player.GlobalPosition = worldPos ?? (GetViewport().GetCanvasTransform().AffineInverse() * (GetViewportRect().Size / 2));
        return player;
    }

    /// <summary>Asks for a looping sound this frame; loops not asked for fade out and stop.</summary>
    private void Want(string key, string sound, Vector2 worldPos, double gain, string bus)
    {
        if (gain <= 0.02 && !_loops.ContainsKey(key))
        {
            return;
        }

        if (!_loops.TryGetValue(key, out var loop))
        {
            if (SoundLibrary.Get(sound, loop: true) is not { } stream)
            {
                return;
            }

            if (LogSounds)
            {
                GD.Print($"[sound] loop {sound} ({key}) gain {gain:0.00}");
            }

            loop = new Loop(MakePlayer(stream, worldPos, bus), !key.StartsWith("flat:", StringComparison.Ordinal));
            loop.Player.VolumeDb = -80;
            loop.Player.Play(_random.NextSingle() * (float)stream.GetLength());
            _loops[key] = loop;
        }

        loop.Target = gain;
        loop.Wanted = true;
        if (loop.Positional)
        {
            loop.Player.GlobalPosition = worldPos;
        }
    }

    private void WantFlat(string sound, double gain, string bus)
    {
        var centre = GetViewport().GetCanvasTransform().AffineInverse() * (GetViewportRect().Size / 2);
        Want("flat:" + sound, sound, centre, gain, bus);
        if (_loops.TryGetValue("flat:" + sound, out var loop))
        {
            loop.Player.PanningStrength = 0;
            loop.Player.GlobalPosition = centre;
        }
    }

    private void UpdateLoops(double delta)
    {
        foreach (var (key, loop) in _loops.ToList())
        {
            var target = loop.Wanted ? loop.Target : 0;
            loop.Gain += (target - loop.Gain) * Math.Min(1, delta * FadeSpeed);
            loop.Player.VolumeDb = (float)SoundFalloff.ToDecibels(loop.Gain);
            if (!loop.Wanted && loop.Gain < 0.01)
            {
                loop.Player.QueueFree();
                _loops.Remove(key);
            }

            loop.Wanted = false;
        }
    }

    private sealed class Loop(AudioStreamPlayer2D player, bool positional)
    {
        public AudioStreamPlayer2D Player { get; } = player;

        public bool Positional { get; } = positional;

        public double Gain { get; set; }

        public double Target { get; set; }

        public bool Wanted { get; set; }
    }
}
