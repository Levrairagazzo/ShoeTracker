using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoeTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunOwnershipAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ShoeId",
                table: "Runs",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<long>(
                name: "StravaActivityId",
                table: "Runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Until now every run belonged to a shoe, so its owner is the shoe's owner.
            migrationBuilder.Sql("UPDATE Runs SET UserId = (SELECT Shoes.UserId FROM Shoes WHERE Shoes.Id = Runs.ShoeId);");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_UserId_StravaActivityId",
                table: "Runs",
                columns: new[] { "UserId", "StravaActivityId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_Users_UserId",
                table: "Runs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ShoeId becomes required again, so unassigned runs can't be kept.
            migrationBuilder.Sql("DELETE FROM Runs WHERE ShoeId IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Runs_Users_UserId",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Runs_UserId_StravaActivityId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "StravaActivityId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Runs");

            migrationBuilder.AlterColumn<int>(
                name: "ShoeId",
                table: "Runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
