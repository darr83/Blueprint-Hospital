namespace BlueprintHospital.Simulation.Medical;

/// <summary>The heart rhythm in a cardiac arrest. Only a shockable rhythm (VF or pulseless VT) can be fixed by the defibrillator.</summary>
public enum ArrestRhythm
{
    /// <summary>VF or pulseless VT: a shock from the defibrillator can restart the heart.</summary>
    Shockable,

    /// <summary>Asystole or PEA: no shock; CPR and adrenaline from the resus trolley are all that can help.</summary>
    NonShockable,
}

/// <summary>
/// The odds in a cardiac arrest, kept apart from the simulation so they can be tuned and tested on their own.
/// Aimed at UK in-hospital figures: about half get their circulation back (ROSC) and roughly one in four
/// survive to leave hospital. What matters most is how quickly CPR starts, then (for a shockable rhythm) how
/// quickly the defibrillator arrives, then the team: a senior, rested team leader and an anaesthetist help.
/// </summary>
public static class ResusOdds
{
    /// <summary>About one in four in-hospital arrests start in a shockable rhythm (more for heart patients).</summary>
    public const double ShockableShare = 0.25;

    public const double ShockableShareCardiac = 0.45;

    /// <summary>Minutes between shocks (one cycle of CPR).</summary>
    public const double MinutesBetweenShocks = 2;

    /// <summary>A shockable rhythm left this long without a shock usually decays to asystole.</summary>
    public const double ShockableDecaysAfterMinutes = 8;

    /// <summary>The team stop once CPR has gone on this long without the heart restarting.</summary>
    public const double StopAfterMinutesOfCpr = 25;

    /// <summary>With nobody doing CPR for this long after the arrest, there is no chance left.</summary>
    public const double HopelessWithoutCprAfterMinutes = 12;

    /// <summary>
    /// How much the time without CPR ("no flow") has cost: 1 for CPR straight away, about a third after
    /// 3 minutes, almost nothing after 10.
    /// </summary>
    public static double Quality(double noFlowMinutes) => Math.Exp(-Math.Max(0, noFlowMinutes) / 3.0);

    /// <summary>Older patients do worse; children (whose arrests are usually breathing problems) do better.</summary>
    public static double AgeFactor(int age) => age switch
    {
        < 16 => 1.2,
        < 60 => 1.1,
        < 70 => 1.0,
        < 80 => 0.85,
        _ => 0.7,
    };

    /// <summary>
    /// The team: one pair of hands tires quickly, a team leader (doctor) runs the arrest well, more so if
    /// senior, skilled and rested, and an anaesthetist secures the airway.
    /// </summary>
    public static double TeamFactor(int compressors, double? leaderSkill, double leaderFatigue, bool airway)
    {
        var hands = compressors >= 2 ? 1.0 : compressors == 1 ? 0.8 : 0;
        var leader = leaderSkill is { } skill ? 0.7 + (0.6 * skill * (1 - (0.4 * leaderFatigue))) : 0.6;
        return hands * leader * (airway ? 1.25 : 1.0);
    }

    /// <summary>The chance one shock restarts the heart (shockable rhythm, defibrillator here, CPR going).</summary>
    public static double ShockSuccess(double quality, double team, double age) => Math.Clamp(0.33 * quality * team * age, 0, 0.9);

    /// <summary>The chance per minute of CPR that the heart restarts in a non-shockable rhythm (higher with adrenaline from the trolley).</summary>
    public static double NonShockablePerMinute(double quality, double team, double age, bool trolley) =>
        Math.Clamp((trolley ? 0.033 : 0.012) * quality * team * age, 0, 0.5);

    /// <summary>Of those whose heart restarts, the share who go on to leave hospital alive.</summary>
    public static double SurvivalAfterRosc(double quality, double age, ArrestRhythm rhythm) =>
        Math.Clamp((rhythm == ArrestRhythm.Shockable ? 0.65 : 0.46) * Math.Sqrt(quality) * age, 0.08, 0.9);

    /// <summary>
    /// A rough chance of leaving hospital alive from here, for the crash panel: the chance of ROSC in the
    /// time left, assuming the team and kit now present stay, times the chance of surviving after it.
    /// </summary>
    public static double EstimateSurvival(ArrestRhythm rhythm, double minutesSoFar, double noFlowMinutes, bool cprGoing, bool trolley, double team, double age)
    {
        var q = Quality(noFlowMinutes);
        if (!cprGoing)
        {
            // Not started yet: the longer it takes, the worse it gets; assume someone starts in a minute.
            q = Quality(noFlowMinutes + 1);
            team = Math.Max(team, 0.5);
        }

        var left = Math.Max(0, StopAfterMinutesOfCpr - minutesSoFar);
        double noRosc;
        if (rhythm == ArrestRhythm.Shockable && trolley)
        {
            var shocks = Math.Floor(left / MinutesBetweenShocks);
            noRosc = Math.Pow(1 - ShockSuccess(q, team, age), shocks);
        }
        else
        {
            noRosc = Math.Pow(1 - NonShockablePerMinute(q, team, age, trolley), left);
        }

        return (1 - noRosc) * SurvivalAfterRosc(q, age, rhythm);
    }
}
