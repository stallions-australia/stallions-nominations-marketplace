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
            Subject = email.Subject,
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody,
            Template = email.Template,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        });
    }
}
