using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Rooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "room_overlays",
                columns: table => new
                {
                    room_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    starts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    yearly = table.Column<bool>(type: "boolean", nullable: false),
                    look = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    adds = table.Column<string>(type: "jsonb", nullable: false),
                    removes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_overlays", x => new { x.room_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "room_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    layout = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_versions", x => x.id);
                    table.CheckConstraint("ck_room_versions_number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_room_versions_players_created_by",
                        column: x => x.created_by,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_versions_created_by",
                table: "room_versions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_room_versions_room_id_number",
                table: "room_versions",
                columns: new[] { "room_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_room_versions_published",
                table: "room_versions",
                column: "room_id",
                unique: true,
                filter: "state = 'Published'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_overlays");

            migrationBuilder.DropTable(
                name: "room_versions");
        }
    }
}
