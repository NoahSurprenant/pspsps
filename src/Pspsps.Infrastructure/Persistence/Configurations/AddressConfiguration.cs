using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class PlayerAddressConfiguration : IEntityTypeConfiguration<PlayerAddress>
{
    public void Configure(EntityTypeBuilder<PlayerAddress> builder)
    {
        builder.ToTable("player_addresses");
        // One per player and network: seen again updates it.
        builder.HasKey(a => new { a.PlayerId, a.AddressHash });
        // A keyed hash (64 hex digits), never an address.
        builder.Property(a => a.AddressHash).HasMaxLength(AddressHashes.Length).IsFixedLength();
        // How many players a network had lately; and the pruning of the old ones.
        builder.HasIndex(a => new { a.AddressHash, a.LastSeen });
        builder.HasIndex(a => a.LastSeen);
        // A player's networks go with them.
        builder.HasOne<Player>().WithMany().HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AddressBanConfiguration : IEntityTypeConfiguration<AddressBan>
{
    public void Configure(EntityTypeBuilder<AddressBan> builder)
    {
        builder.ToTable("address_bans");
        builder.HasKey(b => b.AddressHash);
        builder.Property(b => b.AddressHash).HasMaxLength(AddressHashes.Length).IsFixedLength().ValueGeneratedNever();
        builder.Property(b => b.Reason).HasMaxLength(PlayerModeration.MaxReasonLength).IsRequired();
        // A concurrency token: two admins at once are decided one after the other.
        builder.Property(b => b.Version).IsConcurrencyToken();
        builder.Ignore(b => b.Ban);
        builder.Ignore(b => b.Label);
        // Its audit lines are the banned player's (moderation_entries), added by the use case.
        builder.Ignore(b => b.Entries);
        // A banned player's networks, to lift with them; no foreign key, as for reports: the ban outlives the player.
        builder.HasIndex(b => b.PlayerId);
        builder.HasIndex(b => b.BannedUntil);
    }
}
