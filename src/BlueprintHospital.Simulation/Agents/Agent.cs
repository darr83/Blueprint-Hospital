using BlueprintHospital.Simulation.World;

namespace BlueprintHospital.Simulation.Agents;

/// <summary>Anything that walks around the hospital: patients and staff.</summary>
public abstract class Agent
{
    /// <summary>
    /// Walking speed in tiles per game second. Real people walk about 1.2 m/s, but the game clock
    /// squeezes a day into a few hours, so at that pace everyone would dart across the screen. They
    /// walk at a third of it (0.4 m/s of game time), which looks like a steady walk at 1x and costs
    /// little in game time, as walks are a small part of anyone's shift.
    /// </summary>
    public const double DefaultWalkSpeed = WalkPaceFactor * 1.2 / Units.MetresPerTile;

    /// <summary>How much slower than real life people (and vehicles) move in game time, so the hospital can be watched.</summary>
    public const double WalkPaceFactor = 1.0 / 3.0;

    /// <summary>Tiles still to walk, each with how far the ride to it counts as (0 for an ordinary step; more for a lift or stairs).</summary>
    private readonly Queue<(GridCoord Tile, int Hop)> _path = new();

    /// <summary>How far through the current lift ride or stair climb the agent is, in tiles.</summary>
    private double _hopProgress;

    public int Id { get; init; }

    /// <summary>Position in grid units; a tile's centre is at (x + 0.5, y + 0.5).</summary>
    public double X { get; private set; }

    public double Y { get; private set; }

    /// <summary>Position at the start of the current tick, for smooth drawing between ticks.</summary>
    public double PreviousX { get; private set; }

    public double PreviousY { get; private set; }

    public double WalkSpeed { get; set; } = DefaultWalkSpeed;

    public bool IsMoving => _path.Count > 0;

    /// <summary>The tiles still to walk through, in order, ending at <see cref="Destination"/>.</summary>
    public IEnumerable<GridCoord> RemainingPath => _path.Select(p => p.Tile);

    /// <summary>True while riding a lift or climbing stairs to another floor.</summary>
    public bool IsChangingFloor => _path.Count > 0 && _path.Peek().Hop > 0;

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
        _hopProgress = 0;
        Destination = null;
    }

    /// <summary>Starts walking along <paramref name="path"/> (first entry may be the current tile).</summary>
    public void FollowPath(IReadOnlyList<GridCoord> path)
    {
        _path.Clear();
        _hopProgress = 0;
        var nav = path as NavPath;
        for (var i = 0; i < path.Count; i++)
        {
            _path.Enqueue((path[i], nav?.HopAt(i) ?? 0));
        }

        Destination = path.Count > 0 ? path[^1] : null;
    }

    public void StopMoving()
    {
        _path.Clear();
        _hopProgress = 0;
        Destination = null;
    }

    /// <summary>
    /// Puts the agent at (<paramref name="x"/>, <paramref name="y"/>) facing <paramref name="facing"/>,
    /// e.g. a porter keeping behind the trolley they push. Drawing interpolates from the last position.
    /// </summary>
    public void FollowAt(double x, double y, (double X, double Y) facing)
    {
        PreviousX = X;
        PreviousY = Y;
        X = x;
        Y = y;
        Facing = facing;
        _path.Clear();
        _hopProgress = 0;
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
            var (next, hop) = _path.Peek();
            if (hop > 0)
            {
                // In the lift or on the stairs: wait out the ride, then step out on the other floor.
                _hopProgress += budget;
                if (_hopProgress < hop)
                {
                    budget = 0;
                    break;
                }

                budget = _hopProgress - hop;
                _hopProgress = 0;
                X = PreviousX = next.X + 0.5;
                Y = PreviousY = next.Y + 0.5;
                _path.Dequeue();
                continue;
            }

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
