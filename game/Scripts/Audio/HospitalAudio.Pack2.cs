using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.Audio;

/// <summary>
/// Sound pack 2: busier rooms (resus, majors, staff room, pharmacy, the specialty rooms), bedside
/// equipment (BP cuffs, ECGs, curtains, pumps, call bells, defibs), corridor life (lifts, fire
/// doors, hand gel) and patients moving between trolley and bed.
/// </summary>
public partial class HospitalAudio
{
    private readonly Dictionary<int, string?> _patientSpace = new();
    private readonly Dictionary<int, DateTime?> _lastObs = new();
    private readonly HashSet<string> _ecgsHeard = new();
    private readonly Dictionary<int, int> _lastNews = new();
    private readonly Dictionary<int, bool> _staffMoving = new();
    private readonly Dictionary<string, int?> _triageOccupant = new();
    private readonly Dictionary<string, double> _nextRoomSound = new();
    private double _nextCorridorSound = 20;

    private bool IsRainyDay()
    {
        var date = _session.Clock.Now.Date;
        return ((date.DayOfYear * 7919) + date.Year) % 10 < 3;
    }

    private bool IsDrunkHour()
    {
        var now = _session.Clock.Now;
        var weekendNight = now.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday && now.Hour >= 22
                           || now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && now.Hour < 4;
        return weekendNight;
    }

    private static bool IsVomiting(Patient patient) =>
        patient.Symptoms.Any(s => s.Contains("vomit", StringComparison.OrdinalIgnoreCase) || s.Contains("nausea", StringComparison.OrdinalIgnoreCase));

    private void UpdatePack2(float zoom)
    {
        var sim = _session.Simulation;
        var night = _session.Clock.Now.Hour >= 23 || _session.Clock.Now.Hour < 6;

        foreach (var space in sim.Spaces)
        {
            var r = space.Rect;
            var centre = IsoDraw.ToScreen(r.X + (r.Width / 2.0), r.Y + (r.Length / 2.0));
            var atSpot = sim.Patients.FirstOrDefault(p => !p.IsMoving && p.Tile == space.PatientSpot);
            var occupied = atSpot is not null || space.PlacesUsed > 0;
            var staffHere = sim.Staff.Count(s => !s.IsMoving && r.Contains(s.Tile));

            switch (space.Kind)
            {
                case CareSpaceKind.Resus when occupied:
                    Want($"resus:{space.Id}", "amb_resus", centre, Spatial(centre, zoom, 0.5, 1.2) * 0.55, AudioSettings.AmbienceBus);
                    if (atSpot is { } resusPatient && (resusPatient.LatestObservations?.News2.Total ?? 0) >= 7)
                    {
                        Want($"vent:{space.Id}", "ventilator", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.4, AudioSettings.EffectsBus);
                    }

                    Everyso(space.Id + ":suction", 25, 60, () => PlayAt("suction", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.5, AudioSettings.EffectsBus));
                    break;

                case CareSpaceKind.Majors when occupied:
                    Want($"majors:{space.Id}", "amb_majors", centre, Spatial(centre, zoom, 0.5, 1.2) * 0.35, AudioSettings.AmbienceBus);
                    if (atSpot is { } majorsPatient && (majorsPatient.LatestObservations?.News2.Total ?? 0) >= 5)
                    {
                        Want($"o2:{space.Id}", "oxygen_hiss", centre, Spatial(centre, zoom, 1.0, 1.6) * 0.3, AudioSettings.EffectsBus);
                    }

                    break;

                case CareSpaceKind.StaffRoom when sim.Staff.Any(s => s.Activity == StaffActivity.OnBreak && r.Contains(s.Tile)):
                    Want($"staffroom:{space.Id}", "amb_staff_room", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.5, AudioSettings.AmbienceBus);
                    break;

                case CareSpaceKind.Pharmacy when staffHere > 0:
                    Want($"pharmacy:{space.Id}", "amb_pharmacy", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.5, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Echo or CareSpaceKind.Ultrasound when atSpot is not null:
                    Want($"echo:{space.Id}", "echo_room", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.6, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.CathLab when atSpot is not null:
                    Want($"cath:{space.Id}", "cath_lab", centre, Spatial(centre, zoom, 0.5, 1.2) * 0.7, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Eeg when atSpot is not null:
                    Want($"eeg:{space.Id}", "eeg_room", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.6, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Endoscopy when atSpot is not null:
                    Want($"endo:{space.Id}", "endoscopy", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.6, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.PlasterRoom when atSpot is not null:
                    Everyso(space.Id + ":saw", 15, 30, () => PlayAt("plaster_saw", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.6, AudioSettings.EffectsBus));
                    break;

                case CareSpaceKind.PhysioGym when sim.Patients.Any(p => r.Contains(p.Tile)):
                    Want($"physio:{space.Id}", "physio_gym", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.6, AudioSettings.EffectsBus);
                    break;

                case CareSpaceKind.Ward when space.Beds.Any(b => b.OccupantId is not null):
                    if (sim.UnitOf(space) == Simulation.Data.Models.WardUnit.IntensiveCare)
                    {
                        // Ventilators breathing at every occupied ICU bed.
                        Want($"vent:{space.Id}", "ventilator", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.35, AudioSettings.EffectsBus);
                    }

                    if (night)
                    {
                        Want($"snore:{space.Id}", "snore", centre, Spatial(centre, zoom, 0.9, 1.5) * 0.35, AudioSettings.EffectsBus);
                    }
                    else
                    {
                        Everyso(space.Id + ":bell", 45, 120, () => PlayAt("call_bell", centre, Spatial(centre, zoom, 0.6, 1.3) * 0.4, AudioSettings.EffectsBus));
                    }

                    Everyso(space.Id + ":pump", 60, 150, () => PlayAt("iv_pump_alarm", centre, Spatial(centre, zoom, 0.8, 1.4) * 0.35, AudioSettings.EffectsBus));
                    break;

                case CareSpaceKind.NursesStation or CareSpaceKind.DoctorsOffice when staffHere >= 2:
                    Everyso(space.Id + ":chat", 12, 30, () => PlayAt(_random.Next(2) == 0 ? "staff_chat_1" : "staff_chat_2", centre, Spatial(centre, zoom, 0.9, 1.5) * 0.5, AudioSettings.EffectsBus));
                    break;

                case CareSpaceKind.Triage:
                {
                    // The "next patient" chime as someone is called through.
                    _triageOccupant.TryGetValue(space.Id, out var previous);
                    if (atSpot is not null && atSpot.Id != previous)
                    {
                        PlayAt("next_patient", centre, Spatial(centre, zoom, 0.4, 1.1) * 0.5, AudioSettings.EffectsBus);
                    }

                    _triageOccupant[space.Id] = atSpot?.Id;
                    break;
                }
            }
        }

        UpdateBedsideEvents(zoom);
        UpdateStaffArrivals(zoom);
        UpdateCorridors(zoom);
    }

    /// <summary>Curtains, slide transfers, BP cuffs, ECGs and the deteriorating warning, from changes to each patient.</summary>
    private void UpdateBedsideEvents(float zoom)
    {
        var sim = _session.Simulation;
        foreach (var patient in sim.Patients)
        {
            if (!_world.IsOnMap(patient))
            {
                continue;
            }

            var pos = _world.FootPosition(patient);
            var gain = Spatial(pos, zoom, 0.7, 1.4);

            _patientSpace.TryGetValue(patient.Id, out var previousSpace);
            if (_patientSpace.ContainsKey(patient.Id) && patient.CareSpaceId != previousSpace && patient.CareSpaceId is { } spaceId
                && sim.Spaces.FirstOrDefault(s => s.Id == spaceId) is { } space && (space.IsTreatmentSpace || space.Kind == CareSpaceKind.Ward))
            {
                PlayAt("curtain_pull", pos, gain * 0.5, AudioSettings.EffectsBus, delay: 1.0);
                if (_world.OnTrolley(patient) || patient.ArrivalMethod == ArrivalMethod.Ambulance && previousSpace is null)
                {
                    PlayAt("bed_brake", pos, gain * 0.5, AudioSettings.EffectsBus, delay: 0.3);
                    PlayAt("slide_transfer", pos, gain * 0.5, AudioSettings.EffectsBus, delay: 2.0);
                }
            }

            _patientSpace[patient.Id] = patient.CareSpaceId;

            _lastObs.TryGetValue(patient.Id, out var lastObs);
            if (_lastObs.ContainsKey(patient.Id) && patient.LastObservationsAt != lastObs && OffCooldown("bp_cuff", 4))
            {
                PlayAt("bp_cuff", pos, gain * 0.45, AudioSettings.EffectsBus);
            }

            _lastObs[patient.Id] = patient.LastObservationsAt;

            foreach (var ecg in patient.Investigations.Where(i => i.InvestigationId == "ecg" && i.Status != InvestigationStatus.Requested))
            {
                if (_ecgsHeard.Add($"{patient.Id}:{ecg.RequestedAt.Ticks}"))
                {
                    PlayAt("ecg_print", pos, gain * 0.5, AudioSettings.EffectsBus);
                }
            }

            var news = patient.LatestObservations?.News2.Total ?? 0;
            if (_lastNews.TryGetValue(patient.Id, out var before) && before < 5 && news >= 5 && OffCooldown("deteriorating", 8))
            {
                PlayUi("deteriorating", 0.5);
            }

            _lastNews[patient.Id] = news;
        }
    }

    /// <summary>Staff gel their hands as they arrive at a patient, now and then.</summary>
    private void UpdateStaffArrivals(float zoom)
    {
        foreach (var member in _session.Simulation.Staff)
        {
            _staffMoving.TryGetValue(member.Id, out var wasMoving);
            if (wasMoving && !member.IsMoving && member.CurrentTaskId is not null && _random.NextDouble() < 0.3 && OffCooldown("hand_gel", 5))
            {
                var pos = _world.FootPosition(member);
                PlayAt("hand_gel", pos, Spatial(pos, zoom, 1.0, 1.6) * 0.5, AudioSettings.EffectsBus);
            }

            _staffMoving[member.Id] = member.IsMoving;
        }
    }

    /// <summary>Lifts and fire doors somewhere along the corridors, when zoomed in.</summary>
    private void UpdateCorridors(float zoom)
    {
        if (_time < _nextCorridorSound)
        {
            return;
        }

        _nextCorridorSound = _time + 35 + (_random.NextDouble() * 55);
        var corridors = _session.Simulation.Spaces.Where(s => s.Kind == CareSpaceKind.Corridor).ToList();
        if (corridors.Count == 0 || zoom < 0.9f || _session.Clock.IsPaused)
        {
            return;
        }

        var c = corridors[_random.Next(corridors.Count)].Rect;
        var pos = IsoDraw.ToScreen(c.X + (_random.NextDouble() * c.Width), c.Y + (_random.NextDouble() * c.Length));
        PlayAt(_random.Next(3) == 0 ? "lift_ding" : "fire_door", pos, Spatial(pos, zoom, 0.9, 1.5) * 0.45, AudioSettings.EffectsBus);
    }

    private void OnAnnouncement(Announcement a)
    {
        if (a.Kind != AnnouncementKind.CardiacArrest || a.PatientId is not { } id
            || _session.Simulation.Patients.FirstOrDefault(p => p.Id == id) is not { } patient || !_world.IsOnMap(patient))
        {
            return;
        }

        // The team arrives and shocks after the crash call.
        var pos = _world.FootPosition(patient);
        PlayAt("defib_shock", pos, Spatial(pos, GetViewport().GetCanvasTransform().X.X, 0.4, 1.1, floor: 0.2) * 0.8, AudioSettings.EffectsBus, delay: 12);
    }

    private void OnPatientArrived(PatientArrived e)
    {
        if (e.Patient.ArrivalMethod == ArrivalMethod.WalkIn && _random.NextDouble() < 0.35 && OffCooldown("car_drop_off", 20) && _world.IsOnMap(e.Patient))
        {
            var pos = _world.FootPosition(e.Patient);
            PlayAt("car_drop_off", pos, Spatial(pos, GetViewport().GetCanvasTransform().X.X, 0.6, 1.3) * 0.5, AudioSettings.EffectsBus);
        }
    }

    /// <summary>Runs <paramref name="play"/> at random intervals between the two limits (real seconds).</summary>
    private void Everyso(string key, double min, double max, Action play)
    {
        if (!_nextRoomSound.TryGetValue(key, out var next) || next < _time - 5)
        {
            // First sight of this room, or it has been quiet for a while: wait a random part of the interval.
            _nextRoomSound[key] = _time + (_random.NextDouble() * max);
            return;
        }

        if (_time >= next && !_session.Clock.IsPaused)
        {
            play();
            _nextRoomSound[key] = _time + min + (_random.NextDouble() * (max - min));
        }
    }

    private void HookControl(Node node)
    {
        if (node.HasMeta("click_sound"))
        {
            return;
        }

        node.SetMeta("click_sound", true);
        switch (node)
        {
            case TabBar bar:
                bar.TabChanged += _ => PlayTab();
                break;
            case TabContainer tabs:
                tabs.TabChanged += _ => PlayTab();
                break;
            case Godot.Range range and (Slider or SpinBox):
                range.ValueChanged += _ =>
                {
                    if (IsInstanceValid(this) && OffCooldown("tick", 0.06))
                    {
                        PlayUi("ui_tick", 0.5);
                    }
                };
                break;
        }
    }

    private void PlayTab()
    {
        if (IsInstanceValid(this) && OffCooldown("tab", 0.05))
        {
            PlayUi("ui_tab", 0.6);
        }
    }
}
