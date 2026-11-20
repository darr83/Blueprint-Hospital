namespace BlueprintHospital.Simulation.Medical.Results;

/// <summary>One recorded channel: an ECG lead or an EEG channel, in millivolts (ECG) or microvolts (EEG).</summary>
public sealed record TraceChannel(string Name, float[] Samples);

/// <summary>An ECG or EEG recording, with the machine's measurements and which channels show the finding.</summary>
public sealed class TraceRecording
{
    public required string Title { get; init; }

    public required int SampleRate { get; init; }

    public required IReadOnlyList<TraceChannel> Channels { get; init; }

    /// <summary>The machine's printed measurements (rate, intervals, axis).</summary>
    public IReadOnlyList<string> Measurements { get; init; } = Array.Empty<string>();

    /// <summary>The machine's interpretation line.</summary>
    public string Statement { get; init; } = string.Empty;

    /// <summary>Channels showing the main finding, outlined when the finding marker is on.</summary>
    public IReadOnlyList<string> Marked { get; init; } = Array.Empty<string>();

    public string MarkLabel { get; init; } = string.Empty;

    /// <summary>Channels with the mirror-image change (reciprocal ST depression).</summary>
    public IReadOnlyList<string> Reciprocal { get; init; } = Array.Empty<string>();

    public string ReciprocalLabel { get; init; } = string.Empty;

    public TraceChannel Channel(string name) => Channels.First(c => c.Name == name);
}

/// <summary>Builds ECG and EEG recordings for each trace picture id.</summary>
public static class Waveforms
{
    public static readonly string[] EcgLeads = { "I", "II", "III", "aVR", "aVL", "aVF", "V1", "V2", "V3", "V4", "V5", "V6" };

    private static readonly string[] EegChannels = { "Fp1-F3", "F3-C3", "C3-P3", "P3-O1", "Fp2-F4", "F4-C4", "C4-P4", "P4-O2", "F7-T3", "T3-T5", "F8-T4", "T4-T6" };

    internal static void Register(PictureRegistrar add)
    {
        foreach (var (id, text) in EcgPatterns)
        {
            add(id, PictureKind.Ecg, text, null);
        }

        add("eeg_normal", PictureKind.Eeg, "EEG, normal", null);
        add("eeg_slowing", PictureKind.Eeg, "EEG, some slowing", null);
        add("eeg_epileptiform", PictureKind.Eeg, "EEG, epileptiform discharges", null);
    }

    private static readonly (string Id, string Text)[] EcgPatterns =
    {
        ("ecg_normal", "ECG, normal sinus rhythm"),
        ("ecg_t_flat", "ECG, non-specific T-wave flattening"),
        ("ecg_inferior_stemi", "ECG, inferior STEMI"),
        ("ecg_anterior_stemi", "ECG, anterior STEMI"),
        ("ecg_lateral_st_depression", "ECG, lateral ST depression"),
        ("ecg_deep_twi", "ECG, deep anterior T-wave inversion"),
        ("ecg_af_fast", "ECG, fast atrial fibrillation"),
        ("ecg_svt", "ECG, SVT"),
        ("ecg_lbbb", "ECG, sinus tachycardia with LBBB"),
        ("ecg_chb", "ECG, complete heart block"),
        ("ecg_pericarditis", "ECG, pericarditis"),
        ("ecg_sinus_tachy", "ECG, sinus tachycardia"),
    };

    private sealed record Lead(float P, float Q, float R, float S, float T, float St = 0);

    private static readonly Dictionary<string, Lead> Normal = new()
    {
        ["I"] = new(0.1f, -0.05f, 0.7f, -0.1f, 0.2f),
        ["II"] = new(0.15f, -0.05f, 1.1f, -0.15f, 0.3f),
        ["III"] = new(0.08f, -0.08f, 0.5f, -0.1f, 0.12f),
        ["aVR"] = new(-0.12f, 0, -0.8f, 0.1f, -0.25f),
        ["aVL"] = new(0.05f, -0.03f, 0.4f, -0.15f, 0.1f),
        ["aVF"] = new(0.12f, -0.06f, 0.8f, -0.12f, 0.2f),
        ["V1"] = new(0.08f, 0, 0.25f, -1.0f, -0.05f),
        ["V2"] = new(0.1f, 0, 0.45f, -1.2f, 0.35f),
        ["V3"] = new(0.1f, 0, 0.8f, -0.7f, 0.35f),
        ["V4"] = new(0.1f, -0.05f, 1.3f, -0.4f, 0.3f),
        ["V5"] = new(0.1f, -0.08f, 1.4f, -0.2f, 0.25f),
        ["V6"] = new(0.1f, -0.08f, 1.1f, -0.1f, 0.2f),
    };

    /// <summary>Builds the recording for trace picture <paramref name="id"/>.</summary>
    public static TraceRecording Build(string id, int seed)
    {
        var random = new Random(seed);
        return id.StartsWith("eeg_", StringComparison.Ordinal) ? Eeg(id, random) : Ecg(id, random);
    }

    private static TraceRecording Ecg(string id, Random random)
    {
        const int rate = 250;
        const double seconds = 10;
        var leads = Normal.ToDictionary(kv => kv.Key, kv => kv.Value);
        var hr = 64 + random.Next(20);
        var hasP = true;
        var irregular = false;
        var qrsWidth = 1.0;
        var pr = 160 + random.Next(30);
        var statement = "Sinus rhythm. Normal ECG.";
        string[] marked = Array.Empty<string>();
        string[] reciprocal = Array.Empty<string>();
        var markLabel = string.Empty;
        var recipLabel = string.Empty;
        double? pRate = null;
        var axis = 40 + random.Next(30);

        void St(float st, params string[] names)
        {
            foreach (var n in names)
            {
                leads[n] = leads[n] with { St = st, T = st > 0 ? Math.Max(leads[n].T, st * 1.3f) : leads[n].T };
            }
        }

        void T(float t, params string[] names)
        {
            foreach (var n in names)
            {
                leads[n] = leads[n] with { T = t };
            }
        }

        switch (id)
        {
            case "ecg_t_flat":
                T(0.04f, "I", "aVL", "V5", "V6");
                statement = "Sinus rhythm. Non-specific T-wave flattening.";
                marked = new[] { "V5", "V6" };
                markLabel = "Flat T waves";
                break;
            case "ecg_inferior_stemi":
                hr = 84 + random.Next(16);
                St(0.3f, "II");
                St(0.38f, "III");
                St(0.33f, "aVF");
                St(-0.12f, "I");
                St(-0.25f, "aVL");
                statement = "Sinus rhythm. Inferior ST elevation. *** ACUTE MI / STEMI ***";
                marked = new[] { "II", "III", "aVF" };
                markLabel = "ST elevation II, III, aVF";
                reciprocal = new[] { "I", "aVL" };
                recipLabel = "Reciprocal ST depression I, aVL";
                break;
            case "ecg_anterior_stemi":
                hr = 88 + random.Next(16);
                St(0.25f, "V1");
                St(0.4f, "V2");
                St(0.42f, "V3");
                St(0.32f, "V4");
                St(-0.1f, "III");
                St(-0.12f, "aVF");
                statement = "Sinus rhythm. Anterior ST elevation. *** ACUTE MI / STEMI ***";
                marked = new[] { "V1", "V2", "V3", "V4" };
                markLabel = "ST elevation V1 to V4";
                reciprocal = new[] { "III", "aVF" };
                recipLabel = "Reciprocal ST depression";
                break;
            case "ecg_lateral_st_depression":
                hr = 80 + random.Next(20);
                St(-0.18f, "V4", "V5", "V6", "I");
                T(-0.2f, "V4", "V5", "V6");
                statement = "Sinus rhythm. ST depression and T-wave inversion V4-V6. Consider ischaemia.";
                marked = new[] { "V4", "V5", "V6" };
                markLabel = "ST depression V4 to V6";
                break;
            case "ecg_deep_twi":
                T(-0.55f, "V1", "V2", "V3", "V4");
                statement = "Sinus rhythm. Deep T-wave inversion V1-V4. Consider ischaemia.";
                marked = new[] { "V1", "V2", "V3", "V4" };
                markLabel = "Deep T-wave inversion";
                break;
            case "ecg_af_fast":
                hr = 130 + random.Next(20);
                hasP = false;
                irregular = true;
                statement = "Atrial fibrillation with rapid ventricular response.";
                marked = new[] { "II" };
                markLabel = "Irregular, no P waves";
                break;
            case "ecg_svt":
                hr = 185 + random.Next(15);
                hasP = false;
                statement = "Narrow complex tachycardia. Probable SVT.";
                marked = new[] { "II" };
                markLabel = "Regular, very fast";
                break;
            case "ecg_lbbb":
                hr = 100 + random.Next(12);
                qrsWidth = 1.9;
                leads["V1"] = leads["V1"] with { R = 0.1f, S = -1.4f, T = 0.4f };
                leads["V6"] = leads["V6"] with { R = 1.3f, S = 0, T = -0.25f, Q = 0 };
                leads["I"] = leads["I"] with { R = 1.0f, S = 0, T = -0.2f, Q = 0 };
                leads["aVL"] = leads["aVL"] with { R = 0.8f, S = 0, T = -0.2f, Q = 0 };
                statement = "Sinus tachycardia. Left bundle branch block.";
                marked = new[] { "V1", "V6" };
                markLabel = "Broad QRS (LBBB)";
                break;
            case "ecg_chb":
                hr = 34 + random.Next(6);
                pRate = 78 + random.Next(10);
                qrsWidth = 1.6;
                statement = "Complete heart block. Ventricular escape rhythm.";
                marked = new[] { "II" };
                markLabel = "P waves not linked to QRS";
                break;
            case "ecg_pericarditis":
                hr = 90 + random.Next(16);
                St(0.16f, "I", "II", "aVF", "V2", "V3", "V4", "V5", "V6");
                St(-0.12f, "aVR");
                statement = "Sinus rhythm. Widespread ST elevation with PR depression. Consider pericarditis.";
                marked = new[] { "II", "V5" };
                markLabel = "Saddle-shaped ST elevation";
                break;
            case "ecg_sinus_tachy":
                hr = 108 + random.Next(10);
                statement = "Sinus tachycardia. Otherwise normal ECG.";
                break;
        }

        // Beat times.
        var beats = new List<double>();
        var t = random.NextDouble() * 0.5;
        while (t < seconds + 1)
        {
            beats.Add(t);
            var rr = 60.0 / hr;
            t += irregular ? rr * (0.7 + (random.NextDouble() * 0.6)) : rr;
        }

        var pWaves = new List<double>();
        if (pRate is { } pr2)
        {
            for (var p = random.NextDouble() * 0.3; p < seconds + 1; p += 60.0 / pr2)
            {
                pWaves.Add(p);
            }
        }

        var n = (int)(seconds * rate);
        var channels = new List<TraceChannel>();
        var fib = new float[n];
        for (var i = 0; i < n; i++)
        {
            fib[i] = irregular ? (float)((Math.Sin(i * 0.37) * 0.03) + (Math.Sin(i * 0.53 + 1) * 0.025) + ((random.NextDouble() - 0.5) * 0.02)) : 0;
        }

        var prDepression = id == "ecg_pericarditis" ? -0.06f : 0;
        var prSeconds = pr / 1000.0;
        foreach (var name in EcgLeads)
        {
            var lead = leads[name];
            var samples = new float[n];
            for (var i = 0; i < n; i++)
            {
                var time = i / (double)rate;
                double v = 0;
                foreach (var b in beats)
                {
                    var dt = time - b;
                    if (dt < -0.35 || dt > 0.65)
                    {
                        continue;
                    }

                    v += Complex(dt, lead, qrsWidth);
                    if (hasP && pRate is null)
                    {
                        v += G(lead.P, dt + prSeconds - 0.06, 0.022);
                        v += prDepression * Window(dt + prSeconds, 0.08, prSeconds - 0.04);
                    }
                }

                foreach (var p in pWaves)
                {
                    v += G(lead.P, time - p, 0.022);
                }

                v += fib[i] * (name is "II" or "III" or "aVF" or "V1" ? 1.0 : 0.5);
                v += (random.NextDouble() - 0.5) * 0.012;
                samples[i] = (float)v;
            }

            channels.Add(new TraceChannel(name, samples));
        }

        var qrs = (int)(86 * qrsWidth) + random.Next(10);
        var qt = (int)(360 * Math.Sqrt(60.0 / hr)) + random.Next(20);
        var qtc = (int)(qt / Math.Sqrt(60.0 / hr));
        var measurements = new List<string>
        {
            $"Vent. rate {hr} BPM",
            hasP && pRate is null ? $"PR int {pr} ms" : "PR int --",
            $"QRS dur {qrs} ms",
            $"QT/QTc {qt}/{qtc} ms",
            $"Axis {axis}°",
        };

        return new TraceRecording
        {
            Title = "12-lead ECG",
            SampleRate = rate,
            Channels = channels,
            Measurements = measurements,
            Statement = statement,
            Marked = marked,
            MarkLabel = markLabel,
            Reciprocal = reciprocal,
            ReciprocalLabel = recipLabel,
        };
    }

    private static double G(double a, double x, double s) => a * Math.Exp(-(x * x) / (2 * s * s));

    private static double Window(double x, double from, double to) => x >= from && x <= to ? 1 : 0;

    /// <summary>QRS, ST segment and T wave of one beat, <paramref name="dt"/> seconds after the R wave starts.</summary>
    private static double Complex(double dt, Lead l, double width)
    {
        var w = width;
        var v = G(l.Q, dt + (0.022 * w), 0.007 * w) + G(l.R, dt, 0.009 * w) + G(l.S, dt - (0.024 * w), 0.009 * w);
        var tPeak = 0.2 + (0.03 * w);
        v += G(l.T, dt - tPeak, 0.045);
        if (l.St != 0)
        {
            var j = 0.035 * w;
            double s;
            if (dt < j)
            {
                s = Math.Exp(-((dt - j) * (dt - j)) / (2 * 0.006 * 0.006));
            }
            else if (dt < tPeak - 0.02)
            {
                s = 1;
            }
            else
            {
                var x = dt - (tPeak - 0.02);
                s = Math.Exp(-(x * x) / (2 * 0.05 * 0.05));
            }

            v += l.St * s;
        }

        return v;
    }

    private static TraceRecording Eeg(string id, Random random)
    {
        const int rate = 200;
        const double seconds = 10;
        var n = (int)(seconds * rate);
        var channels = new List<TraceChannel>();
        var spikes = new List<double>();
        if (id == "eeg_epileptiform")
        {
            for (var s = 1.2 + random.NextDouble(); s < seconds - 0.5; s += 2.2 + random.NextDouble())
            {
                spikes.Add(s);
            }
        }

        var phase = random.NextDouble() * 6;
        foreach (var name in EegChannels)
        {
            var posterior = name.Contains('O', StringComparison.Ordinal) || name.Contains("P4", StringComparison.Ordinal) || name.Contains("P3", StringComparison.Ordinal);
            var samples = new float[n];
            var chPhase = random.NextDouble() * 6;
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)rate;
                var v = (posterior ? 32 : 12) * Math.Sin((2 * Math.PI * 10 * t) + chPhase) * (0.7 + (0.3 * Math.Sin(t * 1.3 + phase)));
                v += 6 * Math.Sin((2 * Math.PI * 19 * t) + (chPhase * 2));
                v += (random.NextDouble() - 0.5) * 10;
                if (id == "eeg_slowing")
                {
                    v += 26 * Math.Sin((2 * Math.PI * 4.5 * t) + chPhase) * (name.Contains('T', StringComparison.Ordinal) ? 1.4 : 0.8);
                }

                foreach (var s in spikes)
                {
                    var dt = t - s;
                    if (dt is > -0.05 and < 0.9)
                    {
                        var burst = dt < 0.6 ? 1 : 0;
                        v += burst * ((-90 * Math.Exp(-(Math.Pow((dt % (1 / 3.0)) - 0.02, 2)) / 0.00012)) + (45 * Math.Sin(2 * Math.PI * 3 * dt)));
                    }
                }

                samples[i] = (float)v;
            }

            channels.Add(new TraceChannel(name, samples));
        }

        return new TraceRecording
        {
            Title = "EEG (bipolar montage)",
            SampleRate = rate,
            Channels = channels,
            Measurements = new[] { "Sensitivity 7 µV/mm", "Time base 30 mm/s", "Filters 1-70 Hz" },
            Statement = id switch
            {
                "eeg_epileptiform" => "Generalised 3 Hz spike-and-wave discharges.",
                "eeg_slowing" => "Intermittent theta slowing over the temporal regions.",
                _ => "Posterior alpha rhythm 10 Hz, reactive. No epileptiform activity.",
            },
            Marked = id == "eeg_epileptiform" ? EegChannels : id == "eeg_slowing" ? new[] { "F7-T3", "T3-T5", "F8-T4", "T4-T6" } : Array.Empty<string>(),
            MarkLabel = id == "eeg_epileptiform" ? "Spike-and-wave discharges" : id == "eeg_slowing" ? "Slow waves" : string.Empty,
        };
    }

    /// <summary>True for ECG and EEG trace ids.</summary>
    public static bool IsTrace(string id) => id.StartsWith("ecg_", StringComparison.Ordinal) || id.StartsWith("eeg_", StringComparison.Ordinal);
}
