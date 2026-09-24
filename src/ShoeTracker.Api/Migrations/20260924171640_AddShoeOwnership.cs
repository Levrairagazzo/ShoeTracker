using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoeTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShoeOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Shoes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Shoes created before per-user scoping have no owner; assign them to the
            // original (lowest-Id) account, which is the single seeded admin user.
            migrationBuilder.Sql("UPDATE Shoes SET UserId = (SELECT MIN(Id) FROM Users) WHERE EXISTS (SELECT 1 FROM Users);");

            migrationBuilder.CreateIndex(
                name: "IX_Shoes_UserId",
                table: "Shoes",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shoes_Users_UserId",
                table: "Shoes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shoes_Users_UserId",
                table: "Shoes");

            migrationBuilder.DropIndex(
                name: "IX_Shoes_UserId",
                table: "Shoes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Shoes");
        }
    }
}
