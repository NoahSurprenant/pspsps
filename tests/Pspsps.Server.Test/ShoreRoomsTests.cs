using System.Text.Json;
using Pspsps.Application.Rooms;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Test;

/// <summary>The shore rooms (the dock, the beach, the scratching peaks and the village below them): their props keep clear, their decoration stays off the walks, and the new designs are drawn inside their circles.</summary>
public sealed class ShoreRoomsTests
{
    private static readonly ContentFile Content = ContentFile.Embedded();

    private static readonly string[] ShoreRooms = ["dock", "beach", "scratching-peaks", "peakside-village"];

    private static IEnumerable<DraftPropDesign> Designs => Content.PropDesignList.Where(d => d.Kind.StartsWith("shore-", StringComparison.Ordinal));

    [TestCaseSource(nameof(ShoreRooms))]
    public void TheBackdropOf_KeepsClearOfTheWalksThePropsAndThePond(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var parts = Content.AllLooks.Single(look => look.Id == id).Look.Backdrop.Parts;

        Assert.That(BackdropClearance.Problems(id, layout, parts), Is.Empty);
    }

    [TestCaseSource(nameof(ShoreRooms))]
    public void ThePropsOf_KeepClearOfTheDoorsTheSpawnPointTheWalksAndEachOther(string id)
    {
        var room = Content.RoomList.Single(r => r.Id == id).Layout.ToDefinition(id);

        Assert.That(PropPlacement.Problems(room), Is.Empty);
    }

    [TestCaseSource(nameof(ShoreRooms))]
    public void TheShoreRoom_IsRichlyDecorated_WithItsOwnPaintedFloor(string id)
    {
        var layout = Content.RoomList.Single(room => room.Id == id).Layout;
        var look = Content.AllLooks.Single(l => l.Id == id).Look;

        Assert.Multiple(() =>
        {
            Assert.That(layout.Props.Count, Is.GreaterThanOrEqualTo(12));
            Assert.That(layout.Props.Count(p => p.Kind.StartsWith("shore-", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(8));
            Assert.That(look.Floor!.Painter, Does.StartWith("shore-"));
            Assert.That(look.Backdrop.Parts.Count, Is.GreaterThanOrEqualTo(30));
            Assert.That(look.OpenLandscape, Is.True);
        });
    }

    [Test]
    public void TheShoreDesigns_AreInTheFileAndInTheStartingOnesAlike_AndEveryKindIsOneTheServerPlaces()
    {
        var starting = PropDesignSeeder.Starting().ToDictionary(d => d.Kind);
        var text = (object value) => JsonSerializer.Serialize(value, ContentFile.Json);

        Assert.Multiple(() =>
        {
            Assert.That(Designs.Count(), Is.EqualTo(17));
            foreach (var design in Designs)
            {
                Assert.That(PropKinds.All, Does.Contain(design.Kind), design.Kind);
                Assert.That(starting.ContainsKey(design.Kind), Is.True, design.Kind);
                Assert.That(starting[design.Kind].Size, Is.EqualTo(design.Size), design.Kind);
                Assert.That(text(starting[design.Kind].Parts), Is.EqualTo(text(design.Parts)), design.Kind);
            }
        });
    }

    [Test]
    public void EveryShoreDesign_DrawsInsideItsSize()
    {
        Assert.Multiple(() =>
        {
            foreach (var design in Designs)
            {
                foreach (var (part, index) in design.Parts.Select((p, i) => (p, i)))
                {
                    Assert.That(ReachOf(part), Is.LessThanOrEqualTo(design.Size + 1e-6), $"{design.Kind}, part {index + 1}");
                }
            }
        });
    }

    [Test]
    public void ThePropsOfTheShoreRooms_AreAsBigAsTheirDesigns()
    {
        var sizes = Designs.ToDictionary(d => d.Kind, d => d.Size);
        var wrong = Content.RoomList.Where(room => ShoreRooms.Contains(room.Id))
            .SelectMany(room => room.Layout.Props.Where(p => sizes.TryGetValue(p.Kind, out var size) && Math.Abs(p.Radius - size) > 1e-6).Select(p => $"{room.Id}: {p.Kind}"));

        Assert.That(wrong, Is.Empty);
    }

    private static double ReachOf(PropPart part)
    {
        var tilted = part.Turn is { Length: > 0 } turn && (turn[0] != 0 || (turn.Length > 2 && turn[2] != 0));
        var (width, height, depth, diameter) = (part.Width ?? 0, part.Height ?? 0, part.Depth ?? 0, part.Diameter ?? 0);
        var reach = part.Shape switch
        {
            "box" => tilted ? Math.Sqrt((width * width) + (height * height) + (depth * depth)) / 2 : Math.Sqrt((width * width) + (depth * depth)) / 2,
            "cylinder" => tilted ? Math.Sqrt((diameter * diameter) + (height * height)) / 2 : diameter / 2,
            _ => diameter / 2,
        };
        var outline = part.Outlined == false ? 0 : 0.05;
        return Math.Sqrt((part.At[0] * part.At[0]) + (part.At[2] * part.At[2])) + reach + outline;
    }
}
