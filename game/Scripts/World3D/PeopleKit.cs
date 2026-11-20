using System.Collections.Generic;

namespace BlueprintHospital.World3D;

/// <summary>
/// The Blender-made body parts people are assembled from (tools/Blender/models/people_kit.py).
/// Each part fills a box one unit across, as the plain balls and capsules did, so a figure asks for
/// "head" or "sleeve" at a place and size and gets the modelled shape, painted in its own colour.
/// </summary>
public static class PeopleKit
{
    private const string Model = "people_kit";

    /// <summary>The surfaces of a part, or null if the kit has no such part.</summary>
    public static ModelParts.Surface[]? Part(string name) => ModelParts.Part(Model, name);

    public static IReadOnlyCollection<string> Names => ModelParts.Names(Model);
}
