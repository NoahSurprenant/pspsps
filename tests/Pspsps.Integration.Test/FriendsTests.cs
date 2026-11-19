using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Pspsps.Application.Friends;
using Pspsps.Application.Players;
using Pspsps.Domain;
using Pspsps.Domain.Friends;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Friend requests and the friends list, end to end: two guests, the hub, the grains and PostgreSQL.</summary>
public sealed class FriendsTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task Friends_SeeWhichWorldAndRoomTheOtherIsIn_AndGoThere()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var felixSees = new Recorder(felixHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await tomHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");

        // Tom asks from Felix's card; Felix is told, and accepts.
        var tomsCard = await tomHub.InvokeAsync<PlayerCardView>("AddFriend", felix.Id);
        Assert.That(tomsCard.FriendState, Is.EqualTo("RequestSent"));
        Assert.That(await felixSees.Next<Guid>("FriendsChanged", id => id == tom.Id), Is.EqualTo(tom.Id));
        var request = (await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single();
        Assert.That((request.PlayerId, request.FriendState, request.Place), Is.EqualTo((tom.Id, "RequestReceived", (FriendPlaceView?)null)),
            "a request shows no place");
        var felixsCard = await felixHub.InvokeAsync<PlayerCardView>("AcceptFriend", tom.Id);
        Assert.That(felixsCard, Is.EqualTo(new PlayerCardView(tom.Id, tom.DisplayName, tom.FurColor.ToString(), "Friends", CanVisitHome: true)),
            "a friend's home is open to friends until they say otherwise");

        // Felix's list shows Tom in Calico's café.
        var friend = (await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single();
        Assert.That(friend.FriendState, Is.EqualTo("Friends"));
        Assert.That(friend.Place, Is.EqualTo(new FriendPlaceView("calico", "Calico", "cafe", "Cat Café")));

        // Felix goes to Tom: into Calico, into the café, where Tom is.
        var there = await felixHub.InvokeAsync<RoomSnapshot>("GoToFriend", tom.Id);
        Assert.That((there.WorldId, there.RoomId), Is.EqualTo(("calico", "cafe")));
        Assert.That(there.Cats.Select(c => c.PlayerId), Is.SupersetOf(new[] { tom.Id, felix.Id }));
        var worlds = await felixHub.InvokeAsync<List<WorldSummary>>("GetWorlds");
        Assert.That(worlds.Single(w => w.Id == "calico").Population, Is.GreaterThanOrEqualTo(2));

        // Tom closes the game: offline.
        await tomHub.StopAsync();
        // The server sees the connection close a moment after the client does.
        await AssertEventually(async () => (await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single().Place is null);
        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("GoToFriend", tom.Id));
        Assert.That(refused!.Message, Does.Contain("isn't in a room right now"));
    }

    [Test]
    public async Task Rename_PersistsAndShowsToAFriendAtOnce()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await BefriendAsync(tom.Id, felix.Id);
        var felixSees = new Recorder(felixHub);

        var newName = await tomHub.InvokeAsync<string>("Rename", "Sir Fluff");

        Assert.That(newName, Is.EqualTo("Sir Fluff"));
        Assert.That(await felixSees.Next<Guid>("FriendsChanged", id => id == tom.Id), Is.EqualTo(tom.Id));
        var friend = (await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single();
        Assert.That(friend.DisplayName, Is.EqualTo("Sir Fluff"));

        // Persisted: read fresh from the database, not only kept in the hub's reply.
        var found = await Game.Get<IPlayerService>().FindAsync(tom.Id, CancellationToken.None);
        Assert.That(found?.DisplayName, Is.EqualTo("Sir Fluff"));
    }

    [Test]
    public async Task AFriendWhoseRoomLostTheirCat_ShowsOffline()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await BefriendAsync(tom.Id, felix.Id);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");
        Assert.That((await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single().Place?.WorldId, Is.EqualTo("siamese"));

        // The room forgot Tom's cat (its lease ran out, or the room's pod died and it came back empty): the room is the
        // truth, whatever Tom's session last recorded.
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        await grains.GetGrain<IRoomGrain>(RoomKey.For("siamese", "town-square")).Leave(tom.Id, tomHub.ConnectionId!);

        Assert.That((await felixHub.InvokeAsync<List<FriendView>>("GetFriends")).Single().Place, Is.Null);
    }

    [Test]
    public async Task OnlyFriendsCanBeGoneTo()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (_, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");

        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("GoToFriend", tom.Id));

        Assert.That(refused!.Message, Does.Contain("You can only go to your friends."));
    }

    [Test]
    public async Task GetPlayerCard_ShowsTheCatAndWhereTheyStand()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, _) = await Game.SignInGuestAsync();
        await using var _ = tomHub;

        var card = await tomHub.InvokeAsync<PlayerCardView>("GetPlayerCard", felix.Id);
        Assert.That(card, Is.EqualTo(new PlayerCardView(felix.Id, felix.DisplayName, felix.FurColor.ToString(), "None")));

        var self = Assert.ThrowsAsync<HubException>(() => tomHub.InvokeAsync<PlayerCardView>("AddFriend", tom.Id));
        Assert.That(self!.Message, Does.Contain("You can't be friends with yourself."));
        var nobody = Assert.ThrowsAsync<HubException>(() => tomHub.InvokeAsync<PlayerCardView>("GetPlayerCard", Guid.NewGuid()));
        Assert.That(nobody!.Message, Does.Contain("That cat doesn't exist."));
    }

    [Test]
    public async Task Request_Twice_IsRefused_AndAskingBackAccepts()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();

        await Friends().RequestAsync(tom, felix, CancellationToken.None);
        var twice = Assert.ThrowsAsync<DomainException>(() => Friends().RequestAsync(tom, felix, CancellationToken.None));
        Assert.That(twice!.Message, Is.EqualTo("You've already asked. Wait for an answer."));

        var back = await Friends().RequestAsync(felix, tom, CancellationToken.None);
        Assert.That(back.State, Is.EqualTo(FriendState.Friends));
        Assert.That((await Friends().GetAsync(tom, felix, CancellationToken.None))!.State, Is.EqualTo(FriendState.Friends));
    }

    [Test]
    public async Task BothAskingAtOnce_MakesThemFriends_WithOneFriendship()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();

        await Task.WhenAll(
            Friends().RequestAsync(tom, felix, CancellationToken.None),
            Friends().RequestAsync(felix, tom, CancellationToken.None));

        Assert.That((await Friends().ListAsync(tom, CancellationToken.None)).Single().State, Is.EqualTo(FriendState.Friends));
        Assert.That((await Friends().ListAsync(felix, CancellationToken.None)).Single().State, Is.EqualTo(FriendState.Friends));
    }

    [Test]
    public async Task Decline_ForgetsTheRequest_SoTheyCanAskAgain()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        await Friends().RequestAsync(tom, felix, CancellationToken.None);

        Assert.ThrowsAsync<DomainException>(() => Friends().DeclineAsync(tom, felix, CancellationToken.None),
            "only the one asked declines");
        var declined = await Friends().DeclineAsync(felix, tom, CancellationToken.None);

        Assert.That(declined.State, Is.EqualTo(FriendState.None));
        Assert.That(await Friends().ListAsync(tom, CancellationToken.None), Is.Empty);
        Assert.That((await Friends().RequestAsync(tom, felix, CancellationToken.None)).State, Is.EqualTo(FriendState.RequestSent));
    }

    [Test]
    public async Task CancelAndUnfriend_EndIt_ForBoth()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        await Friends().RequestAsync(tom, felix, CancellationToken.None);
        await Friends().CancelAsync(tom, felix, CancellationToken.None);
        Assert.That(await Friends().ListAsync(felix, CancellationToken.None), Is.Empty);

        await BefriendAsync(tom, felix);
        await Friends().UnfriendAsync(felix, tom, CancellationToken.None);

        Assert.That(await Friends().ListAsync(tom, CancellationToken.None), Is.Empty);
        Assert.ThrowsAsync<DomainException>(() => Friends().UnfriendAsync(tom, felix, CancellationToken.None));
    }

    [Test]
    public async Task List_PutsFriendsFirst_ThenRequestsToThePlayer_ThenTheirs()
    {
        var tom = await NewPlayerAsync();
        var friend = await NewPlayerAsync();
        var asker = await NewPlayerAsync();
        var asked = await NewPlayerAsync();
        await Friends().RequestAsync(tom, asked, CancellationToken.None);
        await Friends().RequestAsync(asker, tom, CancellationToken.None);
        await BefriendAsync(tom, friend);

        var list = await Friends().ListAsync(tom, CancellationToken.None);

        Assert.That(list.Select(r => (r.PlayerId, r.State)), Is.EqualTo(new[]
        {
            (friend, FriendState.Friends),
            (asker, FriendState.RequestReceived),
            (asked, FriendState.RequestSent),
        }));
    }

    [Test]
    public async Task Request_ToAPlayerWithTheMostRequestsWaiting_IsRefused()
    {
        var popular = await NewPlayerAsync();
        for (var i = 0; i < Friendship.MaxReceived; i++)
        {
            await Friends().RequestAsync(await NewPlayerAsync(), popular, CancellationToken.None);
        }

        var refused = Assert.ThrowsAsync<DomainException>(async () =>
            await Friends().RequestAsync(await NewPlayerAsync(), popular, CancellationToken.None));

        Assert.That(refused!.Message, Is.EqualTo("That cat has too many requests waiting. Try again later."));
    }

    [Test]
    public async Task Cancel_RacingAnAccept_DoesNotDeleteTheFriendship()
    {
        var tom = await NewPlayerAsync();
        var felix = await NewPlayerAsync();
        await Friends().RequestAsync(tom, felix, CancellationToken.None);

        // Tom takes his request back from the pending friendship he loaded; Felix accepts it just before Tom's delete.
        Game.BeforeSaving.Once(tom, () => Friends().AcceptAsync(felix, tom, CancellationToken.None));
        var refused = Assert.ThrowsAsync<DomainException>(() => Friends().CancelAsync(tom, felix, CancellationToken.None));

        Assert.That(refused!.Message, Is.EqualTo("That changed just now. Look again."));
        Assert.That((await Friends().GetAsync(tom, felix, CancellationToken.None))!.State, Is.EqualTo(FriendState.Friends));
    }

    [Test]
    public async Task RequestsToAPlayer_NeverStopThemAskingOthers()
    {
        // Many cats ask one: their requests fill only the slots for requests to that cat.
        var victim = await NewPlayerAsync();
        for (var i = 0; i < 100; i++)
        {
            try
            {
                await Friends().RequestAsync(await NewPlayerAsync(), victim, CancellationToken.None);
            }
            catch (DomainException)
            {
                // Refused once the victim has as many requests waiting as they can.
            }
        }

        var friend = await NewPlayerAsync();
        var asked = await Friends().RequestAsync(victim, friend, CancellationToken.None);

        Assert.That(asked.State, Is.EqualTo(FriendState.RequestSent));
    }

    [Test]
    public async Task Locate_ForAPlayerWhoIsOffline_DoesNotKeepTheirSessionActive()
    {
        var grains = Game.Services.GetRequiredService<IGrainFactory>();
        var offline = Guid.NewGuid();

        Assert.That(await grains.GetGrain<IPlayerSessionGrain>(offline).Locate(), Is.Null);

        // A friends list asks after every friend: the offline ones' sessions go again rather than wait to be collected.
        var management = grains.GetGrain<IManagementGrain>(0);
        await AssertEventually(async () => !(await management.GetDetailedGrainStatistics())
            .Any(s => s.GrainId.TryGetGuidKey(out var key, out _) && key == offline));
    }

    private static IFriendService Friends() => Game.Get<IFriendService>();

    private static async Task<Guid> NewPlayerAsync() =>
        (await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None)).Id;

    private static async Task BefriendAsync(Guid one, Guid other)
    {
        await Friends().RequestAsync(one, other, CancellationToken.None);
        await Friends().AcceptAsync(other, one, CancellationToken.None);
    }

    private static async Task AssertEventually(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > until)
            {
                Assert.Fail("The condition didn't hold within 10 s.");
            }

            await Task.Delay(100);
        }
    }
}
