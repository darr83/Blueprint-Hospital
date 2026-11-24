using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Simulation;
using BlueprintHospital.Simulation.Staff;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// The room panel's Patients, Staff and Today tabs: who is in each bed and what is happening to them,
/// who is working here, and the room's day so far.
/// </summary>
public partial class RoomPanel
{
    /// <summary>Rooms with patients (or clinic patients, or babies) get the Patients, Staff and Today tabs.</summary>
    public static bool HasPeople(CareSpace room) =>
        room.IsWard || room.IsTreatmentSpace || room.Kind is CareSpaceKind.Clinic or CareSpaceKind.TheatreRecovery or CareSpaceKind.Triage || room.Beds.Count > 0;

    /// <summary>Switches tab (also used by the smoke test).</summary>
    public void ShowTab(RoomTab tab)
    {
        _tab = tab;
        if (_roomId is not null && _sim.FindSpace(_roomId) is { } room)
        {
            RefreshPeople(room);
        }
    }

    private void BuildSummary()
    {
        _summary.AddThemeConstantOverride("separation", 8);
        var tiles = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tiles.AddThemeConstantOverride("separation", 6);
        for (var i = 0; i < _tiles.Length; i++)
        {
            var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            var box = UiStyle.Box(UiStyle.Ground, UiStyle.RadiusCard);
            box.ContentMarginLeft = box.ContentMarginRight = 4;
            box.ContentMarginTop = 6;
            box.ContentMarginBottom = 6;
            tile.AddThemeStyleboxOverride("panel", box);
            var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", -2);
            var value = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            UiStyle.Strong(value, UiStyle.SizeFigure - 2);
            stack.AddChild(value);
            var caption = UiStyle.Caption(string.Empty, 11);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            stack.AddChild(caption);
            tile.AddChild(stack);
            tiles.AddChild(tile);
            _tiles[i] = (tile, value, caption);
        }

        _summary.AddChild(tiles);
        _summary.AddChild(_bedStrip);
    }

    private void BuildPeoplePage()
    {
        _peoplePage.AddThemeConstantOverride("separation", 7);
        _dayTiles.AddThemeConstantOverride("separation", 6);
        var captions = new[] { "admitted", "went home", "average stay", "died" };
        for (var i = 0; i < captions.Length; i++)
        {
            var (tile, value) = HudWidgets.StatTile(captions[i]);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            _dayValues[i] = value;
            _dayTiles.AddChild(tile);
        }

        _peoplePage.AddChild(_dayTiles);
        foreach (var heading in new[] { _listHeading, _secondHeading })
        {
            heading.AddThemeFontSizeOverride("font_size", UiStyle.SizeCaption);
            heading.AddThemeColorOverride("font_color", UiStyle.TextMuted);
            UiStyle.Strong(heading);
        }

        _listBox.AddThemeConstantOverride("separation", 6);
        _secondBox.AddThemeConstantOverride("separation", 6);
        _listNote.AddThemeColorOverride("default_color", UiStyle.TextDark);
        _secondNote.AddThemeColorOverride("default_color", UiStyle.TextMuted);
        _listNote.AddThemeFontSizeOverride("normal_font_size", UiStyle.SizeSmall);
        _listNote.AddThemeFontSizeOverride("bold_font_size", UiStyle.SizeSmall);
        _secondNote.AddThemeFontSizeOverride("normal_font_size", UiStyle.SizeSmall);
        _peoplePage.AddChild(_listHeading);
        _peoplePage.AddChild(_listBox);
        _peoplePage.AddChild(_listNote);
        _peoplePage.AddChild(_secondHeading);
        _peoplePage.AddChild(_secondBox);
        _peoplePage.AddChild(_secondNote);
        _rows = new DetailRows(_listBox);
        _secondRows = new DetailRows(_secondBox);
    }

    private void RefreshPeople(CareSpace room)
    {
        var people = HasPeople(room);
        if (!people)
        {
            _tab = RoomTab.Room;
        }

        _summary.Visible = _tabTrack.Visible = people;
        _peoplePage.Visible = people && _tab != RoomTab.Room;
        _roomPage.Visible = _tab == RoomTab.Room;
        foreach (var (tab, button) in _tabButtons)
        {
            button.SetPressedNoSignal(tab == _tab);
        }

        if (!people)
        {
            return;
        }

        _tabButtons[RoomTab.Patients].Text = room.Kind == CareSpaceKind.Clinic ? "Clinic list" : _sim.UnitOf(room) == WardUnit.Neonatal ? "Babies" : "Patients";
        RefreshSummary(room);
        _rows.Begin();
        _secondRows.Begin();
        _dayTiles.Visible = _tab == RoomTab.Today;
        _listNote.Text = string.Empty;
        _secondNote.Text = string.Empty;
        _secondHeading.Text = string.Empty;
        switch (_tab)
        {
            case RoomTab.Patients when room.Kind == CareSpaceKind.Clinic:
                ClinicList(room);
                break;
            case RoomTab.Patients when _sim.UnitOf(room) == WardUnit.Neonatal:
                BabyList(room);
                break;
            case RoomTab.Patients:
                PatientList(room);
                break;
            case RoomTab.Staff:
                StaffList(room);
                break;
            case RoomTab.Today:
                TodayPage(room);
                break;
        }

        _rows.End();
        _secondRows.End();
        _listNote.Visible = _listNote.Text.Length > 0;
        _secondNote.Visible = _secondNote.Text.Length > 0;
        _secondHeading.Visible = _secondHeading.Text.Length > 0;
        _listHeading.Visible = _listHeading.Text.Length > 0;
    }

    // ------------------------------------------------------------------ summary

    private void RefreshSummary(CareSpace room)
    {
        var staffHere = _sim.StaffIn(room).Count();
        if (room.Kind == CareSpaceKind.Clinic)
        {
            var today = _sim.CalendarFor(room.Id, _sim.Now).ToList();
            SetTile(0, $"{today.Count(a => a.Status == AppointmentStatus.Seen)}/{today.Count(a => a.Status != AppointmentStatus.Cancelled)}", "seen today");
            SetTile(1, today.Count(a => a.Status == AppointmentStatus.Waiting).ToString(), "waiting");
            SetTile(2, today.Count(a => a.Status == AppointmentStatus.InClinic).ToString(), "with the doctor");
            SetTile(3, staffHere.ToString(), "staff here");
            _bedStrip.Visible = false;
            return;
        }

        var patients = Occupants(room);
        var places = room.Beds.Count > 0 ? room.Beds.Count : 1;
        var used = patients.Count(p => p.Patient is not null || p.Baby is not null);
        var treating = patients.Count(p => p.Patient is { } patient && _sim.BedsideStatus(patient).State == BedsideState.Treating);
        SetTile(0, $"{used}/{places}", room.IsWard ? "beds full" : places == 1 ? "in use" : "places in use");
        SetTile(1, treating.ToString(), "being treated");
        if (room.IsWard)
        {
            var coming = _sim.Patients.Count(p => p.ReservedWardId == room.Id || (p.CareSpaceId == room.Id && p.Stage != PatientStage.OnWard && p.IsInDepartment));
            SetTile(2, coming.ToString(), "coming up");
        }
        else
        {
            SetTile(2, patients.Count(p => p.Patient?.MonitorIsRed == true).ToString(), "red monitor");
        }

        SetTile(3, staffHere.ToString(), "staff here");
        _bedStrip.Visible = true;
        _bedStrip.Set(patients.Select(p => p.Patient is { } pt ? DetailColours.Band(pt.MonitorColour) : p.Baby is { } b ? DetailColours.Band(b.MonitorColour) : DetailColours.Empty).ToList());
    }

    private void SetTile(int index, string value, string caption)
    {
        _tiles[index].Value.Text = value;
        _tiles[index].Caption.Text = caption;
    }

    /// <summary>Each bed (or the room's one place) and who is in it.</summary>
    private List<(int Bed, Patient? Patient, Baby? Baby)> Occupants(CareSpace room)
    {
        var list = new List<(int, Patient?, Baby?)>();
        if (_sim.UnitOf(room) == WardUnit.Neonatal)
        {
            var babies = _sim.BabiesOnUnit(room).ToList();
            for (var i = 0; i < room.Beds.Count; i++)
            {
                list.Add((i, null, babies.FirstOrDefault(b => b.CotIndex == i)));
            }

            return list;
        }

        if (room.Beds.Count > 0)
        {
            for (var i = 0; i < room.Beds.Count; i++)
            {
                list.Add((i, room.Beds[i].OccupantId is { } id ? _sim.FindPatient(id) : null, null));
            }

            return list;
        }

        var here = room.OccupantId is { } occupant ? _sim.FindPatient(occupant) : _sim.PatientsIn(room).FirstOrDefault();
        list.Add((0, here, null));
        return list;
    }

    // ------------------------------------------------------------------ patients

    private void PatientList(CareSpace room)
    {
        _listHeading.Text = room.IsWard ? "BEDS" : room.Beds.Count > 0 ? "BEDS AND TROLLEYS" : "PATIENT";
        foreach (var (bed, patient, _) in Occupants(room))
        {
            var lead = room.Beds.Count > 0 ? $"BED\n{bed + 1}" : "—";
            var row = _rows.Next();
            if (patient is null)
            {
                row.Set(lead, UiStyle.Ground, false, room.Beds.Count > 0 ? "Empty" : "Free", room.IsWard ? "Ready for the next patient" : string.Empty, null, null, null, dashed: true);
                continue;
            }

            var p = patient;
            var illness = p.WorkingDiagnosis is { } d && _sim.Database.Conditions.TryGetValue(d.ConditionId, out var c)
                ? c.Name
                : _sim.Database.Symptoms.TryGetValue(p.PresentingComplaint, out var symptom) ? symptom.Name : p.PresentingComplaint;
            var (state, status) = _sim.BedsideStatus(p);
            var parts = new List<string> { illness };
            if (p.LengthOfStay(_sim.Now) is { } stay && p.Stage == PatientStage.OnWard)
            {
                parts.Add($"Day {(int)stay.TotalDays + 1}");
            }
            else if (p.IsInEmergency)
            {
                parts.Add(ClinicalText.Duration(p.TimeInEmergency(_sim.Now)) + " in A&E");
            }

            if (p.Stage != PatientStage.OnWard && room.IsWard)
            {
                status = "On the way up";
            }

            parts.Add(status);
            (string, Color, Color)? tag = state switch
            {
                BedsideState.Treating => DetailColours.Blue("Treating"),
                BedsideState.Waiting => DetailColours.AmberTag("Waiting"),
                BedsideState.GoingHome => DetailColours.GreenTag("Going home"),
                BedsideState.New => DetailColours.PurpleTag("New"),
                BedsideState.Arrest => DetailColours.RedTag("Arrest"),
                _ => null,
            };
            if (p.MonitorIsRed && state != BedsideState.Arrest)
            {
                tag = DetailColours.RedTag("Red");
            }

            var badge = DetailColours.News2Badge(p.LiveNews2, p.MonitorColour);
            row.Set(lead, UiStyle.Ground, false, $"{p.FullName}, {p.Age}", string.Join(" · ", parts), tag, badge, () => PatientClicked?.Invoke(p),
                $"{p.FullName}: NEWS2 {p.LiveNews2}. Click to open their record.");
        }

        // Patients waiting in a cubicle-less room (corridor care into this bay) are shown by the bed list; say who is coming.
        var coming = _sim.Patients.Where(p => p.ReservedWardId == room.Id && p.Stage != PatientStage.OnWard).ToList();
        if (coming.Count > 0)
        {
            _listNote.Text = $"[color=#5B6B79]Coming up:[/color] {string.Join(", ", coming.Select(p => $"{ClinicalText.Escape(p.FullName)} from {ClinicalText.Escape(_sim.FindSpace(p.CareSpaceId ?? string.Empty)?.Name ?? "A&E")}"))}";
        }
    }

    private void BabyList(CareSpace room)
    {
        _listHeading.Text = "INCUBATORS";
        foreach (var (cot, _, baby) in Occupants(room))
        {
            var row = _rows.Next();
            if (baby is null)
            {
                row.Set($"COT\n{cot + 1}", UiStyle.Ground, false, "Empty", string.Empty, null, null, null, dashed: true);
                continue;
            }

            var hours = baby.HoursOld(_sim.Now);
            var age = hours < 48 ? $"{hours:0} hours old" : $"{hours / 24:0} days old";
            var band = baby.MonitorColour;
            row.Set($"COT\n{cot + 1}", UiStyle.Ground, false, $"Baby {baby.FullName}", age, null, (band.ToString().ToUpperInvariant()[..1], DetailColours.Band(band), band == TriageCategory.Amber ? DetailColours.AmberInk : Colors.White), null);
        }
    }

    private void ClinicList(CareSpace room)
    {
        var problems = _sim.ClinicProblems(room);
        _listHeading.Text = $"TODAY · {_sim.Now:dddd d MMMM}".ToUpperInvariant();
        var today = _sim.CalendarFor(room.Id, _sim.Now).OrderBy(a => a.Time).ToList();
        foreach (var a in today)
        {
            var appointment = a;
            (string, Color, Color) tag = a.Status switch
            {
                AppointmentStatus.Arriving => DetailColours.GreyTag("Arriving"),
                AppointmentStatus.Waiting => DetailColours.AmberTag("Waiting"),
                AppointmentStatus.InClinic => DetailColours.Blue("With doctor"),
                AppointmentStatus.Seen => DetailColours.GreenTag("Seen"),
                AppointmentStatus.DidNotAttend => DetailColours.RedTag("Didn't come"),
                AppointmentStatus.Cancelled => DetailColours.GreyTag("Cancelled"),
                _ => DetailColours.GreyTag("Booked"),
            };
            var person = a.OutpatientId is { } id ? _sim.FindOutpatient(id) : null;
            var detail = person is not null ? person.StatusText : a.Status == AppointmentStatus.Seen && a.SeenAt is { } seen ? $"{a.Reason} · seen {seen:HH:mm}" : a.Reason;
            var row = _rows.Next();
            row.Set(a.Time.ToString("HH:mm"), a.Status is AppointmentStatus.InClinic ? UiStyle.Blue : UiStyle.Ground, false, $"{a.FullName}, {a.Age}", detail, tag, null,
                person is null ? null : () => OutpatientClicked?.Invoke(person), dashed: a.Status is AppointmentStatus.Seen or AppointmentStatus.DidNotAttend or AppointmentStatus.Cancelled);
        }

        if (today.Count == 0)
        {
            _listNote.Text = "[color=#5B6B79]Nobody booked today.[/color]";
        }

        if (problems.Count > 0)
        {
            _listNote.Text = $"[color={ClinicalText.RedHex}]Not running: needs {string.Join(" and ", problems)}.[/color]" + (_listNote.Text.Length > 0 ? "\n" + _listNote.Text : string.Empty);
        }
    }

    // ------------------------------------------------------------------ staff

    private void StaffList(CareSpace room)
    {
        var shift = _sim.CurrentShift;
        _listHeading.Text = $"ON SHIFT NOW · {HospitalSimulation.ShiftName(shift).ToUpperInvariant()}S {HospitalSimulation.ShiftHoursText(shift)}";
        var here = _sim.StaffIn(room).ToList();
        foreach (var member in here.Where(m => m.BaseRoomId == room.Id))
        {
            StaffRow(_rows.Next(), member);
        }

        if (!here.Any(m => m.BaseRoomId == room.Id))
        {
            _listNote.Text = $"[color={ClinicalText.RedHex}]Nobody posted here on this shift.[/color] Post staff from the Staff rota (T).";
        }
        else if (room.IsWard && !here.Any(m => m.BaseRoomId == room.Id && m.Role == StaffRole.Doctor))
        {
            _listNote.Text = $"[color={ClinicalText.AmberTextHex}]No doctor posted to this ward on this shift.[/color]";
        }

        var visiting = here.Where(m => m.BaseRoomId != room.Id).ToList();
        if (visiting.Count > 0)
        {
            _secondHeading.Text = "ALSO HERE NOW";
            foreach (var member in visiting)
            {
                StaffRow(_secondRows.Next(), member);
            }
        }

        var other = shift == StaffShift.Day ? StaffShift.Night : StaffShift.Day;
        var team = _sim.Staff.Where(s => s.BaseRoomId == room.Id && s.Shift == other).Select(s => $"{ClinicalText.Escape(s.Name)} ({ClinicalText.Escape(s.Grade)})").ToList();
        var line = $"[b]{char.ToUpperInvariant(HospitalSimulation.ShiftName(other)[0])}{HospitalSimulation.ShiftName(other)[1..]}s, {HospitalSimulation.ShiftHoursText(other)}:[/b] " + (team.Count > 0 ? string.Join(", ", team) : $"[color={ClinicalText.RedHex}]nobody posted[/color]");
        _secondNote.Text = line;
    }

    private void StaffRow(DetailRow row, StaffMember m)
    {
        var colour = Looks.UniformColour(m);
        if (colour.Luminance > 0.85f)
        {
            colour = new Color("#5A6B7A");
        }

        (string, Color, Color) tag = m.Activity switch
        {
            StaffActivity.OnBreak or StaffActivity.WalkingToBreak => DetailColours.GreenTag("Break"),
            StaffActivity.Idle => DetailColours.GreyTag("Free"),
            StaffActivity.GoingHome or StaffActivity.OffDuty => DetailColours.GreyTag("Going home"),
            _ when m.BedsidePatientId is not null => DetailColours.AmberTag("1:1"),
            _ => DetailColours.Blue("Busy"),
        };
        var parts = m.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length switch { 0 => "?", 1 => parts[0][..1], _ => $"{parts[0][0]}{parts[^1][0]}" };
        var doing = m.Activity is StaffActivity.OnBreak or StaffActivity.WalkingToBreak && m.BreakEndsAt is { } back ? $"On a break until {back:HH:mm}" : m.StatusText;
        var member = m;
        row.Set(initials.ToUpperInvariant(), colour, true, $"{m.Name} · {HospitalSimulation.JobTitle(m.Grade, m.Specialty)}", doing, tag, null, () => StaffClicked?.Invoke(member));
    }

    // ------------------------------------------------------------------ today

    private void TodayPage(CareSpace room)
    {
        var day = _sim.RoomToday(room);
        _dayValues[0].Text = day.Admitted.ToString();
        _dayValues[1].Text = day.WentHome.ToString();
        _dayValues[2].Text = day.AverageStayDays <= 0 ? "–" : day.AverageStayDays >= 1 ? $"{day.AverageStayDays:0.0} d" : $"{day.AverageStayDays * 24:0} h";
        _dayValues[3].Text = day.Died.ToString();
        _dayValues[3].AddThemeColorOverride("font_color", day.Died > 0 ? UiStyle.Red : UiStyle.TextDark);

        _listHeading.Text = "ROOM LOG";
        var log = _sim.RoomLog(room.Id).Take(25).ToList();
        _listNote.Text = log.Count == 0
            ? "[color=#5B6B79]Nothing has happened here yet.[/color]"
            : string.Join("\n", log.Select(l =>
            {
                var colour = l.Kind switch
                {
                    RoomLogKind.Died or RoomLogKind.RedMonitor => ClinicalText.RedHex,
                    RoomLogKind.WentHome => "#2E8B57",
                    RoomLogKind.Admitted => "#1F5FBF",
                    _ => "#17232E",
                };
                var when = l.Time.Date == _sim.Now.Date ? l.Time.ToString("HH:mm") : l.Time.ToString("ddd HH:mm");
                return $"[b]{when}[/b]  [color={colour}]{ClinicalText.Escape(l.Text)}[/color]";
            }));

        // What the patients here have now.
        var illnesses = Occupants(room)
            .Where(o => o.Patient?.WorkingDiagnosis is not null)
            .GroupBy(o => o.Patient!.WorkingDiagnosis!.ConditionId)
            .Select(g => (Name: _sim.Database.Conditions.TryGetValue(g.Key, out var c) ? c.Name : g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
        if (illnesses.Count > 0)
        {
            _secondHeading.Text = "HERE NOW WITH";
            _secondNote.Text = string.Join("  ·  ", illnesses.Select(x => $"[color=#17232E]{ClinicalText.Escape(x.Name)}[/color] {x.Count}"));
        }
    }
}
