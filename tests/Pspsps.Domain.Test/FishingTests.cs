using Pspsps.Domain.Fishing;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Domain.Test;

public sealed class FishingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly RoomDefinition PondRoom =
        new("park", "Park", Width: 20, Depth: 20, Spawn: new(0, 0), Capacity: 10, Pond: new Pond(new(5, 5), Radius: 2));
    private static readonly RoomDefinition DryRoom = new("hall", "Hall", Width: 20, Depth: 20, Spawn: new(0, 0), Capacity: 10);
    private static readonly Guid Tom = Guid.NewGuid();
    private const string Tab = "tom-tab";

    [Test]
    public void Pick_FollowsTheWeights_FromCommonToRare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FishCatalog.Pick(0).Id, Is.EqualTo(FishCatalog.All[0].Id));
            Assert.That(FishCatalog.Pick(0.999999).Id, Is.EqualTo(FishCatalog.All[^1].Id));
        });
        var picks = Enumerable.Range(0, 1000).Select(i => FishCatalog.Pick(i / 1000.0)).ToList();
        var total = FishCatalog.All.Sum(f => f.Weight);
        foreach (var fish in FishCatalog.All)
        {
            Assert.That(picks.Count(p => p.Id == fish.Id), Is.EqualTo(fish.Weight * 1000 / total).Within(1), fish.Name);
        }
    }

    [TestCase(-0.1)]
    [TestCase(1.0)]
    public void Rolls_OutsideZeroToOne_AreRefused(double roll)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FishCatalog.Pick(roll));
        Assert.Throws<ArgumentOutOfRangeException>(() => FishingLine.Cast(Now, roll));
    }

    [TestCase(0.0, 3.0)]
    [TestCase(0.5, 6.0)]
    [TestCase(0.999, 8.994)]
    public void Cast_BitesBetweenTheShortestAndLongestWait(double roll, double seconds)
    {
        var line = FishingLine.Cast(Now, roll);

        Assert.That((line.BiteAt - Now).TotalSeconds, Is.EqualTo(seconds).Within(0.001));
    }

    [Test]
    public void Reel_CatchesOnlyInsideTheBiteWindow()
    {
        var line = FishingLine.Cast(Now, 0);
        var bite = line.BiteAt;

        Assert.Multiple(() =>
        {
            Assert.That(line.Reel(bite - TimeSpan.FromMilliseconds(1), 0).Outcome, Is.EqualTo(ReelOutcome.TooEarly));
            Assert.That(line.Reel(bite, 0), Is.EqualTo(new ReelResult(ReelOutcome.Caught, FishCatalog.Pick(0))));
            Assert.That(line.Reel(bite + FishingLine.BiteWindow, 0.5).Fish, Is.EqualTo(FishCatalog.Pick(0.5)));
            Assert.That(line.Reel(bite + FishingLine.BiteWindow + TimeSpan.FromMilliseconds(1), 0).Outcome,
                Is.EqualTo(ReelOutcome.TooLate));
        });
    }

    [Test]
    public void Cast_NeedsTheCatAtThePondsEdge()
    {
        var state = new RoomState(PondRoom);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);

        var far = Assert.Throws<DomainException>(() => state.Cast(Tom, Now, 0));
        Assert.That(far!.Message, Is.EqualTo("Walk up to the pond to fish."));

        state.Move(Tom, new Position(5, 8.4));
        Assert.That(state.Cast(Tom, Now, 0).BiteAt, Is.EqualTo(Now + FishingLine.ShortestWait));
        Assert.That(state.IsFishing(Tom), Is.True);
    }

    [Test]
    public void Cast_InARoomWithNoPond_IsRefused()
    {
        var state = new RoomState(DryRoom);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);

        var dry = Assert.Throws<DomainException>(() => state.Cast(Tom, Now, 0));
        Assert.That(dry!.Message, Is.EqualTo("There's no water to fish in here."));
    }

    [Test]
    public void Cast_Twice_IsRefused_AndReelingTakesTheLineOut()
    {
        var state = AtThePond();
        var line = state.Cast(Tom, Now, 0);

        Assert.Throws<DomainException>(() => state.Cast(Tom, Now, 0));
        Assert.That(state.Reel(Tom, line.BiteAt, 0).Outcome, Is.EqualTo(ReelOutcome.Caught));
        Assert.That(state.IsFishing(Tom), Is.False);
        var again = Assert.Throws<DomainException>(() => state.Reel(Tom, line.BiteAt, 0));
        Assert.That(again!.Message, Is.EqualTo("Cast your line first."));
    }

    [Test]
    public void Reel_TooEarly_LosesTheFish_AndTheLine()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);

        Assert.That(state.Reel(Tom, Now, 0), Is.EqualTo(new ReelResult(ReelOutcome.TooEarly, null)));
        Assert.That(state.IsFishing(Tom), Is.False);
    }

    [Test]
    public void WalkingOff_OrLeaving_ReelsTheLineIn()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);
        state.Move(Tom, new Position(0, 0));
        Assert.That(state.IsFishing(Tom), Is.False);

        state.Move(Tom, new Position(5, 8));
        state.Cast(Tom, Now, 0);
        state.Leave(Tom, Tab);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        Assert.That(state.IsFishing(Tom), Is.False);
    }

    private static RoomState AtThePond()
    {
        var state = new RoomState(PondRoom);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        state.Move(Tom, new Position(5, 8));
        return state;
    }
}
