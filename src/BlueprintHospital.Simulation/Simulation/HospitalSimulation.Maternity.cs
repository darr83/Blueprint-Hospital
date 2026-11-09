using System.Globalization;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>Births and babies so far.</summary>
public sealed class MaternityStats
{
    public int Births { get; set; }

    public int Caesareans { get; set; }

    public int NeonatalAdmissions { get; set; }

    /// <summary>Babies sent to another hospital's neonatal unit.</summary>
    public int NeonatalTransfers { get; set; }

    /// <summary>Women sent on to another maternity unit because this one couldn't take them.</summary>
    public int Diverted { get; set; }
}

/// <summary>
/// Maternity. Women in labour, or with a pregnancy emergency, come straight to the maternity unit rather
/// than A&amp;E, and only once it is open: Maternity unlocked, a delivery room with a midwife, and a mother
/// and baby ward. Each woman is given a bed on the mother and baby ward, and goes from there to a delivery
/// room (a midwife delivers the baby) or to the C-section theatre (a surgeon and an anaesthetist). Some
/// labours stop progressing and end in an emergency caesarean section. After the birth mum and baby come
/// back to her bed, the baby in the cot beside her, and go home together after the ward round.
///
/// Babies who are premature or poorly are carried to the neonatal unit by one of its nurses and looked
/// after in an incubator until they are well, then go back to mum (or home, if she has already gone).
/// Without a neonatal unit with room and a nurse, they are transferred to another hospital; women in
/// preterm labour are sent to a unit that has one. If there's no free bed, delivery room or (for a
/// caesarean) C-section theatre, women are sent on to a neighbouring maternity unit.
/// </summary>
public sealed partial class HospitalSimulation
{
    public const string MaternityDepartmentId = "maternity";

    /// <summary>Paid for each birth in a delivery room (the stay on the ward is paid as an admission).</summary>
    public const int BirthTariff = 1_900;

    /// <summary>Paid for each caesarean section.</summary>
    public const int CaesareanTariff = 3_800;

    /// <summary>Paid for each day a baby spends on the neonatal unit.</summary>
    public const int NeonatalDayTariff = 1_100;

    /// <summary>Babies one neonatal nurse looks after.</summary>
    public const int BabiesPerNeonatalNurse = 2;

    /// <summary>Mum and baby have skin-to-skin in the delivery room for this long before going back to the ward.</summary>
    public static readonly TimeSpan SkinToSkin = TimeSpan.FromMinutes(45);

    /// <summary>A baby is well enough to leave the neonatal unit once below this.</summary>
    private const double NeonatalFitSeverity = 0.15;

    /// <summary>Babies who have gone are kept (for their records) for this long.</summary>
    private static readonly TimeSpan KeepDepartedBabies = TimeSpan.FromDays(3);

    private readonly List<Baby> _babies = new();
    private int _nextBabyId = 1;
    private bool _maternityWasOpen;
    private readonly HashSet<string> _neonatalAlerts = new();

    public MaternityStats MaternityStats { get; private set; } = new();

    /// <summary>Babies born here who are still in the hospital or left in the last few days.</summary>
    public IReadOnlyList<Baby> Babies => _babies;

    public Baby? FindBaby(int id) => _babies.FirstOrDefault(b => b.Id == id);

    public IEnumerable<Baby> BabiesOf(Patient mother) => _babies.Where(b => b.MotherId == mother.Id);

    private ArrivalProfile? MaternityArrivals => Database.Arrivals.Values.FirstOrDefault(a => a.Department == MaternityDepartmentId);

    // ---------------------------------------------------------------- units

    public IEnumerable<CareSpace> DeliveryRooms => SpacesOfKind(CareSpaceKind.DeliveryRoom);

    /// <summary>A maternity ward that isn't the neonatal unit: mothers and babies recover here together.</summary>
    public bool IsMotherAndBabyWard(CareSpace ward) => ward.IsWard && ward.DepartmentId == MaternityDepartmentId && UnitOf(ward) == WardUnit.None;

    public IEnumerable<CareSpace> MotherAndBabyWards => Wards.Where(IsMotherAndBabyWard);

    public IEnumerable<CareSpace> NeonatalUnits => Wards.Where(w => UnitOf(w) == WardUnit.Neonatal);

    /// <summary>A neonatal unit takes babies while it has a nurse posted to it.</summary>
    public bool IsNeonatalUnitOpen(CareSpace unit) =>
        UnitOf(unit) == WardUnit.Neonatal && _staff.Any(s => s.BaseRoomId == unit.Id && s.Role == StaffRole.Nurse);

    /// <summary>The babies in (or on their way to) a neonatal unit's incubators.</summary>
    public IEnumerable<Baby> BabiesIn(CareSpace unit) =>
        _babies.Where(b => b.IsInHospital && b.UnitId == unit.Id && b.CotIndex is not null);

    /// <summary>What the maternity unit still needs before women can come, in plain words (empty when open).</summary>
    public IReadOnlyList<string> MaternityClosedReasons
    {
        get
        {
            var missing = new List<string>();
            if (!IsUnlocked(MaternityDepartmentId))
            {
                missing.Add("the Maternity department unlocked");
                return missing;
            }

            if (!DeliveryRooms.Any())
            {
                missing.Add("a delivery room");
            }
            else if (!DeliveryRooms.Any(IsStaffed))
            {
                missing.Add("a midwife posted to a delivery room");
            }

            if (!MotherAndBabyWards.Any())
            {
                missing.Add("a mother and baby ward");
            }

            return missing;
        }
    }

    public bool IsMaternityOpen => MaternityClosedReasons.Count == 0;

    private void AnnounceMaternityOpening()
    {
        var open = IsMaternityOpen;
        if (open == _maternityWasOpen)
        {
            return;
        }

        _maternityWasOpen = open;
        if (open)
        {
            Notifications.Add(Now, NotificationLevel.Info, "The maternity unit is open: women in labour will start coming in.");
        }
        else if (IsUnlocked(MaternityDepartmentId))
        {
            Notifications.Add(Now, NotificationLevel.Warning, $"The maternity unit has closed to new women in labour: it needs {Join(MaternityClosedReasons)}.");
        }
    }

    // ---------------------------------------------------------------- arrivals

    private void GenerateMaternityArrivals(double dt)
    {
        if (MaternityArrivals is not { } profile || !IsMaternityOpen)
        {
            return;
        }

        var expected = profile.RateAt(Now) * _options.ArrivalRateMultiplier * dt / 3600.0;
        var count = Random.Poisson(expected);
        for (var i = 0; i < count; i++)
        {
            AdmitToMaternity(_generator.GenerateMaternity(Now));
        }
    }

    /// <summary>
    /// Brings a woman in labour (or with a pregnancy emergency) to the maternity unit. Returns null if
    /// she had to be sent to another unit. Used by arrivals and by scenarios/tests.
    /// </summary>
    public Patient? SpawnMaternityPatient(string? conditionId = null) => AdmitToMaternity(_generator.GenerateMaternity(Now, conditionId));

    private Patient? AdmitToMaternity(Patient patient)
    {
        var condition = patient.Condition;
        var maternity = condition.Maternity!;
        var operation = condition.Operation!;
        var free = FreeMotherAndBabyBed();
        var divert = !IsMaternityOpen ? $"this maternity unit needs {Join(MaternityClosedReasons)}"
            : operation.Room == CareSpaceKind.Theatre && !HasTheatreFor(MaternityDepartmentId) ? "she needs a caesarean section and there is no staffed C-section theatre here"
            : maternity.NeonatalChance >= 0.99 && !NeonatalUnits.Any(IsNeonatalUnitOpen) ? "her baby will need a neonatal unit and there isn't one here"
            : free is null ? "the mother and baby wards are full"
            : null;
        if (divert is not null)
        {
            MaternityStats.Diverted++;
            Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} ({patient.GestationWeeks} weeks pregnant, {condition.Name.ToLowerInvariant()}) was sent to a neighbouring maternity unit: {divert}.");
            return null;
        }

        var (ward, bed, index) = free!.Value;
        _patients[patient.Id] = patient;
        patient.PlaceAt(WalkInSpawn);

        // The midwife assesses her on arrival: she never joins the A&E queue or its four-hour clock.
        patient.LeftEmergencyAt = Now;
        patient.HistoryTaken = true;
        patient.KnownSymptoms.UnionWith(patient.Symptoms);
        patient.Triage = patient.Severity >= 0.45 ? TriageCategory.Red : patient.Severity >= 0.25 ? TriageCategory.Amber : TriageCategory.Green;
        patient.TriagedAt = Now;
        patient.TriageReason = "Maternity triage by the midwife.";
        patient.WorkingDiagnosis = new Diagnosis(condition.Id, 0.95, Now, 0);
        patient.RecordObservations(Now, patient.CurrentObservations());

        bed.OccupantId = patient.Id;
        patient.CareSpaceId = ward.Id;
        patient.BedIndex = index;
        patient.Stage = PatientStage.OnWard;
        patient.PreOp = true;
        patient.AdmittedAt = Now;
        WalkTo(patient, bed.PatientSpot);

        var place = operation.Room == CareSpaceKind.Theatre ? "the C-section theatre" : "a delivery room";
        patient.Log(Now, $"Came in to the maternity unit, {patient.GestationWeeks} weeks pregnant: \"{Database.Symptoms[patient.PresentingComplaint].PatientDescription}\"", important: true);
        patient.Log(Now, $"{condition.DispositionNote} Bed {index + 1} on {ward.Name} while she waits for {place}.");
        Notifications.Add(Now, patient.Triage == TriageCategory.Red ? NotificationLevel.Warning : NotificationLevel.Info,
            $"{patient.FullName} has come in to the maternity unit: {condition.Name.ToLowerInvariant()} at {patient.GestationWeeks} weeks.", patient.Id, ward.Id, patient.Triage);
        AddTask(TaskKind.Surgery, RoleForRoom(operation.Room), patient, operation.Minutes, operation.Name, condition.Id);
        Events.Publish(new PatientArrived(patient));
        return patient;
    }

    private (CareSpace Ward, WardBed Bed, int Index)? FreeMotherAndBabyBed()
    {
        foreach (var ward in MotherAndBabyWards)
        {
            for (var i = 0; i < ward.Beds.Count; i++)
            {
                if (IsVacant(ward.Beds[i]))
                {
                    return (ward, ward.Beds[i], i);
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- the birth

    /// <summary>Whether a delivery room is free for this woman (nobody else in labour or still on the bed).</summary>
    private bool DeliveryRoomFreeFor(CareSpace room, int patientId) =>
        room.Kind != CareSpaceKind.DeliveryRoom || room.OccupantId == patientId || IsVacant(room);

    /// <summary>The midwife (or the surgeon, for a caesarean) has finished: the baby is born, or labour has stopped and she goes to theatre.</summary>
    private void CompleteBirth(StaffMember member, Patient patient)
    {
        var planned = Database.Conditions[patient.WorkingDiagnosis!.ConditionId];
        var operation = planned.Operation!;
        var maternity = planned.Maternity!;
        var room = member.BaseRoomId is { } roomId ? FindSpace(roomId) : null;
        var inTheatre = operation.Room == CareSpaceKind.Theatre;
        var extraNeonatalChance = 0.0;
        var how = operation.Name;

        if (!inTheatre && maternity.EmergencyCaesarean is { } sectionId && Random.Chance(maternity.EmergencyCaesareanChance)
            && Database.Conditions.TryGetValue(sectionId, out var section))
        {
            if (HasTheatreFor(MaternityDepartmentId))
            {
                if (room is not null && room.OccupantId == patient.Id)
                {
                    room.OccupantId = null;
                }

                patient.WorkingDiagnosis = new Diagnosis(section.Id, 0.95, Now, member.Id);
                patient.Log(Now, $"Labour stopped progressing and the baby's heart rate dropped on the CTG. {member.Name} called the obstetric team: {LowerFirst(section.Operation!.Name)}.", important: true);
                Notifications.Add(Now, NotificationLevel.Warning, $"{patient.FullName} needs an emergency caesarean section: her labour has stopped progressing.", patient.Id, room?.Id, patient.Triage);
                AddTask(TaskKind.Surgery, RoleForRoom(section.Operation.Room), patient, section.Operation.Minutes, section.Operation.Name, section.Id);
                return;
            }

            how = "Forceps delivery";
            extraNeonatalChance = 0.15;
            patient.Log(Now, $"Labour stopped progressing. With no C-section theatre here, {member.Name} and the doctor delivered the baby with forceps.", important: true);
        }

        var count = Random.Chance(maternity.TwinsChance) ? 2 : 1;
        var babies = Enumerable.Range(0, count).Select(_ => CreateBaby(patient, how, maternity, extraNeonatalChance)).ToList();
        patient.Delivered = true;
        patient.EffectiveTreatmentGiven = true;
        patient.HadSurgery = inTheatre;
        patient.PreOp = false;
        patient.Severity = Math.Min(patient.Severity, 0.5) * 0.6;
        MaternityStats.Births += count;
        if (inTheatre)
        {
            MaternityStats.Caesareans++;
            Stats.OperationsDone++;
        }

        Book(MoneyCategory.Maternity, inTheatre ? CaesareanTariff : BirthTariff);
        var who = string.Join(" and ", babies.Select(b => $"{b.FirstName} ({(b.Sex == Sex.Female ? "girl" : "boy")}, {b.WeightKg:0.00} kg)"));
        patient.Log(Now, $"{how} by {member.Name} at {Now:HH:mm}. {(count == 2 ? "Twins" : "Baby")}: {who}.", important: true);
        Notifications.Add(Now, NotificationLevel.Info, count == 2
            ? $"{patient.FullName} has had twins: {who}."
            : $"{patient.FullName} has had a baby {(babies[0].Sex == Sex.Female ? "girl" : "boy")}, {babies[0].FirstName} ({babies[0].WeightKg:0.00} kg).", patient.Id);

        // Home after the ward round once the usual stay is up (longer after a caesarean, which comes from the section's condition).
        var stay = planned.LengthOfStayHours;
        patient.DischargeDueAt = Now.AddHours(Random.Range(stay[0], stay[1]));

        if (!inTheatre && room is not null)
        {
            // An hour of skin-to-skin in the delivery room, then back to the ward together.
            room.OccupantId = patient.Id;
            _recovering[patient.Id] = new RecoveryState(patient.Id, Now + SkinToSkin, room.Id);
            patient.Log(Now, $"Skin-to-skin with {(count == 2 ? "the twins" : babies[0].FirstName)} in {room.Name} before going back to the ward.");
        }
        else
        {
            ReturnFromServiceRoom(patient);
        }

        foreach (var baby in babies.Where(b => b.NeedsNeonatalUnit))
        {
            ArrangeNeonatalAdmission(baby, patient);
        }
    }

    private Baby CreateBaby(Patient mother, string delivery, MaternityDefinition maternity, double extraNeonatalChance)
    {
        var names = Database.Names.Values.First();
        var sex = Random.Chance(0.5) ? Sex.Female : Sex.Male;
        var weeks = Math.Max(22, mother.GestationWeeks);

        // About 3.4 kg at 40 weeks, less the earlier the baby comes.
        var expected = weeks >= 37 ? 3.4 + ((weeks - 40) * 0.12) : 3.0 - ((37 - weeks) * 0.2);
        var weight = Math.Round(Math.Clamp(Random.Normal(expected, 0.35), 0.5, 5.2), 2);
        var premature = weeks < 35;
        var needsUnit = premature || Random.Chance(maternity.NeonatalChance + extraNeonatalChance);
        var baby = new Baby
        {
            Id = _nextBabyId++,
            FirstName = Random.Pick(sex == Sex.Female ? names.FemaleFirstNames : names.MaleFirstNames),
            Surname = mother.Surname,
            Sex = sex,
            WeightKg = weight,
            GestationWeeks = weeks,
            BornAt = Now,
            MotherId = mother.Id,
            Delivery = delivery,
            NeedsNeonatalUnit = needsUnit,
            Severity = needsUnit ? Random.Range(0.3, premature ? 0.7 : 0.5) : 0,
        };

        if (needsUnit)
        {
            baby.NeonatalReason = premature
                ? $"Born early at {weeks} weeks: needs an incubator, help with breathing and tube feeding"
                : maternity.NeonatalReasons.Count > 0 ? Random.Pick(maternity.NeonatalReasons) : "Needs a closer eye after a difficult birth";
        }

        var apgar = needsUnit ? Random.Range(5, 7) : Random.Range(9, 10);
        baby.Log(Now, $"Born at {Now:HH:mm} ({LowerFirst(delivery)}), {weeks} weeks, {weight:0.00} kg. Apgar score {apgar}.", important: true);
        if (needsUnit)
        {
            baby.Log(Now, $"Needs the neonatal unit: {baby.NeonatalReason}.", important: true);
        }

        _babies.Add(baby);
        return baby;
    }

    /// <summary>Back on the ward after the birth (from <see cref="UpdateRecovery"/>, or straight from theatre).</summary>
    private static string BackFromBirthNote(CareSpace room) => $"Back to the ward from {room.Name} with the baby.";

    // ---------------------------------------------------------------- the neonatal unit

    /// <summary>A free incubator on an open neonatal unit, the fullest-staffed first.</summary>
    private (CareSpace Unit, int Index)? FreeIncubator()
    {
        foreach (var unit in NeonatalUnits.Where(IsNeonatalUnitOpen))
        {
            var taken = BabiesIn(unit).Select(b => b.CotIndex!.Value).ToHashSet();
            for (var i = 0; i < unit.Beds.Count; i++)
            {
                if (!taken.Contains(i))
                {
                    return (unit, i);
                }
            }
        }

        return null;
    }

    /// <summary>Books an incubator and calls a neonatal nurse to fetch the baby, or transfers the baby out.</summary>
    private void ArrangeNeonatalAdmission(Baby baby, Patient mother)
    {
        if (FreeIncubator() is not { } incubator)
        {
            var why = NeonatalUnits.Any(IsNeonatalUnitOpen) ? "the neonatal unit is full"
                : NeonatalUnits.Any() ? "the neonatal unit has no nurse"
                : "there is no neonatal unit here";
            TransferBaby(baby, $"Needs neonatal care but {why}, so transferred to a neighbouring hospital's neonatal unit.");
            mother.Log(Now, $"{baby.FirstName} needed neonatal care but {why}, so was transferred to another hospital.", important: true);
            return;
        }

        baby.UnitId = incubator.Unit.Id;
        baby.CotIndex = incubator.Index;
        AddTask(TaskKind.CarryBaby, StaffRole.Nurse, mother, 5, $"Take {baby.FirstName} to {incubator.Unit.Name}", baby.Id.ToString(CultureInfo.InvariantCulture));
        baby.Log(Now, $"Incubator {incubator.Index + 1} on {incubator.Unit.Name} booked; a neonatal nurse is coming to collect.");
    }

    private void TransferBaby(Baby baby, string note)
    {
        baby.Place = BabyPlace.Transferred;
        baby.LeftAt = Now;
        baby.UnitId = null;
        baby.CotIndex = null;
        baby.CarriedByStaffId = null;
        baby.NeedsNeonatalUnit = false;
        MaternityStats.NeonatalTransfers++;
        baby.Log(Now, note, important: true);
        Notifications.Add(Now, NotificationLevel.Warning, $"Baby {baby.FullName}: {LowerFirst(note)}", baby.MotherId);
    }

    /// <summary>The baby a carry task is about, and whether it is going back to mum.</summary>
    private (Baby? Baby, bool ToMother) CarryOf(ClinicalTask task)
    {
        var detail = task.Detail ?? string.Empty;
        var toMother = detail.EndsWith(":mother", StringComparison.Ordinal);
        var idText = toMother ? detail[..^":mother".Length] : detail;
        return int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? (FindBaby(id), toMother) : (null, toMother);
    }

    /// <summary>Only the nurses of the baby's neonatal unit carry it.</summary>
    private bool IsCarryForMember(ClinicalTask task, StaffMember member) =>
        member.Role == StaffRole.Nurse && CarryOf(task).Baby is { UnitId: { } unit } && member.BaseRoomId == unit;

    /// <summary>The incubator's nurse spot for a baby.</summary>
    private GridCoord? IncubatorSpot(Baby baby) =>
        baby.UnitId is { } id && FindSpace(id) is { } unit && baby.CotIndex is { } i && i < unit.Beds.Count ? unit.Beds[i].StaffSpot : null;

    /// <summary>
    /// The nurse walks to mum (or the incubator), picks the baby up, carries it to the incubator (or back to
    /// mum's bed) and puts it down.
    /// </summary>
    private void ProgressCarryBaby(StaffMember member, ClinicalTask task, Patient mother)
    {
        var (baby, toMother) = CarryOf(task);
        if (baby is null || !baby.IsInHospital || IncubatorSpot(baby) is not { } incubator)
        {
            FinishCarry(member, task);
            return;
        }

        var nearMum = BesideTile(mother.Tile);
        var pickUp = toMother ? incubator : nearMum;
        var putDown = toMother ? nearMum : incubator;
        if (baby.CarriedByStaffId != member.Id)
        {
            if (!toMother && mother.IsMoving)
            {
                return;
            }

            if (!member.IsMoving && member.Tile != pickUp)
            {
                WalkTo(member, pickUp);
            }

            if (member.IsMoving || member.Tile != pickUp)
            {
                return;
            }

            baby.CarriedByStaffId = member.Id;
            baby.Place = BabyPlace.Carried;
            member.Activity = StaffActivity.WalkingToTask;
            member.StatusText = toMother ? $"Taking {baby.FirstName} back to {mother.FirstName}" : $"Carrying {baby.FirstName} to the neonatal unit";
            WalkTo(member, putDown);
            return;
        }

        if (toMother && mother.IsMoving)
        {
            return;
        }

        if (!member.IsMoving && member.Tile != putDown)
        {
            WalkTo(member, putDown);
        }

        if (member.IsMoving || member.Tile != putDown)
        {
            return;
        }

        PutDownBaby(baby, member.Name);
        FinishCarry(member, task);
    }

    /// <summary>The baby arrives where it was going: an incubator, or back with mum.</summary>
    private void PutDownBaby(Baby baby, string by)
    {
        baby.CarriedByStaffId = null;
        var unit = baby.UnitId is { } id ? FindSpace(id) : null;
        var mother = FindPatient(baby.MotherId);
        if (baby.NeedsNeonatalUnit && unit is not null)
        {
            baby.Place = BabyPlace.NeonatalUnit;
            baby.NeedsNeonatalUnit = false;
            baby.AdmittedToNeonatalAt = Now;
            var reasons = mother?.Condition.Maternity?.NeonatalStayHours ?? new[] { 24.0, 96.0 };
            var hours = Random.Range(reasons[0], reasons[1]) * (baby.GestationWeeks < 32 ? 1.5 : 1.0);
            baby.NeonatalDischargeDueAt = Now.AddHours(hours);
            MaternityStats.NeonatalAdmissions++;
            baby.Log(Now, $"Admitted to {unit.Name}, incubator {baby.CotIndex + 1}, by {by}.", important: true);
            mother?.Log(Now, $"{baby.FirstName} admitted to {unit.Name}.");
            return;
        }

        baby.Place = mother is { IsInDepartment: true } ? BabyPlace.WithMother : BabyPlace.Home;
        baby.UnitId = null;
        baby.CotIndex = null;
        if (baby.Place == BabyPlace.WithMother)
        {
            baby.Log(Now, $"Back with mum, brought by {by}.", important: true);
            mother!.Log(Now, $"{baby.FirstName} is back from the neonatal unit.");
        }
        else
        {
            LeaveHospital(baby, "Went home with the family.");
        }
    }

    private void FinishCarry(StaffMember member, ClinicalTask task)
    {
        _tasks.Remove(task.Id);
        member.CurrentTaskId = null;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
        member.TasksCompleted++;
    }

    private void LeaveHospital(Baby baby, string note)
    {
        if (baby.AdmittedToNeonatalAt is { } admitted && baby.Place != BabyPlace.Transferred)
        {
            var days = Math.Max(1, (int)Math.Ceiling((Now - admitted).TotalDays));
            Book(MoneyCategory.Maternity, days * NeonatalDayTariff);
        }

        baby.Place = BabyPlace.Home;
        baby.LeftAt = Now;
        baby.UnitId = null;
        baby.CotIndex = null;
        baby.CarriedByStaffId = null;
        baby.Log(Now, note, important: true);
    }

    /// <summary>Babies follow their mothers, get better on the neonatal unit and go home.</summary>
    private void UpdateBabies(double dt)
    {
        if (_babies.Count == 0)
        {
            return;
        }

        var hours = dt / 3600.0;
        foreach (var baby in _babies.Where(b => b.IsInHospital).ToList())
        {
            var mother = FindPatient(baby.MotherId);
            var mumHere = mother is { IsInDepartment: true };
            switch (baby.Place)
            {
                case BabyPlace.WithMother when !mumHere:
                    if (baby.NeedsNeonatalUnit && baby.UnitId is not null && FindSpace(baby.UnitId) is not null)
                    {
                        // Mum has gone (or been moved on) before the nurse came: the neonatal team collect the baby.
                        PutDownBaby(baby, "the neonatal team");
                    }
                    else if (mother?.Outcome == PatientOutcome.Transferred)
                    {
                        baby.Place = BabyPlace.Transferred;
                        baby.LeftAt = Now;
                        baby.Log(Now, "Transferred to another hospital with mum.", important: true);
                    }
                    else
                    {
                        LeaveHospital(baby, "Went home with mum.");
                    }

                    break;

                case BabyPlace.WithMother when baby.NeedsNeonatalUnit && !_tasks.Values.Any(t => t.Kind == TaskKind.CarryBaby && CarryOf(t).Baby == baby):
                    // The incubator booked for them has gone: find another, or transfer.
                    baby.UnitId = null;
                    baby.CotIndex = null;
                    ArrangeNeonatalAdmission(baby, mother!);
                    break;

                case BabyPlace.Carried:
                    var carrier = baby.CarriedByStaffId is { } staffId ? FindStaff(staffId) : null;
                    var carrying = carrier?.CurrentTaskId is { } taskId && _tasks.TryGetValue(taskId, out var task) && task.Kind == TaskKind.CarryBaby && CarryOf(task).Baby == baby;
                    if (!carrying)
                    {
                        // The nurse was called away (end of shift, a break): someone else finishes the job.
                        PutDownBaby(baby, "the neonatal team");
                    }

                    break;

                case BabyPlace.NeonatalUnit:
                    CareOnNeonatalUnit(baby, mother, mumHere, hours);
                    break;
            }
        }

        _babies.RemoveAll(b => b.LeftAt is { } left && Now - left > KeepDepartedBabies && !(FindPatient(b.MotherId)?.IsInDepartment ?? false));
    }

    private void CareOnNeonatalUnit(Baby baby, Patient? mother, bool mumHere, double hours)
    {
        if (baby.UnitId is not { } unitId || FindSpace(unitId) is not { } unit)
        {
            TransferBaby(baby, "The neonatal unit has gone, so transferred to a neighbouring hospital's neonatal unit.");
            return;
        }

        // Babies get better with enough nurses on duty; with none they slowly get worse.
        var babies = BabiesIn(unit).Count(b => b.Place == BabyPlace.NeonatalUnit);
        var nurses = _staff.Count(s => s.BaseRoomId == unit.Id && s.Role == StaffRole.Nurse && s.IsOnDuty);
        var needed = Math.Max(1, (babies + BabiesPerNeonatalNurse - 1) / BabiesPerNeonatalNurse);
        if (nurses == 0)
        {
            baby.Severity = Math.Min(0.9, baby.Severity + (0.01 * hours));
            if (_neonatalAlerts.Add(unit.Id))
            {
                Notifications.Add(Now, NotificationLevel.Warning, $"{unit.Name} has no nurse on duty: its babies aren't getting better.", null, unit.Id);
            }
        }
        else
        {
            _neonatalAlerts.Remove(unit.Id);
            var planned = Math.Max(12, ((baby.NeonatalDischargeDueAt ?? Now) - (baby.AdmittedToNeonatalAt ?? Now)).TotalHours);
            var rate = 0.6 / planned * Math.Min(1.0, (double)nurses / needed);
            baby.Severity = Math.Max(0, baby.Severity - (rate * hours));
        }

        if (Now < baby.NeonatalDischargeDueAt || baby.Severity > NeonatalFitSeverity)
        {
            return;
        }

        if (mumHere && mother!.Stage == PatientStage.OnWard)
        {
            if (!_tasks.Values.Any(t => t.Kind == TaskKind.CarryBaby && CarryOf(t).Baby == baby))
            {
                AddTask(TaskKind.CarryBaby, StaffRole.Nurse, mother, 5, $"Take {baby.FirstName} back to {mother.FirstName}", baby.Id.ToString(CultureInfo.InvariantCulture) + ":mother");
                baby.Log(Now, "Well enough to go back to mum.");
            }

            return;
        }

        var days = ((Now - (baby.AdmittedToNeonatalAt ?? Now)).TotalDays).ToString("0.0", CultureInfo.InvariantCulture);
        LeaveHospital(baby, $"Well enough to go home from {unit.Name} after {days} days.");
        Notifications.Add(Now, NotificationLevel.Info, $"Baby {baby.FullName} has gone home from {unit.Name}.");
    }

    /// <summary>Babies with mum go home with her.</summary>
    private void SendBabiesHomeWith(Patient mother)
    {
        foreach (var baby in BabiesOf(mother).Where(b => b.Place == BabyPlace.WithMother && !b.NeedsNeonatalUnit))
        {
            LeaveHospital(baby, "Went home with mum.");
            baby.LeftAt = mother.LeftAt ?? Now;
        }

        var stillIn = BabiesOf(mother).Where(b => b.Place == BabyPlace.NeonatalUnit).ToList();
        if (stillIn.Count > 0)
        {
            mother.Log(Now, $"{string.Join(" and ", stillIn.Select(b => b.FirstName))} stay{(stillIn.Count == 1 ? "s" : string.Empty)} on the neonatal unit for now.");
        }
    }

    /// <summary>Restores babies from a save.</summary>
    internal void RestoreBabies(IEnumerable<BabyState> babies, int nextId, MaternityStats? stats)
    {
        _babies.Clear();
        foreach (var s in babies)
        {
            var baby = new Baby
            {
                Id = s.Id, FirstName = s.FirstName, Surname = s.Surname, Sex = s.Sex, WeightKg = s.WeightKg, GestationWeeks = s.GestationWeeks,
                BornAt = s.BornAt, MotherId = s.MotherId, Delivery = s.Delivery,
                Place = s.Place == BabyPlace.Carried ? (s.NeedsNeonatalUnit ? BabyPlace.WithMother : BabyPlace.NeonatalUnit) : s.Place,
                NeonatalReason = s.NeonatalReason, NeedsNeonatalUnit = s.NeedsNeonatalUnit, Severity = s.Severity, UnitId = s.UnitId, CotIndex = s.CotIndex,
                AdmittedToNeonatalAt = s.AdmittedToNeonatalAt, NeonatalDischargeDueAt = s.NeonatalDischargeDueAt, LeftAt = s.LeftAt,
            };
            baby.RestoreTimeline(s.Timeline);
            _babies.Add(baby);
        }

        _nextBabyId = Math.Max(nextId, _babies.Count == 0 ? 1 : _babies.Max(b => b.Id) + 1);
        MaternityStats = stats ?? new MaternityStats();
        _maternityWasOpen = IsMaternityOpen;
    }

    internal List<BabyState> CaptureBabies() => _babies.Select(b => new BabyState
    {
        Id = b.Id, FirstName = b.FirstName, Surname = b.Surname, Sex = b.Sex, WeightKg = b.WeightKg, GestationWeeks = b.GestationWeeks,
        BornAt = b.BornAt, MotherId = b.MotherId, Delivery = b.Delivery, Place = b.Place, NeonatalReason = b.NeonatalReason,
        NeedsNeonatalUnit = b.NeedsNeonatalUnit, Severity = b.Severity, UnitId = b.UnitId, CotIndex = b.CotIndex,
        AdmittedToNeonatalAt = b.AdmittedToNeonatalAt, NeonatalDischargeDueAt = b.NeonatalDischargeDueAt, LeftAt = b.LeftAt,
        Timeline = b.Timeline.ToList(),
    }).ToList();

    internal int NextBabyId => _nextBabyId;

    /// <summary>Renames a baby: the last word is their surname, the rest their first name.</summary>
    public void RenameBaby(int babyId, string fullName)
    {
        if (FindBaby(babyId) is not { } baby || string.IsNullOrWhiteSpace(fullName))
        {
            return;
        }

        var words = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            baby.FirstName = words[0];
            return;
        }

        baby.FirstName = string.Join(' ', words[..^1]);
        baby.Surname = words[^1];
    }
}

/// <summary>A baby in a save file.</summary>
public sealed class BabyState
{
    public int Id { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string Surname { get; init; } = string.Empty;

    public Sex Sex { get; init; }

    public double WeightKg { get; init; }

    public int GestationWeeks { get; init; }

    public DateTime BornAt { get; init; }

    public int MotherId { get; init; }

    public string Delivery { get; init; } = string.Empty;

    public BabyPlace Place { get; init; }

    public string? NeonatalReason { get; init; }

    public bool NeedsNeonatalUnit { get; init; }

    public double Severity { get; init; }

    public string? UnitId { get; init; }

    public int? CotIndex { get; init; }

    public DateTime? AdmittedToNeonatalAt { get; init; }

    public DateTime? NeonatalDischargeDueAt { get; init; }

    public DateTime? LeftAt { get; init; }

    public List<TimelineEntry> Timeline { get; init; } = new();
}
