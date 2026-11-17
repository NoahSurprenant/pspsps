using Pspsps.Domain.Friends;

namespace Pspsps.Domain.Test;

public sealed class FriendshipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid Felix = Guid.NewGuid();
    private static readonly Guid Garfield = Guid.NewGuid();

    private static Friendship TomAsksFelix() => Friendship.Request(Tom, Felix, 0, 0, Now);

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
        var tomAsks = Friendship.Request(Tom, Felix, 0, 0, Now);
        var felixAsks = Friendship.Request(Felix, Tom, 0, 0, Now);

        Assert.That((felixAsks.FirstPlayerId, felixAsks.SecondPlayerId), Is.EqualTo((tomAsks.FirstPlayerId, tomAsks.SecondPlayerId)));
        Assert.That(tomAsks.FirstPlayerId.CompareTo(tomAsks.SecondPlayerId), Is.Negative);
        Assert.That(felixAsks.AddresseeId, Is.EqualTo(Tom));
    }

    [Test]
    public void Request_ToYourself_IsRefused() =>
        Assert.That(() => Friendship.Request(Tom, Tom, 0, 0, Now),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You can't be friends with yourself."));

    [Test]
    public void Request_ToNobody_IsRefused() =>
        Assert.Throws<DomainException>(() => Friendship.Request(Tom, Guid.Empty, 0, 0, Now));

    [Test]
    public void Request_WhenTheRequesterHasTheMostFriends_IsRefused() =>
        Assert.Throws<DomainException>(() => Friendship.Request(Tom, Felix, Friendship.MaxPerPlayer, 0, Now));

    [Test]
    public void Request_WhenTheAddresseeHasTheMostFriends_IsRefused() =>
        Assert.That(() => Friendship.Request(Tom, Felix, 0, Friendship.MaxPerPlayer, Now),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("That cat can't take any more friends."));

    [Test]
    public void Request_JustUnderTheLimit_IsAllowed() =>
        Assert.That(Friendship.Request(Tom, Felix, Friendship.MaxPerPlayer - 1, Friendship.MaxPerPlayer - 1, Now).Status,
            Is.EqualTo(FriendshipStatus.Pending));

    [Test]
    public void Accept_ByTheAddressee_MakesThemFriends()
    {
        var friendship = TomAsksFelix();

        friendship.Accept(Felix, Later);

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

        Assert.Throws<DomainException>(() => friendship.Accept(Tom, Later));
        Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Pending));
    }

    [Test]
    public void Accept_BySomeoneElse_IsRefused() =>
        Assert.Throws<DomainException>(() => TomAsksFelix().Accept(Garfield, Later));

    [Test]
    public void Accept_Twice_IsRefused()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later);

        Assert.Throws<DomainException>(() => friendship.Accept(Felix, Later));
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
        friendship.Accept(Felix, Later);

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
        friendship.Accept(Felix, Later);

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

        friendship.RequestAgain(Felix, Later);

        Assert.That(friendship.Status, Is.EqualTo(FriendshipStatus.Accepted));
        Assert.That(friendship.AcceptedAt, Is.EqualTo(Later));
    }

    [Test]
    public void RequestAgain_ByTheRequester_IsADuplicate() =>
        Assert.That(() => TomAsksFelix().RequestAgain(Tom, Later),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You've already asked. Wait for an answer."));

    [Test]
    public void RequestAgain_WhenFriends_IsRefused()
    {
        var friendship = TomAsksFelix();
        friendship.Accept(Felix, Later);

        Assert.That(() => friendship.RequestAgain(Tom, Later),
            Throws.TypeOf<DomainException>().With.Message.EqualTo("You're already friends."));
    }

    [Test]
    public void RequestAgain_BySomeoneElse_IsRefused() =>
        Assert.Throws<DomainException>(() => TomAsksFelix().RequestAgain(Garfield, Later));

    [Test]
    public void OtherThan_IsTheOtherOne_AndRefusesAStranger()
    {
        var friendship = TomAsksFelix();

        Assert.That(friendship.OtherThan(Tom), Is.EqualTo(Felix));
        Assert.That(friendship.OtherThan(Felix), Is.EqualTo(Tom));
        Assert.Throws<DomainException>(() => friendship.OtherThan(Garfield));
    }
}
