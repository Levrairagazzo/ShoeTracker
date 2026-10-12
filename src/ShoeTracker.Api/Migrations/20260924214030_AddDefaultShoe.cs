using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoeTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultShoe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultShoeId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultShoeId",
                table: "Users",
                column: "DefaultShoeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Shoes_DefaultShoeId",
                table: "Users",
                column: "DefaultShoeId",
                principalTable: "Shoes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Shoes_DefaultShoeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_DefaultShoeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultShoeId",
                table: "Users");
        }
    }
}
