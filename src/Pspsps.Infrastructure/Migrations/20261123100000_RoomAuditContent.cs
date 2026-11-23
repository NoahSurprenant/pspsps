using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pspsps.Infrastructure.Persistence;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <summary>Each saved draft keeps the room as it left it, so a room's history can show every change and put one back.</summary>
    [DbContext(typeof(PspspsDbContext))]
    [Migration("20261123100000_RoomAuditContent")]
    public partial class RoomAuditContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "room_audit",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_audit_target",
                table: "room_audit",
                column: "target");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_room_audit_target",
                table: "room_audit");

            migrationBuilder.DropColumn(
                name: "content",
                table: "room_audit");
        }
    }
}
