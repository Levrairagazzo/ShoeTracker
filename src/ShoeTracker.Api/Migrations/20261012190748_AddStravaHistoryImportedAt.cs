using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoeTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStravaHistoryImportedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoryImportedAt",
                table: "StravaConnections",
                type: "TEXT",
                nullable: true);

            // Connections that already imported runs have their history in.
            migrationBuilder.Sql("""
                UPDATE StravaConnections SET HistoryImportedAt = ConnectedAt
                WHERE EXISTS (SELECT 1 FROM Runs WHERE Runs.UserId = StravaConnections.UserId AND Runs.Source = 'Strava');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StravaConnections_AthleteId",
                table: "StravaConnections",
                column: "AthleteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StravaConnections_AthleteId",
                table: "StravaConnections");

            migrationBuilder.DropColumn(
                name: "HistoryImportedAt",
                table: "StravaConnections");
        }
    }
}
