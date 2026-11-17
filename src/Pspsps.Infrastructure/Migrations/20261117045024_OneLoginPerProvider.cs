using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OneLoginPerProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_player_logins_player_id",
                table: "player_logins");

            migrationBuilder.CreateIndex(
                name: "ix_player_logins_player_id_provider",
                table: "player_logins",
                columns: new[] { "player_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_player_logins_player_id_provider",
                table: "player_logins");

            migrationBuilder.CreateIndex(
                name: "ix_player_logins_player_id",
                table: "player_logins",
                column: "player_id");
        }
    }
}
