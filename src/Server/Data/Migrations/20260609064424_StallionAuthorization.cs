using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class StallionAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StudDirectories_ArionStudId",
                table: "StudDirectories",
                column: "ArionStudId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionDirectories_ArionId",
                table: "StallionDirectories",
                column: "ArionId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionDirectories_StallionId",
                table: "StallionDirectories",
                column: "StallionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudDirectories_ArionStudId",
                table: "StudDirectories");

            migrationBuilder.DropIndex(
                name: "IX_StallionDirectories_ArionId",
                table: "StallionDirectories");

            migrationBuilder.DropIndex(
                name: "IX_StallionDirectories_StallionId",
                table: "StallionDirectories");
        }
    }
}
