using System.Collections.Concurrent;

namespace Stallions.Server.Payments;

/// <summary>
/// Dev-only stand-in for Stripe (Payments:Provider = Fake). Sessions live in memory, which suits
/// single-instance dev. Its "hosted page" is FakePaymentController; approving a session produces
/// the same PaymentEvent the Stripe webhook would. Refused outside Development/Staging by PaymentOptionsValidator.
/// </summary>
public class FakePaymentProvider : IPaymentProvider
{
    public sealed record FakeSession(
        string Id, bool IsCardSetup, Guid? UserId, string? CustomerId, Guid? SubscriptionId,
        long AmountCents, string Description, string SuccessUrl, string CancelUrl);

    /// <summary>The last four digits of the fake declining card (like Stripe's 4000 0000 0000 0002).</summary>
    public const string DecliningLast4 = "0002";
    private const string DecliningPrefix = "pm_fake_decline_";

    private readonly ConcurrentDictionary<string, FakeSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ChargeResult> _charges = new();
    public string Name => PaymentOptions.ProviderFake;

    public Task<string> CreateCustomerAsync(Guid userId, string email, string name) =>
        Task.FromResult($"cus_fake_{Guid.NewGuid():N}");

    public Task<string> CreateCardSetupSessionAsync(Guid userId, string customerId, string successUrl, string cancelUrl) =>
        Task.FromResult(Start(new FakeSession(NewId(), true, userId, customerId, null, 0,
            "Save a card", successUrl, cancelUrl)));

    public Task<string> CreateListingFeeSessionAsync(
        Guid subscriptionId, decimal amountIncGst, string description, string? payerEmail,
        string successUrl, string cancelUrl) =>
        Task.FromResult(Start(new FakeSession(NewId(), false, null, null, subscriptionId,
            PaymentAmounts.ToCents(amountIncGst), description, successUrl, cancelUrl)));

    public Task<PaymentEvent> ParseWebhookAsync(string rawBody, string? signatureHeader) =>
        throw new NotSupportedException("The fake payment provider has no webhook; approve sessions on its page.");

    public Task DetachCardAsync(string paymentMethodId) => Task.CompletedTask;

    public Task<ChargeResult> ChargeSavedCardAsync(ChargeRequest request) =>
        Task.FromResult(_charges.GetOrAdd(request.IdempotencyKey, _ =>
            request.PaymentMethodId.StartsWith(DecliningPrefix, StringComparison.Ordinal)
                ? ChargeResult.Declined("card_declined", "Your card was declined.")
                : ChargeResult.Success($"pi_fake_{Guid.NewGuid():N}")));
    public FakeSession? GetSession(string id) => _sessions.GetValueOrDefault(id);

    /// <summary>Completes the session successfully. Null if the session doesn't exist (or was already used).</summary>
    public (FakeSession Session, PaymentEvent Event, string SuccessUrl, string CancelUrl)? Approve(string id, bool decliningCard = false)
    {
        if (!_sessions.TryRemove(id, out var s)) return null;
        var eventId = $"evt_fake_{Guid.NewGuid():N}";
        PaymentEvent evt = s.IsCardSetup
            ? new CardSavedEvent(eventId, s.UserId!.Value, s.CustomerId!,
                $"{(decliningCard ? DecliningPrefix : "pm_fake_")}{Guid.NewGuid():N}",
                "visa", decliningCard ? DecliningLast4 : "4242", 12, DateTime.UtcNow.Year + 3)
            : new ListingFeePaidEvent(eventId, s.SubscriptionId!.Value, s.AmountCents, "aud", $"pi_fake_{Guid.NewGuid():N}");
        return (s, evt, s.SuccessUrl, s.CancelUrl);
    }

    /// <summary>Puts a session back after its event could not be processed, so the approval can be retried.</summary>
    public void Restore(FakeSession session) => _sessions[session.Id] = session;

    /// <summary>Returns the cancel URL, or null if the session doesn't exist. Like Stripe, the session stays open and payable until it is approved.</summary>
    public string? Decline(string id) => _sessions.TryGetValue(id, out var s) ? s.CancelUrl : null;

    private string Start(FakeSession session)
    {
        _sessions[session.Id] = session;
        return $"/payments/fake/{session.Id}";
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
