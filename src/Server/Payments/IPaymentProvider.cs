namespace Stallions.Server.Payments;

/// <summary>
/// The payment provider (Stripe, or the dev-only fake). Hosted pages only — card data never
/// reaches the app. Amounts are always passed in by the server, never taken from the browser.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>"Stripe" or "Fake" — recorded on saved cards and processed events.</summary>
    string Name { get; }

    Task<string> CreateCustomerAsync(Guid userId, string email, string name);

    /// <summary>Hosted page that saves a card for later off-session use. Returns the redirect URL.</summary>
    Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl);

    /// <summary>Hosted page that takes a listing-fee payment in AUD. Returns the redirect URL.</summary>
    Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl);

    /// <summary>Verifies the signature and maps the callback. Throws <see cref="PaymentSignatureException"/>.</summary>
    Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader);

    Task DetachCardAsync(string paymentMethodId);

    /// <summary>
    /// Charges a saved card without the cardholder present. The idempotency key makes a repeated
    /// attempt return the original result instead of charging again.
    /// </summary>
    Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request);
}
