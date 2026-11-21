using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

public sealed record DraftRoom(string Id, RoomLayout Layout);

public sealed record DraftLook(string Id, RoomLook Look);

public sealed record DraftVariant(string Id, string Sky, string WallColour);

public sealed record DraftLooks(IReadOnlyList<DraftLook>? Rooms = null, IReadOnlyList<DraftVariant>? Variants = null);

public sealed record DraftPainter(string Id, string Surface, IReadOnlyList<PaintStep> Steps);

public sealed record DraftWildlife(string Id, IReadOnlyList<PropPart> Parts, WildlifeMotion Motion);

public sealed record DraftPropDesign(string Kind, double Size, IReadOnlyList<PropPart> Parts, IReadOnlyList<PropGrowth>? Growth = null);

/// <summary>
/// Content waiting for review (ContentDrafts.json, in the shape of the admin export): a pull request changes the file, its
/// checks run over it, and an admin loads it as drafts, which only go live when published.
/// </summary>
public sealed record ContentFile(
    IReadOnlyList<DraftRoom>? Rooms = null,
    DraftLooks? Looks = null,
    IReadOnlyList<DraftPainter>? Painters = null,
    IReadOnlyList<DraftWildlife>? Wildlife = null,
    IReadOnlyList<RoomOverlay>? Overlays = null,
    IReadOnlyList<DraftPropDesign>? PropDesigns = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ContentFile Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ContentDrafts.json")
            ?? throw new InvalidOperationException("ContentDrafts.json is not embedded.");
        return JsonSerializer.Deserialize<ContentFile>(stream, Json) ?? new ContentFile();
    }

    public IReadOnlyList<DraftRoom> RoomList => Rooms ?? [];

    public IReadOnlyList<DraftLook> LookList => Looks?.Rooms ?? [];

    public IReadOnlyList<DraftVariant> VariantList => Looks?.Variants ?? [];

    public IReadOnlyList<DraftPainter> PainterList => Painters ?? [];

    public IReadOnlyList<DraftWildlife> WildlifeList => Wildlife ?? [];

    public IReadOnlyList<RoomOverlay> OverlayList => Overlays ?? [];

    public IReadOnlyList<DraftPropDesign> PropDesignList => PropDesigns ?? [];
}

/// <summary>What loading the content file did: what was saved, what was already so, and what could not be.</summary>
public sealed record ImportReport(IReadOnlyList<string> Applied, IReadOnlyList<string> Unchanged, IReadOnlyList<string> Problems);
