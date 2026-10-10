using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Email;

public class EmailOutboxTests
{
    [Fact]
    public async Task Enqueue_SavesAnUnsentRow()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var clock = new TestClock();
        var outbox = new EmailOutbox(new OutboundEmailRepository(db), clock, NullLogger<EmailOutbox>.Instance);
        var listingId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OutgoingEmail("buyer@example.com", "Subject", "<p>Hi</p>", "Hi", "Outbid"),
            "Listing", listingId);

        var row = await db.OutboundEmails.SingleAsync();
        row.ToAddress.Should().Be("buyer@example.com");
        row.Template.Should().Be("Outbid");
        row.RelatedEntityId.Should().Be(listingId);
        row.CreatedAt.Should().Be(clock.UtcNow);
        row.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_WithoutAnAddress_IsSkipped()
    {
        using var db = DbContextFactory.Create(Guid.NewGuid().ToString());
        var outbox = new EmailOutbox(new OutboundEmailRepository(db), new TestClock(), NullLogger<EmailOutbox>.Instance);

        await outbox.EnqueueAsync(new OutgoingEmail(" ", "Subject", "<p>Hi</p>", "Hi", "Outbid"));

        (await db.OutboundEmails.CountAsync()).Should().Be(0);
    }
}
