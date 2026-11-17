using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DisplayName).HasMaxLength(DisplayNameRules.MaxLength);
        builder.Property(p => p.FurColor).HasConversion<string>().HasMaxLength(16);
        // PostgreSQL's row version (the xmin system column, no column of ours): a save of a player someone else saved since
        // it was read is refused, so two coin earnings at once can't overwrite each other.
        builder.Property<uint>("xmin").IsRowVersion();

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
    }
}
