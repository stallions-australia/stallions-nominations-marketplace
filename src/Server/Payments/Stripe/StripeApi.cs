using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Stallions.Server.Payments.Stripe;

public class StripeApi : IStripeApi
{
    private readonly IStripeClient _client;
    public StripeApi(IOptions<PaymentOptions> options) => _client = new StripeClient(options.Value.Stripe.SecretKey);

    public async Task<string> CreateCustomerAsync(CustomerCreateOptions options, string idempotencyKey) =>
        (await new CustomerService(_client).CreateAsync(options, new RequestOptions { IdempotencyKey = idempotencyKey })).Id;

    public async Task<string> CreateCheckoutSessionAsync(SessionCreateOptions options) =>
        (await new SessionService(_client).CreateAsync(options)).Url;

    public Task<SetupIntent> GetSetupIntentAsync(string setupIntentId) =>
        new SetupIntentService(_client).GetAsync(setupIntentId);

    public Task<PaymentMethod> GetPaymentMethodAsync(string paymentMethodId) =>
        new PaymentMethodService(_client).GetAsync(paymentMethodId);

    public async Task DetachPaymentMethodAsync(string paymentMethodId) =>
        await new PaymentMethodService(_client).DetachAsync(paymentMethodId);
}
