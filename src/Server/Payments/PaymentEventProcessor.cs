using System.Text.Json;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Payments;

/// <summary>
/// The only code that changes data because of a payment. Provider-neutral and idempotent:
/// each provider event id is processed once. Claim, changes and completion happen in one
/// transaction, so any exception rolls everything back and the provider's retry processes the
/// event again.
/// </summary>
public class PaymentEventProcessor : IPaymentEventProcessor
{
    private readonly IProcessedPaymentEventRepository _processed;
    private readonly ISavedCardRepository _cards;
    private readonly IUserRepository _users;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentProvider _provider;
    private readonly IAuditLogRepository _audit;
    private readonly ITransactionRunner _transactions;
    private readonly ILogger<PaymentEventProcessor> _log;

    public PaymentEventProcessor(
        IProcessedPaymentEventRepository processed, ISavedCardRepository cards, IUserRepository users,
        ISubscriptionRepository subscriptions, IPaymentProvider provider, IAuditLogRepository audit,
        ITransactionRunner transactions, ILogger<PaymentEventProcessor> log)
    {
        _processed = processed; _cards = cards; _users = users; _subscriptions = subscriptions;
        _provider = provider; _audit = audit; _transactions = transactions; _log = log;
    }

    /// <summary>A handler's outcome, plus a replaced payment method to detach once committed.</summary>
    private sealed record HandlerResult(PaymentEventOutcome Outcome, string? PaymentMethodToDetach = null);

    public async Task<PaymentEventOutcome> ProcessAsync(PaymentEvent paymentEvent)
    {
        HandlerResult result;
        try
        {
            result = await _transactions.RunAsync(async () =>
            {
                // Claim the event id first: a repeated or concurrent delivery doesn't get it.
                var claim = await _processed.ClaimAsync(new ProcessedPaymentEvent
                {
                    EventId = paymentEvent.EventId,
                    Provider = _provider.Name,
                    Type = paymentEvent.GetType().Name,
                    ProcessedAt = DateTime.UtcNow
                });
                if (claim == ClaimResult.AlreadyCompleted) return new HandlerResult(PaymentEventOutcome.Duplicate);
                if (claim == ClaimResult.InProgress) return new HandlerResult(PaymentEventOutcome.InProgress);

                var handled = paymentEvent switch
                {
                    CardSavedEvent card => await SaveCardAsync(card),
                    ListingFeePaidEvent fee => await MarkListingFeePaidAsync(fee),
                    _ => new HandlerResult(PaymentEventOutcome.Ignored)
                };
                // Rejected and Ignored are final too — a retry wouldn't change them.
                await _processed.MarkCompletedAsync(paymentEvent.EventId);
                return handled;
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Processing payment event {EventId} failed", paymentEvent.EventId);
            try
            {
                // After a real rollback this is a no-op; providers without transactions need it
                // so the provider's retry is processed.
                await _processed.ReleaseAsync(paymentEvent.EventId);
            }
            catch (Exception releaseEx)
            {
                _log.LogError(releaseEx,
                    "Could not release the claim on payment event {EventId}; a retry may be treated as in progress until the claim times out ({ClaimTimeout})",
                    paymentEvent.EventId, ProcessedPaymentEventRepository.ClaimTimeout);
            }
            throw;
        }

        // Only after the commit, and best effort: a stale card left at the provider is harmless.
        if (result.PaymentMethodToDetach is { } oldId)
        {
            try { await _provider.DetachCardAsync(oldId); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not detach replaced card {PaymentMethodId}", oldId);
            }
        }
        return result.Outcome;
    }

    private async Task<HandlerResult> SaveCardAsync(CardSavedEvent e)
    {
        var user = await _users.GetByIdAsync(e.UserId);
        if (user == null)
        {
            _log.LogError("Card saved for unknown user {UserId} (event {EventId})", e.UserId, e.EventId);
            return new HandlerResult(PaymentEventOutcome.Rejected);
        }

        var existing = await _cards.GetByUserIdAsync(e.UserId);
        string? replaced = existing != null && existing.ProviderPaymentMethodId != e.PaymentMethodId
            ? existing.ProviderPaymentMethodId
            : null;

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

        if (user.PaymentCustomerProvider == _provider.Name
            && !string.IsNullOrEmpty(user.PaymentCustomerId) && user.PaymentCustomerId != e.CustomerId)
        {
            _log.LogWarning(
                "Card saved for user {UserId} under customer {NewCustomerId}, replacing stored customer {OldCustomerId} (event {EventId})",
                e.UserId, e.CustomerId, user.PaymentCustomerId, e.EventId);
        }
        if (user.PaymentCustomerId != e.CustomerId || user.PaymentCustomerProvider != _provider.Name)
        {
            user.PaymentCustomerId = e.CustomerId;
            user.PaymentCustomerProvider = _provider.Name;
            await _users.UpdateAsync(user);
        }

        await _audit.LogAsync("SavedCard", card.Id, existing == null ? "SaveCard" : "ReplaceCard", e.UserId,
            JsonSerializer.Serialize(new { e.Brand, e.Last4, e.ExpMonth, e.ExpYear }));
        return new HandlerResult(PaymentEventOutcome.Processed, replaced);
    }

    private async Task<HandlerResult> MarkListingFeePaidAsync(ListingFeePaidEvent e)
    {
        var subscription = await _subscriptions.GetByIdAsync(e.SubscriptionId);
        if (subscription == null)
        {
            _log.LogError("Listing fee paid for unknown subscription {SubscriptionId} (event {EventId})",
                e.SubscriptionId, e.EventId);
            await _audit.LogAsync("StallionSeasonSubscription", e.SubscriptionId,
                "ListingFeePaymentForUnknownSubscription", null,
                JsonSerializer.Serialize(new { e.PaymentReference, e.AmountCents }));
            return new HandlerResult(PaymentEventOutcome.Rejected);
        }

        if (subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived)
        {
            // Money was taken for a subscription that didn't need it: Staff may need to refund.
            _log.LogError(
                "Listing fee card payment {PaymentReference} received for subscription {SubscriptionId} that is already {Status} (event {EventId}); a refund may be needed",
                e.PaymentReference, e.SubscriptionId, subscription.Status, e.EventId);
            await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeeDuplicatePayment", null,
                JsonSerializer.Serialize(new
                {
                    e.PaymentReference, e.AmountCents, e.Currency, Status = subscription.Status.ToString()
                }));
            return new HandlerResult(PaymentEventOutcome.Ignored);
        }

        var expectedCents = PaymentAmounts.ToCents(subscription.FeeIncGst);
        if (e.AmountCents != expectedCents || !string.Equals(e.Currency, "aud", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogError(
                "Listing fee payment mismatch for subscription {SubscriptionId}: expected {Expected} AUD cents, got {Amount} {Currency} (event {EventId})",
                e.SubscriptionId, expectedCents, e.AmountCents, e.Currency, e.EventId);
            await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaymentMismatch", null,
                JsonSerializer.Serialize(new { ExpectedCents = expectedCents, e.AmountCents, e.Currency, e.PaymentReference }));
            return new HandlerResult(PaymentEventOutcome.Rejected);
        }

        subscription.Status = SubscriptionStatus.Paid;
        subscription.PaymentMethod = SubscriptionPaymentMethod.Card;
        subscription.PaymentReference = e.PaymentReference;
        subscription.PaidAt = DateTime.UtcNow;
        await _subscriptions.UpdateAsync(subscription);

        await _audit.LogAsync("StallionSeasonSubscription", subscription.Id, "ListingFeePaidByCard", null,
            JsonSerializer.Serialize(new { subscription.FeeIncGst, e.PaymentReference }));
        return new HandlerResult(PaymentEventOutcome.Processed);
    }
}
