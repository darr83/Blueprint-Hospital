using System.Globalization;
using BlueprintHospital.Simulation.Data.Models;
using BlueprintHospital.Simulation.Patients;

namespace BlueprintHospital.Simulation.Simulation;

/// <summary>What a cheat console command did: whether it worked, and a line to show the player.</summary>
public sealed record CheatResult(bool Succeeded, string Message);

/// <summary>
/// Cheat console commands. Money added by a cheat is booked under <see cref="MoneyCategory.Cheats"/>
/// so it shows as its own line in Finances.
/// </summary>
public sealed partial class HospitalSimulation
{
    /// <summary>The most money one "money" command can add or take away.</summary>
    public const long MaxCheatAmount = 1_000_000_000;

    /// <summary>The commands the cheat console understands, for its help text.</summary>
    public static readonly IReadOnlyList<string> CheatCommands = new[]
    {
        "money <amount>   add money to the balance (e.g. money 500000, money 2m, money -10k)",
        "emergency [type] start a ward emergency for a patient in bed (types: fall, sepsis, heart_attack, stroke...)",
        "level <number>   jump to a Career level (e.g. level 4)",
        "help             list the commands",
    };

    /// <summary>Runs one cheat console command, such as "money 500000".</summary>
    public CheatResult RunCheat(string? command)
    {
        var words = (command ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return new CheatResult(false, "Type a command, or help.");
        }

        switch (words[0].ToLowerInvariant())
        {
            case "help":
            case "?":
                return new CheatResult(true, string.Join("\n", CheatCommands));

            case "money":
                if (words.Length != 2 || !TryParseCheatAmount(words[1], out var amount))
                {
                    return new CheatResult(false, "Usage: money <amount>, e.g. money 500000");
                }

                if (amount == 0)
                {
                    return new CheatResult(false, "Amount must not be zero.");
                }

                Book(MoneyCategory.Cheats, amount);
                var verb = amount > 0 ? "Added" : "Took away";
                return new CheatResult(true, $"{verb} £{Math.Abs(amount):N0}. Balance is now £{Balance:N0}.");

            case "level":
                if (!IsCareer)
                {
                    return new CheatResult(false, "Levels are only used in a Career game.");
                }

                if (words.Length != 2 || !int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 1 || level > Database.Levels.Count)
                {
                    return new CheatResult(false, $"Usage: level <number>, from 1 to {Database.Levels.Count}.");
                }

                SetLevel(level);
                return new CheatResult(true, $"Now on level {Level}: {CurrentLevel!.Name}.");

            case "emergency":
                return CheatEmergency(words.Length > 1 ? words[1].ToLowerInvariant() : null);

            default:
                return new CheatResult(false, $"Unknown command \"{words[0]}\". Type help for the list.");
        }
    }

    private CheatResult CheatEmergency(string? id)
    {
        InpatientEmergencyDefinition? emergency = null;
        if (id is not null && !Database.Emergencies.TryGetValue(id, out emergency))
        {
            return new CheatResult(false, $"No emergency \"{id}\". Types: {string.Join(", ", Database.Emergencies.Keys)}.");
        }

        var patients = _patients.Values
            .Where(p => p.IsAlive && p.Stage == PatientStage.OnWard && BedOf(p) is not null && p.Emergency is null && !p.InArrest && !p.IsMoving)
            .Where(p => emergency is null || emergency.Requires.All(r => HasRisk(p, r)))
            .ToList();
        if (patients.Count == 0)
        {
            return new CheatResult(false, "Nobody in a ward bed can have that emergency right now.");
        }

        var patient = Random.Pick(patients);
        emergency ??= Random.Pick(Database.Emergencies.Values.Where(e => e.Requires.All(r => HasRisk(patient, r))).ToList());
        StartWardEmergency(patient, emergency);
        return new CheatResult(true, $"{emergency.Name} for {patient.FullName}.");
    }

    /// <summary>Reads amounts like 500000, £500,000, 250k or 1.5m.</summary>
    public static bool TryParseCheatAmount(string text, out long amount)
    {
        amount = 0;
        var t = text.Trim().Replace(",", string.Empty).Replace("_", string.Empty).Replace("£", string.Empty).ToLowerInvariant();
        var negative = t.StartsWith('-');
        if (negative || t.StartsWith('+'))
        {
            t = t[1..].Replace("£", string.Empty);
        }

        var multiplier = 1m;
        if (t.EndsWith('k'))
        {
            multiplier = 1_000m;
            t = t[..^1];
        }
        else if (t.EndsWith('m'))
        {
            multiplier = 1_000_000m;
            t = t[..^1];
        }
        else if (t.EndsWith("bn"))
        {
            multiplier = 1_000_000_000m;
            t = t[..^2];
        }

        if (t.Length == 0 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        value *= multiplier;
        if (value > MaxCheatAmount)
        {
            return false;
        }

        amount = (long)Math.Round(value) * (negative ? -1 : 1);
        return true;
    }
}
