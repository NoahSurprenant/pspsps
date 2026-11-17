using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class HomeConfiguration : IEntityTypeConfiguration<Home>
{
    /// <summary>The longest shop item ID the database keeps (as for player_items).</summary>
    private const int ItemIdLength = 32;

    public void Configure(EntityTypeBuilder<Home> builder)
    {
        // One home per player, keyed by its owner: two tabs making the first change at once can't make two.
        builder.HasKey(h => h.OwnerId);
        builder.Property(h => h.OwnerId).ValueGeneratedNever();
        builder.Property(h => h.Visitors).HasConversion<string>().HasMaxLength(16);
        // A concurrency token: a change decided on the home it read (where the furniture stands) is refused, and decided
        // again, when another change landed meanwhile, never saved over it.
        builder.Property(h => h.Version).IsConcurrencyToken();
        // A player's home goes with them.
        builder.HasOne<Player>().WithOne().HasForeignKey<Home>(h => h.OwnerId).OnDelete(DeleteBehavior.Cascade);

        builder.OwnsMany(h => h.Furniture, piece =>
        {
            piece.ToTable("home_furniture", t => t.HasCheckConstraint(
                "ck_home_furniture_quarter_turns", "quarter_turns >= 0 AND quarter_turns < 4"));
            piece.WithOwner().HasForeignKey("HomeOwnerId");
            piece.HasKey(p => p.Id);
            piece.Property(p => p.Id).ValueGeneratedNever();
            piece.Property(p => p.ItemId).HasMaxLength(ItemIdLength);
            piece.Ignore(p => p.Spot);
        });
        builder.Navigation(h => h.Furniture).HasField("_furniture").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
