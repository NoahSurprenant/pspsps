using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Friends;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        // The pair, in a fixed order, is the key: one friendship per pair, also when both ask at once.
        builder.HasKey(f => new { f.FirstPlayerId, f.SecondPlayerId });
        // A concurrency token: an accept, decline or removal decided on the status it read can't overwrite or delete a
        // friendship whose status changed meanwhile.
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
        builder.Ignore(f => f.AddresseeId);

        // A player's friendships go with them.
        builder.HasOne<Player>().WithMany().HasForeignKey(f => f.FirstPlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Player>().WithMany().HasForeignKey(f => f.SecondPlayerId).OnDelete(DeleteBehavior.Cascade);

        // The key's index finds a player's friendships as the first of the pair; this one as the second.
        builder.HasIndex(f => f.SecondPlayerId);

        // The requester is one of the two.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_friendships_requester_in_pair", "requester_id = first_player_id OR requester_id = second_player_id"));
    }
}
