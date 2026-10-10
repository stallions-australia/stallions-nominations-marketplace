using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public interface IEmailDispatcher
{
    /// <summary>Sends due emails; returns how many were sent.</summary>
    Task<int> SendDueAsync(CancellationToken ct = default);
}

public class EmailDispatcher : IEmailDispatcher
{
    public const int MaxAttempts = 5;
    private const int BatchSize = 50;

    // Wait after the 1st, 2nd, 3rd and 4th failure.
    private static readonly TimeSpan[] Backoff =
        { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };

    // While one instance sends a row, it is "not due" for the others.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly IOutboundEmailRepository _repo;
    private readonly IEmailSender _sender;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailDispatcher> _log;

    public EmailDispatcher(IOutboundEmailRepository repo, IEmailSender sender, TimeProvider clock, ILogger<EmailDispatcher> log)
    {
        _repo = repo;
        _sender = sender;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Delivery is at-least-once: a send that succeeds but whose result can't be recorded is retried
    /// after the lease expires.
    /// </summary>
    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var email in await _repo.GetDueAsync(_clock.GetUtcNow().UtcDateTime, BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var now = _clock.GetUtcNow().UtcDateTime;
            email.Attempts++;
            email.NextAttemptAt = now + Lease;
            if (!await _repo.TryUpdateAsync(email)) continue; // another instance has it

            try
            {
                await _sender.SendAsync(
                    new OutgoingEmail(email.ToAddress, email.Subject, email.HtmlBody, email.TextBody, email.Template), ct);
                email.SentAt = _clock.GetUtcNow().UtcDateTime;
                email.NextAttemptAt = null;
                email.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (email.Attempts >= MaxAttempts)
                {
                    email.FailedAt = now;
                    email.NextAttemptAt = null;
                    _log.LogError(ex, "Gave up sending email {EmailId} ({Template}) after {Attempts} attempts",
                        email.Id, email.Template, email.Attempts);
                }
                else
                {
                    email.NextAttemptAt = now + Backoff[email.Attempts - 1];
                    _log.LogWarning(ex, "Sending email {EmailId} ({Template}) failed; retrying at {NextAttemptAt}",
                        email.Id, email.Template, email.NextAttemptAt);
                }
            }

            try
            {
                await _repo.UpdateAsync(email);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Email {EmailId} ({Template}) was processed but its result could not be recorded",
                    email.Id, email.Template);
            }
        }

        return sent;
    }
}
