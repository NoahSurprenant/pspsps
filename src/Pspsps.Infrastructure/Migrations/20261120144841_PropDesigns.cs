using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PropDesigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prop_designs",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<double>(type: "double precision", nullable: false),
                    parts = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prop_designs", x => x.kind);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prop_designs");
        }
    }
}
