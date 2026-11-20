using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LookRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "painter_recipes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    surface = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_painter_recipes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wildlife_recipes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    parts = table.Column<string>(type: "jsonb", nullable: false),
                    motion = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wildlife_recipes", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "painter_recipes");

            migrationBuilder.DropTable(
                name: "wildlife_recipes");
        }
    }
}
