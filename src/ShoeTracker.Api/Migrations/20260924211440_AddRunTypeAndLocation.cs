using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoeTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunTypeAndLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRace",
                table: "Runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "StartLatitude",
                table: "Runs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartLongitude",
                table: "Runs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaceNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaKey = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceNames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceNames_AreaKey",
                table: "PlaceNames",
                column: "AreaKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceNames");

            migrationBuilder.DropColumn(
                name: "IsRace",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "StartLatitude",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "StartLongitude",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Runs");
        }
    }
}
