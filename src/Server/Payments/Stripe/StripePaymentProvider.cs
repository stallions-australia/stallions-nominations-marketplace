using Microsoft.Extensions.Logging;
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
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(IStripeApi api, IOptions<PaymentOptions> options, ILogger<StripePaymentProvider> logger)
    {
        _api = api;
        _logger = logger;
        _options = options.Value;
    }

    public string Name => PaymentOptions.ProviderStripe;

    public Task<string> CreateCustomerAsync(Guid userId, string email, string name) =>
        _api.CreateCustomerAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            Metadata = new Dictionary<string, string> { ["userId"] = userId.ToString() }
        }, $"customer-{userId}");

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
            // Short-lived so a Staff waive / mark-paid cannot be followed by a late card payment for long.
            ExpiresAt = DateTime.UtcNow.AddHours(1),
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

        string? kind = null;
        session.Metadata?.TryGetValue(KindKey, out kind);
        var type = stripeEvent.Type;

        if (session.Mode == "setup" && kind == KindCardSetup)
        {
            if (!TryGetGuid(session, "userId", out var userId))
            {
                _logger.LogError("Stripe card-setup session {SessionId} has missing or invalid userId metadata (event {EventId})",
                    session.Id, stripeEvent.Id);
                return new UnhandledPaymentEvent(stripeEvent.Id, $"{type}:setup:bad-metadata");
            }

            try
            {
                if (session.SetupIntentId == null) return NoCard(stripeEvent.Id, session.Id, "no SetupIntent");
                var setupIntent = await _api.GetSetupIntentAsync(session.SetupIntentId);
                if (setupIntent.PaymentMethodId == null) return NoCard(stripeEvent.Id, session.Id, "no payment method");
                var method = await _api.GetPaymentMethodAsync(setupIntent.PaymentMethodId);
                if (method.Card == null) return NoCard(stripeEvent.Id, session.Id, "payment method is not a card");

                return new CardSavedEvent(stripeEvent.Id, userId, session.CustomerId,
                    method.Id, method.Card.Brand, method.Card.Last4, (int)method.Card.ExpMonth, (int)method.Card.ExpYear);
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                // Permanent: retrying will never succeed. Anything else (transient) rethrows so Stripe retries.
                _logger.LogError(ex, "Stripe card-setup session {SessionId}: SetupIntent or PaymentMethod no longer exists (event {EventId})",
                    session.Id, stripeEvent.Id);
                return new UnhandledPaymentEvent(stripeEvent.Id, $"{type}:setup:no-card");
            }
        }

        if (session.Mode == "payment" && kind == KindListingFee && session.PaymentStatus == "paid")
        {
            if (!TryGetGuid(session, "subscriptionId", out var subscriptionId))
            {
                _logger.LogError("Stripe listing-fee session {SessionId} (PaymentIntent {PaymentIntentId}) has missing or invalid subscriptionId metadata (event {EventId})",
                    session.Id, session.PaymentIntentId, stripeEvent.Id);
                return new UnhandledPaymentEvent(stripeEvent.Id, $"{type}:payment:bad-metadata");
            }

            return new ListingFeePaidEvent(stripeEvent.Id, subscriptionId,
                session.AmountTotal ?? 0, session.Currency, session.PaymentIntentId);
        }

        return new UnhandledPaymentEvent(stripeEvent.Id, $"{type}:{session.Mode}:{session.PaymentStatus}");
    }

    private static bool TryGetGuid(Session session, string key, out Guid value)
    {
        value = Guid.Empty;
        return session.Metadata != null && session.Metadata.TryGetValue(key, out var raw) && Guid.TryParse(raw, out value);
    }

    private UnhandledPaymentEvent NoCard(string eventId, string sessionId, string reason)
    {
        _logger.LogError("Stripe card-setup session {SessionId} cannot be saved: {Reason} (event {EventId})", sessionId, reason, eventId);
        return new UnhandledPaymentEvent(eventId, "checkout.session.completed:setup:no-card");
    }

    public Task DetachCardAsync(string paymentMethodId) => _api.DetachPaymentMethodAsync(paymentMethodId);

    public async Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = PaymentAmounts.ToCents(request.AmountIncGst),
            Currency = "aud",
            Customer = request.CustomerId,
            PaymentMethod = request.PaymentMethodId,
            // The buyer isn't present: charge now, and fail rather than ask for 3-D Secure.
            OffSession = true,
            Confirm = true,
            Description = request.Description,
            Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
        try
        {
            var intent = await _api.CreatePaymentIntentAsync(options, request.IdempotencyKey);
            if (intent.Status == "succeeded") return ChargeResult.Success(intent.Id);

            // A "processing" intent may still succeed, and a retry with the same idempotency key only
            // replays this snapshot, so look at the intent's current state. Still processing: throw, so
            // the caller treats the attempt as interrupted and repeats it later with the same key.
            var current = await _api.GetPaymentIntentAsync(intent.Id);
            if (current.Status == "succeeded") return ChargeResult.Success(intent.Id);
            if (current.Status == "processing")
                throw new InvalidOperationException($"Buyer-fee PaymentIntent {intent.Id} is still processing");

            _logger.LogWarning("Buyer-fee PaymentIntent {PaymentIntentId} ended in status {Status}", intent.Id, current.Status);
            return ChargeResult.Declined("payment_incomplete", "The payment could not be completed.");
        }
        catch (StripeException ex) when (ex.StripeError?.Type == "card_error")
        {
            // Includes authentication_required: the bank wants the cardholder to approve it.
            return ChargeResult.Declined(
                ex.StripeError.DeclineCode ?? ex.StripeError.Code ?? "card_error",
                ex.StripeError.Message ?? "Your card was declined.");
        }
        catch (StripeException ex) when (ex.StripeError?.Type == "invalid_request_error"
            && (ex.StripeError.Code == "resource_missing" || ex.StripeError.Param == "payment_method"))
        {
            // The saved card was detached or deleted. Other invalid requests (e.g. idempotency_error) propagate.
            return ChargeResult.Declined("payment_method_unavailable",
                "Your saved card can no longer be used. Please save a new card.");
        }
    }
}
