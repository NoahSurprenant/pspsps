using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Players;
using Pspsps.Domain.Players;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

public sealed class PlayerServiceTests
{
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task SignInWithLogin_CreatesThePlayerOnceAndFindsThemAfter()
    {
        var first = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "1001", "Mr. Whiskers!", CancellationToken.None);
        var second = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "1001", "renamed on discord", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.DisplayName, Is.EqualTo("Mr Whiskers"));
            Assert.That(first.IsGuest, Is.False);
            Assert.That(second.Id, Is.EqualTo(first.Id));
            Assert.That(second.DisplayName, Is.EqualTo("Mr Whiskers"));
        });
    }

    [Test]
    public async Task SignInWithLogin_TheSameKeyFromAnotherProviderIsAnotherPlayer()
    {
        var discord = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, "2002", "Tom", CancellationToken.None);
        var google = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Google, "2002", "Tom", CancellationToken.None);

        Assert.That(google.Id, Is.Not.EqualTo(discord.Id));
    }

    [Test]
    public async Task CreateGuest_IsSavedWithAGeneratedName()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var found = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.That(guest.IsGuest, Is.True);
        Assert.That(guest.DisplayName, Does.Match(@"^Kitty \d{4}$"));
        Assert.That(found, Is.EqualTo(guest));
    }

    [Test]
    public async Task LinkLogin_KeepsTheGuestsIdAndCoinsAndMakesThemAnAccount()
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 40);
        var key = NewKey();

        var linked = await Game.Get<IPlayerService>()
            .LinkLoginAsync(guest.Id, LoginProvider.Google, key, CancellationToken.None);
        var signedIn = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Google, key, "Someone Else", CancellationToken.None);
        var found = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(linked, Is.EqualTo(LinkLoginOutcome.Linked));
            Assert.That(found, Is.EqualTo(guest with { Coins = 40, IsGuest = false }));
            // Signing in with the login from anywhere now finds the same cat.
            Assert.That(signedIn, Is.EqualTo(found));
        });
    }

    [Test]
    public async Task LinkLogin_RefusesALoginAnotherPlayerHasAndChangesNothing()
    {
        var key = NewKey();
        var owner = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(guest.Id, LoginProvider.Discord, key, CancellationToken.None);
        var guestAfter = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);
        var ownerAfter = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.TakenByAnotherPlayer));
            Assert.That(guestAfter, Is.EqualTo(guest));
            Assert.That(ownerAfter, Is.EqualTo(owner));
        });
    }

    [Test]
    public async Task LinkLogin_ALoginThePlayerAlreadyHasIsLinked()
    {
        var key = NewKey();
        var player = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Tom", CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(player.Id, LoginProvider.Discord, key, CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.Linked));
    }

    [Test]
    public async Task LinkLogin_RefusesASecondLoginFromTheSameProvider()
    {
        var player = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, NewKey(), "Tom", CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(player.Id, LoginProvider.Discord, NewKey(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.Refused));
    }

    [TestCase(" ")]
    [TestCase("")]
    public async Task LinkLogin_AnEmptyProviderKeyIsInvalidNotRefused(string key)
    {
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);

        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(guest.Id, LoginProvider.Google, key, CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.InvalidLogin));
        Assert.That(await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None), Is.EqualTo(guest));
    }

    [Test]
    public async Task LinkLogin_ToAPlayerThatDoesNotExistIsNotFound()
    {
        var result = await Game.Get<IPlayerService>()
            .LinkLoginAsync(Guid.NewGuid(), LoginProvider.Google, NewKey(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.PlayerNotFound));
    }

    [Test]
    public async Task LinkLogin_ALoginTakenWhileLinking_IsTakenAndOnlyTheLinkIsForgotten()
    {
        // Another browser signs in with the same account between the owner check and the save: the save hits the
        // login's primary key. The interceptor puts that sign-in exactly there.
        var key = NewKey();
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var bystander = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await using var db = RacingContext(ct =>
            Game.Get<IPlayerService>().SignInWithLoginAsync(LoginProvider.Discord, key, "Quick Cat", ct));
        // Another change waiting in the same scope: the refused link mustn't take it with it.
        (await db.Players.SingleAsync(p => p.Id == bystander.Id)).EarnCoins(7);

        var result = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .LinkLoginAsync(guest.Id, LoginProvider.Discord, key, CancellationToken.None);
        var leftToSave = db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged)
            .Select(e => e.Entity).ToList();
        await db.SaveChangesAsync();
        var guestAfter = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);
        var bystanderAfter = await Game.Get<IPlayerService>().FindAsync(bystander.Id, CancellationToken.None);
        var owner = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, key, "Quick Cat", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.TakenByAnotherPlayer));
            Assert.That(leftToSave, Has.Count.EqualTo(1).And.All.InstanceOf<Player>()
                .And.All.Property(nameof(Player.Id)).EqualTo(bystander.Id));
            Assert.That(guestAfter, Is.EqualTo(guest));
            Assert.That(bystanderAfter?.Coins, Is.EqualTo(7));
            Assert.That(owner.Id, Is.Not.EqualTo(guest.Id));
        });
    }

    [Test]
    public async Task LinkLogin_TheSameLinkWonByItsOwnTwin_IsLinked()
    {
        // The same callback twice at once (a double request): the other one saves first.
        var key = NewKey();
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await using var db = RacingContext(async ct =>
            await Game.Get<IPlayerService>().LinkLoginAsync(guest.Id, LoginProvider.Google, key, ct));

        var result = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .LinkLoginAsync(guest.Id, LoginProvider.Google, key, CancellationToken.None);
        var guestAfter = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.That(result, Is.EqualTo(LinkLoginOutcome.Linked));
        Assert.That(guestAfter, Is.EqualTo(guest with { IsGuest = false }));
    }

    [Test]
    public async Task LinkLogin_TwoAccountsFromOneProviderAtOnce_OnlyOneIsLinked()
    {
        // Two tabs link the same guest to two different Discord accounts: both pass the in-memory rule, and the
        // database's one-login-per-provider index decides.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var first = NewKey();
        var second = NewKey();
        await using var db = RacingContext(async ct =>
            await Game.Get<IPlayerService>().LinkLoginAsync(guest.Id, LoginProvider.Discord, first, ct));

        var result = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .LinkLoginAsync(guest.Id, LoginProvider.Discord, second, CancellationToken.None);
        var withFirst = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, first, "Tom", CancellationToken.None);
        var withSecond = await Game.Get<IPlayerService>()
            .SignInWithLoginAsync(LoginProvider.Discord, second, "Tom", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.Refused));
            Assert.That(withFirst.Id, Is.EqualTo(guest.Id));
            Assert.That(withSecond.Id, Is.Not.EqualTo(guest.Id), "the second account was linked as well");
        });
    }

    [Test]
    public async Task SignInWithLogin_ALoginLinkedWhileSigningIn_SignsInToThatCat()
    {
        // A guest links the account while another browser signs in with it for the first time.
        var key = NewKey();
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await using var db = RacingContext(async ct =>
            await Game.Get<IPlayerService>().LinkLoginAsync(guest.Id, LoginProvider.Google, key, ct));

        var signedIn = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .SignInWithLoginAsync(LoginProvider.Google, key, "Tom", CancellationToken.None);

        Assert.That(signedIn, Is.EqualTo(guest with { IsGuest = false }));
        Assert.That(db.ChangeTracker.HasChanges(), Is.False, "the new player is still waiting to be saved");
    }

    [Test]
    public async Task EarnCoins_TwoEarningsForOnePlayerAtOnce_BothCount()
    {
        // Another tab (on another pod, say) pays the same cat just before this earning writes.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        await GiveCoinsAsync(guest.Id, 3);
        await using var db = RacingContext(async ct =>
            await Game.Get<IPlayerService>().EarnCoinsAsync(guest.Id, 5, ct));

        var paid = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .EarnCoinsAsync(guest.Id, 7, CancellationToken.None);
        var saved = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(paid.Coins, Is.EqualTo(15));
            Assert.That(saved?.Coins, Is.EqualTo(15), "one earning overwrote the other");
        });
    }

    [Test]
    public async Task LinkLogin_AndAPaymentAtTheSameMoment_BothSucceed()
    {
        // A catch is paid while the guest saves their cat: the link mustn't fail, nor undo the coins.
        var guest = await Game.Get<IPlayerService>().CreateGuestAsync(CancellationToken.None);
        var key = NewKey();
        await using var db = RacingContext(async ct =>
            await Game.Get<IPlayerService>().EarnCoinsAsync(guest.Id, 20, ct));

        var result = await new PlayerService(db, TimeProvider.System, Random.Shared)
            .LinkLoginAsync(guest.Id, LoginProvider.Google, key, CancellationToken.None);
        var saved = await Game.Get<IPlayerService>().FindAsync(guest.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LinkLoginOutcome.Linked));
            Assert.That(saved, Is.EqualTo(guest with { Coins = 20, IsGuest = false }));
        });
    }

    /// <summary>A provider user ID no other test uses: the tests share one database.</summary>
    internal static string NewKey() => Guid.NewGuid().ToString("N");

    internal static async Task GiveCoinsAsync(Guid playerId, int coins)
    {
        var db = Game.Get<IApplicationDbContext>();
        var player = await db.Players.SingleAsync(p => p.Id == playerId);
        player.EarnCoins(coins);
        await db.SaveChangesAsync();
    }

    internal static async Task DeleteAsync(Guid playerId)
    {
        var db = Game.Get<IApplicationDbContext>();
        db.Players.Remove(await db.Players.SingleAsync(p => p.Id == playerId));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A context of its own whose first write (a save, or an update statement such as ExecuteUpdate's) runs
    /// <paramref name="first"/> (in another context) just before.
    /// </summary>
    internal static PspspsDbContext RacingContext(Func<CancellationToken, Task> first)
    {
        var race = new JustBeforeTheFirstWrite(first);
        return new(new DbContextOptionsBuilder<PspspsDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(race, race.Statements)
            .Options);
    }

    private sealed class JustBeforeTheFirstWrite(Func<CancellationToken, Task> first) : SaveChangesInterceptor
    {
        private bool _done;

        /// <summary>Catches writes that don't go through SaveChanges.</summary>
        public DbCommandInterceptor Statements => new UpdateStatements(this);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await RunFirst(cancellationToken);
            return result;
        }

        private async Task RunFirst(CancellationToken cancellationToken)
        {
            if (!_done)
            {
                _done = true;
                await first(cancellationToken);
            }
        }

        private sealed class UpdateStatements(JustBeforeTheFirstWrite race) : DbCommandInterceptor
        {
            public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    await race.RunFirst(cancellationToken);
                }

                return result;
            }
        }
    }
}
