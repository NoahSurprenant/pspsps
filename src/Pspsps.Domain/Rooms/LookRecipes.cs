using System.Text.RegularExpressions;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// A step drawn again and again over a surface: every <see cref="Dx"/> metres along (0 for once) and every <see cref="Dy"/> metres
/// down (0 for once); each row <see cref="Shift"/> metres further along than the one before, over <see cref="Cycle"/> rows (2 where left out).
/// </summary>
public sealed record PaintRepeat(double Dx, double Dy, double? Shift = null, int? Cycle = null);

/// <summary>
/// One drawing step of a painter recipe, in metres from the surface's top left. Ops: "fill" (the whole surface in a colour), "rect"
/// (a box at X, Y, Width by Height: filled, or only its outline when Outline; optionally repeated) and "checker" (squares of Cell
/// metres alternating Colour and Accent). A colour is "main", "accent" or a hex colour, made lighter (Shade above 0) or darker (below 0).
/// </summary>
public sealed record PaintStep(
    string Op,
    string? Colour = null,
    double? Shade = null,
    string? Accent = null,
    double? X = null,
    double? Y = null,
    double? Width = null,
    double? Height = null,
    double? Cell = null,
    double? Line = null,
    bool? Outline = null,
    PaintRepeat? Repeat = null);

/// <summary>What a painter recipe paints: a floor or a back wall.</summary>
public static class PaintSurfaces
{
    public const string Floor = "floor";
    public const string Wall = "wall";

    public static readonly IReadOnlyList<string> All = [Floor, Wall];
}

/// <summary>How a wildlife recipe's creature moves: "orbit" round the room's pond, "shuttle" between its first two props of kind <see cref="Prop"/>, or "wander" in a circle <see cref="Reach"/> metres wide about the middle of the floor.</summary>
public sealed record WildlifeMotion(string Kind, double Speed, double Height, double? Reach = null, string? Prop = null, double? Bob = null, double? Sway = null);

/// <summary>The names the browser knows how to draw a look with: its own painters and wildlife, and the admins' recipes.</summary>
public sealed record LookNames(IReadOnlyList<string> Floors, IReadOnlyList<string> Walls, IReadOnlyList<string> Wildlife)
{
    public static readonly LookNames BuiltIn = new(RoomLook.Floors, RoomLook.Walls, RoomLook.WildlifeKinds);

    public LookNames With(IEnumerable<(string Surface, string Name)> painters, IEnumerable<string> wildlife)
    {
        var all = painters.ToList();
        return new LookNames(
            [.. Floors, .. all.Where(p => p.Surface == PaintSurfaces.Floor).Select(p => p.Name)],
            [.. Walls, .. all.Where(p => p.Surface == PaintSurfaces.Wall).Select(p => p.Name)],
            [.. Wildlife, .. wildlife]);
    }
}

public static partial class LookRecipes
{
    public const int MostSteps = 64;

    /// <summary>The smallest repeat or cell, so a recipe cannot ask for more drawing than a browser can do.</summary>
    public const double SmallestStep = 0.1;

    public static readonly IReadOnlyList<string> Ops = ["fill", "rect", "checker"];

    public static readonly IReadOnlyList<string> Motions = ["orbit", "shuttle", "wander"];

    /// <summary>What is wrong with a painter recipe, one line each; empty when the browser can draw it.</summary>
    public static IReadOnlyList<string> PainterProblems(string name, string surface, IReadOnlyList<PaintStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var problems = new List<string>();
        problems.AddRange(NameProblems(name, PaintSurfaces.Floor == surface ? RoomLook.Floors : RoomLook.Walls, "painter"));
        if (!PaintSurfaces.All.Contains(surface))
        {
            problems.Add($"{name}: paints a {string.Join(" or ", PaintSurfaces.All)}.");
        }

        if (steps.Count is 0 or > MostSteps)
        {
            problems.Add($"{name}: has 1 to {MostSteps} steps.");
        }

        for (var i = 0; i < steps.Count; i++)
        {
            problems.AddRange(StepProblems(steps[i]).Select(problem => $"{name}, step {i + 1}: {problem}"));
        }

        return problems;
    }

    /// <summary>What is wrong with a wildlife recipe, one line each; empty when the browser can draw it.</summary>
    public static IReadOnlyList<string> WildlifeProblems(string name, IReadOnlyList<PropPart> parts, WildlifeMotion motion)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var problems = new List<string>();
        problems.AddRange(NameProblems(name, RoomLook.WildlifeKinds, "creature"));
        if (parts.Count is 0 or > MostSteps)
        {
            problems.Add($"{name}: has 1 to {MostSteps} parts.");
        }

        for (var i = 0; i < parts.Count; i++)
        {
            problems.AddRange(PropDesign.PartProblems(parts[i]).Select(problem => $"{name}, part {i + 1}: {problem}"));
        }

        if (motion is null)
        {
            problems.Add($"{name}: has no motion.");
            return problems;
        }

        if (!Motions.Contains(motion.Kind))
        {
            problems.Add($"{name}: '{motion.Kind}' is not a motion ({string.Join(", ", Motions)}).");
        }

        if (!double.IsFinite(motion.Speed) || motion.Speed is <= 0 or > 10)
        {
            problems.Add($"{name}: its speed is above 0 and at most 10.");
        }

        if (!double.IsFinite(motion.Height) || motion.Height is < 0 or > 5)
        {
            problems.Add($"{name}: its height is 0 to 5 metres.");
        }

        foreach (var (what, value) in new[] { ("reach", motion.Reach), ("bob", motion.Bob), ("sway", motion.Sway) })
        {
            if (value is { } number && (!double.IsFinite(number) || number is < 0 or > 50))
            {
                problems.Add($"{name}: its {what} is 0 to 50.");
            }
        }

        if (motion.Kind == "shuttle" && (motion.Prop is null || !PropKinds.All.Contains(motion.Prop)))
        {
            problems.Add($"{name}: a shuttle goes between two of a kind of prop ({string.Join(", ", PropKinds.All)}).");
        }

        return problems;
    }

    private static IEnumerable<string> NameProblems(string name, IReadOnlyList<string> builtIn, string what)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32 || !Slug().IsMatch(name))
        {
            yield return $"{name}: a {what}'s name is lower-case letters, digits and hyphens, up to 32.";
        }
        else if (builtIn.Contains(name))
        {
            yield return $"{name}: is a {what} the browser already has.";
        }
    }

    private static IEnumerable<string> StepProblems(PaintStep step)
    {
        if (!Ops.Contains(step.Op))
        {
            yield return $"'{step.Op}' is not a step ({string.Join(", ", Ops)}).";
            yield break;
        }

        if (!ColourRef().IsMatch(step.Colour ?? string.Empty))
        {
            yield return "its colour is main, accent or a hex colour such as #c98f5a.";
        }

        if (step.Accent is not null && !ColourRef().IsMatch(step.Accent))
        {
            yield return "its accent is main, accent or a hex colour such as #c98f5a.";
        }

        foreach (var (what, value) in new[] { ("shade", step.Shade) })
        {
            if (value is { } shade && (!double.IsFinite(shade) || shade is < -1 or > 1))
            {
                yield return $"its {what} is between -1 and 1.";
            }
        }

        if (step.Op == "rect")
        {
            foreach (var (what, value) in new[] { ("x", step.X), ("y", step.Y) })
            {
                if (value is not { } number || !double.IsFinite(number) || Math.Abs(number) > 1000)
                {
                    yield return $"its {what} is a number of metres.";
                }
            }

            foreach (var (what, value) in new[] { ("width", step.Width), ("height", step.Height) })
            {
                if (value is not { } number || !double.IsFinite(number) || number is <= 0 or > 1000)
                {
                    yield return $"its {what} is above 0 metres.";
                }
            }

            if (step.Line is { } line && (!double.IsFinite(line) || line is <= 0 or > 5))
            {
                yield return "its line is above 0 and at most 5 metres.";
            }

            if (step.Repeat is { } repeat)
            {
                foreach (var (what, value) in new[] { ("across", repeat.Dx), ("down", repeat.Dy) })
                {
                    if (!double.IsFinite(value) || (value != 0 && value < SmallestStep) || value > 1000)
                    {
                        yield return $"its repeat {what} is 0 (once) or at least {SmallestStep} metres.";
                    }
                }

                if (repeat.Shift is { } shift && (!double.IsFinite(shift) || Math.Abs(shift) > 1000))
                {
                    yield return "its repeat shift is a number of metres.";
                }

                if (repeat.Cycle is < 1 or > 100)
                {
                    yield return "its repeat cycle is 1 to 100 rows.";
                }
            }
        }
        else if (step.Op == "checker" && (step.Cell is not { } cell || !double.IsFinite(cell) || cell is < SmallestStep or > 1000))
        {
            yield return $"its cell is at least {SmallestStep} metres.";
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();

    [GeneratedRegex("^(main|accent|#[0-9a-fA-F]{6})$")]
    private static partial Regex ColourRef();
}

/// <summary>A painter recipe as it is kept: a floor or back wall drawn from steps, by name, with no client code.</summary>
public sealed class PainterRecipeEntry
{
    private PainterRecipeEntry()
    {
        Id = string.Empty;
        Surface = string.Empty;
        Steps = [];
    }

    public string Id { get; private set; }

    public string Surface { get; private set; }

    public IReadOnlyList<PaintStep> Steps { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PainterRecipeEntry Of(string name, string surface, IReadOnlyList<PaintStep> steps, DateTimeOffset now)
    {
        var entry = new PainterRecipeEntry { Id = name };
        entry.Replace(surface, steps, now);
        return entry;
    }

    public void Replace(string surface, IReadOnlyList<PaintStep> steps, DateTimeOffset now)
    {
        var problems = LookRecipes.PainterProblems(Id, surface, steps);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Surface = surface;
        Steps = steps;
        UpdatedAt = now;
    }
}

/// <summary>A wildlife recipe as it is kept: a creature drawn from parts that moves in one of a few ways, by name, with no client code.</summary>
public sealed class WildlifeRecipeEntry
{
    private WildlifeRecipeEntry()
    {
        Id = string.Empty;
        Parts = [];
        Motion = null!;
    }

    public string Id { get; private set; }

    public IReadOnlyList<PropPart> Parts { get; private set; }

    public WildlifeMotion Motion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static WildlifeRecipeEntry Of(string name, IReadOnlyList<PropPart> parts, WildlifeMotion motion, DateTimeOffset now)
    {
        var entry = new WildlifeRecipeEntry { Id = name };
        entry.Replace(parts, motion, now);
        return entry;
    }

    public void Replace(IReadOnlyList<PropPart> parts, WildlifeMotion motion, DateTimeOffset now)
    {
        var problems = LookRecipes.WildlifeProblems(Id, parts, motion);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Parts = parts;
        Motion = motion;
        UpdatedAt = now;
    }
}
