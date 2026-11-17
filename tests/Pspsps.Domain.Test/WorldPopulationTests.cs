using Pspsps.Domain.Worlds;

namespace Pspsps.Domain.Test;

public sealed class WorldPopulationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorldDefinition World = new("test", "Test World", Capacity: 2);

    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private const string TabA = "tom-tab-a";
    private const string TabB = "tom-tab-b";

    // Leases run out once strictly older than their duration.
    private static readonly TimeSpan PastALease = SessionLease.Duration + TimeSpan.FromTicks(1);

    [Test]
    public void TryEnter_CountsPlayersNotTabs()
    {
        var population = new WorldPopulation(World);

        Assert.That(population.TryEnter(Tom, TabA, Now), Is.True);
        Assert.That(population.TryEnter(Tom, TabB, Now), Is.True);

        Assert.That(population.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryEnter_RefusesANewPlayerWhenFull_ButNotAnotherTabOfOneInside()
    {
        var population = new WorldPopulation(World);
        population.TryEnter(Tom, TabA, Now);
        population.TryEnter(Felix, "felix-tab", Now);

        Assert.That(population.TryEnter(Guid.NewGuid(), "garfield-tab", Now), Is.False);
        Assert.That(population.TryEnter(Tom, TabB, Now), Is.True);
        Assert.That(population.Count, Is.EqualTo(2));
    }

    [Test]
    public void Leave_ThePlayerLeavesWithTheirLastTab()
    {
        var population = new WorldPopulation(World);
        population.TryEnter(Tom, TabA, Now);
        population.TryEnter(Tom, TabB, Now);

        population.Leave(Tom, TabA);
        population.Leave(Tom, TabA);
        Assert.That(population.Count, Is.EqualTo(1));

        population.Leave(Tom, TabB);
        Assert.That(population.Count, Is.Zero);
    }

    [Test]
    public void RemoveExpired_CountsOutTabsThatStoppedRenewing()
    {
        var population = new WorldPopulation(World);
        population.TryEnter(Tom, TabA, Now);
        population.TryEnter(Tom, TabB, Now);
        population.TryEnter(Felix, "felix-tab", Now);
        population.Renew(Tom, TabB, Now + SessionLease.RenewEvery);

        Assert.That(population.RemoveExpired(Now + SessionLease.Duration), Is.Zero);
        Assert.That(population.RemoveExpired(Now + PastALease), Is.EqualTo(2));

        // Tom stays in on his renewed tab; Felix is out, which frees his place.
        Assert.That(population.Count, Is.EqualTo(1));
        Assert.That(population.TryEnter(Guid.NewGuid(), "garfield-tab", Now + PastALease), Is.True);
    }

    [Test]
    public void Renew_FromATabTheWorldForgot_CountsItBackIn_WithinTheCap()
    {
        // The world's actor came back empty on another pod; the live tabs' pods keep renewing them.
        var population = new WorldPopulation(World);

        Assert.That(population.Renew(Tom, TabA, Now), Is.True);
        Assert.That(population.Renew(Felix, "felix-tab", Now), Is.True);
        Assert.That(population.Renew(Guid.NewGuid(), "garfield-tab", Now), Is.False, "full");
        Assert.That(population.Count, Is.EqualTo(2));
        Assert.That(population.RemoveExpired(Now + SessionLease.Duration), Is.Zero, "counted back in with fresh leases");
    }
}
