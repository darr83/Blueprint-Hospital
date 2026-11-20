using BlueprintHospital.Simulation.Medical.Results;

namespace BlueprintHospital.Simulation.Data.Models;

/// <summary>Checks picture ids named in the investigation data against the pictures the game can draw.</summary>
public static class ResultPictureIds
{
    public static bool IsKnown(string id) => ResultPictures.Has(id);
}
