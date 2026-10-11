using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase5aTaxDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NominationBindings");

            migrationBuilder.CreateSequence(
                name: "TaxDocumentNumbers");

            migrationBuilder.AddColumn<Guid>(
                name: "IntroOfferId",
                table: "StallionSeasonSubscriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardFeeIncGst",
                table: "StallionSeasonSubscriptions",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Existing subscriptions: the fee charged was the standard fee (Waived rows hold 0).
            migrationBuilder.Sql("UPDATE [StallionSeasonSubscriptions] SET [StandardFeeIncGst] = [FeeIncGst];");

            migrationBuilder.AddColumn<string>(
                name: "BankAccountName",
                table: "PlatformSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "PlatformSettings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankBsb",
                table: "PlatformSettings",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAbn",
                table: "PlatformSettings",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress",
                table: "PlatformSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessEmail",
                table: "PlatformSettings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessLegalName",
                table: "PlatformSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoicePaymentTermsDays",
                table: "PlatformSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IntroOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountOffIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    StudFarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntroOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntroOffers_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IntroOffers_StudFarms_StudFarmId",
                        column: x => x.StudFarmId,
                        principalTable: "StudFarms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IntroOffers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IntroOffers_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TaxDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StandardAmountIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    IntroOfferName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DiscountIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AmountIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    AmountExGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SupplierAbn = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    SupplierAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SupplierEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RecipientAbn = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    RecipientAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BankAccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BankBsb = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    StudFarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuyerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxDocuments_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxDocuments_StallionSeasonSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "StallionSeasonSubscriptions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxDocuments_StudFarms_StudFarmId",
                        column: x => x.StudFarmId,
                        principalTable: "StudFarms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxDocuments_TaxDocuments_OriginalDocumentId",
                        column: x => x.OriginalDocumentId,
                        principalTable: "TaxDocuments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxDocuments_Users_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "PlatformSettings",
                keyColumn: "Id",
                keyValue: new Guid("5e771265-0000-4000-8000-000000000001"),
                columns: new[] { "BankAccountName", "BankAccountNumber", "BankBsb", "BusinessAbn", "BusinessAddress", "BusinessEmail", "BusinessLegalName", "InvoicePaymentTermsDays" },
                values: new object[] { null, null, null, null, null, null, null, 14 });

            migrationBuilder.CreateIndex(
                name: "IX_StallionSeasonSubscriptions_IntroOfferId",
                table: "StallionSeasonSubscriptions",
                column: "IntroOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroOffers_CreatedByUserId",
                table: "IntroOffers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroOffers_IsRetired_EndsAt",
                table: "IntroOffers",
                columns: new[] { "IsRetired", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntroOffers_SeasonId",
                table: "IntroOffers",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroOffers_StudFarmId",
                table: "IntroOffers",
                column: "StudFarmId");

            migrationBuilder.CreateIndex(
                name: "IX_IntroOffers_UpdatedByUserId",
                table: "IntroOffers",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_BuyerUserId",
                table: "TaxDocuments",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_Kind_IssuedAt",
                table: "TaxDocuments",
                columns: new[] { "Kind", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_Number",
                table: "TaxDocuments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_OriginalDocumentId",
                table: "TaxDocuments",
                column: "OriginalDocumentId",
                unique: true,
                filter: "[Kind] = 'AdjustmentNote'");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_PurchaseId",
                table: "TaxDocuments",
                column: "PurchaseId",
                unique: true,
                filter: "[Kind] = 'BuyerFeeInvoice'");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_StudFarmId",
                table: "TaxDocuments",
                column: "StudFarmId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxDocuments_SubscriptionId",
                table: "TaxDocuments",
                column: "SubscriptionId",
                unique: true,
                filter: "[Kind] = 'ListingFeeInvoice'");

            migrationBuilder.AddForeignKey(
                name: "FK_StallionSeasonSubscriptions_IntroOffers_IntroOfferId",
                table: "StallionSeasonSubscriptions",
                column: "IntroOfferId",
                principalTable: "IntroOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StallionSeasonSubscriptions_IntroOffers_IntroOfferId",
                table: "StallionSeasonSubscriptions");

            migrationBuilder.DropTable(
                name: "IntroOffers");

            migrationBuilder.DropTable(
                name: "TaxDocuments");

            migrationBuilder.DropIndex(
                name: "IX_StallionSeasonSubscriptions_IntroOfferId",
                table: "StallionSeasonSubscriptions");

            migrationBuilder.DropColumn(
                name: "IntroOfferId",
                table: "StallionSeasonSubscriptions");

            migrationBuilder.DropColumn(
                name: "StandardFeeIncGst",
                table: "StallionSeasonSubscriptions");

            migrationBuilder.DropColumn(
                name: "BankAccountName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BankBsb",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BusinessAbn",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BusinessAddress",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BusinessEmail",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "BusinessLegalName",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "InvoicePaymentTermsDays",
                table: "PlatformSettings");

            migrationBuilder.DropSequence(
                name: "TaxDocumentNumbers");

            migrationBuilder.CreateTable(
                name: "NominationBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BuyerSignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FarmSignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PdfBlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NominationBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NominationBindings_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NominationBindings_Users_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_NominationBindings_AcknowledgedByUserId",
                table: "NominationBindings",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NominationBindings_PurchaseId",
                table: "NominationBindings",
                column: "PurchaseId",
                unique: true);
        }
    }
}
