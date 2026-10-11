namespace Stallions.Server.Payments;

/// <summary>An off-session charge of a buyer's saved card. The amount always comes from the server.</summary>
public sealed record ChargeRequest(
    string CustomerId,
    string PaymentMethodId,
    decimal AmountIncGst,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    string IdempotencyKey);

/// <summary>
/// The outcome of a charge. A decline is a result, not an exception; an unreachable provider
/// throws, so the attempt is repeated later with the same idempotency key.
/// </summary>
public sealed record ChargeResult(bool Succeeded, string? PaymentReference, string? FailureCode, string? FailureMessage)
{
    public static ChargeResult Success(string paymentReference) => new(true, paymentReference, null, null);
    public static ChargeResult Declined(string code, string message) => new(false, null, code, message);
}
