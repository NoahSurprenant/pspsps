using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pspsps.Infrastructure.Persistence;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <summary>A room's draft may carry the overlays that go live with it, so an overlay never goes live before the layout it was written for.</summary>
    [DbContext(typeof(PspspsDbContext))]
    [Migration("20261126230000_RoomVersionOverlays")]
    public partial class RoomVersionOverlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "overlays",
                table: "room_versions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "overlays",
                table: "room_versions");
        }
    }
}
