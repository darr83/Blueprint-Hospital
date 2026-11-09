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
/// everyone clinical bare below the elbows; porters in green polo shirts; reception staff in their own uniform with a lanyard.
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

    private static readonly Color[] ShirtColours =
    {
        new("#BFD7EE"), new("#F2F4F7"), new("#D8CDE8"), new("#F3D9DC"), new("#CFE3D4"),
    };

    private static readonly Color[] CapColours =
    {
        new("#1D3A6E"), new("#7A2E4A"), new("#2F7D6B"), new("#5A3D7A"), new("#C2571A"), new("#3B7DD8"),
    };

    /// <summary>A staff member's own hair style: long-haired staff tie it back (bare below the elbows, hair off the collar).</summary>
    private static HairStyle StaffHair(StaffMember member, bool longHair) => longHair
        ? (member.Id % 3) switch { 0 => HairStyle.Bun, 1 => HairStyle.Ponytail, _ => HairStyle.Long }
        : member.Age >= 55 && member.Id % 4 == 0 ? HairStyle.Bald : (member.Id % 5) switch { 0 => HairStyle.Curly, 1 => HairStyle.Crop, _ => HairStyle.Short };

    public static PersonLook Staff(StaffMember member)
    {
        var skin = SkinTones[((member.Id * 5) + 2) % SkinTones.Length];
        var hair = member.Age >= 58 ? new Color("#A8A8A8") : HairColours[(member.Id * 3) % HairColours.Length];
        var longHair = member.Id % 3 == 1;
        var style = StaffHair(member, longHair);
        var beard = !longHair && member.Id % 7 == 2;
        var glasses = member.Id % 6 == 4;
        var cap = CapColours[(member.Id * 5) % CapColours.Length];

        var look = member.Role switch
        {
            // Consultants wear their own shirt and trousers, sleeves rolled up; other doctors wear scrubs.
            StaffRole.Doctor when member.Grade.Contains("Consultant", StringComparison.OrdinalIgnoreCase) => new PersonLook(
                Top: ShirtColours[member.Id % ShirtColours.Length], Trousers: new Color("#2E3440"), Skin: skin, Hair: hair,
                Stethoscope: true, Lanyard: true, Collar: true, Pocket: true),
            StaffRole.Doctor => new PersonLook(
                Top: new Color("#1F8A8A"), Trousers: new Color("#1A7676"), Skin: skin, Hair: hair,
                Stethoscope: true, Lanyard: true, Pocket: true),
            StaffRole.Nurse when StaffGrades.IsSeniorNurse(member) || member.Grade.Contains("Senior", StringComparison.OrdinalIgnoreCase) || member.Grade.Contains("Sister", StringComparison.OrdinalIgnoreCase) => new PersonLook(
                Top: new Color("#1D3A6E"), Trousers: new Color("#18233A"), Skin: skin, Hair: hair,
                Piping: new Color("#E04848"), Tunic: true, Lanyard: true, Pocket: true, FobWatch: true),
            StaffRole.Nurse => new PersonLook(
                Top: new Color("#3B7DD8"), Trousers: new Color("#1B2C4A"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true, Pocket: true, FobWatch: true),
            // Midwives wear plum tunics with lilac piping; senior and consultant midwives a deep aubergine.
            StaffRole.Midwife when member.Grade.Contains("Band 7", StringComparison.OrdinalIgnoreCase) || member.Grade.Contains("Band 8", StringComparison.OrdinalIgnoreCase) => new PersonLook(
                Top: new Color("#4A1F55"), Trousers: new Color("#24182B"), Skin: skin, Hair: hair,
                Piping: new Color("#E7C6E0"), Tunic: true, Lanyard: true, Pocket: true, FobWatch: true),
            StaffRole.Midwife => new PersonLook(
                Top: new Color("#8A2D6E"), Trousers: new Color("#2A1E33"), Skin: skin, Hair: hair,
                Piping: new Color("#E7C6E0"), Tunic: true, Lanyard: true, Pocket: true, FobWatch: true),
            StaffRole.Radiographer => new PersonLook(
                Top: new Color("#E9EEF2"), Trousers: new Color("#2B3440"), Skin: skin, Hair: hair,
                Piping: new Color("#8E2448"), Tunic: true, Lanyard: true, Pocket: true),
            StaffRole.Surgeon => new PersonLook(
                Top: new Color("#4E9C8F"), Trousers: new Color("#438A7E"), Skin: skin, Hair: hair,
                Cap: cap, Mask: true, ShoeColour: new Color("#E8ECEF")),
            StaffRole.Pharmacist => new PersonLook(
                Top: new Color("#F3F5F7"), Trousers: new Color("#3A3F4A"), Skin: skin, Hair: hair,
                Lanyard: true, LongSleeves: true, Collar: true, Pocket: true),
            StaffRole.CardiacPhysiologist => new PersonLook(
                Top: new Color("#7A2E4A"), Trousers: new Color("#2B2F3A"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true),
            StaffRole.Neurophysiologist => new PersonLook(
                Top: new Color("#5A3D7A"), Trousers: new Color("#2B2F3A"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true),
            StaffRole.Sonographer => new PersonLook(
                Top: new Color("#2F5D73"), Trousers: new Color("#1E2E3A"), Skin: skin, Hair: hair,
                Piping: new Color("#9ED0E6"), Tunic: true, Lanyard: true),
            StaffRole.Endoscopist => new PersonLook(
                Top: new Color("#2C4F7C"), Trousers: new Color("#26446C"), Skin: skin, Hair: hair,
                Lanyard: true, Cap: cap),
            StaffRole.PlasterTechnician => new PersonLook(
                Top: new Color("#5D6B78"), Trousers: new Color("#2B3440"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true, Pocket: true),
            StaffRole.Physiotherapist => new PersonLook(
                Top: Colors.White, Trousers: new Color("#1A3A5C"), Skin: skin, Hair: hair,
                Piping: new Color("#1A5E8A"), Tunic: true, Lanyard: true, Collar: true),
            StaffRole.Anaesthetist => new PersonLook(
                Top: new Color("#3F5FA8"), Trousers: new Color("#36549A"), Skin: skin, Hair: hair,
                Lanyard: true, Cap: cap, Mask: true, Stethoscope: true, ShoeColour: new Color("#2F6FD0")),
            StaffRole.OutreachNurse => new PersonLook(
                Top: new Color("#1D3A6E"), Trousers: new Color("#18233A"), Skin: skin, Hair: hair,
                Piping: new Color("#F2B134"), Tunic: true, Lanyard: true, Stethoscope: true, FobWatch: true),
            StaffRole.HealthcareAssistant => new PersonLook(
                Top: new Color("#8C98A4"), Trousers: new Color("#2B3440"), Skin: skin, Hair: hair,
                Piping: Colors.White, Tunic: true, Lanyard: true, FobWatch: true),
            StaffRole.Porter => new PersonLook(
                Top: new Color("#3C6E47"), Trousers: new Color("#22262B"), Skin: skin, Hair: hair,
                Lanyard: true, Collar: true),
            _ => new PersonLook(
                Top: new Color("#E9E4F2"), Trousers: new Color("#2E2E38"), Skin: skin, Hair: hair,
                Lanyard: true, LongSleeves: true, Jacket: new Color("#7C6AA8")),
        };

        return look with { LongHair = longHair, Style = style, Beard = beard, Glasses = glasses };
    }

    private static readonly Color[] CoatColours =
    {
        new("#2F3E57"), new("#5B4636"), new("#3F5240"), new("#6E2F3A"), new("#4A4A52"), new("#8A6E4B"),
    };

    /// <summary>A patient's hair style, from their sex and age.</summary>
    private static HairStyle PatientHair(int seed, Sex sex, int age) => sex == Sex.Female
        ? (seed % 5) switch { 0 => HairStyle.Short, 1 => HairStyle.Bun, 2 => HairStyle.Ponytail, 3 when age < 60 => HairStyle.Curly, _ => HairStyle.Long }
        : age >= 50 && seed % 3 == 0 ? HairStyle.Bald : (seed % 4) switch { 0 => HairStyle.Crop, 1 => HairStyle.Curly, _ => HairStyle.Short };

    public static PersonLook Patient(Patient patient) => new(
        Top: ClothesColours[patient.Id % ClothesColours.Length],
        Trousers: TrouserColours[(patient.Id * 7) % TrouserColours.Length],
        Skin: SkinTones[(patient.Id * 7) % SkinTones.Length],
        Hair: patient.Age >= 65 ? new Color("#B8B8B8") : HairColours[(patient.Id * 5) % HairColours.Length],
        LongSleeves: true,
        LongHair: patient.Sex == Sex.Female && patient.Id % 4 != 0,
        Style: PatientHair(patient.Id, patient.Sex, patient.Age),
        Beard: patient.Sex == Sex.Male && patient.Age >= 25 && patient.Id % 4 == 1,
        Glasses: patient.Age >= 50 && patient.Id % 3 != 1,
        Skirt: patient.Sex == Sex.Female && patient.Id % 3 == 0,
        Jacket: patient.Id % 3 == 2 && !patient.Maternity ? CoatColours[patient.Id % CoatColours.Length] : null,
        Stick: patient.Age >= 78,
        Bump: patient.Maternity && !patient.Delivered);

    /// <summary>A clinic patient in their own clothes, with a coat on (they have come from home).</summary>
    public static PersonLook Outpatient(Outpatient outpatient, Appointment? appointment)
    {
        var seed = outpatient.AppointmentId + 3;
        var sex = appointment?.Sex ?? Sex.Male;
        var age = appointment?.Age ?? 40;
        return new PersonLook(
            Top: ClothesColours[(seed * 5) % ClothesColours.Length],
            Trousers: TrouserColours[(seed * 3) % TrouserColours.Length],
            Skin: SkinTones[(seed * 11) % SkinTones.Length],
            Hair: age >= 65 ? new Color("#B8B8B8") : HairColours[(seed * 7) % HairColours.Length],
            LongSleeves: true,
            LongHair: sex == Sex.Female && seed % 4 != 0,
            Style: PatientHair(seed, sex, age),
            Beard: sex == Sex.Male && seed % 4 == 1,
            Glasses: age >= 50 && seed % 3 != 1,
            Skirt: sex == Sex.Female && seed % 3 == 0,
            Jacket: CoatColours[seed % CoatColours.Length],
            Stick: age >= 78,
            Bump: sex == Sex.Female && outpatient.ClinicId.StartsWith("antenatal", StringComparison.Ordinal) && seed % 3 != 0);
    }

    private static readonly Color[] BabyHats =
    {
        new("#F4C2D7"), new("#BFDDF5"), new("#F7E3A1"), new("#C8E6C9"), new("#E1D3F2"), new("#F6D1B8"),
    };

    /// <summary>A newborn: a white cellular blanket edged in the colour of their hat, and mum's skin tone.</summary>
    public static BabyLook Baby(Baby baby, Patient? mother)
    {
        var hat = BabyHats[(baby.Id * 5) % BabyHats.Length];
        var skin = mother is not null ? Patient(mother).Skin : SkinTones[(baby.Id * 3) % SkinTones.Length];
        return new BabyLook(hat.Lerp(new Color("#FAFAF7"), 0.7f), hat.Darkened(0.12f), hat, skin.Lightened(0.06f));
    }

    /// <summary>The blanket on a mother and baby ward bed: a soft lilac, to set the maternity unit apart.</summary>
    public static readonly Color MaternityBlanket = new("#B79CCB");

    /// <summary>Clinic patients have a soft lilac ring, so they stand apart from A&amp;E patients.</summary>
    public static readonly Color OutpatientRing = new("#9C8AD0", 0.85f);

    /// <summary>The blanket shows the patient's triage category at a glance.</summary>
    public static Color Blanket(TriageCategory? category) => category switch
    {
        TriageCategory.Red => Red,
        TriageCategory.Amber => Amber,
        TriageCategory.Green => Green,
        _ => new Color("#DCE3E9"),
    };

    /// <summary>The A&amp;E's blue cellular blanket. The triage colour shows on the wristband, the floor ring and the monitor.</summary>
    public static readonly Color EdBlanket = new("#8DB3D9");

    /// <summary>The triage colour for a patient's wristband, or null (a plain white band) before triage.</summary>
    public static Color? Wristband(TriageCategory? category) => category is null ? null : Blanket(category);

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
