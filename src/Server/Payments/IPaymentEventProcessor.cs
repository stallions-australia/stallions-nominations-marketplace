namespace Stallions.Server.Payments;

public enum PaymentEventOutcome { Processed, Duplicate, Ignored, Rejected }

public interface IPaymentEventProcessor
{
    Task<PaymentEventOutcome> ProcessAsync(PaymentEvent paymentEvent);
}
