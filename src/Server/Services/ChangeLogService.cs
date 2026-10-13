using System.Globalization;
using System.Text.Json;
using Stallions.Server.Content;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Shared.DTOs.ChangeLog;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public interface IChangeLogService
{
    /// <summary>Staff only: changes to settings, business details, intro offers, wording and terms, newest first, 50 a page.</summary>
    Task<ServiceResult<ChangeLogPageDto>> GetAsync(ChangeLogFilter filter);
}

public class ChangeLogService : IChangeLogService
{
    public const int PageSize = 50;
    private const int MaxPage = 1_000_000; // keeps skip well inside int range
    private const string Changed = "changed";
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    /// <summary>The audit actions shown, and their area. Action names are unique across entity types.</summary>
    private static readonly IReadOnlyDictionary<string, string> AreaByAction = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UpdatePlatformSettings"] = ChangeLogAreas.Settings,
        ["UpdateBusinessDetails"] = ChangeLogAreas.BusinessDetails,
        ["CreateIntroOffer"] = ChangeLogAreas.IntroOffers,
        ["UpdateIntroOffer"] = ChangeLogAreas.IntroOffers,
        ["RetireIntroOffer"] = ChangeLogAreas.IntroOffers,
        [ContentService.AuditAction] = ChangeLogAreas.Wording,
        [TermsService.PublishAuditAction] = ChangeLogAreas.Terms,
        [TermsService.AcceptStudAuditAction] = ChangeLogAreas.StudTermsAccepted
    };

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BuyerFeeIncGst"] = "Buyer fee (inc. GST)",
        ["StandardListingFeeIncGst"] = "Standard listing fee (inc. GST)",
        ["MinimumBidIncrement"] = "Minimum bid increment",
        ["ChargeGracePeriodHours"] = "Charge grace period (hours)",
        ["OfferExpiryDays"] = "Offer expiry (days)",
        ["BusinessLegalName"] = "Legal name",
        ["BusinessAbn"] = "ABN",
        ["BusinessAddress"] = "Address",
        ["BusinessEmail"] = "Accounts email",
        ["BankAccountName"] = "Bank account name",
        ["BankBsb"] = "BSB",
        ["BankAccountNumber"] = "Bank account number",
        ["InvoicePaymentTermsDays"] = "Payment terms (days)",
        ["Name"] = "Name",
        ["Type"] = "Type",
        ["AmountOffIncGst"] = "Amount off (inc. GST)",
        ["StudFarmId"] = "Stud farm",
        ["SeasonId"] = "Season",
        ["EndsAt"] = "Ends (UTC)",
        ["IsRetired"] = "Retired"
    };

    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly IStudFarmRepository _farms;
    private readonly ISeasonRepository _seasons;

    public ChangeLogService(IAuditLogRepository audit, IUserService users, IStudFarmRepository farms, ISeasonRepository seasons)
    {
        _audit = audit;
        _users = users;
        _farms = farms;
        _seasons = seasons;
    }

    public async Task<ServiceResult<ChangeLogPageDto>> GetAsync(ChangeLogFilter filter)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<ChangeLogPageDto>.Forbidden("Only Stallions Australia staff can see the change log.");
        if (filter.Area != null && !ChangeLogAreas.IsKnown(filter.Area))
            return ServiceResult<ChangeLogPageDto>.BadRequest("Choose one of the listed areas.");
        if (filter.From is { } fromDate && filter.To is { } toDate && fromDate > toDate)
            return ServiceResult<ChangeLogPageDto>.BadRequest("The From date must be on or before the To date.");

        var page = Math.Clamp(filter.Page, 1, MaxPage);
        var actions = AreaByAction.Where(a => filter.Area == null || a.Value == filter.Area).Select(a => a.Key).ToList();
        DateTime? fromUtc = filter.From is { } f && f > DateOnly.MinValue ? SydneyTime.StartOfDayUtc(f) : null;
        DateTime? toUtc = filter.To is { } t && t < DateOnly.MaxValue ? SydneyTime.StartOfDayUtc(t.AddDays(1)) : null;
        var (rows, total) = await _audit.GetPageAsync(actions, fromUtc, toUtc, (page - 1) * PageSize, PageSize);

        var names = await ResolveNamesAsync(rows);

        return ServiceResult<ChangeLogPageDto>.Ok(new ChangeLogPageDto
        {
            Entries = rows.Select(r => Map(r, names)).ToList(),
            Page = page,
            PageSize = PageSize,
            Total = total
        });
    }

    /// <summary>Names for the stud farm and season ids that intro offer changes refer to. An id that no longer resolves is left out.</summary>
    private async Task<Dictionary<(string Field, string Id), string>> ResolveNamesAsync(IEnumerable<AuditLog> rows)
    {
        var wanted = new HashSet<(string Field, string Id)>();
        foreach (var row in rows.Where(r => AreaByAction[r.Action] == ChangeLogAreas.IntroOffers))
        {
            using var doc = Parse(row.Details);
            JsonElement? root = doc?.RootElement;
            foreach (var side in new[] { Prop(root, "Old"), Prop(root, "New") })
                foreach (var field in new[] { "StudFarmId", "SeasonId" })
                    if (Text(side, field) is { } id) wanted.Add((field, id));
        }

        var names = new Dictionary<(string Field, string Id), string>();
        foreach (var (field, id) in wanted)
        {
            if (!Guid.TryParse(id, out var guid)) continue;
            var name = field == "StudFarmId"
                ? (await _farms.GetByIdAsync(guid))?.Name
                : (await _seasons.GetByIdAsync(guid))?.Name;
            if (!string.IsNullOrWhiteSpace(name)) names[(field, id)] = name;
        }
        return names;
    }

    private static ChangeLogEntryDto Map(AuditLog a, IReadOnlyDictionary<(string Field, string Id), string> names)
    {
        var area = AreaByAction[a.Action];
        var utc = DateTime.SpecifyKind(a.OccurredAt, DateTimeKind.Utc);
        var entry = new ChangeLogEntryDto
        {
            Id = a.Id,
            OccurredAt = utc,
            When = TimeZoneInfo.ConvertTimeFromUtc(utc, SydneyTime.Zone).ToString("d MMM yyyy, h:mm tt", Au),
            Who = a.User?.DisplayName ?? "System",
            Area = area,
            AreaLabel = ChangeLogAreas.Label(area)
        };

        using var doc = Parse(a.Details);
        JsonElement? root = doc?.RootElement;
        switch (area)
        {
            case ChangeLogAreas.Settings:
                entry.Item = "Platform settings";
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"), names);
                break;
            case ChangeLogAreas.BusinessDetails:
                entry.Item = "Business details";
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"), names);
                break;
            case ChangeLogAreas.IntroOffers:
                entry.Item = Text(Prop(root, "New"), "Name") ?? Text(Prop(root, "Old"), "Name") ?? "Intro offer";
                entry.Note = a.Action switch { "CreateIntroOffer" => "Created", "RetireIntroOffer" => "Retired", _ => "Edited" };
                entry.Changes = Diff(Prop(root, "Old"), Prop(root, "New"), names);
                break;
            case ChangeLogAreas.Wording:
                MapWording(entry, root);
                break;
            case ChangeLogAreas.Terms:
                MapTerms(entry, root);
                break;
            default: // StudTermsAccepted
                entry.Item = Text(root, "StudFarmName") ?? "Stud farm";
                entry.Changes = [new ChangeLogChangeDto { Field = "Accepted version", NewValue = Text(root, "Version") }];
                break;
        }
        return entry;
    }

    private static void MapWording(ChangeLogEntryDto entry, JsonElement? root)
    {
        var key = Text(root, "Key") ?? string.Empty;
        var version = Text(root, "Version") ?? "?";
        entry.Item = $"{ContentCatalog.Find(key)?.Label ?? key} — version {version}";
        entry.Note = Text(root, "Source") switch
        {
            "Restore" => $"Restored version {Text(root, "RestoredFromVersion")}",
            "Reset" => "Reset to the default wording",
            _ => null
        };
        entry.Link = $"/staff/wording?key={Uri.EscapeDataString(key)}&version={version}";

        var old = Prop(root, "Old");
        var @new = Prop(root, "New");
        var changes = new List<ChangeLogChangeDto>();
        var oldSubject = Text(old, "Subject");
        var newSubject = Text(@new, "Subject");
        if (oldSubject != newSubject)
            changes.Add(new ChangeLogChangeDto { Field = "Subject", OldValue = oldSubject, NewValue = newSubject });
        if (Text(old, "Body") != Text(@new, "Body"))
            changes.Add(new ChangeLogChangeDto { Field = "Message", IsLongText = true });
        entry.Changes = changes;
    }

    private static void MapTerms(ChangeLogEntryDto entry, JsonElement? root)
    {
        var audience = Text(root, "Audience") == "Stud" ? "Stud" : "Buyer";
        var version = Text(root, "Version") ?? "?";
        entry.Item = $"{(audience == "Stud" ? "Stud listing terms" : "Buyer T&C")} — version {version}";
        entry.Link = $"/staff/terms?audience={audience}&version={version}";
        entry.Changes = [new ChangeLogChangeDto { Field = "Text", IsLongText = true }];
    }

    /// <summary>Old → new for every field whose value differs; unchanged fields are left out.</summary>
    private static List<ChangeLogChangeDto> Diff(JsonElement? old, JsonElement? @new,
        IReadOnlyDictionary<(string Field, string Id), string> names)
    {
        var fields = new List<string>();
        foreach (var side in new[] { @new, old })
            if (side is { ValueKind: JsonValueKind.Object } obj)
                foreach (var p in obj.EnumerateObject())
                    if (!fields.Contains(p.Name)) fields.Add(p.Name);

        var changes = new List<ChangeLogChangeDto>();
        foreach (var field in fields)
        {
            var before = Text(old, field);
            var after = Text(@new, field);
            if (before == after) continue;
            changes.Add(new ChangeLogChangeDto
            {
                Field = FieldLabels.TryGetValue(field, out var label) ? label : field,
                OldValue = Display(field, before, names),
                NewValue = Display(field, after, names)
            });
        }
        return changes;
    }

    /// <summary>
    /// Bank account numbers show only their last 3 digits (the stored audit row is unchanged); stud farm and season
    /// ids show their name, or "changed" when the id no longer resolves — Staff never see a raw id.
    /// </summary>
    private static string? Display(string field, string? value, IReadOnlyDictionary<(string Field, string Id), string> names)
    {
        if (value == null) return null;
        return field switch
        {
            "BankAccountNumber" => value.Length <= 3 ? "•••" : "•••" + value[^3..],
            "StudFarmId" or "SeasonId" => names.TryGetValue((field, value), out var name) ? name : Changed,
            _ => value
        };
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static JsonElement? Prop(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static string? Text(JsonElement? obj, string name) => Prop(obj, name) is { } v ? Format(v) : null;

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number.ToString("0.############################", CultureInfo.InvariantCulture),
        _ => value.GetRawText()
    };
}
