namespace BlueprintHospital.Simulation.World;

/// <summary>
/// A path found by <see cref="NavGrid.FindPath"/>. Where it changes floor (by lift or stairs) the next
/// tile is not beside the last one, and <see cref="HopAt"/> gives how far that ride counts as, in tiles walked.
/// </summary>
public sealed class NavPath : List<GridCoord>
{
    private Dictionary<int, int>? _hops;

    /// <summary>Extra distance (in tiles) to reach entry <paramref name="index"/> from the one before: 0 for an ordinary step.</summary>
    public int HopAt(int index) => _hops is not null && _hops.TryGetValue(index, out var hop) ? hop : 0;

    internal void SetHop(int index, int distance) => (_hops ??= new())[index] = distance;
}

/// <summary>A link between floors: a lift or a flight of stairs from one tile to another.</summary>
public readonly record struct NavLink(GridCoord To, int Cost, bool StepFree);

/// <summary>
/// Walkable-tile graph for pathfinding. Tiles have a movement cost (0 = impassable) and
/// walls block the edge between two tiles, so rooms are only entered through their doors.
/// Lifts and stairs add links between floors; trolleys and wheelchairs use only the step-free ones (lifts).
/// </summary>
public sealed class NavGrid
{
    private static readonly (int Dx, int Dy)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    private readonly byte[] _cost;
    private readonly HashSet<long> _blockedEdges = new();
    private readonly Dictionary<int, List<NavLink>> _links = new();

    /// <param name="floorStride">Rows from one floor to the next (see <see cref="SiteMap.FloorStride"/>); 0 for a single floor.</param>
    public NavGrid(int width, int length, int floorStride = 0)
    {
        Width = width;
        Length = length;
        FloorStride = floorStride;
        _cost = new byte[width * length];
    }

    public int Width { get; }

    public int Length { get; }

    /// <summary>Rows from one floor to the next, or 0 if the grid has only one floor.</summary>
    public int FloorStride { get; }

    /// <summary>The cheapest a link can cost per floor climbed, so the A* estimate never overshoots.</summary>
    public int MinLinkCostPerFloor { get; set; } = 1;

    /// <summary>Every floor link, for drawing and tests.</summary>
    public IEnumerable<(GridCoord From, NavLink Link)> Links => _links.SelectMany(kv => kv.Value.Select(l => (Coord(kv.Key), l)));

    /// <summary>Links <paramref name="a"/> and <paramref name="b"/> both ways, e.g. the same spot in a lift on two floors.</summary>
    public void AddLink(GridCoord a, GridCoord b, int cost, bool stepFree)
    {
        if (!InBounds(a) || !InBounds(b) || a == b)
        {
            return;
        }

        Add(a, new NavLink(b, Math.Max(1, cost), stepFree));
        Add(b, new NavLink(a, Math.Max(1, cost), stepFree));

        void Add(GridCoord from, NavLink link)
        {
            if (!_links.TryGetValue(Index(from), out var list))
            {
                _links[Index(from)] = list = new List<NavLink>();
            }

            list.Add(link);
        }
    }

    /// <summary>Tiles one step (or one ride) from <paramref name="c"/>, with what the step costs.</summary>
    public IEnumerable<(GridCoord Tile, int Cost, bool IsLink)> Neighbours(GridCoord c, bool stepFreeOnly = false)
    {
        foreach (var (dx, dy) in Directions)
        {
            var n = new GridCoord(c.X + dx, c.Y + dy);
            if (InBounds(n) && CanStep(c, n))
            {
                yield return (n, _cost[Index(n)], false);
            }
        }

        if (InBounds(c) && _links.TryGetValue(Index(c), out var links))
        {
            foreach (var link in links.Where(l => (l.StepFree || !stepFreeOnly) && IsWalkable(l.To)))
            {
                yield return (link.To, link.Cost, true);
            }
        }
    }

    /// <summary>Every tile that can be reached from <paramref name="start"/>.</summary>
    public HashSet<GridCoord> Reachable(GridCoord start, bool stepFreeOnly = false)
    {
        var seen = new HashSet<GridCoord> { start };
        var queue = new Queue<GridCoord>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var c))
        {
            foreach (var (n, _, _) in Neighbours(c, stepFreeOnly))
            {
                if (seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    public bool InBounds(GridCoord c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Length;

    public void SetCost(GridCoord c, byte cost) => _cost[Index(c)] = cost;

    public byte CostAt(GridCoord c) => InBounds(c) ? _cost[Index(c)] : (byte)0;

    public bool IsWalkable(GridCoord c) => CostAt(c) > 0;

    /// <summary>Blocks movement between two orthogonally adjacent tiles (in both directions).</summary>
    public void BlockEdge(GridCoord a, GridCoord b)
    {
        if (!InBounds(a) || !InBounds(b))
        {
            return;
        }

        _blockedEdges.Add(EdgeKey(a, b));
        _blockedEdges.Add(EdgeKey(b, a));
    }

    public void UnblockEdge(GridCoord a, GridCoord b)
    {
        _blockedEdges.Remove(EdgeKey(a, b));
        _blockedEdges.Remove(EdgeKey(b, a));
    }

    public bool CanStep(GridCoord from, GridCoord to) =>
        IsWalkable(to) && !_blockedEdges.Contains(EdgeKey(from, to));

    /// <summary>
    /// A* shortest path from <paramref name="start"/> to <paramref name="goal"/>, inclusive of both.
    /// Returns an empty list if the goal can't be reached. With <paramref name="stepFreeOnly"/> the path
    /// changes floor only by lift (for wheelchairs and trolleys).
    /// </summary>
    public IReadOnlyList<GridCoord> FindPath(GridCoord start, GridCoord goal, bool stepFreeOnly = false)
    {
        if (!InBounds(start) || !IsWalkable(goal))
        {
            return Array.Empty<GridCoord>();
        }

        if (start == goal)
        {
            return new NavPath { start };
        }

        var startIndex = Index(start);
        var goalIndex = Index(goal);
        var cameFrom = new Dictionary<int, int>();
        var hops = new Dictionary<int, int>();
        var gScore = new Dictionary<int, int> { [startIndex] = 0 };
        var open = new PriorityQueue<int, int>();
        open.Enqueue(startIndex, Heuristic(start, goal));
        var closed = new HashSet<int>();

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goalIndex)
            {
                return Reconstruct(cameFrom, hops, current);
            }

            if (!closed.Add(current))
            {
                continue;
            }

            var c = Coord(current);
            foreach (var (dx, dy) in Directions)
            {
                var n = new GridCoord(c.X + dx, c.Y + dy);
                if (InBounds(n) && CanStep(c, n))
                {
                    Visit(current, n, _cost[Index(n)], false);
                }
            }

            if (_links.Count > 0 && _links.TryGetValue(current, out var links))
            {
                foreach (var link in links)
                {
                    if ((link.StepFree || !stepFreeOnly) && IsWalkable(link.To))
                    {
                        Visit(current, link.To, link.Cost, true);
                    }
                }
            }
        }

        void Visit(int from, GridCoord n, int cost, bool isLink)
        {
            var ni = Index(n);
            if (closed.Contains(ni))
            {
                return;
            }

            var tentative = gScore[from] + cost;
            if (!gScore.TryGetValue(ni, out var existing) || tentative < existing)
            {
                gScore[ni] = tentative;
                cameFrom[ni] = from;
                if (isLink)
                {
                    hops[ni] = cost;
                }
                else
                {
                    hops.Remove(ni);
                }

                open.Enqueue(ni, tentative + Heuristic(n, goal));
            }
        }

        return Array.Empty<GridCoord>();
    }

    private NavPath Reconstruct(Dictionary<int, int> cameFrom, Dictionary<int, int> hops, int current)
    {
        var reversed = new List<(int Index, int Hop)> { (current, hops.GetValueOrDefault(current)) };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            reversed.Add((current, hops.GetValueOrDefault(current)));
        }

        reversed.Reverse();
        var path = new NavPath();
        for (var i = 0; i < reversed.Count; i++)
        {
            path.Add(Coord(reversed[i].Index));
            if (i > 0 && reversed[i].Hop > 0)
            {
                path.SetHop(i, reversed[i].Hop);
            }
        }

        return path;
    }

    /// <summary>Walking distance ignoring walls; across floors, the distance on the plan plus the cheapest possible ride.</summary>
    private int Heuristic(GridCoord a, GridCoord b)
    {
        if (FloorStride <= 0)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        var (fa, fb) = (a.Y / FloorStride, b.Y / FloorStride);
        var (ya, yb) = (a.Y - (fa * FloorStride), b.Y - (fb * FloorStride));
        return Math.Abs(a.X - b.X) + Math.Abs(ya - yb) + (Math.Abs(fa - fb) * MinLinkCostPerFloor);
    }

    private int Index(GridCoord c) => (c.Y * Width) + c.X;

    private GridCoord Coord(int index) => new(index % Width, index / Width);

    private long EdgeKey(GridCoord from, GridCoord to) => ((long)Index(from) * Width * Length) + Index(to);
}
