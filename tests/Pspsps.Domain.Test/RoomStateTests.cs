using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly RoomDefinition Room = new("test", "Test Room", Width: 10, Depth: 6, Spawn: new(1, 2), Capacity: 2);

    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();

    [Test]
    public void Join_PutsTheCatAtTheSpawnPoint()
    {
        var state = new RoomState(Room);

        var cat = state.Join(Tom, "Tom", FurColor.Grey);

        Assert.That(cat.Position, Is.EqualTo(new Position(1, 2)));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void Join_Twice_KeepsTheCatsSpot()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);
        state.Move(Tom, new Position(3, 0));

        var again = state.Join(Tom, "Tom", FurColor.Grey);

        Assert.That(again.Position, Is.EqualTo(new Position(3, 0)));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void Join_RefusesAFullRoom()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);
        state.Join(Felix, "Felix", FurColor.Tuxedo);

        Assert.Throws<DomainException>(() => state.Join(Guid.NewGuid(), "Garfield", FurColor.Orange));
    }

    [Test]
    public void Move_StopsAtTheWalls()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        var position = state.Move(Tom, new Position(100, -100));

        Assert.That(position, Is.EqualTo(new Position(5, -3)));
        Assert.That(state.Cats.Single().Position, Is.EqualTo(position));
    }

    [TestCase(double.NaN, 0)]
    [TestCase(0, double.PositiveInfinity)]
    public void Move_RefusesNonsense(double x, double z)
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        Assert.Throws<DomainException>(() => state.Move(Tom, new Position(x, z)));
    }

    [Test]
    public void Move_And_Say_RefuseACatThatIsNotHere()
    {
        var state = new RoomState(Room);

        Assert.Throws<DomainException>(() => state.Move(Tom, new Position(0, 0)));
        Assert.Throws<DomainException>(() => state.Say(Tom, "hi", Now));
    }

    [Test]
    public void Say_TrimsAndKeepsOnlyTheLatestLines()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        Assert.That(state.Say(Tom, "  meow  ", Now).Text, Is.EqualTo("meow"));
        for (var i = 0; i < RoomState.ChatHistory + 5; i++)
        {
            state.Say(Tom, $"line {i}", Now);
        }

        Assert.That(state.RecentChat, Has.Count.EqualTo(RoomState.ChatHistory));
        Assert.That(state.RecentChat.Last().Text, Is.EqualTo($"line {RoomState.ChatHistory + 4}"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Say_RefusesEmptyLines(string text)
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        Assert.Throws<DomainException>(() => state.Say(Tom, text, Now));
    }

    [Test]
    public void Say_RefusesLongLines()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        Assert.Throws<DomainException>(() => state.Say(Tom, new string('a', RoomState.MaxChatLength + 1), Now));
    }

    [Test]
    public void Leave_RemovesTheCat()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey);

        Assert.That(state.Leave(Tom), Is.True);
        Assert.That(state.Leave(Tom), Is.False);
        Assert.That(state.Cats, Is.Empty);
    }
}
