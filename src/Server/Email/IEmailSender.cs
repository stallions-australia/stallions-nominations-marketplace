namespace Stallions.Server.Email;

/// <summary>Hands one email to the email service. Throws on failure; the dispatcher retries.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct);
}
