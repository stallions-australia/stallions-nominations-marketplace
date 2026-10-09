using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

/// <summary>Stripe-hosted Checkout for card setup and listing-fee payments; verified webhooks.</summary>
public class StripePaymentProvider : IPaymentProvider
{
    private const string KindKey = "kind";
    private const string KindCardSetup = "card-setup";
    private const string KindListingFee = "listing-fee";

    private readonly IStripeApi _api;
    private readonly PaymentOptions _options;

    public StripePaymentProvider(IStripeApi api, IOptions<PaymentOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    public string Name => PaymentOptions.ProviderStripe;

    public Task<string> CreateCustomerAsync(Guid userId, string email, string name) =>
        _api.CreateCustomerAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            Metadata = new Dictionary<string, string> { ["userId"] = userId.ToString() }
        });

    public Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string> { [KindKey] = KindCardSetup, ["userId"] = userId.ToString() };
        return _api.CreateCheckoutSessionAsync(new SessionCreateOptions
        {
            Mode = "setup",
            Customer = customerId,
            Currency = "aud",
            AllowedPaymentMethodTypes = new List<string> { "card" },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            SetupIntentData = new SessionSetupIntentDataOptions { Metadata = metadata }
        });
    }

    public Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string>
        {
            [KindKey] = KindListingFee,
            ["subscriptionId"] = subscriptionId.ToString()
        };
        return _api.CreateCheckoutSessionAsync(new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = payerEmail,
            AllowedPaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aud",
                        UnitAmount = PaymentAmounts.ToCents(amountIncGst),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = description }
                    }
                }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = metadata, Description = description }
        });
    }

    public async Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader)
    {
        Event stripeEvent;
        try
        {
            // Verifies the HMAC signature and rejects timestamps older than 5 minutes.
            stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader ?? string.Empty,
                _options.Stripe.WebhookSigningSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new PaymentSignatureException(ex.Message);
        }

        if (stripeEvent.Type != "checkout.session.completed" || stripeEvent.Data.Object is not Session session)
            return new UnhandledPaymentEvent(stripeEvent.Id, stripeEvent.Type);

        session.Metadata.TryGetValue(KindKey, out var kind);

        if (session.Mode == "setup" && kind == KindCardSetup)
        {
            var setupIntent = await _api.GetSetupIntentAsync(session.SetupIntentId);
            var method = await _api.GetPaymentMethodAsync(setupIntent.PaymentMethodId);
            return new CardSavedEvent(stripeEvent.Id, Guid.Parse(session.Metadata["userId"]), session.CustomerId,
                method.Id, method.Card.Brand, method.Card.Last4, (int)method.Card.ExpMonth, (int)method.Card.ExpYear);
        }

        if (session.Mode == "payment" && kind == KindListingFee && session.PaymentStatus == "paid")
            return new ListingFeePaidEvent(stripeEvent.Id, Guid.Parse(session.Metadata["subscriptionId"]),
                session.AmountTotal ?? 0, session.Currency, session.PaymentIntentId);

        return new UnhandledPaymentEvent(stripeEvent.Id, $"{stripeEvent.Type}:{session.Mode}:{session.PaymentStatus}");
    }

    public Task DetachCardAsync(string paymentMethodId) => _api.DetachPaymentMethodAsync(paymentMethodId);
}
