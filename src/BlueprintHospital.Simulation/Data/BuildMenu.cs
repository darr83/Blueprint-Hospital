using System.Text.Json;
using BlueprintHospital.Simulation.Data.Models;

namespace BlueprintHospital.Simulation.Data;

/// <summary>
/// The bottom build bar's layout, read from Data/BuildMenu/build_menu.json: groups of icons, each
/// icon with the rooms (and special actions) it pops up. A room may sit under several icons.
/// <see cref="Resolve"/> fits the file to the loaded rooms and departments, so content added later
/// (a new department, a new room) still shows up without editing the menu file.
/// </summary>
public sealed class BuildMenu
{
    public const string Folder = "BuildMenu";
    public const string FileName = "build_menu.json";

    /// <summary>The icon that opens the Decorate tool: click a room to choose its floor and walls.</summary>
    public const string Decorate = "decorate";

    /// <summary>Pops up the building extension tool.</summary>
    public const string ExtendBuilding = "extend_building";

    /// <summary>Builds another floor straight away.</summary>
    public const string AddFloor = "add_floor";

    /// <summary>Places waiting chairs in waiting rooms and along corridors.</summary>
    public const string WaitingChairs = "waiting_chairs";

    /// <summary>Places (or moves) a waiting room's reception desk.</summary>
    public const string WaitingDesk = "waiting_desk";

    /// <summary>Puts doors in a waiting room's walls: the entrance on the front path, and doors through to corridors.</summary>
    public const string WaitingDoors = "waiting_doors";

    /// <summary>Placing or moving resus trolleys.</summary>
    public const string ResusTrolleys = "resus_trolleys";

    public static readonly IReadOnlySet<string> SpecialItems = new HashSet<string> { ExtendBuilding, AddFloor, WaitingChairs, WaitingDesk, WaitingDoors, ResusTrolleys };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<BuildMenuGroup> Groups { get; init; } = new();

    /// <summary>Every icon, left to right.</summary>
    public IEnumerable<BuildMenuIcon> Icons => Groups.SelectMany(g => g.Icons);

    public static BuildMenu Parse(string json) =>
        JsonSerializer.Deserialize<BuildMenu>(json, Options) ?? throw new InvalidDataException("The build menu file is empty.");

    /// <summary>Reads the build menu and fits it to the loaded game data.</summary>
    public static BuildMenu Load(IDataSource source, GameDatabase database) =>
        Parse(source.ReadText($"{Folder}/{FileName}")).Resolve(database);

    /// <summary>
    /// Returns a copy that only holds what exists: icons for missing departments are dropped, unknown
    /// room ids are skipped, and every room template not listed anywhere is added to the icon of its
    /// own department (or the catch-all icon, if no icon has that department).
    /// </summary>
    public BuildMenu Resolve(GameDatabase database)
    {
        var groups = Groups
            .Select(g => new BuildMenuGroup
            {
                Name = g.Name,
                Icons = g.Icons
                    .Where(i => i.Department is null || database.Departments.ContainsKey(i.Department))
                    .Select(i => i with { Items = i.Items.Where(id => SpecialItems.Contains(id) || database.RoomTemplates.ContainsKey(id)).Distinct().ToList() })
                    .ToList(),
            })
            .Where(g => g.Icons.Count > 0)
            .ToList();

        var icons = groups.SelectMany(g => g.Icons).ToList();
        var listed = icons.SelectMany(i => i.Items).ToHashSet();
        foreach (var template in database.RoomTemplates.Values.Where(t => !listed.Contains(t.Id)).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var home = icons.FirstOrDefault(i => i.Department == template.Department) ?? icons.FirstOrDefault(i => i.CatchAll) ?? icons.Last();
            home.Items.Add(template.Id);
        }

        return new BuildMenu { Groups = groups };
    }

    /// <summary>The icons whose pop-up holds a room, left to right.</summary>
    public IEnumerable<BuildMenuIcon> IconsWith(string itemId) => Icons.Where(i => i.Items.Contains(itemId));
}

public sealed class BuildMenuGroup
{
    public string Name { get; init; } = string.Empty;

    public List<BuildMenuIcon> Icons { get; init; } = new();
}

public sealed record BuildMenuIcon
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Short name under the icon.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Full name at the top of the pop-up.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The department this icon opens, or null for the building icon.</summary>
    public string? Department { get; init; }

    public string Colour { get; init; } = "#5B6B79";

    /// <summary>Which drawing the icon uses.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Rooms nobody else claims go here.</summary>
    public bool CatchAll { get; init; }

    public List<string> Items { get; init; } = new();
}
