using System.Globalization;
using System.Text;
using Stallions.Shared.DTOs.TaxDocuments;
using Stallions.Shared.Enums;

namespace Stallions.Server.TaxDocuments;

/// <summary>
/// The register as CSV: one line per document; adjustment-note amounts negative; amounts written as plain
/// numbers. Text that a spreadsheet would run as a formula (starting = + - @ tab or CR) is prefixed with '.
/// </summary>
public static class TaxDocumentCsv
{
    private static readonly string[] Header =
    {
        "Number", "Kind", "Issued", "Status", "Due", "Paid", "Recipient", "Recipient email", "Description",
        "Amount inc GST", "Amount ex GST", "GST", "Adjusts", "Reversed by", "Reason"
    };

    public static string Build(IEnumerable<TaxDocumentSummaryDto> rows)
    {
        var csv = new StringBuilder();
        csv.Append(string.Join(",", Header)).Append("\r\n");
        foreach (var r in rows)
        {
            var sign = r.Kind == nameof(TaxDocumentKind.AdjustmentNote) ? -1m : 1m;
            csv.Append(string.Join(",", new[]
            {
                Text(r.Number), Text(r.Kind), Date(r.IssuedOn), Text(r.Status), Date(r.DueOn), Date(r.PaidOn),
                Text(r.RecipientName), Text(r.RecipientEmail), Text(r.Description),
                Amount(sign * r.AmountIncGst), Amount(sign * r.AmountExGst), Amount(sign * r.GstAmount),
                Text(r.OriginalNumber), Text(r.ReversedByNumber), Text(r.Reason)
            })).Append("\r\n");
        }
        return csv.ToString();
    }

    public static string Text(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
