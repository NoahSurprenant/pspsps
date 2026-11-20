using System.Text.RegularExpressions;

namespace Pspsps.Domain.Rooms;

/// <summary>A painter the browser runs by name on a floor or a back wall, with the colours it paints in (the painter's own where left out).</summary>
public sealed record LookPaint(string Painter, string? Colour = null, string? Accent = null);

/// <summary>The ground round a room (a town's street): its colour, and a painter that repeats over it (by name) if it has one.</summary>
public sealed record LookGround(string Colour, string? Painter = null);

/// <summary>What stands behind a room's back wall and round it: parts in the room's own space (metres, standing on the floor at the origin), and optionally a named generator the browser runs besides.</summary>
public sealed record LookBackdrop(IReadOnlyList<PropPart> Parts, string? Generator = null);

/// <summary>
/// How a room is painted and what is round it, as data the browser draws from. The painters, generators and wildlife are
/// named here and run by the browser (<see cref="Floors"/> and the rest list the names it knows); a new room picks among
/// them and gives its own colours and backdrop parts, so it needs no new client.
/// </summary>
public sealed partial record RoomLook(
    string Sky,
    string FloorColour,
    string WallColour,
    LookBackdrop Backdrop,
    LookGround? Ground = null,
    LookPaint? Floor = null,
    LookPaint? Wall = null,
    bool? OpenLandscape = null,
    string? Wildlife = null)
{
    public static readonly IReadOnlyList<string> Floors =
        ["town-square", "cafe", "shop", "garden", "pear-orchard", "riverside", "home", "planks", "tiles", "checker"];

    public static readonly IReadOnlyList<string> Walls = ["town-square", "cafe", "shop", "garden", "riverside", "home", "panelled", "bricks"];

    public static readonly IReadOnlyList<string> Grounds = ["street"];

    public static readonly IReadOnlyList<string> Generators = ["town"];

    public static readonly IReadOnlyList<string> WildlifeKinds = ["fish", "butterfly"];

    /// <summary>What is wrong with a look, one line each; empty when the browser can draw it.</summary>
    public IReadOnlyList<string> Problems(string id)
    {
        var problems = new List<string>();
        void Colour(string what, string? value)
        {
            if (!HexColour().IsMatch(value ?? string.Empty))
            {
                problems.Add($"{id}: its {what} is a hex colour such as #c98f5a.");
            }
        }

        Colour("sky", Sky);
        Colour("floor colour", FloorColour);
        Colour("wall colour", WallColour);
        if (Ground is not null)
        {
            Colour("ground colour", Ground.Colour);
            if (Ground.Painter is not null && !Grounds.Contains(Ground.Painter))
            {
                problems.Add($"{id}: '{Ground.Painter}' is not a ground painter ({string.Join(", ", Grounds)}).");
            }
        }

        foreach (var (what, paint, known) in new[] { ("floor", Floor, Floors), ("wall", Wall, Walls) })
        {
            if (paint is null)
            {
                continue;
            }

            if (!known.Contains(paint.Painter))
            {
                problems.Add($"{id}: '{paint.Painter}' is not a {what} painter ({string.Join(", ", known)}).");
            }

            if (paint.Colour is not null && !HexColour().IsMatch(paint.Colour) || paint.Accent is not null && !HexColour().IsMatch(paint.Accent))
            {
                problems.Add($"{id}: the {what} painter's colours are hex colours such as #c98f5a.");
            }
        }

        if (Backdrop is null)
        {
            problems.Add($"{id}: has no backdrop (it may be empty).");
        }
        else
        {
            if (Backdrop.Generator is not null && !Generators.Contains(Backdrop.Generator))
            {
                problems.Add($"{id}: '{Backdrop.Generator}' is not a backdrop generator ({string.Join(", ", Generators)}).");
            }

            for (var i = 0; i < (Backdrop.Parts?.Count ?? 0); i++)
            {
                problems.AddRange(PropDesign.PartProblems(Backdrop.Parts![i]).Select(problem => $"{id}, backdrop part {i + 1}: {problem}"));
            }
        }

        if (Wildlife is not null && !WildlifeKinds.Contains(Wildlife))
        {
            problems.Add($"{id}: '{Wildlife}' is not wildlife ({string.Join(", ", WildlifeKinds)}).");
        }

        return problems;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}

/// <summary>What an overlay's named look changes of any room: its sky and back wall's colours.</summary>
public sealed record LookVariant(string Sky, string WallColour)
{
    public IReadOnlyList<string> Problems(string name)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            problems.Add($"{name}: a look's name is lower-case letters, digits and hyphens.");
        }

        if (!Regex.IsMatch(Sky ?? string.Empty, "^#[0-9a-fA-F]{6}$", RegexOptions.None, TimeSpan.FromSeconds(1))
            || !Regex.IsMatch(WallColour ?? string.Empty, "^#[0-9a-fA-F]{6}$", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            problems.Add($"{name}: its sky and wall colours are hex colours such as #c98f5a.");
        }

        return problems;
    }
}

/// <summary>A room's <see cref="RoomLook"/> as it is kept (by the room's ID; "home" is every player's home).</summary>
public sealed class RoomLookEntry
{
    private RoomLookEntry()
    {
        Id = string.Empty;
        Look = null!;
    }

    public string Id { get; private set; }

    public RoomLook Look { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static RoomLookEntry Of(string id, RoomLook look, DateTimeOffset now)
    {
        var entry = new RoomLookEntry { Id = id };
        entry.Replace(look, now);
        return entry;
    }

    public void Replace(RoomLook look, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(look);
        var problems = look.Problems(Id);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Look = look;
        UpdatedAt = now;
    }
}

/// <summary>A named <see cref="LookVariant"/> as it is kept.</summary>
public sealed class LookVariantEntry
{
    private LookVariantEntry()
    {
        Id = string.Empty;
        Sky = string.Empty;
        WallColour = string.Empty;
    }

    public string Id { get; private set; }

    public string Sky { get; private set; }

    public string WallColour { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LookVariantEntry Of(string name, LookVariant variant, DateTimeOffset now)
    {
        var entry = new LookVariantEntry { Id = name };
        entry.Replace(variant, now);
        return entry;
    }

    public void Replace(LookVariant variant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var problems = variant.Problems(Id);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems));
        }

        Sky = variant.Sky;
        WallColour = variant.WallColour;
        UpdatedAt = now;
    }

    public LookVariant ToVariant() => new(Sky, WallColour);
}
