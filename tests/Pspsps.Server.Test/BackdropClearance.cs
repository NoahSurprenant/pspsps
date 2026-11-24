using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Test;

/// <summary>
/// The server's prop checks (<see cref="PropPlacement"/>) don't look at a room's backdrop parts, so this does for the standing
/// decoration inside a room's floor what they do for props: it must not stand where cats walk, on a prop, or in the pond, and
/// flat parts must not fight each other or lie under a rug or bed. Parts outside the floor are scenery and are left alone.
/// </summary>
internal static class BackdropClearance
{
    /// <summary>A part taller than this (its top, in metres) is something a cat bumps into; lower ones (paths, sand, mats) it walks over.</summary>
    private const double StandingAbove = 0.3;

    /// <summary>A part whose underside is higher than this (about a cat's height) is overhead: cats walk under it.</summary>
    private const double OverheadFrom = 1;

    /// <summary>How deep a standing part may stand into the floor from its edge (about the depth of furniture against a wall): further in it is on the walkable floor, where only props belong.</summary>
    private const double WallBand = 1;

    /// <summary>How far a standing part may reach into a prop's circle, as a share of the radius (capped at one metre of it): trim may touch a prop's rim, not its middle.</summary>
    private const double PropCoreShare = 0.5;

    /// <summary>How near a standing part may come to a straight walk between the spawn and the doors: half a cat's width.</summary>
    private const double WalkClearance = 0.3;

    /// <summary>The top of a floor covering (tatami, seams, sand, paths): rugs and beds are laid on these, so only flat parts proud of them are checked against flat props.</summary>
    private const double FloorCoveringTop = 0.08;

    /// <summary>Two flat parts whose tops are this close, over the same ground, are drawn on top of each other and flicker.</summary>
    private const double SameDepth = 0.005;

    /// <summary>How far past a pond's edge a part must keep (a little more than the water, for the bank).</summary>
    private const double PondBank = 0.1;

    /// <summary>The parts cats are likely to run into or flicker, in words, one problem per entry.</summary>
    public static IEnumerable<string> Problems(string id, RoomLayout layout, IReadOnlyList<PropPart> parts)
    {
        var room = layout.ToDefinition(id);
        var stops = new List<Position> { room.Spawn };
        stops.AddRange(room.Doors.SelectMany(door => new[] { door.Spot, door.Arrival }));
        var solids = parts.Select(Solid.Of).ToList();
        var problems = new List<string>();
        for (var i = 0; i < solids.Count; i++)
        {
            var solid = solids[i];
            var label = $"{id}: backdrop part {i + 1} ({parts[i].Shape} at {parts[i].At[0]}, {parts[i].At[2]})";
            if (!solid.Reaches(-layout.Width / 2, layout.Width / 2, -layout.Depth / 2, layout.Depth / 2))
            {
                continue;
            }

            if (solid.Top > StandingAbove && solid.Bottom < OverheadFrom)
            {
                if (solid.Reaches((-layout.Width / 2) + WallBand, (layout.Width / 2) - WallBand, (-layout.Depth / 2) + WallBand, (layout.Depth / 2) - WallBand))
                {
                    problems.Add($"{label} stands on the walkable floor, further than {WallBand} m from the walls");
                }

                problems.AddRange(layout.Props
                    .Where(prop => solid.DistanceTo(prop.Spot) < Math.Min(prop.Radius, 1) * PropCoreShare)
                    .Select(prop => $"{label} stands on the {prop.Kind} at ({prop.Spot.X}, {prop.Spot.Z})"));
                problems.AddRange(Walks(stops).Where(walk => solid.DistanceToWalk(walk.From, walk.To) < WalkClearance)
                    .Select(walk => $"{label} stands on the walk from ({walk.From.X}, {walk.From.Z}) to ({walk.To.X}, {walk.To.Z})"));
            }

            if (layout.Pond is { } pond && solid.Bottom < 0.5 && solid.DistanceTo(pond.Center) < pond.Radius + PondBank)
            {
                problems.Add($"{label} is in the pond");
            }

            if (solid.Top > StandingAbove)
            {
                continue;
            }

            if (solid.Top > FloorCoveringTop)
            {
                problems.AddRange(layout.Props
                    .Where(prop => prop.Kind is PropKinds.Rug or PropKinds.CatBed && solid.DistanceTo(prop.Spot) < prop.Radius)
                    .Select(prop => $"{label} lies under the {prop.Kind} at ({prop.Spot.X}, {prop.Spot.Z})"));
            }

            for (var j = i + 1; j < solids.Count; j++)
            {
                if (solids[j].Top <= StandingAbove && Math.Abs(solid.Top - solids[j].Top) < SameDepth && solid.Overlaps(solids[j]))
                {
                    problems.Add($"{label} and part {j + 1} lie flat at the same height over the same ground and would flicker");
                }
            }
        }

        return problems;
    }

    /// <summary>Where the browser draws the back wall, and about how high: the wall is a slab this thick, on the floor's far edge.</summary>
    private const double WallThickness = 0.3;

    private const double WallHeight = 4;

    /// <summary>
    /// The browser's own rule for a room's backdrop (looks.test.ts): a part that stands over the floor's rectangle is overhead (its underside above
    /// 2 m, higher than the cats' heads); lower than that it stands where cats walk. Flat decoration (paths, mats, sand: nothing above 0.3 m) is
    /// not a part cats bump into, and what lies outside the floor is scenery.
    /// </summary>
    public static IEnumerable<string> LowPartsOverTheFloor(string id, RoomLayout layout, IReadOnlyList<PropPart> parts) =>
        Describe(id, parts, solid => solid.Top > StandingAbove && solid.Bottom <= 2 && solid.OverTheBox(layout.Width / 2, layout.Depth / 2), "stands over the floor lower than 2 m");

    /// <summary>Parts that lie wholly inside the back wall's slab: drawn behind its face they are hidden, and where they meet it they flicker.</summary>
    public static IEnumerable<string> InsideTheBackWall(string id, RoomLayout layout, IReadOnlyList<PropPart> parts) =>
        Describe(id, parts, solid => solid.Top > StandingAbove && solid.Bottom < WallHeight && solid.OverTheBox(layout.Width / 2, layout.Depth / 2 + WallThickness)
            && solid.Z0 >= (layout.Depth / 2) - 0.1 && solid.Z1 <= (layout.Depth / 2) + WallThickness + 0.02, "lies inside the back wall");

    /// <summary>Standing parts (nothing above a cat's height under them, more than flat decoration) that reach into the middle of a prop.</summary>
    public static IEnumerable<string> InsideProps(string id, RoomLayout layout, IReadOnlyList<PropPart> parts) =>
        parts.Select(Solid.Of).Select((solid, i) => (solid, i)).Where(p => p.solid.Top > StandingAbove && p.solid.Bottom < OverheadFrom)
            .SelectMany(p => layout.Props.Where(prop => p.solid.DistanceToBox(prop.Spot) < Math.Min(prop.Radius, 1) * PropCoreShare)
                .Select(prop => $"{id}: backdrop part {p.i + 1} ({parts[p.i].Shape} at {parts[p.i].At[0]}, {parts[p.i].At[2]}) sits inside the {prop.Kind} at ({prop.Spot.X}, {prop.Spot.Z})"));

    private static IEnumerable<string> Describe(string id, IReadOnlyList<PropPart> parts, Func<Solid, bool> test, string what) =>
        parts.Select(Solid.Of).Select((solid, i) => (solid, i)).Where(p => test(p.solid))
            .Select(p => $"{id}: backdrop part {p.i + 1} ({parts[p.i].Shape} at {parts[p.i].At[0]}, {parts[p.i].At[1]}, {parts[p.i].At[2]}) {what}");

    private static IEnumerable<(Position From, Position To)> Walks(IReadOnlyList<Position> stops) =>
        stops.SelectMany((start, a) => stops.Skip(a + 1).Select(end => (From: start, To: end)));

    /// <summary>A part's bounding box in the room (after turning it), or its round footprint when it stands upright and is round.</summary>
    private sealed record Solid(double X0, double X1, double Y0, double Y1, double Z0, double Z1, bool Round, Position Middle, double Radius)
    {
        public double Top => Y1;

        public double Bottom => Y0;

        public static Solid Of(PropPart part)
        {
            var (x, y, z) = (part.At[0], part.At[1], part.At[2]);
            // A cylinder that widens upwards (or a cone the other way) takes up its wider end's footprint.
            var across = Math.Max(part.Diameter.GetValueOrDefault(), part.Top.GetValueOrDefault());
            var width = part.Shape == "box" ? part.Width.GetValueOrDefault() : across;
            var depth = part.Shape == "box" ? part.Depth.GetValueOrDefault() : across;
            var height = part.Height ?? part.Diameter ?? 0;
            var half = new[] { width / 2, height / 2, depth / 2 };
            var turn = part.Turn is { Length: 3 } ? part.Turn : [0, 0, 0];

            // The browser turns a part about Y, then X, then Z (yaw, pitch, roll); the box that holds it has half-sizes of |R| * half.
            var rotation = Multiply(Multiply(AboutY(turn[1]), AboutX(turn[0])), AboutZ(turn[2]));
            var reach = Enumerable.Range(0, 3).Select(row => Enumerable.Range(0, 3).Sum(col => Math.Abs(rotation[row][col]) * half[col])).ToArray();
            // Turning about Y leaves a round part round; leaning it (about X or Z) does not.
            var round = part.Shape != "box" && turn[0] == 0 && turn[2] == 0;
            return new Solid(x - reach[0], x + reach[0], y - reach[1], y + reach[1], z - reach[2], z + reach[2], round, new Position(x, z), width / 2);
        }

        /// <summary>Whether any of it stands over the rectangle (x0..x1, z0..z1) of the floor; a round part is measured by its circle, not its box.</summary>
        public bool Reaches(double x0, double x1, double z0, double z1) => Round
            ? Math.Sqrt(Math.Pow(Math.Max(Math.Max(x0 - Middle.X, Middle.X - x1), 0), 2) + Math.Pow(Math.Max(Math.Max(z0 - Middle.Z, Middle.Z - z1), 0), 2)) < Radius
            : X1 > x0 && X0 < x1 && Z1 > z0 && Z0 < z1;

        /// <summary>Whether its bounding box stands over the rectangle of half-sizes (<paramref name="halfX"/>, <paramref name="halfZ"/>) round the room's middle (the browser's test, which does not round the corners).</summary>
        public bool OverTheBox(double halfX, double halfZ) => X1 > -halfX && X0 < halfX && Z1 > -halfZ && Z0 < halfZ;

        public double DistanceToBox(Position spot) =>
            Math.Sqrt(Math.Pow(Math.Max(Math.Max(X0 - spot.X, spot.X - X1), 0), 2) + Math.Pow(Math.Max(Math.Max(Z0 - spot.Z, spot.Z - Z1), 0), 2));

        public double DistanceTo(Position spot) => Round
            ? Math.Max(0, spot.DistanceTo(Middle) - Radius)
            : Math.Sqrt(Math.Pow(Math.Max(Math.Max(X0 - spot.X, spot.X - X1), 0), 2) + Math.Pow(Math.Max(Math.Max(Z0 - spot.Z, spot.Z - Z1), 0), 2));

        /// <summary>How near it comes to the straight walk from <paramref name="from"/> to <paramref name="to"/>, found by stepping along the walk every 5 cm.</summary>
        public double DistanceToWalk(Position from, Position to)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / 0.05));
            return Enumerable.Range(0, steps + 1).Min(step =>
            {
                var along = (double)step / steps;
                return DistanceTo(new Position(from.X + ((to.X - from.X) * along), from.Z + ((to.Z - from.Z) * along)));
            });
        }

        public bool Overlaps(Solid other) =>
            Math.Min(X1, other.X1) - Math.Max(X0, other.X0) > 0.01 && Math.Min(Z1, other.Z1) - Math.Max(Z0, other.Z0) > 0.01;

        private static double[][] AboutX(double a) => [[1, 0, 0], [0, Math.Cos(a), -Math.Sin(a)], [0, Math.Sin(a), Math.Cos(a)]];

        private static double[][] AboutY(double a) => [[Math.Cos(a), 0, Math.Sin(a)], [0, 1, 0], [-Math.Sin(a), 0, Math.Cos(a)]];

        private static double[][] AboutZ(double a) => [[Math.Cos(a), -Math.Sin(a), 0], [Math.Sin(a), Math.Cos(a), 0], [0, 0, 1]];

        private static double[][] Multiply(double[][] a, double[][] b) =>
            [.. a.Select(row => Enumerable.Range(0, 3).Select(col => Enumerable.Range(0, 3).Sum(k => row[k] * b[k][col])).ToArray())];
    }
}
