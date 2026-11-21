using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pspsps.Infrastructure.Persistence;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <summary>A seasonal look may carry a floor of its own (a frozen rink), so its painter and colours are kept beside its sky.</summary>
    [DbContext(typeof(PspspsDbContext))]
    [Migration("20261121100000_VariantFloors")]
    public partial class VariantFloors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "floor_accent",
                table: "look_variants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "floor_colour",
                table: "look_variants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "floor_painter",
                table: "look_variants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "floor_accent",
                table: "look_variants");

            migrationBuilder.DropColumn(
                name: "floor_colour",
                table: "look_variants");

            migrationBuilder.DropColumn(
                name: "floor_painter",
                table: "look_variants");
        }
    }
}
