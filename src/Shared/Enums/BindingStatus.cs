namespace Stallions.Shared.Enums;

public enum BindingStatus
{
    PendingAcknowledgement,
    Acknowledged,
    PdfGenerated,
    AwaitingSignatures,
    BuyerSigned,
    FarmSigned,
    Complete,
    Disputed
}
