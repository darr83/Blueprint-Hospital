namespace BlueprintHospital.Simulation.Audio;

/// <summary>
/// How loud a sound placed in the hospital should be, from how far the camera is zoomed in and
/// how far the sound is from the middle of the screen. Kept free of Godot so it can be tested.
/// </summary>
public static class SoundFalloff
{
    /// <summary>
    /// 0 to 1 from the camera zoom. Silent at or below <paramref name="silentAt"/>, full at or
    /// above <paramref name="fullAt"/>, eased in between. <paramref name="floor"/> keeps a sound
    /// faintly audible however far out you are (used for red alarms).
    /// </summary>
    public static double ZoomGain(double zoom, double silentAt, double fullAt, double floor = 0)
    {
        if (fullAt <= silentAt)
        {
            return zoom >= fullAt ? 1 : floor;
        }

        var t = Math.Clamp((zoom - silentAt) / (fullAt - silentAt), 0, 1);
        var eased = t * t * (3 - (2 * t));
        return floor + ((1 - floor) * eased);
    }

    /// <summary>
    /// 0 to 1 from the distance in screen pixels between the sound and the middle of the screen.
    /// Full inside <paramref name="innerRadius"/>, silent beyond <paramref name="outerRadius"/>.
    /// </summary>
    public static double DistanceGain(double screenDistance, double innerRadius, double outerRadius)
    {
        if (screenDistance <= innerRadius)
        {
            return 1;
        }

        if (screenDistance >= outerRadius)
        {
            return 0;
        }

        var t = (screenDistance - innerRadius) / (outerRadius - innerRadius);
        return (1 - t) * (1 - t);
    }

    /// <summary>Converts a 0 to 1 gain to decibels for the audio engine (silence is −80 dB).</summary>
    public static double ToDecibels(double gain) => gain <= 0.0001 ? -80 : 20 * Math.Log10(gain);
}
