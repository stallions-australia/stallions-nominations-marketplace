using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stallions.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class V2Phase3ChargeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargeCustomerId",
                table: "Purchases",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeDescription",
                table: "Purchases",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChargeNeedsAttention",
                table: "Purchases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ChargePaymentMethodId",
                table: "Purchases",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargeCustomerId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargeDescription",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargeNeedsAttention",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ChargePaymentMethodId",
                table: "Purchases");
        }
    }
}
