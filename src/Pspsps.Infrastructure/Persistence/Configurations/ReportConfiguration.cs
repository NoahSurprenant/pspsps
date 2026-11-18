using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pspsps.Domain.Players;
using Pspsps.Domain.Safety;

namespace Pspsps.Infrastructure.Persistence.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ReportedName).HasMaxLength(DisplayNameRules.MaxLength);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Note).HasMaxLength(Report.MaxNoteLength);
        builder.Property(r => r.WorldId).HasMaxLength(64);
        builder.Property(r => r.Room).HasMaxLength(128);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);

        // No foreign keys to the players: a report stays for review when either player is deleted (it keeps the reported
        // cat's name, and its lines their speakers'), and deleting a player is never held up by reports.

        // A reporter's reports lately (the limits); reports of a player; and the open ones, oldest first, to review.
        builder.HasIndex(r => new { r.ReporterId, r.FiledAt });
        builder.HasIndex(r => r.ReportedId);
        builder.HasIndex(r => new { r.Status, r.FiledAt });

        builder.ToTable(t => t.HasCheckConstraint("ck_reports_not_self", "reporter_id <> reported_id"));

        builder.OwnsMany(r => r.Context, line =>
        {
            line.ToTable("report_lines");
            line.WithOwner().HasForeignKey("ReportId");
            line.HasKey("ReportId", nameof(ReportedLine.Position));
            line.Property(l => l.Position).ValueGeneratedNever();
            line.Property(l => l.DisplayName).HasMaxLength(DisplayNameRules.MaxLength);
        });
        builder.Navigation(r => r.Context).HasField("_context").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
