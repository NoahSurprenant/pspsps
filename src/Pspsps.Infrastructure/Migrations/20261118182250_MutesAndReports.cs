using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pspsps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MutesAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mutes",
                columns: table => new
                {
                    muter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    muted_id = table.Column<Guid>(type: "uuid", nullable: false),
                    muted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mutes", x => new { x.muter_id, x.muted_id });
                    table.CheckConstraint("ck_mutes_not_self", "muter_id <> muted_id");
                    table.ForeignKey(
                        name: "fk_mutes_players_muted_id",
                        column: x => x.muted_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mutes_players_muter_id",
                        column: x => x.muter_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    world_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    room = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    filed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.CheckConstraint("ck_reports_not_self", "reporter_id <> reported_id");
                    table.ForeignKey(
                        name: "fk_reports_players_reported_id",
                        column: x => x.reported_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reports_players_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_lines",
                columns: table => new
                {
                    position = table.Column<int>(type: "integer", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_lines", x => new { x.report_id, x.position });
                    table.ForeignKey(
                        name: "fk_report_lines_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mutes_muted_id",
                table: "mutes",
                column: "muted_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reported_id",
                table: "reports",
                column: "reported_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_id_filed_at",
                table: "reports",
                columns: new[] { "reporter_id", "filed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_filed_at",
                table: "reports",
                columns: new[] { "status", "filed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mutes");

            migrationBuilder.DropTable(
                name: "report_lines");

            migrationBuilder.DropTable(
                name: "reports");
        }
    }
}
