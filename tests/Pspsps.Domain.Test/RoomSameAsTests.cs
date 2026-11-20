using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomSameAsTests
{
    private static readonly RoomDefinition Garden = RoomCatalog.Find("garden")!;

    [Test]
    public void ARoomBuiltAgain_IsTheSameRoom() =>
        Assert.That(RoomBook.Create(RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of), []).Find("garden")!.SameAs(Garden), Is.True);

    [Test]
    public void ARoomWithAnotherPropOrLook_IsNot()
    {
        var lamp = new Prop(PropKinds.Lamp, new Position(0, 0), 0.35);

        Assert.Multiple(() =>
        {
            Assert.That(Garden.SameAs(Garden with { Props = [.. Garden.Props, lamp] }), Is.False);
            Assert.That(Garden.SameAs(Garden with { Look = "winter" }), Is.False);
            Assert.That(Garden.SameAs(Garden with { Width = Garden.Width + 1 }), Is.False);
        });
    }
}
