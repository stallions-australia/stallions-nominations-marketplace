using System;
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
            migrationBuilder.AddColumn<Guid>(
                name: "StudDirectoryId",
                table: "StudFarms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StallionDirectoryId",
                table: "Stallions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StudDirectories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArionStudId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Website = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Town = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudDirectories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StallionDirectories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StallionId = table.Column<int>(type: "int", nullable: false),
                    ArionId = table.Column<int>(type: "int", nullable: false),
                    StudDirectoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    YearOfBirth = table.Column<int>(type: "int", nullable: false),
                    Colour = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Height = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SireName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StallionDirectories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StallionDirectories_StudDirectories_StudDirectoryId",
                        column: x => x.StudDirectoryId,
                        principalTable: "StudDirectories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudFarms_StudDirectoryId",
                table: "StudFarms",
                column: "StudDirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Stallions_StallionDirectoryId",
                table: "Stallions",
                column: "StallionDirectoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionDirectories_StudDirectoryId",
                table: "StallionDirectories",
                column: "StudDirectoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stallions_StallionDirectories_StallionDirectoryId",
                table: "Stallions",
                column: "StallionDirectoryId",
                principalTable: "StallionDirectories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StudFarms_StudDirectories_StudDirectoryId",
                table: "StudFarms",
                column: "StudDirectoryId",
                principalTable: "StudDirectories",
                principalColumn: "Id");

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
            migrationBuilder.DropForeignKey(
                name: "FK_Stallions_StallionDirectories_StallionDirectoryId",
                table: "Stallions");

            migrationBuilder.DropForeignKey(
                name: "FK_StudFarms_StudDirectories_StudDirectoryId",
                table: "StudFarms");

            migrationBuilder.DropTable(
                name: "StallionDirectories");

            migrationBuilder.DropTable(
                name: "StudDirectories");

            migrationBuilder.DropIndex(
                name: "IX_StudFarms_StudDirectoryId",
                table: "StudFarms");

            migrationBuilder.DropIndex(
                name: "IX_Stallions_StallionDirectoryId",
                table: "Stallions");

            migrationBuilder.DropColumn(
                name: "StudDirectoryId",
                table: "StudFarms");

            migrationBuilder.DropColumn(
                name: "StallionDirectoryId",
                table: "Stallions");
        }
    }
}
