using Pspsps.Domain.Friends;

namespace Pspsps.Domain.Test;

public sealed class FriendshipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly Guid Garfield = Guid.NewGuid();

    private static readonly FriendCounts NoFriends = new(0, 0, 0);

    private static Friendship TomAsksFelix() => Friendship.Request(Tom, Felix, NoFriends, NoFriends, Now);

    [Test]
    public void Request_IsPending_FromTheRequesterToTheAddressee()
    {
        var friendship = TomAsksFelix();

        Assert.Multiple(() =>
        {
            Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Pending));
            Assert.That(friendship.RequesterId, Is.EqualTo(Tom));
            Assert.That(friendship.AddresseeId, Is.EqualTo(Felix));
            Assert.That(friendship.RequestedAt, Is.EqualTo(Now));
            Assert.That(friendship.AcceptedAt, Is.Null);
            Assert.That(friendship.StateFor(Tom), Is.EqualTo(FriendState.RequestSent));
            Assert.That(friendship.StateFor(Felix), Is.EqualTo(FriendState.RequestReceived));
            Assert.That(friendship.StateFor(Garfield), Is.EqualTo(FriendState.None));
        });
    }

    [Test]
    public void Request_KeysThePairTheSameWayWhoeverAsks()
    {
        var tomAsks = Friendship.Request(Tom, Felix, NoFriends, NoFriends, Now);
        var felixAsks = Friendship.Request(Felix, Tom, NoFriends, NoFriends, Now);

        Assert.That((felixAsks.FirstPlayerId, felixAsks.SecondPlayerId), Is.EqualTo((tomAsks.FirstPlayerId, tomAsks.SecondPlayerId)));
        Assert.That(tomAsks.FirstPlayerId.CompareTo(tomAsks.SecondPlayerId), Is.Negative);
        Assert.That(felixAsks.AddresseeId, Is.EqualTo(Tom));
    }

    [Test]
    public void Request_ToYourself_IsRefused() =>
        Assert.That(() => Friendship.Request(Tom, Tom, NoFriends, NoFriends, Now),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You can't be friends with yourself."));

    [Test]
    public void Request_ToNobody_IsRefused() =>
        Assert.Throws<DomainException>(() => Friendship.Request(Tom, Guid.Empty, NoFriends, NoFriends, Now));

    [Test]
    public void Request_WhenTheRequesterHasTheMostFriends_IsRefused() =>
        Assert.Throws<DomainException>(() =>
            Friendship.Request(Tom, Felix, new FriendCounts(Friendship.MaxFriends, 0, 0), NoFriends, Now));

    [Test]
    public void Request_WhenTheRequesterHasTheMostRequestsOut_IsRefused() =>
        Assert.Throws<DomainException>(() =>
            Friendship.Request(Tom, Felix, new FriendCounts(0, Friendship.MaxSent, 0), NoFriends, Now));

    [Test]
    public void Request_WhenTheAddresseeHasTheMostFriends_IsRefused() =>
        Assert.That(() => Friendship.Request(Tom, Felix, NoFriends, new FriendCounts(Friendship.MaxFriends, 0, 0), Now),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("That cat has as many friends as it can have."));

    [Test]
    public void Request_WhenTheAddresseeHasTheMostRequestsWaiting_IsRefused() =>
        Assert.That(() => Friendship.Request(Tom, Felix, NoFriends, new FriendCounts(0, 0, Friendship.MaxReceived), Now),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("That cat has too many requests waiting. Try again later."));

    [Test]
    public void Request_RequestsWaitingForTheRequester_DoNotStopThemAsking()
    {
        // Many cats asking one fill only the slots for requests to them: they can still ask others.
        var asks = Friendship.Request(Tom, Felix, new FriendCounts(0, 0, Friendship.MaxReceived), NoFriends, Now);

        Assert.That(asks.Status, Is.EqualTo(FriendshipStatus.Pending));
    }

    [Test]
    public void Request_JustUnderTheLimits_IsAllowed()
    {
        var almost = new FriendCounts(Friendship.MaxFriends - 1, Friendship.MaxSent - 1, Friendship.MaxReceived - 1);

        Assert.That(Friendship.Request(Tom, Felix, almost, almost, Now).Status, Is.EqualTo(FriendshipStatus.Pending));
    }

    [Test]
    public void Accept_WhenEitherHasTheMostFriends_IsRefused()
    {
        var full = new FriendCounts(Friendship.MaxFriends, 0, 0);

        Assert.Throws<DomainException>(() => TomAsksFelix().Accept(Felix, Later, full, NoFriends));
        Assert.That(() => TomAsksFelix().Accept(Felix, Later, NoFriends, full),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("That cat has as many friends as it can have."));
    }

    [Test]
    public void EnsureCanVisit_OnlyAFriend()
    {
        var friendship = TomAsksFelix();
        Assert.That(() => friendship.EnsureCanVisit(Tom),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You can only go to your friends."));

        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.DoesNotThrow(() => friendship.EnsureCanVisit(Tom));
        Assert.DoesNotThrow(() => friendship.EnsureCanVisit(Felix));
        Assert.Throws<DomainException>(() => friendship.EnsureCanVisit(Garfield));
    }

    [Test]
    public void Accept_ByTheAddressee_MakesThemFriends()
    {
        var friendship = TomAsksFelix();

        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.Multiple(() =>
        {
            Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Accepted));
            Assert.That(friendship.AcceptedAt, Is.EqualTo(Later));
            Assert.That(friendship.StateFor(Tom), Is.EqualTo(FriendState.Friends));
            Assert.That(friendship.StateFor(Felix), Is.EqualTo(FriendState.Friends));
        });
    }

    [Test]
    public void Accept_ByTheRequester_IsRefused()
    {
        var friendship = TomAsksFelix();

        Assert.Throws<DomainException>(() => friendship.Accept(Tom, Later, NoFriends, NoFriends));
        Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Pending));
    }

    [Test]
    public void Accept_BySomeoneElse_IsRefused() =>
        Assert.Throws<DomainException>(() => TomAsksFelix().Accept(Garfield, Later, NoFriends, NoFriends));

    [Test]
    public void Accept_Twice_IsRefused()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.Throws<DomainException>(() => friendship.Accept(Felix, Later, NoFriends, NoFriends));
    }

    [Test]
    public void Decline_ByTheAddressee_IsAllowed() =>
        Assert.DoesNotThrow(() => TomAsksFelix().Decline(Felix));

    [Test]
    public void Decline_ByTheRequesterOrSomeoneElse_IsRefused()
    {
        var friendship = TomAsksFelix();

        Assert.Throws<DomainException>(() => friendship.Decline(Tom));
        Assert.Throws<DomainException>(() => friendship.Decline(Garfield));
    }

    [Test]
    public void Decline_OnceFriends_IsRefused()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.Throws<DomainException>(() => friendship.Decline(Felix));
    }

    [Test]
    public void Cancel_ByTheRequester_IsAllowed_ByAnyoneElseRefused()
    {
        var friendship = TomAsksFelix();

        Assert.DoesNotThrow(() => friendship.Cancel(Tom));
        Assert.Throws<DomainException>(() => friendship.Cancel(Felix));
        Assert.Throws<DomainException>(() => friendship.Cancel(Garfield));
    }

    [Test]
    public void Unfriend_ByEitherFriend_IsAllowed()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.DoesNotThrow(() => friendship.Unfriend(Tom));
        Assert.DoesNotThrow(() => friendship.Unfriend(Felix));
        Assert.Throws<DomainException>(() => friendship.Unfriend(Garfield));
    }

    [Test]
    public void Unfriend_WhilePending_IsRefused() =>
        Assert.Throws<DomainException>(() => TomAsksFelix().Unfriend(Tom));

    [Test]
    public void RequestAgain_ByTheAddressee_AcceptsTheirRequest()
    {
        var friendship = TomAsksFelix();

        friendship.RequestAgain(Felix, Later, NoFriends, NoFriends);

        Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Accepted));
        Assert.That(friendship.AcceptedAt, Is.EqualTo(Later));
    }

    [Test]
    public void RequestAgain_ByTheRequester_IsADuplicate() =>
        Assert.That(() => TomAsksFelix().RequestAgain(Tom, Later, NoFriends, NoFriends),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You've already asked. Wait for an answer."));

    [Test]
    public void RequestAgain_WhenFriends_IsRefused()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later, NoFriends, NoFriends);

        Assert.That(() => friendship.RequestAgain(Tom, Later, NoFriends, NoFriends),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You're already friends."));
    }

    [Test]
    public void RequestAgain_BySomeoneElse_IsRefused() =>
        Assert.Throws<DomainException>(() => TomAsksFelix().RequestAgain(Garfield, Later, NoFriends, NoFriends));

    [Test]
    public void OtherThan_IsTheOtherOne_AndRefusesAStranger()
    {
        var friendship = TomAsksFelix();

        Assert.That(friendship.OtherThan(Tom), Is.EqualTo(Felix));
        Assert.That(friendship.OtherThan(Felix), Is.EqualTo(Tom));
        Assert.Throws<DomainException>(() => friendship.OtherThan(Garfield));
    }
}
