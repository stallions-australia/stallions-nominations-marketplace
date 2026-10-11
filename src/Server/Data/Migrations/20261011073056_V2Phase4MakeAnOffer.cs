using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase4MakeAnOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OfferId",
                table: "Purchases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MakeAnOfferListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAuctionListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MakeAnOfferListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MakeAnOfferListings_AuctionListings_SourceAuctionListingId",
                        column: x => x.SourceAuctionListingId,
                        principalTable: "AuctionListings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MakeAnOfferListings_Listings_Id",
                        column: x => x.Id,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountIncGst = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offers_MakeAnOfferListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "MakeAnOfferListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Offers_Users_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_OfferId",
                table: "Purchases",
                column: "OfferId",
                unique: true,
                filter: "[OfferId] IS NOT NULL AND [Status] <> 'Voided'");

            migrationBuilder.CreateIndex(
                name: "IX_MakeAnOfferListings_SourceAuctionListingId",
                table: "MakeAnOfferListings",
                column: "SourceAuctionListingId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_BuyerUserId",
                table: "Offers",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_ListingId_BuyerUserId",
                table: "Offers",
                columns: new[] { "ListingId", "BuyerUserId" },
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Status_ExpiresAt",
                table: "Offers",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Offers_OfferId",
                table: "Purchases",
                column: "OfferId",
                principalTable: "Offers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Offers_OfferId",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "Offers");

            migrationBuilder.DropTable(
                name: "MakeAnOfferListings");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_OfferId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "OfferId",
                table: "Purchases");
        }
    }
}
