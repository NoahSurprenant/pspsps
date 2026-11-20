using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class RoomVersionConfiguration : IEntityTypeConfiguration<RoomVersion>
{
    public void Configure(EntityTypeBuilder<RoomVersion> builder)
    {
        builder.ToTable("room_versions", t => t.HasCheckConstraint("ck_room_versions_number", "number >= 1"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.RoomId).HasMaxLength(RoomOverlayConfiguration.IdLength);
        builder.Property(v => v.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(v => v.Layout).HasColumnType("jsonb")
            .HasConversion(RoomJson.Converter<RoomLayout>(), RoomJson.Comparer<RoomLayout>());
        builder.HasIndex(v => new { v.RoomId, v.Number }).IsUnique();
        // A room has at most one published version, whichever pods publish at once.
        builder.HasIndex(v => v.RoomId).IsUnique().HasFilter("state = 'Published'").HasDatabaseName("ux_room_versions_published");
        builder.HasOne<Player>().WithMany().HasForeignKey(v => v.CreatedBy).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class RoomOverlayConfiguration : IEntityTypeConfiguration<RoomOverlayEntry>
{
    public const int IdLength = 64;

    public void Configure(EntityTypeBuilder<RoomOverlayEntry> builder)
    {
        builder.ToTable("room_overlays");
        builder.HasKey(o => new { o.RoomId, o.Id });
        builder.Property(o => o.RoomId).HasMaxLength(IdLength);
        builder.Property(o => o.Id).HasMaxLength(IdLength);
        builder.Property(o => o.Name).HasMaxLength(80);
        builder.Property(o => o.Look).HasMaxLength(32);
        builder.Property(o => o.Adds).HasColumnType("jsonb")
            .HasConversion(RoomJson.Converter<IReadOnlyList<Prop>>(), RoomJson.Comparer<IReadOnlyList<Prop>>());
        builder.Property(o => o.Removes).HasColumnType("jsonb")
            .HasConversion(RoomJson.Converter<IReadOnlyList<Prop>>(), RoomJson.Comparer<IReadOnlyList<Prop>>());
    }
}

internal sealed class PropDesignConfiguration : IEntityTypeConfiguration<PropDesign>
{
    public void Configure(EntityTypeBuilder<PropDesign> builder)
    {
        builder.ToTable("prop_designs");
        builder.HasKey(d => d.Kind);
        builder.Property(d => d.Kind).HasMaxLength(RoomOverlayConfiguration.IdLength);
        builder.Property(d => d.Parts).HasColumnType("jsonb")
            .HasConversion(RoomJson.Converter<IReadOnlyList<PropPart>>(), RoomJson.Comparer<IReadOnlyList<PropPart>>());
        builder.Property(d => d.Growth).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(RoomJson.Converter<IReadOnlyList<PropGrowth>>(), RoomJson.Comparer<IReadOnlyList<PropGrowth>>());
    }
}

internal sealed class RoomLookConfiguration : IEntityTypeConfiguration<RoomLookEntry>
{
    public void Configure(EntityTypeBuilder<RoomLookEntry> builder)
    {
        builder.ToTable("room_looks");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasMaxLength(RoomOverlayConfiguration.IdLength);
        builder.Property(l => l.Look).HasColumnType("jsonb")
            .HasConversion(RoomJson.Converter<RoomLook>(), RoomJson.Comparer<RoomLook>());
    }
}

internal sealed class LookVariantConfiguration : IEntityTypeConfiguration<LookVariantEntry>
{
    public void Configure(EntityTypeBuilder<LookVariantEntry> builder)
    {
        builder.ToTable("look_variants");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasMaxLength(32);
        builder.Property(v => v.Sky).HasMaxLength(7);
        builder.Property(v => v.WallColour).HasMaxLength(7);
    }
}
