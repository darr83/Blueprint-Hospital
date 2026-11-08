namespace BlueprintHospital.Simulation.World;

/// <summary>
/// Walkable-tile graph for pathfinding. Tiles have a movement cost (0 = impassable) and
/// walls block the edge between two tiles, so rooms are only entered through their doors.
/// </summary>
public sealed class NavGrid
{
    private static readonly (int Dx, int Dy)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    private readonly byte[] _cost;
    private readonly HashSet<long> _blockedEdges = new();

    public NavGrid(int width, int length)
    {
        Width = width;
        Length = length;
        _cost = new byte[width * length];
    }

    public int Width { get; }

    public int Length { get; }

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
    /// Returns an empty list if the goal can't be reached.
    /// </summary>
    public IReadOnlyList<GridCoord> FindPath(GridCoord start, GridCoord goal)
    {
        if (!InBounds(start) || !IsWalkable(goal))
        {
            return Array.Empty<GridCoord>();
        }

        if (start == goal)
        {
            return new[] { start };
        }

        var startIndex = Index(start);
        var goalIndex = Index(goal);
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, int> { [startIndex] = 0 };
        var open = new PriorityQueue<int, int>();
        open.Enqueue(startIndex, Heuristic(start, goal));
        var closed = new HashSet<int>();

        while (open.TryDequeue(out var current, out _))
        {
            if (current == goalIndex)
            {
                return Reconstruct(cameFrom, current);
            }

            if (!closed.Add(current))
            {
                continue;
            }

            var c = Coord(current);
            foreach (var (dx, dy) in Directions)
            {
                var n = new GridCoord(c.X + dx, c.Y + dy);
                if (!InBounds(n) || !CanStep(c, n))
                {
                    continue;
                }

                var ni = Index(n);
                if (closed.Contains(ni))
                {
                    continue;
                }

                var tentative = gScore[current] + _cost[ni];
                if (!gScore.TryGetValue(ni, out var existing) || tentative < existing)
                {
                    gScore[ni] = tentative;
                    cameFrom[ni] = current;
                    open.Enqueue(ni, tentative + Heuristic(n, goal));
                }
            }
        }

        return Array.Empty<GridCoord>();
    }

    private List<GridCoord> Reconstruct(Dictionary<int, int> cameFrom, int current)
    {
        var path = new List<GridCoord> { Coord(current) };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            path.Add(Coord(current));
        }

        path.Reverse();
        return path;
    }

    private static int Heuristic(GridCoord a, GridCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private int Index(GridCoord c) => (c.Y * Width) + c.X;

    private GridCoord Coord(int index) => new(index % Width, index / Width);

    private long EdgeKey(GridCoord from, GridCoord to) => ((long)Index(from) * Width * Length) + Index(to);
}
