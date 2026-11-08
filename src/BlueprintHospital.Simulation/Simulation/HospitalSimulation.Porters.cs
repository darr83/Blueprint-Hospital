using BlueprintHospital.Simulation.Agents;
using BlueprintHospital.Simulation.Hospital;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>
/// Moving patients. Patients who can walk get about on their own. Patients who can't wait for a
/// porter to push their wheelchair or trolley: anyone triaged Red, anyone who came by ambulance and
/// isn't fit to sit, and frail older patients. The ambulance crew wheel their own patients in from
/// the bay. Without porters, a nurse does the pushing.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>Pushing a wheelchair or a trolley is a little slower than walking.</summary>
    public const double PushSpeedFactor = 0.8;

    /// <summary>A nurse steps in when a move has waited this long for a porter.</summary>
    public static readonly TimeSpan NurseCoversPorterAfter = TimeSpan.FromMinutes(10);

    /// <summary>Where each pushed patient has been recently, so the porter follows the same route behind them.</summary>
    private readonly Dictionary<int, List<(double X, double Y)>> _pushTrails = new();

    public bool HasPorters => _staff.Any(s => s.Role == StaffRole.Porter);

    /// <summary>How far behind the patient's position the pusher walks, in tiles.</summary>
    public static double PushGap(Mobility mobility) => mobility == Mobility.Trolley ? 2.6 : 1.3;

    /// <summary>
    /// Sends a patient somewhere. Walking patients set off straight away; a patient in a wheelchair or
    /// on a trolley waits where they are until a porter comes to push them.
    /// </summary>
    internal void WalkTo(Patient patient, GridCoord destination)
    {
        UpdateMobility(patient);

        var crewWheelsIn = AmbulanceBayPatientTiles.Contains(patient.Tile) && !AmbulanceBayPatientTiles.Contains(destination)
                           && patient.Stage != PatientStage.AwaitingTransfer && patient.LeftEmergencyAt is null;
        if (patient.Mobility == Mobility.Walking || patient.PushedByStaffId is not null || crewWheelsIn)
        {
            patient.PendingMove = null;
            Go(patient, destination);
            return;
        }

        if (patient.Tile == destination && !patient.IsMoving)
        {
            patient.PendingMove = null;
            return;
        }

        patient.StopMoving();
        patient.PendingMove = destination;
        if (!HasTask(patient, TaskKind.MovePatient))
        {
            var label = patient.Mobility == Mobility.Trolley ? "Push trolley" : "Push wheelchair";
            AddTask(TaskKind.MovePatient, HasPorters ? StaffRole.Porter : StaffRole.Nurse, patient, 0, label);
        }
    }

    /// <summary>
    /// Decides how the patient gets about from how unwell they are. Leaving patients walk out. Red
    /// patients, patients going to theatre and ambulance patients who aren't fit to sit go on a
    /// trolley. Ambulance patients who are fit to sit (Green), Amber walk-ins aged 75 or over and
    /// patients being admitted use a wheelchair, as does anyone who has come off a trolley.
    /// </summary>
    internal void UpdateMobility(Patient patient)
    {
        if (patient.Stage is PatientStage.Leaving or PatientStage.Gone)
        {
            patient.Mobility = Mobility.Walking;
            return;
        }

        var ambulance = patient.ArrivalMethod == ArrivalMethod.Ambulance;
        if (patient.Triage == TriageCategory.Red || patient.Stage == PatientStage.AwaitingSurgery || patient.PreOp || patient.CareLevel >= 2 || (ambulance && patient.Triage != TriageCategory.Green))
        {
            patient.Mobility = Mobility.Trolley;
        }
        else if (ambulance
                 || patient.Mobility == Mobility.Trolley
                 || (patient.Triage == TriageCategory.Amber && patient.Age >= 75)
                 || patient.Stage is PatientStage.AwaitingBed or PatientStage.OnWard)
        {
            patient.Mobility = Mobility.Wheelchair;
        }
    }

    /// <summary>The pusher walks to the patient, waits for any care under way to finish, then pushes them there.</summary>
    private void ProgressPush(StaffMember member, ClinicalTask task, Patient patient)
    {
        if (task.Started)
        {
            if (patient.PushedByStaffId != member.Id || !patient.IsMoving)
            {
                FinishPush(member, task, patient);
            }

            return;
        }

        if (patient.PendingMove is not { } destination)
        {
            RemoveTask(task);
            return;
        }

        var spot = StaffPositionFor(task, patient);
        if (!member.IsMoving && member.Tile != spot)
        {
            WalkTo(member, spot);
        }

        var busy = patient.BusyWithTaskId is { } other && _tasks.TryGetValue(other, out var care) && care.Started;
        if (member.IsMoving || busy || patient.IsMoving)
        {
            return;
        }

        task.Started = true;
        member.Activity = StaffActivity.Working;
        member.PushingPatientId = patient.Id;
        member.StatusText = $"{task.Label}: {patient.FullName}";
        patient.PushedByStaffId = member.Id;
        patient.PendingMove = null;
        patient.WalkSpeed = member.WalkSpeed = Agent.DefaultWalkSpeed * PushSpeedFactor;
        _pushTrails[patient.Id] = new List<(double X, double Y)> { (patient.X, patient.Y) };
        Go(patient, destination);
        if (!patient.IsMoving)
        {
            FinishPush(member, task, patient);
            return;
        }

        patient.Log(Now, $"{(patient.Mobility == Mobility.Trolley ? "Taken on a trolley" : "Taken in a wheelchair")} by {member.Name}{(member.Role == StaffRole.Porter ? " (porter)" : string.Empty)}.");
    }

    private void FinishPush(StaffMember member, ClinicalTask task, Patient patient)
    {
        _tasks.Remove(task.Id);
        EndPush(patient);
        member.CurrentTaskId = null;
        member.Activity = StaffActivity.Idle;
        member.StatusText = "Available";
        member.TasksCompleted++;
    }

    /// <summary>Lets go of the patient's wheelchair or trolley.</summary>
    private void EndPush(Patient patient)
    {
        if (patient.PushedByStaffId is { } id && FindStaff(id) is { } member)
        {
            member.PushingPatientId = null;
            member.WalkSpeed = Agent.DefaultWalkSpeed;
        }

        patient.PushedByStaffId = null;
        patient.WalkSpeed = Agent.DefaultWalkSpeed;
        _pushTrails.Remove(patient.Id);
    }

    /// <summary>Each tick, whoever is pushing stays just behind the patient, along the route the patient took.</summary>
    private void KeepPushersWithPatients()
    {
        foreach (var patient in _patients.Values)
        {
            // A patient left waiting for a move always has someone coming for them.
            if (patient.PendingMove is not null && patient.PushedByStaffId is null && patient.IsInDepartment && !HasTask(patient, TaskKind.MovePatient))
            {
                AddTask(TaskKind.MovePatient, HasPorters ? StaffRole.Porter : StaffRole.Nurse, patient, 0, patient.Mobility == Mobility.Trolley ? "Push trolley" : "Push wheelchair");
            }

            if (patient.PushedByStaffId is not { } id)
            {
                continue;
            }

            if (FindStaff(id) is not { } member || !patient.IsInDepartment)
            {
                EndPush(patient);
                continue;
            }

            if (!_pushTrails.TryGetValue(patient.Id, out var trail))
            {
                trail = new List<(double X, double Y)>();
                _pushTrails[patient.Id] = trail;
            }

            if (trail.Count == 0 || trail[^1] != (patient.X, patient.Y))
            {
                trail.Add((patient.X, patient.Y));
            }

            var (x, y) = PointBehind(trail, PushGap(patient.Mobility), patient.Facing);
            var (dx, dy) = (patient.X - x, patient.Y - y);
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            member.FollowAt(x, y, length > 1e-6 ? (dx / length, dy / length) : patient.Facing);
        }
    }

    /// <summary>The point <paramref name="gap"/> tiles back along the trail, trimming what is no longer needed.</summary>
    private static (double X, double Y) PointBehind(List<(double X, double Y)> trail, double gap, (double X, double Y) facing)
    {
        var remaining = gap;
        for (var i = trail.Count - 1; i > 0; i--)
        {
            var (ax, ay) = trail[i];
            var (bx, by) = trail[i - 1];
            var step = Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
            if (step >= remaining && step > 1e-9)
            {
                if (i > 1)
                {
                    trail.RemoveRange(0, i - 1);
                }

                var t = remaining / step;
                return (ax + ((bx - ax) * t), ay + ((by - ay) * t));
            }

            remaining -= step;
        }

        // Not enough trail yet (just set off): stand straight behind.
        var (sx, sy) = trail[0];
        return (sx - (facing.X * remaining), sy - (facing.Y * remaining));
    }
}
