using BlueprintHospital.Simulation.Data;

namespace BlueprintHospital.Simulation.Tests;

public class BuildMenuTests
{
    private static (GameDatabase Db, BuildMenu Menu) Shipped()
    {
        var source = new FileSystemDataSource(TestData.GameDataDirectory());
        var db = GameDatabase.Load(source);
        return (db, BuildMenu.Load(source, db));
    }

    [Fact]
    public void Every_room_can_be_built_from_the_bar()
    {
        var (db, menu) = Shipped();
        var listed = menu.Icons.SelectMany(i => i.Items).ToHashSet();
        foreach (var id in db.RoomTemplates.Keys)
        {
            Assert.Contains(id, listed);
        }
    }

    [Fact]
    public void Building_icon_holds_the_structure_and_comes_first()
    {
        var (_, menu) = Shipped();
        var building = menu.Icons.First();
        Assert.Equal("building", building.Id);
        foreach (var id in new[] { BuildMenu.ExtendBuilding, BuildMenu.AddFloor, "corridor", "lift", "stairs" })
        {
            Assert.Contains(id, building.Items);
        }
    }

    [Fact]
    public void Maternity_and_toilet_rooms_have_their_icons()
    {
        var (_, menu) = Shipped();
        var maternity = menu.Icons.Single(i => i.Id == "maternity");
        foreach (var id in new[] { "delivery_room", "mother_baby_ward", "neonatal_unit", "caesarean_theatre", "antenatal_clinic", "antenatal_scan_room" })
        {
            Assert.Contains(id, maternity.Items);
        }

        var facilities = menu.Icons.Single(i => i.Id == "facilities");
        foreach (var id in new[] { "public_toilets", "accessible_toilet", "staff_toilet" })
        {
            Assert.Contains(id, facilities.Items);
        }
    }

    [Fact]
    public void Shared_rooms_appear_under_several_departments()
    {
        var (_, menu) = Shipped();
        Assert.True(menu.IconsWith("operating_theatre").Count() >= 3);
        Assert.True(menu.IconsWith("nurses_station").Count() >= 5);
        Assert.True(menu.IconsWith("xray_room").Count() >= 2);
    }

    [Fact]
    public void Icons_have_different_colours_and_known_departments()
    {
        var (db, menu) = Shipped();
        var icons = menu.Icons.ToList();
        Assert.Equal(icons.Count, icons.Select(i => i.Colour.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(icons.Count, icons.Select(i => i.Id).Distinct().Count());
        Assert.All(icons.Where(i => i.Department is not null), i => Assert.Contains(i.Department!, db.Departments.Keys));
    }

    [Fact]
    public void Missing_departments_and_rooms_are_dropped_and_unlisted_rooms_go_to_their_department()
    {
        var (db, _) = Shipped();
        var menu = BuildMenu.Parse("""
            { "groups": [
              { "name": "A", "icons": [
                { "id": "emergency", "label": "A&E", "name": "A&E", "department": "emergency", "items": ["triage_room", "no_such_room"] },
                { "id": "ghost", "label": "Ghost", "name": "Ghost", "department": "no_such_department", "items": ["resus_bay"] },
                { "id": "misc", "label": "Misc", "name": "Misc", "catch_all": true, "items": [] }
              ] } ] }
            """).Resolve(db);

        var icons = menu.Icons.ToList();
        Assert.Equal(new[] { "emergency", "misc" }, icons.Select(i => i.Id));
        Assert.DoesNotContain("no_such_room", icons[0].Items);
        Assert.Contains("resus_bay", icons[0].Items);
        Assert.Contains("cath_lab", icons[1].Items);
    }
}
