using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase5bEditableContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TermsDocuments_Version",
                table: "TermsDocuments");

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "TermsDocuments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Buyer");

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedStudTermsAt",
                table: "StudFarms",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcceptedStudTermsByUserId",
                table: "StudFarms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedStudTermsVersion",
                table: "StudFarms",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentItems_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ContentItems",
                columns: new[] { "Id", "Body", "CreatedAt", "CreatedByUserId", "Key", "Subject", "Version" },
                values: new object[,]
                {
                    { new Guid("185f4f6e-dedd-66ab-103c-35569229d4bf"), "Your offer of {Amount} for the {StallionName} nomination ({Season}) was accepted by {StudName}.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.OfferAcceptedAndCharged", "Your offer for the {StallionName} nomination was accepted", 1 },
                    { new Guid("1e34b589-f308-fc05-1b5c-bd0dc0511888"), "The stud has declined your offer of {Amount} for the {StallionName} nomination ({Season}).", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.OfferDeclined", "Offer declined: {StallionName}", 1 },
                    { new Guid("1fc28302-c1ec-60ed-f21d-d90a8f660248"), "Your offer of {Amount} for the {StallionName} nomination ({Season}) has expired without a response from the stud.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.OfferExpired", "Offer closed: {StallionName}", 1 },
                    { new Guid("2a5b68b2-5346-fec9-cf3f-0d298de1019b"), "You accepted an offer for the {StallionName} nomination ({Season}), and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.StudSaleConfirmationOffer", "Sale confirmed: {StallionName} nomination", 1 },
                    { new Guid("2fab357a-bf2b-d7d0-df7c-5adc68c9b643"), "The auction for the {StallionName} nomination ({Season}) has closed without a sale: {Reason}\n\nThe lot is marked Unsold in My Listings.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.StudNoSaleAuction", "No sale: {StallionName} nomination", 1 },
                    { new Guid("30f5f610-2d9d-ebe6-e573-c71005b0ce5f"), "Here is your tax invoice for the listing fee. Please pay {Amount} by {DueDate}.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.ListingFeeInvoiceDue", "Tax invoice {InvoiceNumber} — amount due {Amount} by {DueDate}", 1 },
                    { new Guid("36917098-77a0-481c-a723-437834cc6d7d"), "Your bid of {Amount} won the auction for the {StallionName} nomination, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.PaymentFailed", "Action needed: payment for {StallionName} failed", 1 },
                    { new Guid("5922d4db-4231-3b59-0187-4497db7b2c1b"), "Another buyer has bid {HighBid} on the {StallionName} nomination, so you are no longer the highest bidder.\n\nThe auction closes {ClosesAt}.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.Outbid", "You've been outbid on {StallionName}", 1 },
                    { new Guid("5ef4e676-d420-619c-59ec-67b7c8d16cd1"), "The {StallionName} nomination ({Season}) is no longer taking offers, so your offer of {Amount} has closed without a response.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.OfferExpiredListingClosed", "Offer closed: {StallionName}", 1 },
                    { new Guid("697c9e86-64bb-9e1e-51e9-3b134690191e"), "Thank you — your listing fee has been paid. Your tax invoice is below.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.ListingFeeTaxInvoice", "Tax invoice {InvoiceNumber}: {Description}", 1 },
                    { new Guid("6b4c350a-0d24-3d90-bc83-0cfa8cc58cda"), "The auction for the {StallionName} nomination ({Season}) has closed without a sale.\n\nThank you for bidding.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.AuctionEndedWithoutSale", "Auction closed: {StallionName}", 1 },
                    { new Guid("819eb1f9-280c-7ab3-f48c-a9433ca775a8"), "{BuyerName} ({BuyerEmail}) has offered {Amount} for the {StallionName} nomination ({Season}).\n\nThe offer is open until {Deadline}. Accepting it charges the buyer the buyer fee straight away.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.OfferReceived", "New offer: {StallionName} nomination", 1 },
                    { new Guid("81d4055b-e1c2-11f3-8220-62e462604bf1"), "We couldn't charge the buyer fee for your accepted offer on the {StallionName} nomination by the deadline, so the sale has not gone ahead.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.PaymentAbandonedOffer", "No sale: {StallionName}", 1 },
                    { new Guid("95660d2b-4c8b-d50d-fd39-cc131aa39c4c"), "Your saved card is charged the buyer fee automatically when you win an auction or a stud accepts your offer. The nomination price is not charged to this card — you pay the balance directly to the stud, under the stud's own terms.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Disclosure.SavedCardExplanation", null, 1 },
                    { new Guid("9e64ce67-b881-5179-091b-390c5fc942a9"), "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Disclosure.StudFarmBalanceArrangement", null, 1 },
                    { new Guid("a2405b69-c086-06f8-9609-ff40c626f360"), "Your offer of {Amount} for the {StallionName} nomination was accepted, but we couldn't charge the buyer fee of {BuyerFee} to your saved card: {Reason}\n\nPlease update your card by {Deadline}. Saving a new card retries the payment straight away. If the payment can't be completed by then, the sale won't go ahead.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.PaymentFailedOffer", "Action needed: payment for {StallionName} failed", 1 },
                    { new Guid("a439d551-b432-1c4e-e8fb-0323d37d002b"), "Your bid of {Amount} won the auction for the {StallionName} nomination ({Season}), offered by {StudName}.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.WonAndCharged", "You won the {StallionName} nomination", 1 },
                    { new Guid("a8a4608b-f8d2-fb3e-e5a5-189916336ac7"), "The buyer fee has been charged to your saved card. Your tax invoice is below.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.BuyerFeeTaxInvoice", "Tax invoice {InvoiceNumber}: buyer fee", 1 },
                    { new Guid("ab018e34-6fa0-cae0-5c6c-9796a745a85c"), "The sale from the offer you accepted for the {StallionName} nomination ({Season}) has not gone ahead: the buyer's payment couldn't be completed.\n\nYour Make an Offer listing stays open for other offers.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.StudNoSaleMakeAnOffer", "No sale: {StallionName} nomination", 1 },
                    { new Guid("be17784d-561f-38e4-0302-77c49fa18cb7"), "The auction for the {StallionName} nomination ({Season}) has closed with a sale, and the buyer fee has been paid.\n\nBuyer: {BuyerName}, {BuyerEmail}", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.StudSaleConfirmation", "Sale confirmed: {StallionName} nomination", 1 },
                    { new Guid("c08fc2c6-17f6-682c-3400-09ff00ee4f5d"), "We couldn't charge the buyer fee for the {StallionName} nomination by the deadline, so the sale has not gone ahead and the nomination is no longer yours.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.PaymentAbandoned", "No sale: {StallionName}", 1 },
                    { new Guid("df17b217-5fe9-c3b1-29a9-068cca19a269"), "The buyer fee is paid to Stallions Australia and forms part of the price — it is not added on top. You pay the balance (the price less the buyer fee) directly to the stud, under the stud's own terms. The price arrangement is entirely between you and the stud.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Disclosure.BuyerFeeExplanation", null, 1 },
                    { new Guid("e3eeed14-4f52-563f-8786-64bf46fc5e8f"), "We've received your payment of {Amount} for tax invoice {InvoiceNumber}. Thank you.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.ListingFeeReceipt", "Receipt: tax invoice {InvoiceNumber} paid", 1 },
                    { new Guid("ee67be59-c01a-3851-9318-237d3c84b3cd"), "The auction for the {StallionName} nomination ({Season}) has closed and another bid won.\n\nThank you for bidding.", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.AuctionLost", "Auction closed: {StallionName}", 1 },
                    { new Guid("f104d7f7-98f4-d756-c1eb-7fd21fb8f628"), "This adjustment note reverses tax invoice {OriginalNumber} dated {OriginalDate} in full.\n\nReason: {Reason}", new DateTime(2026, 10, 12, 0, 0, 0, 0, DateTimeKind.Utc), null, "Email.AdjustmentNote", "Adjustment note {InvoiceNumber} for tax invoice {OriginalNumber}", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TermsDocuments_Audience_Version",
                table: "TermsDocuments",
                columns: new[] { "Audience", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudFarms_AcceptedStudTermsByUserId",
                table: "StudFarms",
                column: "AcceptedStudTermsByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_CreatedByUserId",
                table: "ContentItems",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_Key_Version",
                table: "ContentItems",
                columns: new[] { "Key", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StudFarms_Users_AcceptedStudTermsByUserId",
                table: "StudFarms",
                column: "AcceptedStudTermsByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudFarms_Users_AcceptedStudTermsByUserId",
                table: "StudFarms");

            migrationBuilder.DropTable(
                name: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_TermsDocuments_Audience_Version",
                table: "TermsDocuments");

            migrationBuilder.DropIndex(
                name: "IX_StudFarms_AcceptedStudTermsByUserId",
                table: "StudFarms");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "TermsDocuments");

            migrationBuilder.DropColumn(
                name: "AcceptedStudTermsAt",
                table: "StudFarms");

            migrationBuilder.DropColumn(
                name: "AcceptedStudTermsByUserId",
                table: "StudFarms");

            migrationBuilder.DropColumn(
                name: "AcceptedStudTermsVersion",
                table: "StudFarms");

            migrationBuilder.CreateIndex(
                name: "IX_TermsDocuments_Version",
                table: "TermsDocuments",
                column: "Version",
                unique: true);
        }
    }
}
