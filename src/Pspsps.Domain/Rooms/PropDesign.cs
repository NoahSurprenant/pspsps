using System.Text.RegularExpressions;

namespace Pspsps.Domain.Rooms;

/// <summary>
/// One part of a prop as the browser draws it, in metres, in the prop's own space (standing on the floor at the origin, its
/// front towards -Z). A box has a Width, Height and Depth; a cylinder a Diameter, Height and optionally a Top diameter and
/// Sides; a ball a Diameter and optionally a Height. At is its centre; Turn is radians about each axis.
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
    bool? Outlined = null);

/// <summary>What a kind of prop looks like: the parts it is drawn from, at the size it is designed for (<see cref="Size"/>: the radius it fits in, outline included).</summary>
public sealed partial class PropDesign
{
    public static readonly IReadOnlyList<string> Shapes = ["box", "cylinder", "ball"];

    private PropDesign()
    {
        Kind = string.Empty;
        Parts = [];
    }

    public string Kind { get; private set; }

    public double Size { get; private set; }

    public IReadOnlyList<PropPart> Parts { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PropDesign Of(string kind, double size, IReadOnlyList<PropPart> parts, DateTimeOffset now)
    {
        var problems = Problems(kind, size, parts);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        return new PropDesign { Kind = kind, Size = size, Parts = parts, UpdatedAt = now };
    }

    public void Replace(double size, IReadOnlyList<PropPart> parts, DateTimeOffset now)
    {
        var problems = Problems(Kind, size, parts);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Size = size;
        Parts = parts;
        UpdatedAt = now;
    }

    /// <summary>What is wrong with a design, one line each; empty when it can be drawn.</summary>
    public static IReadOnlyList<string> Problems(string kind, double size, IReadOnlyList<PropPart> parts)
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

        return problems;
    }

    private static IEnumerable<string> PartProblems(PropPart part)
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
            _ => new[] { part.Diameter },
        };
        if (wanted.Any(v => v is not > 0 || !double.IsFinite(v.Value)))
        {
            yield return $"a {part.Shape} needs its sizes above nothing.";
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
