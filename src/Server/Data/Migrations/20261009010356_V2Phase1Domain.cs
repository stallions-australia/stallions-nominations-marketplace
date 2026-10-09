using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase1Domain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fixed-price listings no longer exist (v2). Delete them and every row that depends
            // on them BEFORE dropping the TPT child table, so no foreign key blocks the drop.
            // Dev data only — prod was confirmed to hold no real data on 2026-10-09.
            migrationBuilder.Sql(@"
DECLARE @FixedPriceIds TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT INTO @FixedPriceIds (Id)
    SELECT Id FROM FixedPriceListings
    UNION
    SELECT Id FROM Listings WHERE ListingType = 'FixedPrice';

DELETE nb FROM NominationBindings nb
    JOIN Purchases p ON p.Id = nb.PurchaseId
    WHERE p.ListingId IN (SELECT Id FROM @FixedPriceIds);
DELETE FROM Purchases WHERE ListingId IN (SELECT Id FROM @FixedPriceIds);
DELETE m FROM EnquiryMessages m
    JOIN Enquiries e ON e.Id = m.EnquiryId
    WHERE e.ListingId IN (SELECT Id FROM @FixedPriceIds);
DELETE FROM Enquiries WHERE ListingId IN (SELECT Id FROM @FixedPriceIds);
DELETE FROM FixedPriceListings WHERE Id IN (SELECT Id FROM @FixedPriceIds);
DELETE FROM Listings WHERE Id IN (SELECT Id FROM @FixedPriceIds);
");

            migrationBuilder.DropTable(
                name: "FixedPriceListings");

            migrationBuilder.DropColumn(
                name: "MareBreed",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "MareName",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "MareRegistration",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PlatformFeePercent",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "StartingPrice",
                table: "AuctionListings");

            migrationBuilder.RenameColumn(
                name: "PlatformFeeIncGst",
                table: "Purchases",
                newName: "BuyerFeeIncGst");

            migrationBuilder.RenameColumn(
                name: "PlatformFeeGst",
                table: "Purchases",
                newName: "BuyerFeeGst");

            migrationBuilder.RenameColumn(
                name: "PlatformFeeExGst",
                table: "Purchases",
                newName: "BuyerFeeExGst");

            migrationBuilder.AddColumn<decimal>(
                name: "BalancePayableToStudIncGst",
                table: "Purchases",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Existing purchases: the fee came out of the price, so the stud's balance is the remainder.
            migrationBuilder.Sql("UPDATE Purchases SET BalancePayableToStudIncGst = TotalPriceIncGst - BuyerFeeIncGst;");

            migrationBuilder.AddColumn<decimal>(
                name: "BuyerFeeIncGst",
                table: "Listings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerFeeIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StandardListingFeeIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MinimumBidIncrement = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ChargeGracePeriodHours = table.Column<int>(type: "int", nullable: false),
                    OfferExpiryDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StallionSeasonSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StallionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudFarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeeIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FeeExGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WaiverReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StallionSeasonSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StallionSeasonSubscriptions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StallionSeasonSubscriptions_Stallions_StallionId",
                        column: x => x.StallionId,
                        principalTable: "Stallions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StallionSeasonSubscriptions_StudFarms_StudFarmId",
                        column: x => x.StudFarmId,
                        principalTable: "StudFarms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StallionSeasonSubscriptions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "PlatformSettings",
                columns: new[] { "Id", "BuyerFeeIncGst", "ChargeGracePeriodHours", "MinimumBidIncrement", "OfferExpiryDays", "StandardListingFeeIncGst", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("5e771265-0000-4000-8000-000000000001"), 150m, 2, 25m, 7, 990m, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettings_UpdatedByUserId",
                table: "PlatformSettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionSeasonSubscriptions_CreatedByUserId",
                table: "StallionSeasonSubscriptions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionSeasonSubscriptions_SeasonId",
                table: "StallionSeasonSubscriptions",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_StallionSeasonSubscriptions_StallionId_SeasonId",
                table: "StallionSeasonSubscriptions",
                columns: new[] { "StallionId", "SeasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StallionSeasonSubscriptions_StudFarmId_SeasonId",
                table: "StallionSeasonSubscriptions",
                columns: new[] { "StudFarmId", "SeasonId" });

            // Listings already published before v2 snapshot the buyer fee now, from the settings
            // row seeded above (no fee amount is repeated here), so their auctions accept bids.
            migrationBuilder.Sql(@"
UPDATE Listings
SET BuyerFeeIncGst = (SELECT BuyerFeeIncGst FROM PlatformSettings WHERE Id = '5e771265-0000-4000-8000-000000000001')
WHERE PublishedAt IS NOT NULL AND BuyerFeeIncGst IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "StallionSeasonSubscriptions");

            migrationBuilder.DropColumn(
                name: "BalancePayableToStudIncGst",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "BuyerFeeIncGst",
                table: "Listings");

            migrationBuilder.RenameColumn(
                name: "BuyerFeeIncGst",
                table: "Purchases",
                newName: "PlatformFeeIncGst");

            migrationBuilder.RenameColumn(
                name: "BuyerFeeGst",
                table: "Purchases",
                newName: "PlatformFeeGst");

            migrationBuilder.RenameColumn(
                name: "BuyerFeeExGst",
                table: "Purchases",
                newName: "PlatformFeeExGst");

            migrationBuilder.AddColumn<string>(
                name: "MareBreed",
                table: "Purchases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MareName",
                table: "Purchases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MareRegistration",
                table: "Purchases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeePercent",
                table: "Listings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StartingPrice",
                table: "AuctionListings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "FixedPriceListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    QuantityRemaining = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedPriceListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedPriceListings_Listings_Id",
                        column: x => x.Id,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
