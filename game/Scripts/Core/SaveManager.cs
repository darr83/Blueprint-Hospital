using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>Reads and writes save files in user://saves.</summary>
public static class SaveManager
{
    public const string SaveDirectory = "user://saves";
    private const string Extension = ".bhsave";

    public static string PathFor(string slotName) => $"{SaveDirectory}/{slotName}{Extension}";

    public static void Write(string slotName, SaveGame save)
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);
        var path = PathFor(slotName);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
            ?? throw new InvalidOperationException($"Could not write {path}: {FileAccess.GetOpenError()}");
        file.StoreString(SaveSerializer.Serialize(save));
    }

    public static SaveGame Read(string slotName)
    {
        var path = PathFor(slotName);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read)
            ?? throw new InvalidOperationException($"Could not open {path}: {FileAccess.GetOpenError()}");
        return SaveSerializer.Deserialize(file.GetAsText());
    }

    public static bool Delete(string slotName) => DirAccess.RemoveAbsolute(PathFor(slotName)) == Error.Ok;

    /// <summary>All readable saves, newest first. Damaged files are skipped.</summary>
    public static IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        var slots = new List<SaveSlotInfo>();
        using var dir = DirAccess.Open(SaveDirectory);
        if (dir is null)
        {
            return slots;
        }

        foreach (var name in dir.GetFiles().Where(f => f.EndsWith(Extension, StringComparison.Ordinal)))
        {
            var slot = name[..^Extension.Length];
            try
            {
                var save = Read(slot);
                slots.Add(new SaveSlotInfo(slot, save.HospitalName, save.Clock.Now, save.SavedAtUtc));
            }
            catch (Exception ex)
            {
                GD.PushWarning($"Skipping unreadable save '{name}': {ex.Message}");
            }
        }

        return slots.OrderByDescending(s => s.SavedAtUtc).ToList();
    }
}
