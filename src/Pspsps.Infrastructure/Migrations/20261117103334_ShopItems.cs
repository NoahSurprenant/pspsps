using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShopItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "collar",
                table: "players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hat",
                table: "players",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "outfit_version",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "player_items",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_items", x => new { x.player_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_player_items_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every cat owns the coat it has now (new cats get theirs from Player): it can always go back to it after
            // wearing another. Fur is saved as its name ("Orange"); the shop's item is "fur-orange".
            migrationBuilder.Sql(
                "INSERT INTO player_items (player_id, item_id, at) " +
                "SELECT id, 'fur-' || lower(fur_color), created_at FROM players;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_players_coins_not_negative",
                table: "players",
                sql: "coins >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_players_coins_not_negative",
                table: "players");

            migrationBuilder.DropColumn(
                name: "collar",
                table: "players");

            migrationBuilder.DropColumn(
                name: "hat",
                table: "players");

            migrationBuilder.DropColumn(
                name: "outfit_version",
                table: "players");
        }
    }
}
