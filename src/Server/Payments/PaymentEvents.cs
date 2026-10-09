namespace Stallions.Server.Payments;

/// <summary>A provider callback, translated into terms the platform understands.</summary>
public abstract record PaymentEvent(string EventId);

/// <summary>A buyer finished saving a card on the hosted page.</summary>
public sealed record CardSavedEvent(
    string EventId, Guid UserId, string CustomerId, string PaymentMethodId,
    string Brand, string Last4, int ExpMonth, int ExpYear) : PaymentEvent(EventId);

/// <summary>A stud paid a listing-fee subscription on the hosted page. Amount in cents.</summary>
public sealed record ListingFeePaidEvent(
    string EventId, Guid SubscriptionId, long AmountCents, string Currency, string PaymentReference)
    : PaymentEvent(EventId);

/// <summary>Any event the platform doesn't act on; acknowledged and ignored.</summary>
public sealed record UnhandledPaymentEvent(string EventId, string Type) : PaymentEvent(EventId);

/// <summary>Thrown when a webhook's signature can't be verified.</summary>
public class PaymentSignatureException(string message) : Exception(message);
