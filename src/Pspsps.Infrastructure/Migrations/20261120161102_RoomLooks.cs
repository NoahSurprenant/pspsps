using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoomLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "look_variants",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sky = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    wall_colour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_look_variants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "room_looks",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    look = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_looks", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "look_variants");

            migrationBuilder.DropTable(
                name: "room_looks");
        }
    }
}
