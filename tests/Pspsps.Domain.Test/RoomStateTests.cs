using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class RoomStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly RoomDefinition Room = new("test", "Test Room", Width: 10, Depth: 6, Spawn: new(1, 2), Capacity: 2);

    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private const string TabA = "tom-tab-a";
    private const string TabB = "tom-tab-b";

    [Test]
    public void Join_PutsTheCatAtTheSpawnPoint()
    {
        var state = new RoomState(Room);

        var cat = state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        Assert.That(cat.Position, Is.EqualTo(new Position(1, 2)));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void Join_FromAnotherTab_TakesTheCatOverWhereItStands()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);
        state.Move(Tom, new Position(3, 0));

        var again = state.Join(Tom, "Tom", FurColor.Grey, TabB, Now);

        Assert.That(again.Position, Is.EqualTo(new Position(3, 0)));
        Assert.That(state.Cats, Has.Count.EqualTo(1));
    }

    [Test]
    public void Join_RefusesAFullRoom()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);
        state.Join(Felix, "Felix", FurColor.Tuxedo, "felix-tab", Now);

        Assert.Throws<DomainException>(() => state.Join(Guid.NewGuid(), "Garfield", FurColor.Orange, "garfield-tab", Now));
    }

    [Test]
    public void Move_StopsAtTheWalls()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        var position = state.Move(Tom, new Position(100, -100));

        Assert.That(position, Is.EqualTo(new Position(5, -3)));
        Assert.That(state.Cats.Single().Position, Is.EqualTo(position));
    }

    [TestCase(double.NaN, 0)]
    [TestCase(0, double.PositiveInfinity)]
    public void Move_RefusesNonsense(double x, double z)
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

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
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

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
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        Assert.Throws<DomainException>(() => state.Say(Tom, text, Now));
    }

    [Test]
    public void Say_RefusesLongLines()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        Assert.Throws<DomainException>(() => state.Say(Tom, new string('a', RoomState.MaxChatLength + 1), Now));
    }

    [Test]
    public void Leave_FromAReplacedTab_LeavesTheNewTabsCat()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);
        state.Join(Tom, "Tom", FurColor.Grey, TabB, Now);

        Assert.That(state.Leave(Tom, TabA), Is.False);
        Assert.That(state.Cats, Has.Count.EqualTo(1));
        Assert.That(state.Leave(Tom, TabB), Is.True);
        Assert.That(state.Cats, Is.Empty);
    }

    [Test]
    public void Leave_RemovesTheCat()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        Assert.That(state.Leave(Tom, TabA), Is.True);
        Assert.That(state.Leave(Tom, TabA), Is.False);
        Assert.That(state.Cats, Is.Empty);
    }

    [Test]
    public void RemoveExpired_TakesOutCatsWhoseLeaseRanOut()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);
        state.Join(Felix, "Felix", FurColor.Tuxedo, "felix-tab", Now + TimeSpan.FromSeconds(30));

        Assert.That(state.RemoveExpired(Now + SessionLease.Duration - TimeSpan.FromTicks(1)), Is.Empty);
        Assert.That(state.RemoveExpired(Now + SessionLease.Duration), Is.EqualTo(new[] { Tom }));
        Assert.That(state.Cats.Select(c => c.PlayerId), Is.EqualTo(new[] { Felix }));

        // Gone for good: its session can't renew it or take it out again.
        Assert.That(state.Renew(Tom, TabA, Now + SessionLease.Duration), Is.False);
        Assert.That(state.Leave(Tom, TabA), Is.False);
    }

    [Test]
    public void Renew_KeepsTheCatIn()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);

        Assert.That(state.Renew(Tom, TabA, Now + SessionLease.RenewEvery), Is.True);

        Assert.That(state.RemoveExpired(Now + SessionLease.Duration), Is.Empty);
        Assert.That(state.Cats, Has.Count.EqualTo(1));
        Assert.That(state.RemoveExpired(Now + SessionLease.RenewEvery + SessionLease.Duration), Is.EqualTo(new[] { Tom }));
    }

    [Test]
    public void Renew_FromAReplacedTab_DoesNotKeepTheCatIn()
    {
        var state = new RoomState(Room);
        state.Join(Tom, "Tom", FurColor.Grey, TabA, Now);
        state.Join(Tom, "Tom", FurColor.Grey, TabB, Now + TimeSpan.FromSeconds(10));

        Assert.That(state.Renew(Tom, TabA, Now + TimeSpan.FromSeconds(60)), Is.False);

        // The new tab's lease started when it took over, and only it counts.
        Assert.That(state.RemoveExpired(Now + SessionLease.Duration), Is.Empty);
        Assert.That(state.RemoveExpired(Now + TimeSpan.FromSeconds(10) + SessionLease.Duration), Is.EqualTo(new[] { Tom }));
    }

    [Test]
    public void Renew_RefusesACatThatIsNotHere()
    {
        var state = new RoomState(Room);

        Assert.That(state.Renew(Tom, TabA, Now), Is.False);
        Assert.That(state.Cats, Is.Empty);
    }
}
