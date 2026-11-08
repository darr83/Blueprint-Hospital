using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Agents;

/// <summary>Anything that walks around the hospital: patients and staff.</summary>
public abstract class Agent
{
    /// <summary>Walking speed in tiles per game second (1.2 m/s).</summary>
    public const double DefaultWalkSpeed = 1.2 / Units.MetresPerTile;

    private readonly Queue<GridCoord> _path = new();

    public int Id { get; init; }

    /// <summary>Position in grid units; a tile's centre is at (x + 0.5, y + 0.5).</summary>
    public double X { get; private set; }

    public double Y { get; private set; }

    /// <summary>Position at the start of the current tick, for smooth drawing between ticks.</summary>
    public double PreviousX { get; private set; }

    public double PreviousY { get; private set; }

    public double WalkSpeed { get; set; } = DefaultWalkSpeed;

    public bool IsMoving => _path.Count > 0;

    public GridCoord Tile => new((int)Math.Floor(X), (int)Math.Floor(Y));

    /// <summary>Where the agent is heading, if moving.</summary>
    public GridCoord? Destination { get; private set; }

    /// <summary>Direction of travel, for drawing.</summary>
    public (double X, double Y) Facing { get; private set; } = (1, 0);

    public void PlaceAt(GridCoord tile)
    {
        X = PreviousX = tile.X + 0.5;
        Y = PreviousY = tile.Y + 0.5;
        _path.Clear();
        Destination = null;
    }

    /// <summary>Starts walking along <paramref name="path"/> (first entry may be the current tile).</summary>
    public void FollowPath(IReadOnlyList<GridCoord> path)
    {
        _path.Clear();
        foreach (var tile in path)
        {
            _path.Enqueue(tile);
        }

        Destination = path.Count > 0 ? path[^1] : null;
    }

    public void StopMoving()
    {
        _path.Clear();
        Destination = null;
    }

    /// <summary>Moves along the path for <paramref name="seconds"/> of game time.</summary>
    public void Move(double seconds)
    {
        PreviousX = X;
        PreviousY = Y;
        var budget = WalkSpeed * seconds;

        while (budget > 0 && _path.Count > 0)
        {
            var next = _path.Peek();
            var tx = next.X + 0.5;
            var ty = next.Y + 0.5;
            var dx = tx - X;
            var dy = ty - Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance <= budget)
            {
                X = tx;
                Y = ty;
                budget -= distance;
                _path.Dequeue();
            }
            else
            {
                X += dx / distance * budget;
                Y += dy / distance * budget;
                budget = 0;
            }

            if (distance > 1e-6)
            {
                Facing = (dx / distance, dy / distance);
            }
        }

        if (_path.Count == 0)
        {
            Destination = null;
        }
    }
}
