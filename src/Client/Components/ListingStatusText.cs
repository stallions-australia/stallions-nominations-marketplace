namespace Stallions.Client.Components;

/// <summary>Display text and badge class for listing statuses (stud and Staff pages).</summary>
public static class ListingStatusText
{
    public static string Label(string status, string? closeReason) => status switch
    {
        "AwaitingPayment" => "Awaiting payment",
        "Unsold" => closeReason switch
        {
            "NoBids" => "Unsold — no bids",
            "ReserveNotMet" => "Unsold — reserve not met",
            "ChargeFailed" => "Unsold — payment failed",
            _ => "Unsold"
        },
        _ => status
    };

    public static string BadgeClass(string status) => status switch
    {
        "Active" => "badge-active",
        "Draft" => "badge-draft",
        "Cancelled" => "badge-cancelled",
        "Expired" => "badge-expired",
        "Sold" => "badge-sold",
        "AwaitingPayment" => "badge-draft",
        "Unsold" => "badge-expired",
        _ => ""
    };

    /// <summary>Closed, sold, or being charged: no more stud edits or Close action.</summary>
    public static bool IsFinished(string status) =>
        status is "Cancelled" or "Sold" or "AwaitingPayment" or "Unsold";
}
