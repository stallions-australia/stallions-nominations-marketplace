using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;

namespace Stallions.Server.Email;

public class EmailOutbox : IEmailOutbox
{
    private readonly IOutboundEmailRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutbox> _log;

    public EmailOutbox(IOutboundEmailRepository repo, TimeProvider clock, ILogger<EmailOutbox> log)
    {
        _repo = repo;
        _clock = clock;
        _log = log;
    }

    public async Task EnqueueAsync(OutgoingEmail email, string? relatedEntityType = null, Guid? relatedEntityId = null)
    {
        if (string.IsNullOrWhiteSpace(email.ToAddress))
        {
            _log.LogWarning("Email {Template} for {EntityType} {EntityId} skipped: no recipient address",
                email.Template, relatedEntityType, relatedEntityId);
            return;
        }

        await _repo.AddAsync(new OutboundEmail
        {
            ToAddress = email.ToAddress.Trim(),
            Subject = CapSubject(email.Subject),
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody,
            Template = email.Template,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        });
    }

    /// <summary>The OutboundEmails.Subject column length.</summary>
    public const int MaxSubjectLength = 300;

    /// <summary>
    /// Every email passes through here, so the subject is capped centrally: a Staff subject with long placeholder values
    /// must never fail the insert (it can share a transaction with a buyer-fee charge). Truncated rather than rejected.
    /// </summary>
    private static string CapSubject(string subject)
    {
        if (subject.Length <= MaxSubjectLength) return subject;
        var keep = MaxSubjectLength - 1;
        if (char.IsHighSurrogate(subject[keep - 1])) keep--; // don't split a surrogate pair
        return subject[..keep] + "…";
    }
}
