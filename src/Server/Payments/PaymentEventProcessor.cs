using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Payments;

/// <summary>
/// The only code that changes data because of a payment. Provider-neutral and idempotent:
/// each provider event id is processed once. An exception leaves the event unrecorded, so the
/// provider's retry processes it again.
/// </summary>
public class PaymentEventProcessor : IPaymentEventProcessor
{
    private readonly IProcessedPaymentEventRepository _processed;
    private readonly ISavedCardRepository _cards;
    private readonly IUserRepository _users;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentProvider _provider;
    private readonly IAuditLogRepository _audit;
    private readonly ILogger<PaymentEventProcessor> _log;

    public PaymentEventProcessor(
        IProcessedPaymentEventRepository processed, ISavedCardRepository cards, IUserRepository users,
        ISubscriptionRepository subscriptions, IPaymentProvider provider, IAuditLogRepository audit,
        ILogger<PaymentEventProcessor> log)
    {
        _processed = processed; _cards = cards; _users = users; _subscriptions = subscriptions;
        _provider = provider; _audit = audit; _log = log;
    }

    public async Task<PaymentEventOutcome> ProcessAsync(PaymentEvent paymentEvent)
    {
        // Claim the event id first: a repeated or concurrent delivery loses the claim and is a
        // duplicate. If processing then fails, release the claim so the provider's retry runs again.
        var claimed = await _processed.TryClaimAsync(new ProcessedPaymentEvent
        {
            EventId = paymentEvent.EventId,
            Provider = _provider.Name,
            Type = paymentEvent.GetType().Name,
            ProcessedAt = DateTime.UtcNow
        });
        if (!claimed) return PaymentEventOutcome.Duplicate;

        try
        {
            var outcome = paymentEvent switch
            {
                CardSavedEvent card => await SaveCardAsync(card),
                ListingFeePaidEvent fee => await MarkListingFeePaidAsync(fee),
                _ => PaymentEventOutcome.Ignored
            };
            // Rejected and Ignored are final too — a retry wouldn't change them.
            await _processed.MarkCompletedAsync(paymentEvent.EventId);
            return outcome;
        }
        catch
        {
            await _processed.ReleaseAsync(paymentEvent.EventId);
            throw;
        }
    }

    private async Task<PaymentEventOutcome> SaveCardAsync(CardSavedEvent e)
    {
        var user = await _users.GetByIdAsync(e.UserId);
        if (user == null)
        {
            _log.LogError("Card saved for unknown user {UserId} (event {EventId})", e.UserId, e.EventId);
            return PaymentEventOutcome.Rejected;
        }

        var existing = await _cards.GetByUserIdAsync(e.UserId);
        if (existing != null && existing.ProviderPaymentMethodId != e.PaymentMethodId)
        {
            try { await _provider.DetachCardAsync(existing.ProviderPaymentMethodId); }
            catch (Exception ex)
            {
                // The new card is still saved; a stale card left at the provider is harmless.
                _log.LogWarning(ex, "Could not detach replaced card {PaymentMethodId}", existing.ProviderPaymentMethodId);
            }
        }

        var card = existing ?? new SavedCard { UserId = e.UserId };
        card.Provider = _provider.Name;
        card.ProviderCustomerId = e.CustomerId;
        card.ProviderPaymentMethodId = e.PaymentMethodId;
        card.Brand = e.Brand;
        card.Last4 = e.Last4;
        card.ExpMonth = e.ExpMonth;
        card.ExpYear = e.ExpYear;
        card.UpdatedAt = DateTime.UtcNow;
        if (existing == null) await _cards.AddAsync(card); else await _cards.UpdateAsync(card);

        if (user.PaymentCustomerId != e.CustomerId || user.PaymentCustomerProvider != _provider.Name)
        {
            user.PaymentCustomerId = e.CustomerId;
            user.PaymentCustomerProvider = _provider.Name;
            await _users.UpdateAsync(user);
        }

        await _audit.LogAsync("SavedCard", card.Id, existing == null ? "SaveCard" : "ReplaceCard", e.UserId,
            JsonSerializer.Serialize(new { e.Brand, e.Last4, e.ExpMonth, e.ExpYear }));
        return PaymentEventOutcome.Processed;
    }

    private async Task<PaymentEventOutcome> MarkListingFeePaidAsync(ListingFeePaidEvent e)
    {
        var subscription = await _subscriptions.GetByIdAsync(e.SubscriptionId);
        if (subscription == null)
        {
            _log.LogError("Listing fee paid for unknown subscription {SubscriptionId} (event {EventId})",
                e.SubscriptionId, e.EventId);
            return PaymentEventOutcome.Rejected;
        }

        if (subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived)
        {
            _log.LogWarning("Listing fee paid for subscription {SubscriptionId} that is already {Status} (event {EventId})",
                e.SubscriptionId, subscription.Status, e.EventId);
            return PaymentEventOutcome.Ignored;
        }

        var expectedCents = (long)Math.Round(subscription.FeeIncGst * 100m, MidpointRounding.AwayFromZero);
        if (e.AmountCents != expectedCents || !string.Equals(e.Currency, "aud", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogError(
                "Listing fee payment mismatch for subscription {SubscriptionId}: expected {Expected} AUD cents, got {Amount} {Currency} (event {EventId})",
                e.SubscriptionId, expectedCents, e.AmountCents, e.Currency, e.EventId);
            await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaymentMismatch", null,
                JsonSerializer.Serialize(new { ExpectedCents = expectedCents, e.AmountCents, e.Currency, e.PaymentReference }));
            return PaymentEventOutcome.Rejected;
        }

        subscription.Status = SubscriptionStatus.Paid;
        subscription.PaymentMethod = SubscriptionPaymentMethod.Card;
        subscription.PaymentReference = e.PaymentReference;
        subscription.PaidAt = DateTime.UtcNow;
        await _subscriptions.UpdateAsync(subscription);

        await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaidByCard", null,
            JsonSerializer.Serialize(new { subscription.FeeIncGst, e.PaymentReference }));
        return PaymentEventOutcome.Processed;
    }
}
