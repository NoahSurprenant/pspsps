using System.Text.RegularExpressions;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// One part of a prop as the browser draws it, in metres, in the prop's own space (standing on the floor at the origin, its
/// front towards -Z). A box has a Width, Height and Depth; a cylinder a Diameter, Height and optionally a Top diameter and
/// Sides; a ball a Diameter and optionally a Height; a model is one of the client's built-in meshes (Model, by name), drawn Scale times its own size. At is its centre; Turn is radians about each axis.
/// </summary>
public sealed record PropPart(
    string Shape,
    double[] At,
    string Colour,
    double? Width = null,
    double? Height = null,
    double? Depth = null,
    double? Diameter = null,
    double? Top = null,
    int? Sides = null,
    double[]? Turn = null,
    bool? Outlined = null,
    string? Model = null,
    double? Scale = null);

/// <summary>Parts drawn only at some stages of a prop that grows (fruit on a tree, pears in a crate): from stage <see cref="From"/> (default 0) and below stage <see cref="Until"/> (default no end).</summary>
public sealed record PropGrowth(IReadOnlyList<PropPart> Parts, int? From = null, int? Until = null);

/// <summary>What a kind of prop looks like: the parts it is drawn from, at the size it is designed for (<see cref="Size"/>: the radius it fits in, outline included).</summary>
public sealed partial class PropDesign
{
    public static readonly IReadOnlyList<string> Shapes = ["box", "cylinder", "ball", "model"];

    private PropDesign()
    {
        Kind = string.Empty;
        Parts = [];
    }

    public string Kind { get; private set; }

    public double Size { get; private set; }

    public IReadOnlyList<PropPart> Parts { get; private set; }

    public IReadOnlyList<PropGrowth> Growth { get; private set; } = [];

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PropDesign Of(string kind, double size, IReadOnlyList<PropPart> parts, DateTimeOffset now, IReadOnlyList<PropGrowth>? growth = null)
    {
        var problems = Problems(kind, size, parts, growth);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        return new PropDesign { Kind = kind, Size = size, Parts = parts, Growth = growth ?? [], UpdatedAt = now };
    }

    public void Replace(double size, IReadOnlyList<PropPart> parts, DateTimeOffset now, IReadOnlyList<PropGrowth>? growth = null)
    {
        var problems = Problems(Kind, size, parts, growth);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Size = size;
        Parts = parts;
        Growth = growth ?? [];
        UpdatedAt = now;
    }

    /// <summary>What is wrong with a design, one line each; empty when it can be drawn.</summary>
    public static IReadOnlyList<string> Problems(string kind, double size, IReadOnlyList<PropPart> parts, IReadOnlyList<PropGrowth>? growth = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(kind) || !KindName().IsMatch(kind))
        {
            problems.Add($"{kind}: a prop's kind is lower-case letters, digits and hyphens.");
        }

        if (!double.IsFinite(size) || size <= 0)
        {
            problems.Add($"{kind}: its size is not above nothing.");
        }

        if (parts.Count == 0)
        {
            problems.Add($"{kind}: has no parts.");
        }

        for (var i = 0; i < parts.Count; i++)
        {
            foreach (var problem in PartProblems(parts[i]))
            {
                problems.Add($"{kind}, part {i + 1}: {problem}");
            }
        }

        for (var g = 0; g < (growth?.Count ?? 0); g++)
        {
            var step = growth![g];
            if (step.Parts.Count == 0 || step.From is < 0 || step.Until is < 0 || (step.From is { } from && step.Until is { } until && until <= from))
            {
                problems.Add($"{kind}, growth {g + 1}: needs parts, and stages from 0 with an end after its start.");
            }

            for (var i = 0; i < step.Parts.Count; i++)
            {
                foreach (var problem in PartProblems(step.Parts[i]))
                {
                    problems.Add($"{kind}, growth {g + 1}, part {i + 1}: {problem}");
                }
            }
        }

        return problems;
    }

    internal static IEnumerable<string> PartProblems(PropPart part)
    {
        if (!Shapes.Contains(part.Shape))
        {
            yield return $"'{part.Shape}' is not a shape ({string.Join(", ", Shapes)}).";
            yield break;
        }

        if (part.At is not { Length: 3 } || part.At.Any(v => !double.IsFinite(v)))
        {
            yield return "its place needs three numbers.";
        }

        if (part.Turn is not null && (part.Turn.Length != 3 || part.Turn.Any(v => !double.IsFinite(v))))
        {
            yield return "its turn needs three numbers.";
        }

        if (!Colour().IsMatch(part.Colour ?? string.Empty))
        {
            yield return "its colour is a hex colour such as #c98f5a.";
        }

        var wanted = part.Shape switch
        {
            "box" => new[] { part.Width, part.Height, part.Depth },
            "cylinder" => [part.Diameter, part.Height],
            "model" => [part.Scale],
            _ => new[] { part.Diameter },
        };
        if (wanted.Any(v => v is not > 0 || !double.IsFinite(v.Value)))
        {
            yield return $"a {part.Shape} needs its sizes above nothing.";
        }

        if (part.Shape == "model" && !KindName().IsMatch(part.Model ?? string.Empty))
        {
            yield return "a model needs the name of one of the client's meshes.";
        }

        if (part.Top is < 0 || part.Height is <= 0 || part.Sides is < 3 or > 64)
        {
            yield return "its top, height or sides are out of range.";
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KindName();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Colour();
}
