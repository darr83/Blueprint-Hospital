namespace BlueprintHospital.Simulation.Core;

/// <summary>Seeded random source so a game (and every test) can be replayed exactly.</summary>
public sealed class GameRandom(int seed)
{
    private Random _random = new(seed);

    public int Seed { get; private set; } = seed;

    /// <summary>Starts a fresh sequence, e.g. after loading a game (the old sequence can't be saved).</summary>
    public void Reseed(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    public double NextDouble() => _random.NextDouble();

    /// <summary>Integer in [min, max] inclusive.</summary>
    public int Range(int min, int max) => _random.Next(min, max + 1);

    public double Range(double min, double max) => min + (_random.NextDouble() * (max - min));

    public bool Chance(double probability) => probability > 0 && _random.NextDouble() < probability;

    /// <summary>Normally distributed value (Box–Muller).</summary>
    public double Normal(double mean, double standardDeviation)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + (z * standardDeviation);
    }

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        }

        return items[_random.Next(items.Count)];
    }

    public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        var total = items.Sum(weight);
        if (total <= 0)
        {
            throw new ArgumentException("Weights must add up to more than zero.", nameof(items));
        }

        var roll = _random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll < 0)
            {
                return item;
            }
        }

        return items[^1];
    }

    /// <summary>Number of events in a Poisson process with the given expected count.</summary>
    public int Poisson(double expected)
    {
        if (expected <= 0)
        {
            return 0;
        }

        var limit = Math.Exp(-expected);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= _random.NextDouble();
        }
        while (p > limit);
        return k - 1;
    }
}
