using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameEntraObjectIdToObjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EntraObjectId",
                table: "Users",
                newName: "ObjectId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_EntraObjectId",
                table: "Users",
                newName: "IX_Users_ObjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ObjectId",
                table: "Users",
                newName: "EntraObjectId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_ObjectId",
                table: "Users",
                newName: "IX_Users_EntraObjectId");
        }
    }
}
