namespace Stallions.Shared.DTOs.Subscriptions;

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    public decimal FeeIncGst { get; set; }
    public decimal FeeExGst { get; set; }
    public decimal GstAmount { get; set; }
    public string Status { get; set; } = string.Empty;          // Pending | Paid | Waived
    public string? PaymentMethod { get; set; }                   // Card | Invoice | BankTransfer | Waived
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? WaiverReason { get; set; }
    public string? Notes { get; set; }                           // Staff-only; null for stud admins
    public DateTime CreatedAt { get; set; }
}
