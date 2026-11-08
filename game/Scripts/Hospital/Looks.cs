using System;
using BlueprintHospital.Simulation.Medical;
using BlueprintHospital.Simulation.Patients;
using BlueprintHospital.Simulation.Staff;
using BlueprintHospital.Simulation.Transport;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Uniforms and appearances. Staff follow common UK hospital practice: doctors in scrubs,
/// registered nurses in mid-blue tunics with white piping, senior nurses in navy with red piping,
/// everyone clinical bare below the elbows; reception staff in their own uniform with a lanyard.
/// </summary>
public static class Looks
{
    public static readonly Color Red = new("#E53935");
    public static readonly Color Amber = new("#F9A825");
    public static readonly Color Green = new("#43A047");

    private static readonly Color[] SkinTones =
    {
        new("#F1C7A5"), new("#E0AC86"), new("#C68C5F"), new("#9C6B43"), new("#6E4A2E"), new("#F5D3BC"),
    };

    private static readonly Color[] HairColours =
    {
        new("#2B1E16"), new("#4A3020"), new("#8A5A33"), new("#C9A15A"), new("#1A1A1A"), new("#9A9A9A"), new("#6B3A22"),
    };

    private static readonly Color[] ClothesColours =
    {
        new("#6B4E3D"), new("#3E5C76"), new("#7A3B47"), new("#4D6B3C"), new("#8A6E2F"), new("#55476E"), new("#2F4F4F"), new("#9C5B2E"), new("#B5525C"),
    };

    private static readonly Color[] TrouserColours =
    {
        new("#2F3E57"), new("#3B3B3B"), new("#5B5048"), new("#24324A"), new("#6B6B6B"),
    };

    public static PersonLook Staff(StaffMember member)
    {
        var skin = SkinTones[((member.Id * 5) + 2) % SkinTones.Length];
        var hair = HairColours[(member.Id * 3) % HairColours.Length];
        var longHair = member.Id % 3 == 1;

        return member.Role switch
        {
            StaffRole.Doctor => new PersonLook(
                Top: new Color("#1F8A8A"), Trousers: new Color("#1A7676"), Skin: skin, Hair: hair,
                Stethoscope: true, Lanyard: true, LongHair: longHair),
            StaffRole.Nurse when member.Grade.Contains("Senior", StringComparison.OrdinalIgnoreCase) || member.Grade.Contains("Sister", StringComparison.OrdinalIgnoreCase) => new PersonLook(
                Top: new Color("#1D3A6E"), Trousers: new Color("#18233A"), Skin: skin, Hair: hair,
                Piping: new Color("#E04848"), Tunic: true, Lanyard: true, LongHair: longHair),
            StaffRole.Nurse => new PersonLook(
                Top: new Color("#3B7DD8"), Trousers: new Color("#1B2C4A"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true, LongHair: longHair),
            StaffRole.Radiographer => new PersonLook(
                Top: new Color("#E9EEF2"), Trousers: new Color("#2B3440"), Skin: skin, Hair: hair,
                Piping: new Color("#8E2448"), Tunic: true, Lanyard: true, LongHair: longHair),
            StaffRole.Surgeon => new PersonLook(
                Top: new Color("#4E9C8F"), Trousers: new Color("#438A7E"), Skin: skin, Hair: hair,
                LongHair: longHair),
            StaffRole.Pharmacist => new PersonLook(
                Top: new Color("#F3F5F7"), Trousers: new Color("#3A3F4A"), Skin: skin, Hair: hair,
                Lanyard: true, LongSleeves: true, LongHair: longHair),
            _ => new PersonLook(
                Top: new Color("#7C6AA8"), Trousers: new Color("#2E2E38"), Skin: skin, Hair: hair,
                Lanyard: true, LongSleeves: true, LongHair: longHair),
        };
    }

    public static PersonLook Patient(Patient patient) => new(
        Top: ClothesColours[patient.Id % ClothesColours.Length],
        Trousers: TrouserColours[(patient.Id * 7) % TrouserColours.Length],
        Skin: SkinTones[(patient.Id * 7) % SkinTones.Length],
        Hair: patient.Age >= 65 ? new Color("#B8B8B8") : HairColours[(patient.Id * 5) % HairColours.Length],
        LongSleeves: true,
        LongHair: patient.Sex == Sex.Female && patient.Id % 4 != 0);

    /// <summary>The blanket shows the patient's triage category at a glance.</summary>
    public static Color Blanket(TriageCategory? category) => category switch
    {
        TriageCategory.Red => Red,
        TriageCategory.Amber => Amber,
        TriageCategory.Green => Green,
        _ => new Color("#DCE3E9"),
    };

    /// <summary>Ward patients have an ordinary hospital blanket: the ward isn't triaged.</summary>
    public static readonly Color WardBlanket = new("#5C8FC4");

    public static Color TriageRing(TriageCategory? category) => category switch
    {
        TriageCategory.Red => Red,
        TriageCategory.Amber => Amber,
        TriageCategory.Green => Green,
        _ => new Color("#FFFFFF", 0.8f),
    };
}

/// <summary>Vehicles on the hospital site.</summary>
public static class Vehicles
{
    public static void Ambulance(CanvasItem c, WorldLayer world, AmbulanceVehicle ambulance)
    {
        var (x, y) = world.Interpolated(ambulance);
        var dir = ambulance.Facing;

        // The position is the front of the vehicle; the body sits behind it along its heading.
        var body = (X: x - (dir.X * 4.5), Y: y - (dir.Y * 4.5));
        var cab = (X: x + (dir.X * 1.6), Y: y + (dir.Y * 1.6));
        var yellow = new Color("#F2D21B");

        IsoDraw.OrientedBox(c, cab.X, cab.Y, dir, 2.6, 4.2, 38, yellow);
        IsoDraw.OrientedBox(c, cab.X + (dir.X * 0.9), cab.Y + (dir.Y * 0.9), dir, 0.6, 3.8, 12, new Color("#26343F"), lift: 24);
        IsoDraw.OrientedBox(c, body.X, body.Y, dir, 8.4, 4.6, 58, yellow);
        IsoDraw.OrientedBox(c, body.X, body.Y, dir, 8.4, 4.6, 0, new Color("#E9EEF2"), lift: 58);

        // Blue lights flash on an emergency run.
        var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                       && world.Simulation.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
        var front = IsoDraw.ToScreen(body.X + (dir.X * 4), body.Y + (dir.Y * 4)) + new Vector2(0, -62);
        var on = flashing && ((int)(world.Time * 6) % 2 == 0);
        c.DrawCircle(front + new Vector2(-8, 0), 4, on ? new Color("#3D7BFF") : new Color("#1C3F8A"));
        c.DrawCircle(front + new Vector2(8, 0), 4, !on && flashing ? new Color("#3D7BFF") : new Color("#1C3F8A"));

        var labelPos = IsoDraw.ToScreen(body.X, body.Y) + new Vector2(-18, -36);
        c.DrawString(ThemeDB.FallbackFont, labelPos, ambulance.CallSign, HorizontalAlignment.Left, -1, 12, new Color("#1E5631"));
    }
}
