using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Homes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "count",
                table: "player_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "homes",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitors = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_homes", x => x.owner_id);
                    table.ForeignKey(
                        name: "fk_homes_players_owner_id",
                        column: x => x.owner_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "home_furniture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    z = table.Column<double>(type: "double precision", nullable: false),
                    quarter_turns = table.Column<int>(type: "integer", nullable: false),
                    home_owner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_home_furniture", x => x.id);
                    table.CheckConstraint("ck_home_furniture_quarter_turns", "quarter_turns >= 0 AND quarter_turns < 4");
                    table.ForeignKey(
                        name: "fk_home_furniture_homes_home_owner_id",
                        column: x => x.home_owner_id,
                        principalTable: "homes",
                        principalColumn: "owner_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every cat has the one of whatever it had (the column starts at 1), and, like the cats born from now on
            // (Player), its own bed for its home.
            migrationBuilder.Sql(
                "INSERT INTO player_items (player_id, item_id, at, count) " +
                "SELECT id, 'cat-bed', created_at, 1 FROM players ON CONFLICT DO NOTHING;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_player_items_count_positive",
                table: "player_items",
                sql: "count >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_home_furniture_home_owner_id",
                table: "home_furniture",
                column: "home_owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "home_furniture");

            migrationBuilder.DropTable(
                name: "homes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_player_items_count_positive",
                table: "player_items");

            migrationBuilder.DropColumn(
                name: "count",
                table: "player_items");
        }
    }
}
