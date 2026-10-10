using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

/// <summary>The handful of Stripe.net calls the platform makes — wrapped so the provider is unit-testable.</summary>
public interface IStripeApi
{
    Task<string> CreateCustomerAsync(CustomerCreateOptions options, string idempotencyKey);
    /// <summary>Creates a hosted Checkout session and returns its URL.</summary>
    Task<string> CreateCheckoutSessionAsync(SessionCreateOptions options);
    Task<SetupIntent> GetSetupIntentAsync(string setupIntentId);
    Task<PaymentMethod> GetPaymentMethodAsync(string paymentMethodId);
    Task DetachPaymentMethodAsync(string paymentMethodId);
    Task<PaymentIntent> CreatePaymentIntentAsync(PaymentIntentCreateOptions options, string idempotencyKey);
}
