using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pspsps.Infrastructure.Persistence;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <summary>A room's version may carry the look that goes live with it, so a look is drafted, scheduled, published and rolled back with its layout.</summary>
    [DbContext(typeof(PspspsDbContext))]
    [Migration("20261122100000_RoomVersionLooks")]
    public partial class RoomVersionLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "look",
                table: "room_versions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "look",
                table: "room_versions");
        }
    }
}
