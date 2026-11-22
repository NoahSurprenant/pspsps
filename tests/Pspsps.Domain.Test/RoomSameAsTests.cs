using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomSameAsTests
{
    private static readonly RoomDefinition Shop = RoomCatalog.Find("shop")!;

    [Test]
    public void ARoomBuiltAgain_IsTheSameRoom() =>
        Assert.That(RoomBook.Create(RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of), []).Find("shop")!.SameAs(Shop), Is.True);

    [Test]
    public void ARoomWithAnotherPropOrLook_IsNot()
    {
        var lamp = new Prop(PropKinds.Lamp, new Position(0, 0), 0.35);

        Assert.Multiple(() =>
        {
            Assert.That(Shop.SameAs(Shop with { Props = [.. Shop.Props, lamp] }), Is.False);
            Assert.That(Shop.SameAs(Shop with { Look = "winter" }), Is.False);
            Assert.That(Shop.SameAs(Shop with { Width = Shop.Width + 1 }), Is.False);
        });
    }
}
