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
    // The ambulance sprite sheet is baked from a 3D model by tools/ArtBaker/bake_ambulance.py:
    // 16 headings across (0 = towards grid +x, then 22.5 degree steps), and 3 rows for the blue
    // lights (off, left on, right on). Keep these numbers in step with that script.
    private const string AmbulanceSheetPath = "res://Art/Vehicles/ambulance.png";
    private const int Headings = 16;
    private const int CellWidth = 576;
    private const int CellHeight = 400;
    private static readonly Vector2 Anchor = new(288, 232);

    // The simulation positions an ambulance by its front; the sprite is drawn about its centre.
    private const double FrontToCentre = 2.9;
    private const double Length = 11.6;
    private const double Width = 5.0;

    private static Texture2D? _ambulanceSheet;

    private static Texture2D AmbulanceSheet => _ambulanceSheet ??= GD.Load<Texture2D>(AmbulanceSheetPath);

    public static void Ambulance(CanvasItem c, WorldLayer world, AmbulanceVehicle ambulance)
    {
        var (x, y) = world.Interpolated(ambulance);
        var dir = ambulance.Facing;
        var centre = (X: x - (dir.X * FrontToCentre), Y: y - (dir.Y * FrontToCentre));

        // Soft shadow on the ground under the vehicle.
        var len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y));
        var (fx, fy) = len < 1e-6 ? (1.0, 0.0) : (dir.X / len, dir.Y / len);
        var (nx, ny) = (-fy, fx);
        var (hl, hw) = ((Length / 2) + 0.2, (Width / 2) + 0.2);
        var shadow = new[]
        {
            IsoDraw.ToScreen(centre.X + (fx * hl) + (nx * hw), centre.Y + (fy * hl) + (ny * hw)),
            IsoDraw.ToScreen(centre.X + (fx * hl) - (nx * hw), centre.Y + (fy * hl) - (ny * hw)),
            IsoDraw.ToScreen(centre.X - (fx * hl) - (nx * hw), centre.Y - (fy * hl) - (ny * hw)),
            IsoDraw.ToScreen(centre.X - (fx * hl) + (nx * hw), centre.Y - (fy * hl) + (ny * hw)),
        };
        c.DrawColoredPolygon(Array.ConvertAll(shadow, p => p + new Vector2(3, 3)), new Color(0, 0, 0, 0.16f));

        // Pick the baked heading that matches the facing as it looks in the current view rotation.
        var (ox, oy) = IsoDraw.Rotate(0, 0);
        var (rx, ry) = IsoDraw.Rotate(fx, fy);
        var angle = Math.Atan2(ry - oy, rx - ox);
        var column = ((int)Math.Round(angle / (Math.Tau / Headings)) % Headings + Headings) % Headings;

        // Blue lights flash on an emergency run, alternating left and right.
        var flashing = ambulance.State is AmbulanceState.Driving or AmbulanceState.WaitingForBay
                       && world.Simulation.FindPatient(ambulance.PatientId) is { Triage: TriageCategory.Red or null };
        var row = flashing ? 1 + ((int)(world.Time * 6) % 2) : 0;

        var source = new Rect2(column * CellWidth, row * CellHeight, CellWidth, CellHeight);
        var at = IsoDraw.ToScreen(centre.X, centre.Y) - Anchor;
        c.DrawTextureRectRegion(AmbulanceSheet, new Rect2(at, new Vector2(CellWidth, CellHeight)), source);

        var labelPos = IsoDraw.ToScreen(centre.X, centre.Y) + new Vector2(-18, -36);
        c.DrawString(ThemeDB.FallbackFont, labelPos, ambulance.CallSign, HorizontalAlignment.Left, -1, 12, new Color("#1E5631"));
    }
}
