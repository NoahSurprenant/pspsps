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
        line.Bite(bite);

        Assert.Multiple(() =>
        {
            Assert.That(line.BitAt, Is.EqualTo(bite));
            Assert.That(line.Reel(bite - TimeSpan.FromMilliseconds(1), 0).Outcome, Is.EqualTo(ReelOutcome.TooEarly));
            Assert.That(line.Reel(bite, 0), Is.EqualTo(new ReelResult(ReelOutcome.Caught, FishCatalog.Pick(0))));
            Assert.That(line.Reel(bite + FishingLine.BiteWindow, 0.5).Fish, Is.EqualTo(FishCatalog.Pick(0.5)));
            Assert.That(line.Reel(bite + FishingLine.BiteWindow + TimeSpan.FromMilliseconds(1), 0).Outcome,
                Is.EqualTo(ReelOutcome.TooLate));
        });
    }

    [Test]
    public void Reel_ALateBite_GetsTheWholeWindowFromWhenItWasSent()
    {
        // The bite timer fires a second after the bite was due (a busy room, a late timer): the player is told then, and
        // has the whole window from then.
        var line = FishingLine.Cast(Now, 0);
        var sent = line.BiteAt + TimeSpan.FromSeconds(1);
        line.Bite(sent);

        Assert.Multiple(() =>
        {
            Assert.That(line.BitAt, Is.EqualTo(sent));
            Assert.That(line.Reel(sent + FishingLine.BiteWindow, 0).Outcome, Is.EqualTo(ReelOutcome.Caught));
            Assert.That(line.Reel(sent + FishingLine.BiteWindow + TimeSpan.FromMilliseconds(1), 0).Outcome,
                Is.EqualTo(ReelOutcome.TooLate));
        });
    }

    [Test]
    public void Reel_AfterTheBiteWasDue_ButBeforeItWasSent_IsTooEarly()
    {
        var line = FishingLine.Cast(Now, 0);

        Assert.That(line.BitAt, Is.Null);
        Assert.That(line.Reel(line.BiteAt + TimeSpan.FromMilliseconds(500), 0).Outcome, Is.EqualTo(ReelOutcome.TooEarly));
    }

    [Test]
    public void ReelResult_PaysTheCatchsCoins_AndNothingOtherwise()
    {
        var koi = FishCatalog.All.Single(f => f.Id == "koi");

        Assert.Multiple(() =>
        {
            Assert.That(new ReelResult(ReelOutcome.Caught, koi).IsCatch, Is.True);
            Assert.That(new ReelResult(ReelOutcome.Caught, koi).Coins, Is.EqualTo(100));
            Assert.That(new ReelResult(ReelOutcome.TooEarly, null).IsCatch, Is.False);
            Assert.That(new ReelResult(ReelOutcome.TooLate, null).Coins, Is.Zero);
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
        Assert.That(state.LineOf(Tom), Is.Not.Null);
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
        state.Bite(Tom, line, line.BiteAt);
        Assert.That(state.Reel(Tom, line.BiteAt, 0).Outcome, Is.EqualTo(ReelOutcome.Caught));
        Assert.That(state.LineOf(Tom), Is.Null);
        var again = Assert.Throws<DomainException>(() => state.Reel(Tom, line.BiteAt, 0));
        Assert.That(again!.Message, Is.EqualTo("Cast your line first."));
    }

    [Test]
    public void Reel_TooEarly_LosesTheFish_AndTheLine()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);

        Assert.That(state.Reel(Tom, Now, 0), Is.EqualTo(new ReelResult(ReelOutcome.TooEarly, null)));
        Assert.That(state.LineOf(Tom), Is.Null);
    }

    [Test]
    public void WalkingOff_OrLeaving_ReelsTheLineIn()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);
        state.Move(Tom, new Position(0, 0));
        Assert.That(state.LineOf(Tom), Is.Null);

        state.Move(Tom, new Position(5, 8));
        state.Cast(Tom, Now + RoomState.CastCooldown, 0);
        state.Leave(Tom, Tab);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        Assert.That(state.LineOf(Tom), Is.Null);
    }

    [Test]
    public void Bite_MarksTheLineBitten_AndSaysWhichSessionToTell()
    {
        var state = AtThePond();
        var line = state.Cast(Tom, Now, 0);
        var sent = line.BiteAt + TimeSpan.FromMilliseconds(300);

        Assert.That(state.Bite(Tom, line, sent), Is.EqualTo(Tab));
        Assert.That(line.BitAt, Is.EqualTo(sent));
        Assert.That(state.Reel(Tom, sent + FishingLine.BiteWindow, 0).Outcome, Is.EqualTo(ReelOutcome.Caught));
    }

    [Test]
    public void Bite_ForALineNoLongerOut_TellsNobody()
    {
        var state = AtThePond();
        var old = state.Cast(Tom, Now, 0);
        state.Reel(Tom, Now, 0);
        var line = state.Cast(Tom, Now + RoomState.CastCooldown, 0);

        Assert.That(state.Bite(Tom, old, old.BiteAt), Is.Null, "a bite for a line already reeled in");
        Assert.That(old.BitAt, Is.Null);
        Assert.That(line.BitAt, Is.Null, "the new line didn't bite");
        state.Leave(Tom, Tab);
        Assert.That(state.Bite(Tom, line, line.BiteAt), Is.Null, "the cat has left");
    }

    [Test]
    public void AnotherTabTakingTheCatOver_ReelsTheLineIn()
    {
        var state = AtThePond();
        var line = state.Cast(Tom, Now, 0);

        state.Join(Tom, "Tom", FurColor.Grey, "tom-new-tab", Now);

        Assert.That(state.LineOf(Tom), Is.Null);
        Assert.That(state.Bite(Tom, line, line.BiteAt), Is.Null, "the old tab's bite goes nowhere");
        var again = Assert.Throws<DomainException>(() => state.Reel(Tom, line.BiteAt, 0));
        Assert.That(again!.Message, Is.EqualTo("Cast your line first."));
        // The new tab fishes from where the cat stands.
        var mine = state.Cast(Tom, Now + RoomState.CastCooldown, 0);
        Assert.That(state.Bite(Tom, mine, mine.BiteAt), Is.EqualTo("tom-new-tab"));
    }

    [Test]
    public void EveryFish_PaysAtLeastOneCoin()
    {
        // A catch is always paid: Player.Earning refuses nothing.
        Assert.That(FishCatalog.All.Select(f => f.Coins), Is.All.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Rolls_AreCheckedTheSameWayEverywhere()
    {
        var pick = Assert.Throws<ArgumentOutOfRangeException>(() => FishCatalog.Pick(1));
        var cast = Assert.Throws<ArgumentOutOfRangeException>(() => FishingLine.Cast(Now, 1));

        Assert.That(cast!.Message, Is.EqualTo(pick!.Message));
    }

    [Test]
    public void Cast_WaitsACooldownAfterTheLastCastOrReel()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);
        var reeledAt = Now + TimeSpan.FromMilliseconds(500);
        state.Reel(Tom, reeledAt, 0);

        var soon = Assert.Throws<DomainException>(() => state.Cast(Tom, reeledAt + RoomState.CastCooldown - TimeSpan.FromMilliseconds(1), 0));
        Assert.That(soon!.Message, Is.EqualTo("Not so fast: wait a moment before casting again."));
        Assert.That(state.Cast(Tom, reeledAt + RoomState.CastCooldown, 0), Is.Not.Null);
    }

    [Test]
    public void Cast_RightAfterWalkingOffACast_WaitsTheCooldownToo()
    {
        var state = AtThePond();
        state.Cast(Tom, Now, 0);
        state.Move(Tom, new Position(5, 8.2));

        Assert.Throws<DomainException>(() => state.Cast(Tom, Now + TimeSpan.FromMilliseconds(100), 0));
    }

    [Test]
    public void AMissedBite_GetsAway_OnceItsWindowIsOver()
    {
        var state = AtThePond();
        var line = state.Cast(Tom, Now, 0);
        state.Bite(Tom, line, line.BiteAt);
        var lastChance = line.BiteAt + FishingLine.BiteWindow;

        Assert.That(state.LetGetAway(Tom, line, lastChance), Is.Null, "the window isn't over yet");
        Assert.That(state.LineOf(Tom), Is.SameAs(line));
        Assert.That(state.LetGetAway(Tom, line, lastChance + TimeSpan.FromMilliseconds(1)), Is.EqualTo(Tab));
        Assert.That(state.LineOf(Tom), Is.Null);
        var again = Assert.Throws<DomainException>(() => state.Reel(Tom, lastChance, 0));
        Assert.That(again!.Message, Is.EqualTo("Cast your line first."));
    }

    [Test]
    public void ALineThatHasNotBitten_OrIsNoLongerOut_DoesNotGetAway()
    {
        var state = AtThePond();
        var line = state.Cast(Tom, Now, 0);

        Assert.That(state.LetGetAway(Tom, line, Now + TimeSpan.FromMinutes(1)), Is.Null, "nothing has bitten");
        Assert.That(state.LineOf(Tom), Is.SameAs(line));
        state.Bite(Tom, line, line.BiteAt);
        state.Reel(Tom, line.BiteAt, 0);
        Assert.That(state.LetGetAway(Tom, line, Now + TimeSpan.FromMinutes(1)), Is.Null, "it was reeled in");
    }

    private static RoomState AtThePond()
    {
        var state = new RoomState(PondRoom);
        state.Join(Tom, "Tom", FurColor.Grey, Tab, Now);
        state.Move(Tom, new Position(5, 8));
        return state;
    }
}
