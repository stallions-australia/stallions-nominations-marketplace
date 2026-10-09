using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase2Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentCustomerId",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentCustomerProvider",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingCheckoutExpiresAt",
                table: "StallionSeasonSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingCheckoutUrl",
                table: "StallionSeasonSubscriptions",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessedPaymentEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedPaymentEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "SavedCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProviderPaymentMethodId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ExpMonth = table.Column<int>(type: "int", nullable: false),
                    ExpYear = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedCards_UserId",
                table: "SavedCards",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedPaymentEvents");

            migrationBuilder.DropTable(
                name: "SavedCards");

            migrationBuilder.DropColumn(
                name: "PaymentCustomerId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PaymentCustomerProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PendingCheckoutExpiresAt",
                table: "StallionSeasonSubscriptions");

            migrationBuilder.DropColumn(
                name: "PendingCheckoutUrl",
                table: "StallionSeasonSubscriptions");
        }
    }
}
