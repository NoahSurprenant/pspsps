using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Players;
using Pspsps.Domain.Safety;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class MuteConfiguration : IEntityTypeConfiguration<Mute>
{
    public void Configure(EntityTypeBuilder<Mute> builder)
    {
        // The pair is the key: one mute per pair, also when two tabs mute at once. Its index finds a player's mutes.
        builder.HasKey(m => new { m.MuterId, m.MutedId });

        // A player's mutes go with them, and so do the mutes of them.
        builder.HasOne<Player>().WithMany().HasForeignKey(m => m.MuterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Player>().WithMany().HasForeignKey(m => m.MutedId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.MutedId);

        builder.ToTable(t => t.HasCheckConstraint("ck_mutes_not_self", "muter_id <> muted_id"));
    }
}
