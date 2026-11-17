using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <summary>The longest shop item ID the database keeps.</summary>
    private const int ItemIdLength = 32;

    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DisplayName).HasMaxLength(DisplayNameRules.MaxLength);
        builder.Property(p => p.FurColor).HasConversion<string>().HasMaxLength(16);
        // A concurrency token: a purchase decided on the coins it read is refused (and tried again) when they changed
        // meanwhile (another purchase, a catch), never saved over them. Catches add in one statement and don't check it.
        builder.Property(p => p.Coins).IsConcurrencyToken();
        builder.Property(p => p.Hat).HasMaxLength(ItemIdLength);
        builder.Property(p => p.Collar).HasMaxLength(ItemIdLength);
        // A concurrency token too: two changes of outfit at once are counted one after the other, never both as the same
        // version (a room keeps the newer of two by it).
        builder.Property(p => p.OutfitVersion).IsConcurrencyToken();
        // Whatever the code above does, coins never go below zero.
        builder.ToTable(t => t.HasCheckConstraint("ck_players_coins_not_negative", "coins >= 0"));

        builder.OwnsMany(p => p.Logins, login =>
        {
            login.ToTable("player_logins");
            login.WithOwner().HasForeignKey("PlayerId");
            login.HasKey(l => new { l.Provider, l.ProviderKey });
            login.Property(l => l.Provider).HasConversion<string>().HasMaxLength(16);
            login.Property(l => l.ProviderKey).HasMaxLength(128);
            // One login per provider per cat, also when two links race past the rule on Player.LinkLogin.
            login.HasIndex("PlayerId", nameof(PlayerLogin.Provider)).IsUnique();
        });
        builder.Navigation(p => p.Logins).HasField("_logins").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Items, item =>
        {
            item.ToTable("player_items");
            item.WithOwner().HasForeignKey("PlayerId");
            // One of each per cat, also when two tabs buy the same thing at once.
            item.HasKey("PlayerId", nameof(OwnedItem.ItemId));
            item.Property(i => i.ItemId).HasMaxLength(ItemIdLength);
        });
        builder.Navigation(p => p.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
