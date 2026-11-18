using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Moderation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_moderation",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ban_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    banned_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    banned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    mute_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    muted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    muted_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    muted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    warnings_seen_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_moderation", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_player_moderation_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "moderation_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    by_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_entries_player_moderation_player_id",
                        column: x => x.player_id,
                        principalTable: "player_moderation",
                        principalColumn: "player_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_entries_at",
                table: "moderation_entries",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_entries_player_id_at",
                table: "moderation_entries",
                columns: new[] { "player_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_entries");

            migrationBuilder.DropTable(
                name: "player_moderation");
        }
    }
}
