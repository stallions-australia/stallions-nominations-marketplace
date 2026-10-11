namespace Stallions.Shared.DTOs.Checkout;

public class RefundResultDto
{
    public Guid? AdjustmentNoteId { get; set; }
    public string? AdjustmentNoteNumber { get; set; }
    /// <summary>What happened, for Staff (including when no adjustment note could be issued).</summary>
    public string Message { get; set; } = string.Empty;
}
