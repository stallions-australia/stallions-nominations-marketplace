using FluentAssertions;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Tests.TaxDocuments;

public class TaxDocumentCsvTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+61 2 6545 0000", "'+61 2 6545 0000")]
    [InlineData("-5 nominations", "'-5 nominations")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("Arrowfield, Scone", "\"Arrowfield, Scone\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\",\"y\")", "\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"")]
    [InlineData("Plain", "Plain")]
    [InlineData(null, "")]
    public void Text_NeutralisesFormulas_AndQuotesWhenNeeded(string? value, string expected) =>
        TaxDocumentCsv.Text(value).Should().Be(expected);

    [Fact]
    public void Build_WritesAHeader_SignedNumericAmounts_AndOneLinePerDocument()
    {
        var rows = new[]
        {
            new TaxDocumentSummaryDto
            {
                Number = "SA-000124", Kind = "BuyerFeeInvoice", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 11),
                PaidOn = new DateOnly(2026, 10, 11), RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com",
                Description = "Buyer fee — nomination to Dundeel, 2026 Season (auction)", AmountIncGst = 150m,
                AmountExGst = 136.36m, GstAmount = 13.64m, ReversedByNumber = "SA-000130"
            },
            new TaxDocumentSummaryDto
            {
                Number = "SA-000130", Kind = "AdjustmentNote", Status = "Paid", IssuedOn = new DateOnly(2026, 10, 12),
                PaidOn = new DateOnly(2026, 10, 12), RecipientName = "Jane Buyer", RecipientEmail = "jane@example.com",
                Description = "Adjustment to SA-000124", AmountIncGst = 150m, AmountExGst = 136.36m, GstAmount = 13.64m,
                OriginalNumber = "SA-000124", Reason = "Buyer fee refunded"
            }
        };

        var lines = TaxDocumentCsv.Build(rows).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        lines[0].Should().Be("Number,Kind,Issued,Status,Due,Paid,Recipient,Recipient email,Description,Amount inc GST,Amount ex GST,GST,Adjusts,Reversed by,Reason");
        lines[1].Should().Be("SA-000124,BuyerFeeInvoice,2026-10-11,Paid,,2026-10-11,Jane Buyer,jane@example.com,Buyer fee — nomination to Dundeel, 2026 Season (auction),150.00,136.36,13.64,,SA-000130,"
            .Replace("Buyer fee — nomination to Dundeel, 2026 Season (auction)", "\"Buyer fee — nomination to Dundeel, 2026 Season (auction)\""));
        lines[2].Should().EndWith(",-150.00,-136.36,-13.64,SA-000124,,Buyer fee refunded");
    }
}
