using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase3AuctionClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pending purchases left by the removed interim checkout must never be charged.
            migrationBuilder.Sql("UPDATE Purchases SET Status = 'Voided' WHERE Status = 'Pending';");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_BidId",
                table: "Purchases");

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargeAttemptStartedAt",
                table: "Purchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChargeAttempts",
                table: "Purchases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargeDueBy",
                table: "Purchases",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Purchases",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "LastChargeFailure",
                table: "Purchases",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RetryRequested",
                table: "Purchases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "Listings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Listings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "OutboundEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Template = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_BidId",
                table: "Purchases",
                column: "BidId",
                unique: true,
                filter: "[BidId] IS NOT NULL AND [Status] <> 'Voided'");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_Status_ChargeDueBy",
                table: "Purchases",
                columns: new[] { "Status", "ChargeDueBy" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmails_SentAt_FailedAt_NextAttemptAt",
                table: "OutboundEmails",
                columns: new[] { "SentAt", "FailedAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundEmails");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_BidId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_Status_ChargeDueBy",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargeAttemptStartedAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargeAttempts",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargeDueBy",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LastChargeFailure",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RetryRequested",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Listings");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_BidId",
                table: "Purchases",
                column: "BidId");
        }
    }
}
