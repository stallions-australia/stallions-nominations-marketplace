using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Email;
using Stallions.Server.Tests.Helpers;

namespace Stallions.Server.Tests.Email;

public class EmailDispatcherTests
{
    private sealed class FakeSender : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = new();

        public int Attempts { get; set; }

        public bool Fail { get; set; }

        public Task SendAsync(OutgoingEmail email, CancellationToken ct)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("ACS unavailable");
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private readonly string _name = Guid.NewGuid().ToString();
    private readonly TestClock _clock = new();
    private readonly FakeSender _sender = new();

    private AppDbContext Db() => DbContextFactory.Create(_name);

    private EmailDispatcher Sut(AppDbContext db) =>
        new(new OutboundEmailRepository(db), _sender, _clock, NullLogger<EmailDispatcher>.Instance);

    private async Task<Guid> Queue(DateTime? nextAttemptAt = null)
    {
        await using var db = Db();
        var row = new OutboundEmail
        {
            ToAddress = "buyer@example.com", Subject = "S", HtmlBody = "<p>B</p>", TextBody = "B",
            Template = "Outbid", CreatedAt = _clock.UtcNow, NextAttemptAt = nextAttemptAt
        };
        db.OutboundEmails.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<OutboundEmail> Row(Guid id)
    {
        await using var db = Db();
        return await db.OutboundEmails.SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task SendsDueEmails_AndMarksThemSent()
    {
        var id = await Queue();

        var sent = await Sut(Db()).SendDueAsync();

        sent.Should().Be(1);
        _sender.Sent.Single().ToAddress.Should().Be("buyer@example.com");
        (await Row(id)).SentAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task AFailure_IsRetriedAfterABackoff()
    {
        var id = await Queue();
        _sender.Fail = true;

        await Sut(Db()).SendDueAsync();

        var row = await Row(id);
        row.SentAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("ACS unavailable");
        row.NextAttemptAt.Should().Be(_clock.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task NotYetDue_IsLeftAlone()
    {
        await Queue(nextAttemptAt: _clock.UtcNow.AddMinutes(5));

        (await Sut(Db()).SendDueAsync()).Should().Be(0);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AfterFiveFailures_GivesUp()
    {
        var id = await Queue();
        _sender.Fail = true;
        for (var i = 0; i < 5; i++)
        {
            await Sut(Db()).SendDueAsync();
            _clock.Advance(TimeSpan.FromHours(2));
        }

        var row = await Row(id);
        row.Attempts.Should().Be(5);
        row.FailedAt.Should().NotBeNull();

        _sender.Fail = false;
        (await Sut(Db()).SendDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AFailedEmail_IsNotRetriedBeforeItsBackoff()
    {
        var id = await Queue();
        _sender.Fail = true;
        await Sut(Db()).SendDueAsync();

        (await Sut(Db()).SendDueAsync()).Should().Be(0);

        _sender.Attempts.Should().Be(1);
        (await Row(id)).Attempts.Should().Be(1);
    }

    private static OutboundEmail DueRow(string to) => new()
    {
        ToAddress = to, Subject = "S", HtmlBody = "<p>B</p>", TextBody = "B", Template = "Outbid"
    };

    [Fact]
    public async Task ARowAnotherInstanceClaimed_IsSkipped()
    {
        var repo = new Mock<IOutboundEmailRepository>();
        repo.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(new List<OutboundEmail> { DueRow("a@example.com") });
        repo.Setup(r => r.TryUpdateAsync(It.IsAny<OutboundEmail>())).ReturnsAsync(false);
        var sut = new EmailDispatcher(repo.Object, _sender, _clock, NullLogger<EmailDispatcher>.Instance);

        (await sut.SendDueAsync()).Should().Be(0);

        _sender.Attempts.Should().Be(0);
        repo.Verify(r => r.UpdateAsync(It.IsAny<OutboundEmail>()), Times.Never);
    }

    [Fact]
    public async Task WhenRecordingTheResultFails_TheRestOfTheBatchStillSends()
    {
        var first = DueRow("a@example.com");
        var second = DueRow("b@example.com");
        var repo = new Mock<IOutboundEmailRepository>();
        repo.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(new List<OutboundEmail> { first, second });
        repo.Setup(r => r.TryUpdateAsync(It.IsAny<OutboundEmail>())).ReturnsAsync(true);
        repo.Setup(r => r.UpdateAsync(first)).ThrowsAsync(new DbUpdateConcurrencyException("conflict"));
        var sut = new EmailDispatcher(repo.Object, _sender, _clock, NullLogger<EmailDispatcher>.Instance);

        var act = () => sut.SendDueAsync();

        await act.Should().NotThrowAsync();
        _sender.Sent.Select(e => e.ToAddress).Should().Equal("a@example.com", "b@example.com");
    }
}
