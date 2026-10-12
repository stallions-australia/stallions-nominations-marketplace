using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;

namespace Stallions.Server.Tests.Helpers;

/// <summary>A real ContentService over a test database (call Database.EnsureCreated() first for the seeded wording).</summary>
public static class ContentTestData
{
    public static ContentService Service(AppDbContext db, TimeProvider? clock = null, ILogger<ContentService>? log = null,
        IUserService? users = null, ContentCache? cache = null)
    {
        var time = clock ?? TimeProvider.System;
        return new ContentService(new ContentRepository(db), cache ?? new ContentCache(time), new AuditLogRepository(db),
            users ?? new Mock<IUserService>().Object, new InlineTransactionRunner(), time,
            log ?? NullLogger<ContentService>.Instance);
    }

    /// <summary>Stores the next version of a key directly, as if Staff had saved it (no validation).</summary>
    public static void SetWording(AppDbContext db, string key, string? subject, string body)
    {
        var current = db.ContentItems.Where(c => c.Key == key).Select(c => (int?)c.Version).Max() ?? 0;
        db.ContentItems.Add(new ContentItem
        {
            Key = key, Version = current + 1, Subject = subject, Body = body, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }
}
