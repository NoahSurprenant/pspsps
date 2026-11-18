using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class PlayerModerationConfiguration : IEntityTypeConfiguration<PlayerModeration>
{
    public void Configure(EntityTypeBuilder<PlayerModeration> builder)
    {
        builder.ToTable("player_moderation");
        // One per player, keyed by them: two admins acting first at once can't make two.
        builder.HasKey(m => m.PlayerId);
        builder.Property(m => m.PlayerId).ValueGeneratedNever();
        builder.Property(m => m.BanReason).HasMaxLength(PlayerModeration.MaxReasonLength);
        builder.Property(m => m.MuteReason).HasMaxLength(PlayerModeration.MaxReasonLength);
        builder.Property(m => m.Warning).HasMaxLength(PlayerModeration.MaxReasonLength);
        // A concurrency token: a change decided on what it read is refused, and decided again, when another landed
        // meanwhile.
        builder.Property(m => m.Version).IsConcurrencyToken();
        builder.Ignore(m => m.Ban);
        builder.Ignore(m => m.Mute);
        // A player's moderation goes with them.
        builder.HasOne<Player>().WithOne().HasForeignKey<PlayerModeration>(m => m.PlayerId).OnDelete(DeleteBehavior.Cascade);

        // The audit trail: its own rows, not owned, so reading a player's standing (on every hub call) doesn't read it.
        builder.HasMany(m => m.Entries).WithOne().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ModerationEntryConfiguration : IEntityTypeConfiguration<ModerationEntry>
{
    public void Configure(EntityTypeBuilder<ModerationEntry> builder)
    {
        builder.ToTable("moderation_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Reason).HasMaxLength(PlayerModeration.MaxReasonLength);
        // The admin page reads the latest lines.
        builder.HasIndex(e => e.At);
        builder.HasIndex(e => new { e.PlayerId, e.At });
    }
}
