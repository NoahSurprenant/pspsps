using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddressBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "moderation_entries",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "address_bans",
                columns: table => new
                {
                    address_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    banned_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    banned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    lifted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lifted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_address_bans", x => x.address_hash);
                });

            migrationBuilder.CreateTable(
                name: "player_addresses",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_addresses", x => new { x.player_id, x.address_hash });
                    table.ForeignKey(
                        name: "fk_player_addresses_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_address_bans_banned_until",
                table: "address_bans",
                column: "banned_until");

            migrationBuilder.CreateIndex(
                name: "ix_address_bans_player_id",
                table: "address_bans",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_addresses_address_hash_last_seen",
                table: "player_addresses",
                columns: new[] { "address_hash", "last_seen" });

            migrationBuilder.CreateIndex(
                name: "ix_player_addresses_last_seen",
                table: "player_addresses",
                column: "last_seen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_bans");

            migrationBuilder.DropTable(
                name: "player_addresses");

            migrationBuilder.DropColumn(
                name: "address",
                table: "moderation_entries");
        }
    }
}
