using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledPublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "go_live_at",
                table: "room_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scheduled_by",
                table: "room_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_versions_go_live_at",
                table: "room_versions",
                column: "go_live_at",
                filter: "go_live_at is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_room_versions_go_live_at",
                table: "room_versions");

            migrationBuilder.DropColumn(
                name: "go_live_at",
                table: "room_versions");

            migrationBuilder.DropColumn(
                name: "scheduled_by",
                table: "room_versions");
        }
    }
}
