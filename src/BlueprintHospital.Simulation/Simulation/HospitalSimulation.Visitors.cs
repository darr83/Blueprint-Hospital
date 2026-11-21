using BlueprintHospital.Simulation.Core;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>A ward's visiting hours, and how many visitors may be at a bed at once.</summary>
/// <param name="FromHour">Visiting starts on this hour (0-23).</param>
/// <param name="ToHour">Visiting ends on this hour (1-24); visitors still there are asked to go.</param>
/// <param name="AtOnce">The most visitors at one bed at a time.</param>
public sealed record VisitingHours(int FromHour, int ToHour, int AtOnce)
{
    public bool IsOpenAt(DateTime time) => time.Hour >= FromHour && time.Hour < ToHour;

    /// <summary>"11:00 to 20:00".</summary>
    public string Text => $"{FromHour:00}:00 to {(ToHour == 24 ? "midnight" : $"{ToHour:00}:00")}";
}

/// <summary>A visit someone means to make today.</summary>
internal sealed record PlannedVisit(int PatientId, int? BabyId, DateTime At, bool Partner, int Seed);

/// <summary>
/// Visitors. Each admitted patient gets 0 to 3 visits a day (children and new mums more), at times inside
/// their ward's visiting hours, which the player sets per ward in the room panel. A visitor comes in at the
/// front, checks in at reception, walks or takes the lift to the ward, sits on the chair by the bed (or stands,
/// if someone else has it) for half an hour to an hour and a half, perhaps fetches a cup of tea or uses the
/// toilet, and goes home. A visit cheers the patient up a little. ICU, HDU and the neonatal unit allow two
/// visitors at a time; partners on the maternity ward may come at any time.
///
/// Visitors and the day's plans are not saved (a load plans the rest of the day again); the visiting hours are.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>How much one visit cheers a patient up (happiness runs from 0 to 1).</summary>
    public const double VisitHappiness = 0.12;

    /// <summary>Where happiness drifts back to, slowly, without visits.</summary>
    public const double UsualHappiness = 0.6;

    /// <summary>How fast happiness drifts back each hour.</summary>
    private const double HappinessDriftPerHour = 0.015;

    private readonly Dictionary<string, VisitingHours> _visitingHours = new();
    private readonly List<PlannedVisit> _plannedVisits = new();
    private readonly HashSet<(int Id, DateTime Day)> _plannedFor = new();
    private readonly List<Visitor> _visitors = new();
    private int _nextVisitorId = Visitor.IdOffset;
    private GameRandom? _visitRandom;

    /// <summary>A separate random stream, so visitors don't change anything else that happens.</summary>
    private GameRandom VisitRandom => _visitRandom ??= new GameRandom(_options.Seed + 7_368_787);

    private static readonly string[] WomenNames = { "Margaret", "Susan", "Linda", "Karen", "Julie", "Sarah", "Emma", "Laura", "Rachel", "Priya", "Aisha", "Joanne", "Claire", "Sophie", "Pat", "Jean", "Ellie", "Chloe", "Nadia", "Bev" };

    private static readonly string[] MenNames = { "John", "David", "Peter", "Steve", "Ian", "James", "Daniel", "Chris", "Mark", "Tom", "Imran", "Gareth", "Paul", "Mohammed", "Keith", "Ray", "Josh", "Liam", "Harry", "Dev" };

    private static readonly string[] FriendSurnames = { "Taylor", "Brown", "Wilson", "Evans", "Thomas", "Roberts", "Walker", "Wright", "Hughes", "Patel", "Khan", "Green", "Hall", "Wood", "Clarke", "Jackson" };

    /// <summary>Visitors in the hospital right now.</summary>
    public IReadOnlyList<Visitor> Visitors => _visitors;

    public Visitor? FindVisitor(int id) => _visitors.FirstOrDefault(v => v.Id == id);

    /// <summary>Visitors with a patient (or on their way to them) right now.</summary>
    public IEnumerable<Visitor> VisitorsOf(Patient patient) => _visitors.Where(v => v.PatientId == patient.Id && v.BabyId is null && v.Activity != VisitorActivity.Leaving);

    /// <summary>Visitors on a ward right now (arrived at the bedside, or away for a cup of tea).</summary>
    public IEnumerable<Visitor> VisitorsOnWard(CareSpace ward) =>
        _visitors.Where(v => v.WardId == ward.Id && v.Activity is VisitorActivity.Visiting or VisitorActivity.GoingToBed or VisitorActivity.Away);

    /// <summary>Visits planned for a patient that haven't happened yet today.</summary>
    public int VisitsStillToCome(Patient patient) => _plannedVisits.Count(v => v.PatientId == patient.Id && v.BabyId is null);

    /// <summary>Whether a room has visiting hours: wards and units with beds (or incubators) for admitted patients.</summary>
    public bool HasVisiting(CareSpace room) => room.IsWard && room.HasBeds;

    /// <summary>A ward's visiting hours: what the player set, or the usual ones for that kind of ward.</summary>
    public VisitingHours VisitingHoursOf(CareSpace ward) =>
        _visitingHours.TryGetValue(ward.Id, out var hours) ? hours : DefaultVisitingHours(ward);

    /// <summary>The usual visiting hours: afternoons and evenings, ICU and HDU a little shorter, two at a bed in critical care and on the neonatal unit.</summary>
    public VisitingHours DefaultVisitingHours(CareSpace ward)
    {
        if (IsChildrensWard(ward))
        {
            return new VisitingHours(9, 19, 3);
        }

        return UnitOf(ward) switch
        {
            WardUnit.IntensiveCare or WardUnit.HighDependency => new VisitingHours(11, 19, 2),
            WardUnit.Neonatal => new VisitingHours(10, 20, 2),
            _ when IsMaternityWard(ward) => new VisitingHours(14, 20, 2),
            _ => new VisitingHours(11, 20, 3),
        };
    }

    /// <summary>Sets a ward's visiting hours (the room panel). Hours are clamped to a sensible range.</summary>
    public void SetVisitingHours(string wardId, int fromHour, int toHour, int atOnce)
    {
        if (FindSpace(wardId) is not { } ward || !HasVisiting(ward))
        {
            return;
        }

        fromHour = Math.Clamp(fromHour, 0, 23);
        toHour = Math.Clamp(toHour, fromHour + 1, 24);
        atOnce = Math.Clamp(atOnce, 1, 4);
        var hours = new VisitingHours(fromHour, toHour, atOnce);
        var before = VisitingHoursOf(ward);
        _visitingHours[wardId] = hours;
        if (before.FromHour != hours.FromHour || before.ToHour != hours.ToHour)
        {
            // The "Visiting times" sign on the ward wall shows the new hours.
            Events.Publish(new LayoutChanged());
        }
    }

    /// <summary>Visiting hours the player has set, for saving.</summary>
    internal Dictionary<string, VisitingHours> CaptureVisitingHours() =>
        _visitingHours.Where(h => _spaces.ContainsKey(h.Key)).OrderBy(h => h.Key, StringComparer.Ordinal).ToDictionary(h => h.Key, h => h.Value);

    internal void RestoreVisitingHours(Dictionary<string, VisitingHours>? saved)
    {
        _visitingHours.Clear();
        foreach (var (id, hours) in saved ?? new())
        {
            SetVisitingHours(id, hours.FromHour, hours.ToHour, hours.AtOnce);
        }
    }

    /// <summary>A mother and baby ward (the maternity ward with cots), where partners can visit at any time.</summary>
    public bool IsMaternityWard(CareSpace ward) =>
        ward.IsWard && Database.RoomTemplates.TryGetValue(ward.TemplateId, out var t) && t.Furniture.Contains("cot");

    private bool IsChildrensWard(CareSpace ward) =>
        ward.IsWard && Database.RoomTemplates.TryGetValue(ward.TemplateId, out var t) && t.Furniture.Contains("parent_bed");

    private void UpdateVisitors(double dt)
    {
        if (!_options.LivelyPeople)
        {
            return;
        }

        DriftHappiness(dt);
        PlanVisits();
        StartDueVisits();

        foreach (var visitor in _visitors.ToList())
        {
            visitor.Move(dt);
            Progress(visitor);
        }

        _visitors.RemoveAll(v => v.Activity == VisitorActivity.Leaving && !v.IsMoving && Chebyshev(v.Tile, WalkInSpawn) <= 1);
    }

    private void DriftHappiness(double dt)
    {
        var step = HappinessDriftPerHour * dt / 3600.0;
        foreach (var patient in _patients.Values)
        {
            if (patient.Happiness > UsualHappiness)
            {
                patient.Happiness = Math.Max(UsualHappiness, patient.Happiness - step);
            }
            else if (patient.Happiness < UsualHappiness)
            {
                patient.Happiness = Math.Min(UsualHappiness, patient.Happiness + step);
            }
        }
    }

    // ---------------------------------------------------------------- planning

    /// <summary>Plans the day's visits for each patient on a ward (and each baby on a neonatal unit) the first time they are seen that day.</summary>
    private void PlanVisits()
    {
        var today = Now.Date;
        _plannedFor.RemoveWhere(p => p.Day < today);
        foreach (var patient in _patients.Values)
        {
            if (patient.Stage != PatientStage.OnWard || patient.CareSpaceId is not { } wardId || FindSpace(wardId) is not { } ward
                || !HasVisiting(ward) || !_plannedFor.Add((patient.Id, today)))
            {
                continue;
            }

            // Mostly 0 to 3 visits; children (whose parents are already there) get grandparents and the like,
            // and new mums their partner.
            var roll = VisitRandom.NextDouble();
            var count = roll < 0.2 ? 0 : roll < 0.55 ? 1 : roll < 0.85 ? 2 : 3;
            if (patient.Age < AdultAge)
            {
                count += 1;
            }

            var partner = patient.Maternity && IsMaternityWard(ward);
            if (partner)
            {
                PlanVisit(patient.Id, null, ward, partner: true);
                count = Math.Max(1, count);
            }

            for (var i = 0; i < count; i++)
            {
                PlanVisit(patient.Id, null, ward, partner: false);
            }
        }

        foreach (var baby in _babies.Where(b => b.Place == BabyPlace.NeonatalUnit && b.UnitId is not null))
        {
            if (FindSpace(baby.UnitId!) is not { } unit || !_plannedFor.Add((-baby.Id, today)))
            {
                continue;
            }

            // Dad (or a grandparent) comes to the incubator once or twice a day.
            for (var i = 0; i < 1 + (VisitRandom.Chance(0.5) ? 1 : 0); i++)
            {
                PlanVisit(baby.MotherId, baby.Id, unit, partner: false);
            }
        }
    }

    private void PlanVisit(int patientId, int? babyId, CareSpace ward, bool partner)
    {
        var hours = VisitingHoursOf(ward);
        var (from, to) = partner ? (Math.Min(hours.FromHour, 9), Math.Max(hours.ToHour, 21)) : (hours.FromHour, hours.ToHour);
        var start = Now.Date.AddHours(from);
        var end = Now.Date.AddHours(to).AddMinutes(-40);
        if (start < Now)
        {
            start = Now;
        }

        if (end <= start)
        {
            return;
        }

        var at = start + TimeSpan.FromMinutes(VisitRandom.Range(0.0, (end - start).TotalMinutes));
        _plannedVisits.Add(new PlannedVisit(patientId, babyId, at, partner, VisitRandom.Range(0, 1_000_000)));
    }

    /// <summary>Sends in the visitors whose time has come, if the patient is still there and visiting is open.</summary>
    private void StartDueVisits()
    {
        foreach (var plan in _plannedVisits.Where(v => v.At <= Now).ToList())
        {
            _plannedVisits.Remove(plan);
            if (!_patients.TryGetValue(plan.PatientId, out var patient))
            {
                continue;
            }

            CareSpace? ward;
            if (plan.BabyId is { } babyId)
            {
                ward = FindBaby(babyId) is { Place: BabyPlace.NeonatalUnit, UnitId: { } unitId } ? FindSpace(unitId) : null;
            }
            else
            {
                ward = patient.Stage == PatientStage.OnWard && patient.CareSpaceId is { } wardId ? FindSpace(wardId) : null;
            }

            if (ward is null || !HasVisiting(ward) || (!plan.Partner && !VisitingHoursOf(ward).IsOpenAt(Now)) || (MainWaitingRoom is null && !SpacesOfKind(CareSpaceKind.OutpatientReception).Any()))
            {
                continue;
            }

            var here = _visitors.Count(v => v.PatientId == plan.PatientId && v.BabyId == plan.BabyId && v.Activity != VisitorActivity.Leaving);
            if (here >= VisitingHoursOf(ward).AtOnce)
            {
                // Full at the bedside: they come a little later instead, if visiting is still open.
                if (Now.AddMinutes(45).Hour < VisitingHoursOf(ward).ToHour || plan.Partner)
                {
                    _plannedVisits.Add(plan with { At = Now.AddMinutes(45) });
                }

                continue;
            }

            SendInVisitor(patient, ward, plan);
        }
    }

    private void SendInVisitor(Patient patient, CareSpace ward, PlannedVisit plan)
    {
        var r = new GameRandom(plan.Seed);
        var baby = plan.BabyId is { } id ? FindBaby(id) : null;
        var (relation, sex, age, family) = baby is not null ? BabyVisitor(r) : plan.Partner ? PartnerOf(patient) : RelativeOf(patient, r);
        var names = sex == Sex.Female ? WomenNames : MenNames;
        var visitor = new Visitor
        {
            Id = _nextVisitorId++,
            PatientId = patient.Id,
            BabyId = plan.BabyId,
            FirstName = names[r.Range(0, names.Length - 1)],
            Surname = family ? (baby?.Surname ?? patient.Surname) : FriendSurnames[r.Range(0, FriendSurnames.Length - 1)],
            Sex = sex,
            Age = Math.Max(5, age),
            Relation = relation,
            Partner = plan.Partner,
            Flowers = !plan.Partner && baby is null && r.Chance(0.35),
            WardId = ward.Id,
            WalkSpeed = Agent_WalkSpeedFor(age),
            LeaveAt = Now + TimeSpan.FromMinutes(plan.Partner ? r.Range(120, 240) : r.Range(35, 90)),
        };
        visitor.PlaceAt(WalkInSpawn);
        visitor.StatusText = $"Coming to visit {(baby?.FullName ?? patient.FullName)}";
        if (r.Chance(0.55))
        {
            visitor.BreakAt = Now + TimeSpan.FromMinutes(r.Range(20, 50));
        }

        _visitors.Add(visitor);
        Head(visitor, CheckInSpot(visitor));
    }

    private static double Agent_WalkSpeedFor(int age) => Agents.Agent.DefaultWalkSpeed * (age >= 75 ? 0.7 : age >= 65 ? 0.85 : 1.0);

    /// <summary>Who comes to see a patient: family for the most part, sometimes a friend.</summary>
    private static (string Relation, Sex Sex, int Age, bool Family) RelativeOf(Patient patient, GameRandom r)
    {
        var age = patient.Age;
        var roll = r.Range(0, 99);
        if (age < AdultAge)
        {
            return roll < 50 ? (roll < 25 ? "grandma" : "grandad", roll < 25 ? Sex.Female : Sex.Male, 55 + r.Range(0, 20), true)
                : roll < 80 ? ("auntie", Sex.Female, 28 + r.Range(0, 15), true)
                : (roll < 90 ? "brother" : "sister", roll < 90 ? Sex.Male : Sex.Female, Math.Max(6, age + r.Range(-5, 6)), true);
        }

        if (age >= 60 && roll < 55)
        {
            var son = roll < 25;
            return (son ? "son" : "daughter", son ? Sex.Male : Sex.Female, age - 25 - r.Range(0, 10), true);
        }

        if (roll < 75 && age >= 25)
        {
            var husband = patient.Sex == Sex.Female ? r.Chance(0.85) : r.Chance(0.1);
            return (husband ? (patient.Sex == Sex.Female ? "husband" : "partner") : (patient.Sex == Sex.Male ? "wife" : "partner"),
                husband ? Sex.Male : Sex.Female, Math.Max(20, age + r.Range(-6, 6)), true);
        }

        if (roll < 88 && age < 60)
        {
            var mum = r.Chance(0.6);
            return (mum ? "mum" : "dad", mum ? Sex.Female : Sex.Male, Math.Min(90, age + 24 + r.Range(0, 8)), true);
        }

        var woman = r.Chance(0.5);
        return ("friend", woman ? Sex.Female : Sex.Male, Math.Max(18, age + r.Range(-8, 8)), false);
    }

    private static (string Relation, Sex Sex, int Age, bool Family) PartnerOf(Patient patient) =>
        ("partner", Sex.Male, Math.Max(18, patient.Age + 2), true);

    private static (string Relation, Sex Sex, int Age, bool Family) BabyVisitor(GameRandom r)
    {
        var roll = r.Range(0, 99);
        return roll < 60 ? ("dad", Sex.Male, 24 + r.Range(0, 14), true)
            : roll < 80 ? ("nan", Sex.Female, 50 + r.Range(0, 15), true)
            : ("grandad", Sex.Male, 52 + r.Range(0, 15), true);
    }

    // ---------------------------------------------------------------- the visit

    private void Progress(Visitor visitor)
    {
        if (visitor.Activity == VisitorActivity.Leaving)
        {
            if (!visitor.IsMoving && Chebyshev(visitor.Tile, WalkInSpawn) > 1)
            {
                Head(visitor, WalkInSpawn, force: true);
            }

            return;
        }

        // The patient has gone home, moved off the ward or died; or the baby has gone home with mum.
        if (VisitOver(visitor) is { } why)
        {
            Leave(visitor, why);
            return;
        }

        if (visitor.Activity == VisitorActivity.Away)
        {
            // The café or toilet trip looks after them; if it has finished without sending them back, go back.
            if (TripOf(visitor) is null && ToiletTripOf(visitor) is null)
            {
                visitor.Activity = VisitorActivity.GoingToBed;
                visitor.Heading = null;
            }

            return;
        }

        var ward = FindSpace(visitor.WardId)!;
        var hours = VisitingHoursOf(ward);
        if (!visitor.Partner && !hours.IsOpenAt(Now) && visitor.Activity is VisitorActivity.GoingToBed or VisitorActivity.Visiting)
        {
            Leave(visitor, "Visiting time was over.");
            return;
        }

        switch (visitor.Activity)
        {
            case VisitorActivity.Arriving when !visitor.IsMoving:
                visitor.Activity = VisitorActivity.CheckingIn;
                visitor.Until = Now + TimeSpan.FromSeconds(VisitRandom.Range(30, 70));
                visitor.StatusText = $"Asking at reception for {ward.Name}";
                break;

            case VisitorActivity.CheckingIn when Now >= visitor.Until:
                visitor.Activity = VisitorActivity.GoingToBed;
                visitor.Heading = null;
                visitor.StatusText = $"On the way to {ward.Name}";
                break;

            case VisitorActivity.GoingToBed:
                GoToBedside(visitor, ward);
                break;

            case VisitorActivity.Visiting:
                StayAtBedside(visitor, ward);
                break;
        }
    }

    /// <summary>Why a visit has to end early, or null while the patient (or baby) is still there to visit.</summary>
    private string? VisitOver(Visitor visitor)
    {
        if (!_patients.TryGetValue(visitor.PatientId, out var patient))
        {
            return "They had gone home.";
        }

        if (visitor.BabyId is { } babyId)
        {
            return FindBaby(babyId) is { Place: BabyPlace.NeonatalUnit } baby && baby.UnitId == visitor.WardId ? null : "The baby had left the unit.";
        }

        if (!patient.IsAlive)
        {
            return "Sadly, they had died.";
        }

        return patient.Stage == PatientStage.OnWard && patient.CareSpaceId == visitor.WardId ? null : "They had left the ward.";
    }

    private void GoToBedside(Visitor visitor, CareSpace ward)
    {
        var spot = visitor.Spot is { } chosen && !IsSpotTaken(chosen, visitor) ? chosen : BedsideSpot(visitor, ward);
        visitor.Spot = spot;
        if (visitor.Tile == spot && !visitor.IsMoving)
        {
            visitor.Activity = VisitorActivity.Visiting;
            if (visitor.VisitStartedAt is null)
            {
                visitor.VisitStartedAt = Now;
                ArrivedAtBedside(visitor);
            }

            return;
        }

        // A patient walking about the ward (or in the bathroom): wait for them at their bed all the same.
        Head(visitor, spot);
        if (!visitor.IsMoving && visitor.Tile != spot)
        {
            // Can't get there (the ward was rebuilt): stand where they are.
            visitor.Spot = visitor.Tile;
        }
    }

    private void ArrivedAtBedside(Visitor visitor)
    {
        Tally(StatKeys.Visits);
        var patient = _patients[visitor.PatientId];
        if (visitor.BabyId is { } babyId && FindBaby(babyId) is { } baby)
        {
            visitor.StatusText = $"Visiting {baby.FirstName} on {FindSpace(visitor.WardId)?.Name}";
            baby.Log(Now, $"Their {visitor.Relation}, {visitor.FirstName}, came to see them.");
            return;
        }

        patient.Happiness = Math.Min(1, patient.Happiness + VisitHappiness);
        patient.VisitsReceived++;
        visitor.StatusText = $"Visiting {patient.FullName}";
        var gift = visitor.Flowers ? " and brought flowers" : string.Empty;
        patient.Log(Now, $"Their {visitor.Relation}, {visitor.FirstName}, came to visit{gift}.");
    }

    private void StayAtBedside(Visitor visitor, CareSpace ward)
    {
        if (Now >= visitor.LeaveAt)
        {
            Leave(visitor, null);
            return;
        }

        // Someone else is in the way (or the bed has been moved): find another spot.
        if (visitor.IsMoving || visitor.Spot is not { } spot || visitor.Tile != spot)
        {
            visitor.Activity = VisitorActivity.GoingToBed;
            return;
        }

        if (visitor.BreakAt is { } breakAt && Now >= breakAt && Now + TimeSpan.FromMinutes(20) < visitor.LeaveAt)
        {
            visitor.BreakAt = null;
            TakeVisitorBreak(visitor);
        }
    }

    /// <summary>Halfway through a visit: a cup of tea from the café (or a vending machine) or the toilet.</summary>
    private void TakeVisitorBreak(Visitor visitor)
    {
        var cafes = RefreshmentRooms.ToList();
        var toilets = ToiletRooms.Where(r => UsableBy(r, staff: false)).ToList();
        visitor.Activity = VisitorActivity.Away;
        if (cafes.Count > 0 && (toilets.Count == 0 || VisitRandom.Chance(0.6)))
        {
            StartTrip(visitor, cafes);
            visitor.StatusText = "Getting a cup of tea";
        }
        else if (toilets.Count > 0)
        {
            StartToiletTrip(visitor, toilets, preferAccessible: visitor.Age >= AccessibleToiletAge, staff: false);
            visitor.StatusText = "Gone to the toilet";
        }

        if (TripOf(visitor) is null && ToiletTripOf(visitor) is null)
        {
            visitor.Activity = VisitorActivity.Visiting;
        }
    }

    /// <summary>Back to the bedside after a cup of tea or the toilet.</summary>
    private void VisitorBack(Visitor visitor)
    {
        visitor.Activity = VisitorActivity.GoingToBed;
        visitor.Heading = null;
        var patient = _patients.GetValueOrDefault(visitor.PatientId);
        visitor.StatusText = patient is null ? "Visiting" : $"Visiting {patient.FullName}";
        if (visitor.Spot is { } spot)
        {
            Head(visitor, spot, force: true);
        }
    }

    private void Leave(Visitor visitor, string? why)
    {
        _trips.Remove(visitor);
        _toiletTrips.Remove(visitor);
        visitor.Activity = VisitorActivity.Leaving;
        visitor.StatusText = "Going home";
        visitor.Spot = null;
        if (why is not null && visitor.VisitStartedAt is null && _patients.TryGetValue(visitor.PatientId, out var patient) && visitor.BabyId is null && patient.IsAlive)
        {
            patient.Log(Now, $"Their {visitor.Relation}, {visitor.FirstName}, came to visit but {why.TrimEnd('.').ToLowerInvariant()}.");
        }

        Head(visitor, WalkInSpawn, force: true);
    }

    // ---------------------------------------------------------------- where they go

    /// <summary>Where a visitor stops to ask the way: the outpatients desk nearest the ward if there is one, else the A&amp;E reception desk.</summary>
    private GridCoord CheckInSpot(Visitor visitor)
    {
        var ward = FindSpace(visitor.WardId);
        var target = ward?.Rect ?? new GridRect(EntranceTile.X, EntranceTile.Y, 1, 1);
        var reception = SpacesOfKind(CareSpaceKind.OutpatientReception)
            .Where(IsStaffed)
            .OrderBy(r => Math.Abs(r.Rect.X - target.X) + Math.Abs(r.Rect.Y - target.Y))
            .FirstOrDefault();
        if (reception is not null)
        {
            // Beside the clinic queue, not in it.
            var queue = reception.QueueTiles.Count > 0 ? reception.QueueTiles[0] : reception.PatientSpot;
            return FreeSpot(visitor, NearbyFloor(queue, reception, 1));
        }

        if (MainWaitingRoom is { } waiting)
        {
            // At the end of the reception desk, out of the way of patients booking in.
            return FreeSpot(visitor, NearbyFloor(waiting.PatientSpot, waiting, 1));
        }

        return WalkInSpawn;
    }

    /// <summary>A walkable tile in a room a step or two from <paramref name="tile"/>, picked to one side.</summary>
    private GridCoord NearbyFloor(GridCoord tile, CareSpace room, int step)
    {
        foreach (var (dx, dy) in new[] { (step, 0), (-step, 0), (step, step), (-step, step), (0, step) })
        {
            var t = new GridCoord(tile.X + dx, tile.Y + dy);
            if (room.Rect.Contains(t) && Nav.IsWalkable(t) && !IsSeat(t))
            {
                return t;
            }
        }

        return tile;
    }

    /// <summary>The chair by the patient's bed (or by the baby's incubator) if it's free, otherwise somewhere to stand beside the bed.</summary>
    private GridCoord BedsideSpot(Visitor visitor, CareSpace ward)
    {
        var bed = BedFor(visitor, ward);
        if (bed is null)
        {
            return FreeSpot(visitor, ward.StaffSpot);
        }

        var chairs = BedsideChairs(ward, bed).Where(c => !IsSpotTaken(c, visitor)).ToList();
        if (chairs.Count > 0)
        {
            return chairs[0];
        }

        // Stand beside the bed, on the side away from the staff at its foot.
        var near = Neighbours(bed.PatientSpot)
            .Concat(new[] { new GridCoord(bed.PatientSpot.X - 2, bed.PatientSpot.Y), new GridCoord(bed.PatientSpot.X + 2, bed.PatientSpot.Y) })
            .Where(t => ward.Rect.Contains(t) && !ward.Rect.IsOnEdge(t) && Nav.IsWalkable(t) && t != bed.StaffSpot && !IsBedTile(ward, t) && !IsSpotTaken(t, visitor))
            .OrderBy(t => Chebyshev(t, bed.PatientSpot))
            .Cast<GridCoord?>()
            .FirstOrDefault();
        return near ?? FreeSpot(visitor, bed.StaffSpot);
    }

    private WardBed? BedFor(Visitor visitor, CareSpace ward)
    {
        if (visitor.BabyId is { } babyId)
        {
            return FindBaby(babyId) is { CotIndex: { } cot } && cot < ward.Beds.Count ? ward.Beds[cot] : null;
        }

        return _patients.TryGetValue(visitor.PatientId, out var patient) ? BedOf(patient) : null;
    }

    /// <summary>The visitor's chair (or chairs) beside a bed: chairs whose middle is within a couple of tiles of the bed's middle.</summary>
    public IEnumerable<GridCoord> BedsideChairs(CareSpace ward, WardBed bed) =>
        ward.Furniture.Where(f => f.ItemId == "visitor_chair" && Math.Abs(f.CentreX - (bed.PatientSpot.X + 0.5)) <= 2.6 && Math.Abs(f.CentreY - (bed.PatientSpot.Y + 0.5)) <= 3)
            .OrderBy(f => Math.Abs(f.CentreX - (bed.PatientSpot.X + 0.5)) + Math.Abs(f.CentreY - (bed.PatientSpot.Y + 0.5)))
            .Select(f => f.Tile);

    /// <summary>Whether a tile lies under a bed (or an incubator) on the ward.</summary>
    private static bool IsBedTile(CareSpace ward, GridCoord tile) =>
        ward.Furniture.Any(f => f.ItemId is "hospital_bed" or "incubator" or "cot" or "parent_bed"
                                && tile.X + 0.5 > f.X && tile.X + 0.5 < f.X + f.Width && tile.Y + 0.5 > f.Y && tile.Y + 0.5 < f.Y + f.Length);

    private void Head(Visitor visitor, GridCoord to, bool force = false)
    {
        if (!force && visitor.Heading == to && (visitor.IsMoving || visitor.Tile == to))
        {
            return;
        }

        visitor.Heading = to;
        if (visitor.Tile == to)
        {
            visitor.StopMoving();
            return;
        }

        var path = PathFor(visitor, to);
        if (path.Count > 0)
        {
            visitor.FollowPath(path);
        }
    }
}
