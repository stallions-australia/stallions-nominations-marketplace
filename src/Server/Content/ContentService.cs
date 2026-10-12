using System.Text.Json;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;
using Stallions.Shared.Enums;

namespace Stallions.Server.Content;

/// <summary>The current text of a wording item. Version 0 = nothing stored, so the catalog default is used.</summary>
public sealed record ContentText(string Key, int Version, string? Subject, string Body);

/// <summary>An email's rendered subject and message paragraphs; the email code adds its generated blocks.</summary>
public sealed record RenderedEmail(string Subject, IReadOnlyList<RenderedParagraph> Paragraphs);

public interface IContentService
{
    /// <summary>The current text for a catalog key (cached 60 s). Throws ArgumentException for an unknown key.</summary>
    Task<ContentText> GetAsync(string key);
    /// <summary>A disclosure's paragraphs, encoded for HTML.</summary>
    Task<IReadOnlyList<RenderedParagraph>> RenderTextAsync(string key);
    /// <summary>An email's subject and message with the values substituted (and encoded in the HTML form).</summary>
    Task<RenderedEmail> RenderEmailAsync(string key, IReadOnlyDictionary<string, string?> values);

    // Staff — StaffOnly at the controller and re-checked here.
    Task<ServiceResult<IReadOnlyList<ContentItemSummaryDto>>> ListAsync();
    Task<ServiceResult<ContentItemDetailDto>> GetDetailAsync(string key);
    Task<ServiceResult<IReadOnlyList<ContentVersionDto>>> GetHistoryAsync(string key);
    Task<ServiceResult<ContentItemDetailDto>> SaveAsync(string key, SaveContentRequest request);
    Task<ServiceResult<ContentItemDetailDto>> RestoreAsync(string key, int version);
    Task<ServiceResult<ContentItemDetailDto>> ResetAsync(string key);
    Task<ServiceResult<ContentPreviewDto>> PreviewAsync(string key, SaveContentRequest request);
}

public class ContentService : IContentService
{
    public const string AuditEntity = "ContentItem";
    public const string AuditAction = "UpdateWording";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage wording.";
    private const string UnknownKeyMessage = "That wording item doesn't exist.";

    private static readonly IReadOnlyDictionary<string, string?> NoValues = new Dictionary<string, string?>();

    private readonly IContentRepository _repo;
    private readonly ContentCache _cache;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;
    private readonly ILogger<ContentService> _log;

    public ContentService(IContentRepository repo, ContentCache cache, IAuditLogRepository audit, IUserService users,
        ITransactionRunner transactions, TimeProvider clock, ILogger<ContentService> log)
    {
        _repo = repo;
        _cache = cache;
        _audit = audit;
        _users = users;
        _transactions = transactions;
        _clock = clock;
        _log = log;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static ContentCatalogEntry Entry(string key) =>
        ContentCatalog.Find(key) ?? throw new ArgumentException($"Unknown wording key '{key}'.", nameof(key));

    /// <summary>
    /// The text exactly as ContentTemplate.Validate checks it: line endings normalised and trimmed; no subject for
    /// a disclosure. This — never the raw input — is what is stored, audited and previewed.
    /// </summary>
    private static (string? Subject, string Body) Clean(ContentCatalogEntry entry, string? subject, string? body) =>
        (entry.HasSubject ? (subject ?? string.Empty).Trim() : null, ContentTemplate.Normalise(body).Trim());

    // ── Reading and rendering ───────────────────────────────────────────────

    public async Task<ContentText> GetAsync(string key)
    {
        var entry = Entry(key);
        if (_cache.TryGet(key, out var cached)) return cached;

        var row = await _repo.GetCurrentAsync(key);
        ContentText text;
        if (row == null)
        {
            // The migration seeds every key, so this means a missing row: never send a blank email or disclosure.
            _log.LogWarning("No wording is stored for {Key}; using the catalog default", key);
            text = new ContentText(key, 0, entry.DefaultSubject, entry.DefaultBody);
        }
        else
        {
            text = new ContentText(key, row.Version, row.Subject, row.Body);
        }
        _cache.Set(key, text);
        return text;
    }

    public async Task<IReadOnlyList<RenderedParagraph>> RenderTextAsync(string key)
    {
        var text = await GetAsync(key);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var paragraphs = ContentTemplate.RenderBody(text.Body, NoValues, missing);
        WarnMissing(key, text.Version, missing);
        return paragraphs;
    }

    public async Task<RenderedEmail> RenderEmailAsync(string key, IReadOnlyDictionary<string, string?> values)
    {
        var entry = Entry(key);
        var text = await GetAsync(key);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var subject = ContentTemplate.RenderLine(text.Subject ?? string.Empty, values, missing);
        var paragraphs = ContentTemplate.RenderBody(text.Body, values, missing);
        WarnMissing(key, text.Version, missing);

        var unexpected = values.Keys.Where(k => !entry.Allows(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (unexpected.Count > 0)
            _log.LogWarning("Wording {Key} was given values that aren't in its catalog entry: {Names}",
                key, string.Join(", ", unexpected));

        return new RenderedEmail(subject, paragraphs);
    }

    private void WarnMissing(string key, int version, IReadOnlyCollection<string> missing)
    {
        if (missing.Count > 0)
            _log.LogWarning("Wording {Key} version {Version} uses placeholders with no value: {Names}",
                key, version, string.Join(", ", missing));
    }

    // ── Staff ───────────────────────────────────────────────────────────────

    private async Task<User?> StaffAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller is { Role: UserRole.Staff } ? caller : null;
    }

    public async Task<ServiceResult<IReadOnlyList<ContentItemSummaryDto>>> ListAsync()
    {
        if (await StaffAsync() == null)
            return ServiceResult<IReadOnlyList<ContentItemSummaryDto>>.Forbidden(StaffOnlyMessage);

        var current = (await _repo.GetCurrentForAllAsync()).ToDictionary(c => c.Key, StringComparer.Ordinal);
        var list = ContentCatalog.Entries.Select(e =>
        {
            current.TryGetValue(e.Key, out var row);
            return new ContentItemSummaryDto
            {
                Key = e.Key,
                Group = e.Group,
                Label = e.Label,
                Description = e.Description,
                HasSubject = e.HasSubject,
                CurrentVersion = row?.Version ?? 0,
                ChangedAt = row?.CreatedAt,
                ChangedByName = row?.CreatedBy?.DisplayName
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<ContentItemSummaryDto>>.Ok(list);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> GetDetailAsync(string key)
    {
        if (await StaffAsync() == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);
        return ServiceResult<ContentItemDetailDto>.Ok(await DetailAsync(entry));
    }

    public async Task<ServiceResult<IReadOnlyList<ContentVersionDto>>> GetHistoryAsync(string key)
    {
        if (await StaffAsync() == null) return ServiceResult<IReadOnlyList<ContentVersionDto>>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry)
            return ServiceResult<IReadOnlyList<ContentVersionDto>>.NotFound(UnknownKeyMessage);

        var rows = await _repo.GetHistoryAsync(entry.Key);
        var list = rows.Select((r, i) => new ContentVersionDto
        {
            Version = r.Version,
            Subject = r.Subject,
            Body = r.Body,
            CreatedAt = r.CreatedAt,
            CreatedByName = r.CreatedBy?.DisplayName,
            IsCurrent = i == 0
        }).ToList();
        return ServiceResult<IReadOnlyList<ContentVersionDto>>.Ok(list);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> SaveAsync(string key, SaveContentRequest request)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        var errors = ContentTemplate.Validate(entry, request.Subject, request.Body);
        if (errors.Count > 0) return ServiceResult<ContentItemDetailDto>.BadRequest(string.Join("\n", errors));

        var (subject, body) = Clean(entry, request.Subject, request.Body);
        return await AddVersionAsync(entry, staff, subject, body, "Save", restoredFrom: null);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> RestoreAsync(string key, int version)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        var old = await _repo.GetVersionAsync(entry.Key, version);
        if (old == null) return ServiceResult<ContentItemDetailDto>.NotFound($"Version {version} doesn't exist.");

        // The catalog may have changed since that version was written (e.g. a placeholder became required).
        var errors = ContentTemplate.Validate(entry, old.Subject, old.Body);
        if (errors.Count > 0)
            return ServiceResult<ContentItemDetailDto>.BadRequest(
                $"Version {version} can't be restored as it is:\n" + string.Join("\n", errors));

        var (subject, body) = Clean(entry, old.Subject, old.Body);
        return await AddVersionAsync(entry, staff, subject, body, "Restore", version);
    }

    public async Task<ServiceResult<ContentItemDetailDto>> ResetAsync(string key)
    {
        var staff = await StaffAsync();
        if (staff == null) return ServiceResult<ContentItemDetailDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentItemDetailDto>.NotFound(UnknownKeyMessage);

        var (subject, body) = Clean(entry, entry.DefaultSubject, entry.DefaultBody);
        return await AddVersionAsync(entry, staff, subject, body, "Reset", restoredFrom: null);
    }

    public async Task<ServiceResult<ContentPreviewDto>> PreviewAsync(string key, SaveContentRequest request)
    {
        if (await StaffAsync() == null) return ServiceResult<ContentPreviewDto>.Forbidden(StaffOnlyMessage);
        if (ContentCatalog.Find(key) is not { } entry) return ServiceResult<ContentPreviewDto>.NotFound(UnknownKeyMessage);

        var errors = ContentTemplate.Validate(entry, request.Subject, request.Body);
        if (errors.Count > 0) return ServiceResult<ContentPreviewDto>.Ok(new ContentPreviewDto { Errors = errors.ToList() });

        var (subject, body) = Clean(entry, request.Subject, request.Body);
        var samples = entry.Placeholders.ToDictionary(p => p.Name, p => (string?)p.Sample, StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        return ServiceResult<ContentPreviewDto>.Ok(new ContentPreviewDto
        {
            Subject = subject == null ? null : ContentTemplate.RenderLine(subject, samples, missing),
            Paragraphs = ContentTemplate.RenderBody(body, samples, missing).Select(p => p.Text).ToList()
        });
    }

    /// <summary>Adds the next version and its audit row together; clears this instance's cache.</summary>
    private async Task<ServiceResult<ContentItemDetailDto>> AddVersionAsync(ContentCatalogEntry entry, User staff,
        string? subject, string body, string source, int? restoredFrom)
    {
        var unchanged = false;
        var conflict = false;
        ContentItem? added = null;
        await _transactions.RunAsync(async () =>
        {
            var current = await _repo.GetCurrentAsync(entry.Key);
            if (current != null && current.Subject == subject && current.Body == body)
            {
                unchanged = true;
                return false;
            }

            var item = new ContentItem
            {
                Key = entry.Key,
                Version = (current?.Version ?? 0) + 1,
                Subject = subject,
                Body = body,
                CreatedAt = Now,
                CreatedByUserId = staff.Id
            };
            if (!await _repo.TryAddAsync(item))
            {
                conflict = true;
                return false;
            }

            await _audit.LogAsync(AuditEntity, item.Id, AuditAction, staff.Id, JsonSerializer.Serialize(new
            {
                entry.Key,
                item.Version,
                Source = source,
                RestoredFromVersion = restoredFrom,
                Old = current == null ? null : new { current.Version, current.Subject, current.Body },
                New = new { item.Subject, item.Body }
            }));
            added = item;
            return true;
        });

        if (unchanged)
            return ServiceResult<ContentItemDetailDto>.BadRequest(source == "Save"
                ? "Nothing has changed since the current version."
                : "That is already the current wording.");
        if (conflict || added == null)
            return ServiceResult<ContentItemDetailDto>.Conflict(
                "Someone else saved this wording at the same moment. Reload it and try again.");

        _cache.Clear();
        _log.LogInformation("Staff {UserId} saved {Key} version {Version} ({Source})", staff.Id, entry.Key, added.Version, source);
        return ServiceResult<ContentItemDetailDto>.Ok(await DetailAsync(entry));
    }

    private async Task<ContentItemDetailDto> DetailAsync(ContentCatalogEntry entry)
    {
        var row = await _repo.GetCurrentAsync(entry.Key);
        var subject = row == null ? entry.DefaultSubject : row.Subject;
        var body = row?.Body ?? entry.DefaultBody;
        return new ContentItemDetailDto
        {
            Key = entry.Key,
            Group = entry.Group,
            Label = entry.Label,
            Description = entry.Description,
            HasSubject = entry.HasSubject,
            SingleParagraph = entry.SingleParagraph,
            Subject = subject,
            Body = body,
            Version = row?.Version ?? 0,
            ChangedAt = row?.CreatedAt,
            ChangedByName = row?.CreatedBy?.DisplayName,
            IsDefault = subject == entry.DefaultSubject && body == entry.DefaultBody,
            Placeholders = entry.Placeholders.Select(p => new ContentPlaceholderDto
            {
                Name = p.Name, Description = p.Description, Sample = p.Sample, Required = entry.Required.Contains(p.Name)
            }).ToList(),
            MaxSubjectLength = ContentCatalog.MaxSubjectLength,
            MaxBodyLength = ContentCatalog.MaxBodyLength
        };
    }
}
