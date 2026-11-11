using System.Collections.Generic;
using BlueprintHospital.Simulation.Data.Models;
using Godot;

namespace BlueprintHospital.UI;

/// <summary>
/// Line drawings for the build bar: one per department and one per kind of room, kept as SVG path
/// data on a 24 × 24 grid and turned into textures in any colour and size, so they stay crisp.
/// </summary>
public static class BuildIcons
{
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        // Departments and the building.
        ["roller"] = "<rect x='3.5' y='3' width='14' height='5.5' rx='1.5'/><path d='M17.5 5.75h2.5v5.5h-8.5v3'/><rect x='10' y='14.25' width='3' height='7' rx='1'/>",
        ["building"] = "<path d='M3 21h18'/><path d='M5 21V9l7-5 7 5v12'/><path d='M9.5 21v-4.5h5V21'/><path d='M8.5 11h2M13.5 11h2M8.5 14h2M13.5 14h2'/>",
        ["ae"] = "<path d='M5 18a7 7 0 0 1 14 0'/><path d='M3.5 18h17v3h-17z'/><path d='M12 3v3M4.6 6.6l2 2M19.4 6.6l-2 2'/><path d='M10 15.5a2 2 0 0 1 2-2'/>",
        ["medicine"] = "<path d='M6 3.5v5a4 4 0 0 0 8 0v-5'/><path d='M10 12.5v2.5a4.5 4.5 0 0 0 9 0v-2'/><circle cx='19' cy='11' r='2'/><path d='M5 3.5h2M13 3.5h2'/>",
        ["cardiology"] = "<path d='M12 20s-7.5-4.6-7.5-10.2A4.3 4.3 0 0 1 12 7a4.3 4.3 0 0 1 7.5 2.8C19.5 15.4 12 20 12 20z'/><path d='M4.5 12.5h4l1.5-2.5 2 5 1.5-2.5h6'/>",
        ["neurology"] = "<path d='M12 5.5A3 3 0 0 0 6.3 6.8 3 3 0 0 0 4.5 12a3 3 0 0 0 2 5 3 3 0 0 0 5.5 1.5z'/><path d='M12 5.5a3 3 0 0 1 5.7 1.3 3 3 0 0 1 1.8 5.2 3 3 0 0 1-2 5 3 3 0 0 1-5.5 1.5'/><path d='M12 5.5v13M8.5 10.5c1 0 2 .7 2 2M15.5 10.5c-1 0-2 .7-2 2'/>",
        ["gastro"] = "<path d='M10 3v4.5c0 1.8-4.5 2.5-4.5 7.5a6 6 0 0 0 6.5 6c4.5 0 7.5-2.5 7.5-6.5 0-3.5-3.2-4.6-5.3-3.4-1.7 1-3.2.3-3.2-1.6V3'/>",
        ["gynae"] = "<circle cx='12' cy='9' r='5'/><path d='M12 14v7M8.8 18h6.4'/>",
        ["ortho"] = "<path d='M8.2 8.2l7.6 7.6'/><circle cx='5.6' cy='7.6' r='2.1'/><circle cx='7.6' cy='5.6' r='2.1'/><circle cx='16.4' cy='18.4' r='2.1'/><circle cx='18.4' cy='16.4' r='2.1'/>",
        ["surgery"] = "<path d='M3.5 20.5l8-8'/><path d='M11.5 12.5l7-7c1.8 1.8 1.6 4.6-.3 6.4L14.4 15.6z'/><path d='M7 17l-1.5-1.5'/>",
        ["icu"] = "<rect x='3' y='4' width='18' height='13' rx='2'/><path d='M5.5 11h3l1.8-3 2.4 6 1.8-3h4'/><path d='M9 21h6M12 17v4'/>",
        ["maternity"] = "<path d='M3.5 10.5h12a5.5 5.5 0 0 1-5.5 5.5H9a5.5 5.5 0 0 1-5.5-5.5z'/><path d='M15.5 10.5V4.5a6 6 0 0 1 5 6z'/><circle cx='7' cy='19.5' r='1.8'/><circle cx='14' cy='19.5' r='1.8'/>",
        ["clinics"] = "<rect x='4.5' y='4' width='15' height='17' rx='2'/><path d='M9 4V2.5h6V4'/><path d='M8 10h8M8 13.5h8M8 17h5'/>",
        ["imaging"] = "<rect x='3' y='3' width='18' height='18' rx='2.5'/><path d='M12 6v12'/><path d='M7.5 8.5c2.4-.6 4.5.8 4.5.8s2.1-1.4 4.5-.8M7 12.3c2.6-.6 5 .9 5 .9s2.4-1.5 5-.9M7.5 16c2.4-.5 4.5.9 4.5.9s2.1-1.4 4.5-.9'/>",
        ["theatres"] = "<path d='M3.5 12a8.5 5.5 0 0 1 17 0z'/><path d='M12 2.5v4'/><path d='M7.5 15.5l-1.5 3M12 15.5v4M16.5 15.5l1.5 3'/>",
        ["pharmacy"] = "<path d='M10.2 20.2a4.5 4.5 0 0 1-6.4-6.4l10-10a4.5 4.5 0 0 1 6.4 6.4z'/><path d='M8.8 8.8l6.4 6.4'/>",
        ["facilities"] = "<path d='M4 9.5h12V14a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5z'/><path d='M16 10.5h1.8a2.2 2.2 0 0 1 0 4.4H16'/><path d='M8 3c-1 1 1 2 0 3.5M12 3c-1 1 1 2 0 3.5'/><path d='M3.5 21.5h13'/>",

        // Rooms.
        ["bed"] = "<path d='M3 18V7M3 14h18v4'/><path d='M3 11h5a2 2 0 0 1 2 2v1'/><path d='M10 11h8a3 3 0 0 1 3 3'/><circle cx='6' cy='9' r='1.6'/>",
        ["wardbed"] = "<path d='M2.5 17V9M2.5 14h8v3M13.5 17V9M13.5 14h8v3'/><circle cx='5' cy='11.5' r='1.3'/><circle cx='16' cy='11.5' r='1.3'/>",
        ["chairs"] = "<path d='M5 12V7a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v5M14 12V7a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v5'/><path d='M3.5 12h17v3h-17zM5 15v4M19 15v4'/>",
        ["triage"] = "<rect x='5' y='4' width='14' height='17' rx='2'/><path d='M9 4V2.5h6V4'/><circle cx='9' cy='10' r='1.3'/><circle cx='9' cy='14' r='1.3'/><circle cx='9' cy='18' r='1.3'/><path d='M12 10h4M12 14h4M12 18h3'/>",
        ["resus"] = "<path d='M13 2.5L5 13.5h6l-1 8 8-11h-6z'/>",
        ["desk"] = "<path d='M3 10h18M5 10v10M19 10v10M14 10v5h5'/><rect x='7' y='3.5' width='7' height='5' rx='1'/>",
        ["people"] = "<circle cx='8' cy='8' r='2.8'/><circle cx='16.5' cy='9' r='2.3'/><path d='M3 19.5a5 5 0 0 1 10 0M13.5 19.5a4 4 0 0 1 7.5-1.5'/>",
        ["station"] = "<path d='M3 13h18v6H3z'/><path d='M6 13V9a6 6 0 0 1 12 0v4'/><path d='M12 6v4M10 8h4'/>",
        ["office"] = "<rect x='4' y='3' width='16' height='18' rx='1.5'/><path d='M8 8h8M8 12h8M8 16h4'/>",
        ["door"] = "<path d='M5 21V3h14v18'/><path d='M3 21h18'/><circle cx='15.5' cy='12' r='1'/>",
        ["corridor"] = "<path d='M3 20l5-16M21 20l-5-16'/><path d='M12 6v2.5M12 11v3M12 17v3.5'/>",
        ["lift"] = "<rect x='5' y='3' width='14' height='18' rx='1.5'/><path d='M12 3v18'/><path d='M7.5 9.5l1.5-2 1.5 2M13.5 7.5l1.5 2 1.5-2'/>",
        ["stairs"] = "<path d='M3 20h5v-5h5v-5h5V5h3'/>",
        ["floor"] = "<path d='M12 3l9 4.5-9 4.5-9-4.5z'/><path d='M3 12l9 4.5 9-4.5M3 16.5l9 4.5 9-4.5'/>",
        ["extend"] = "<rect x='3' y='7' width='10' height='10'/><path d='M13 7h2M17 7h4v4M21 13v4h-4M15 17h-2'/><path d='M15.5 12h4M17.5 10v4'/>",
        ["seat"] = "<path d='M7 11V5a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v6'/><path d='M5 11h14v4H5zM7 15v5M17 15v5'/>",
        ["scanner"] = "<circle cx='12' cy='11' r='7'/><circle cx='12' cy='11' r='3'/><path d='M3 21h18'/><path d='M12 18v3'/>",
        ["wave"] = "<path d='M3 12c1.5-4 3-4 4.5 0s3 4 4.5 0 3-4 4.5 0 3 4 4.5 0'/><path d='M3 19h18'/>",
        ["cast"] = "<path d='M7 3h6v8l3 3v7H7z'/><path d='M7 7h6M7 11h6M8 17h7'/>",
        ["dumbbell"] = "<path d='M6.5 6.5l11 11'/><path d='M3 9l6-6M5 11l6-6M13 19l6-6M15 21l6-6'/>",
        ["scope"] = "<path d='M4 4h4a4 4 0 0 1 4 4v3a4 4 0 0 0 4 4h1'/><circle cx='19' cy='15' r='2'/><path d='M4 4v3'/>",
        ["heart"] = "<path d='M12 20s-7.5-4.6-7.5-10.2A4.3 4.3 0 0 1 12 7a4.3 4.3 0 0 1 7.5 2.8C19.5 15.4 12 20 12 20z'/>",
        ["brainwave"] = "<path d='M3 8h3l1.5-3 2 6 1.5-3h2l1.5 2 1.5-4 2 5h3'/><path d='M3 15h4M10 15h4M17 15h4M3 19h4M10 19h4M17 19h4'/>",
        ["cot"] = "<path d='M4 9h16v8H4zM4 17v3M20 17v3'/><path d='M8 9v8M12 9v8M16 9v8'/>",
        ["incubator"] = "<path d='M4 13a8 6 0 0 1 16 0z'/><path d='M3 13h18v3H3zM6 16v4M18 16v4'/><circle cx='12' cy='10.5' r='1.3'/>",
        ["vend"] = "<rect x='5' y='3' width='14' height='18' rx='1.5'/><path d='M8 7h5M8 10.5h5M8 14h5'/><path d='M16 7v2M8 17.5h8'/>",
        ["wc"] = "<path d='M6 4h5v7H6z'/><path d='M4 11h15a6 6 0 0 1-6 6H9a5 5 0 0 1-5-5z'/><path d='M9 17l-1 4h7l-1-4'/>",
        ["access"] = "<circle cx='11' cy='4' r='1.8'/><path d='M11 6.5V13h6l2 5'/><path d='M8 9.5a6 6 0 1 0 7.5 7.5'/>",
        ["reception"] = "<path d='M3 12h18v8H3z'/><circle cx='12' cy='6' r='2.5'/><path d='M8 12a4 4 0 0 1 8 0'/>",
        ["resus_trolley"] = "<rect x='5' y='8' width='12' height='10' rx='1'/><path d='M5 11.5h12M5 15h12'/><path d='M17 9h2.5v-3'/><circle cx='7' cy='20' r='1.3'/><circle cx='15' cy='20' r='1.3'/><path d='M9 3.5l-1.5 2.5h3L9 8.5'/>",
        ["mop"] = "<path d='M15 3l-4 11'/><path d='M7 14h8l1 3H6z'/><path d='M7 17l-1 3M10 17v3M13 17v3M16 17l1 3'/><path d='M3 12h4v8H3'/>",
        ["lock"] = "<rect x='5' y='11' width='14' height='10' rx='2'/><path d='M8 11V8a4 4 0 0 1 8 0v3'/>",
    };

    /// <summary>Drawings for particular rooms; anything else is picked by the room's kind and layout.</summary>
    private static readonly Dictionary<string, string> RoomGlyphs = new()
    {
        ["extend_building"] = "extend", ["add_floor"] = "floor", ["waiting_chairs"] = "seat", ["resus_trolleys"] = "resus_trolley", ["waiting_desk"] = "reception", ["waiting_doors"] = "door", ["corridor"] = "corridor",
        ["lift"] = "lift", ["stairs"] = "stairs",
        ["waiting_room"] = "chairs", ["triage_room"] = "triage", ["resus_bay"] = "resus",
        ["nurses_station"] = "station", ["doctors_office"] = "office", ["staff_room"] = "people", ["outreach_office"] = "office",
        ["ambulatory_care_unit"] = "chairs", ["echo_room"] = "heart", ["cath_lab"] = "icu", ["eeg_room"] = "brainwave",
        ["ct_scanner"] = "scanner", ["xray_room"] = "imaging", ["operating_theatre"] = "theatres", ["csection_theatre"] = "theatres", ["caesarean_theatre"] = "theatres", ["antenatal_scan_room"] = "wave", ["delivery_room"] = "bed",
        ["early_pregnancy_unit"] = "wave", ["ultrasound_room"] = "wave", ["endoscopy_room"] = "scope",
        ["plaster_room"] = "cast", ["physio_gym"] = "dumbbell", ["intensive_care_unit"] = "icu", ["high_dependency_unit"] = "icu",
        ["day_surgery_unit"] = "bed", ["mother_baby_ward"] = "cot", ["neonatal_unit"] = "incubator",
        ["outpatient_reception"] = "reception", ["pharmacy"] = "pharmacy", ["cafeteria"] = "facilities", ["vending_point"] = "vend",
        ["accessible_toilet"] = "access", ["cleaning_room"] = "mop",
    };

    private static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>The drawing used for a room, special build item or room template.</summary>
    public static string RoomGlyph(string id, RoomTemplate? template)
    {
        if (RoomGlyphs.TryGetValue(id, out var glyph))
        {
            return glyph;
        }

        if (id.Contains("toilet", System.StringComparison.Ordinal))
        {
            return "wc";
        }

        if (id.Contains("clinic", System.StringComparison.Ordinal))
        {
            return "desk";
        }

        return template?.Layout switch
        {
            RoomLayoutKind.Ward => "wardbed",
            RoomLayoutKind.BedSpace => "bed",
            RoomLayoutKind.Waiting => "chairs",
            RoomLayoutKind.Triage => "triage",
            RoomLayoutKind.Office => "office",
            RoomLayoutKind.StaffRoom => "people",
            _ => "door",
        };
    }

    public static bool Has(string glyph) => Glyphs.ContainsKey(glyph);

    /// <summary>A glyph drawn with <paramref name="colour"/> lines, <paramref name="pixels"/> square.</summary>
    public static Texture2D Get(string glyph, Color colour, int pixels)
    {
        var key = $"{glyph}|{colour.ToHtml(false)}|{pixels}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var body = Glyphs.TryGetValue(glyph, out var paths) ? paths : Glyphs["door"];
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' stroke='#{colour.ToHtml(false)}' stroke-width='1.9' stroke-linecap='round' stroke-linejoin='round'>{body}</svg>";
        var image = new Image();
        var error = image.LoadSvgFromString(svg, pixels / 24f);
        Texture2D texture = error == Error.Ok ? ImageTexture.CreateFromImage(image) : new PlaceholderTexture2D { Size = new Vector2(pixels, pixels) };
        Cache[key] = texture;
        return texture;
    }
}
