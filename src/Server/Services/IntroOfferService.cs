using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Infrastructure;
using Stallions.Shared.DTOs.IntroOffers;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class IntroOfferService : IIntroOfferService
{
    private const string AuditEntity = "IntroOffer";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage intro offers.";
    private const decimal MaxAmountOffIncGst = 1_000_000m;

    private readonly IIntroOfferRepository _offers;
    private readonly IStudFarmRepository _farms;
    private readonly ISeasonRepository _seasons;
    private readonly IPlatformSettingsRepository _settings;
    private readonly IAuditLogRepository _audit;
    private readonly IUserService _users;
    private readonly TimeProvider _clock;

    public IntroOfferService(IIntroOfferRepository offers, IStudFarmRepository farms, ISeasonRepository seasons,
        IPlatformSettingsRepository settings, IAuditLogRepository audit, IUserService users, TimeProvider clock)
    {
        _offers = offers; _farms = farms; _seasons = seasons; _settings = settings; _audit = audit; _users = users; _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>The validated, normalised values of a create / edit request.</summary>
    private sealed record Validated(string Name, IntroOfferType Type, decimal? AmountOffIncGst, Guid? StudFarmId,
        Guid? SeasonId, DateTime EndsAt);

    public async Task<ServiceResult<IReadOnlyList<IntroOfferDto>>> GetAllAsync()
    {
        if (await GetStaffCallerAsync() == null)
            return ServiceResult<IReadOnlyList<IntroOfferDto>>.Forbidden(StaffOnlyMessage);

        var offers = await _offers.GetAllAsync();
        var applied = await _offers.GetTimesAppliedAsync();
        var now = Now;
        return ServiceResult<IReadOnlyList<IntroOfferDto>>.Ok(
            offers.Select(o => ToDto(o, applied.TryGetValue(o.Id, out var n) ? n : 0, now)).ToList());
    }

    public async Task<ServiceResult<IntroOfferDto>> CreateAsync(SaveIntroOfferRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var (values, error) = await ValidateAsync(request);
        if (values == null) return ServiceResult<IntroOfferDto>.BadRequest(error!);

        var now = Now;
        if (values.EndsAt <= now) return ServiceResult<IntroOfferDto>.BadRequest("The end date must be in the future.");

        var offer = new IntroOffer
        {
            Name = values.Name, Type = values.Type, AmountOffIncGst = values.AmountOffIncGst,
            StudFarmId = values.StudFarmId, SeasonId = values.SeasonId, EndsAt = values.EndsAt,
            CreatedAt = now, CreatedByUserId = staff.Id
        };
        await _offers.AddAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "CreateIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = (object?)null, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Created(ToDto(await _offers.GetByIdAsync(offer.Id) ?? offer, 0, now));
    }

    public async Task<ServiceResult<IntroOfferDto>> UpdateAsync(Guid id, SaveIntroOfferRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var offer = await _offers.GetByIdAsync(id);
        if (offer == null) return ServiceResult<IntroOfferDto>.NotFound("Intro offer not found.");
        if (offer.IsRetired) return ServiceResult<IntroOfferDto>.BadRequest("A retired offer can't be changed.");

        var (values, error) = await ValidateAsync(request);
        if (values == null) return ServiceResult<IntroOfferDto>.BadRequest(error!);

        // Recorded subscriptions name this offer: its terms must stay what they were when applied.
        var timesApplied = await _offers.CountTimesAppliedAsync(id);
        var termsChanged = values.Type != offer.Type || values.AmountOffIncGst != offer.AmountOffIncGst
                           || values.StudFarmId != offer.StudFarmId || values.SeasonId != offer.SeasonId;
        if (timesApplied > 0 && termsChanged)
            return ServiceResult<IntroOfferDto>.Conflict(
                $"This offer has been applied to {timesApplied} subscription(s), so only its name and end date can change.");

        var old = Snapshot(offer);
        offer.Name = values.Name;
        offer.Type = values.Type;
        offer.AmountOffIncGst = values.AmountOffIncGst;
        offer.StudFarmId = values.StudFarmId;
        offer.SeasonId = values.SeasonId;
        offer.EndsAt = values.EndsAt;
        offer.UpdatedAt = Now;
        offer.UpdatedByUserId = staff.Id;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "UpdateIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Ok(ToDto(await _offers.GetByIdAsync(id) ?? offer, timesApplied, Now));
    }

    public async Task<ServiceResult<IntroOfferDto>> RetireAsync(Guid id)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<IntroOfferDto>.Forbidden(StaffOnlyMessage);

        var offer = await _offers.GetByIdAsync(id);
        if (offer == null) return ServiceResult<IntroOfferDto>.NotFound("Intro offer not found.");
        var timesApplied = await _offers.CountTimesAppliedAsync(id);
        if (offer.IsRetired) return ServiceResult<IntroOfferDto>.Ok(ToDto(offer, timesApplied, Now));

        var old = Snapshot(offer);
        offer.IsRetired = true;
        offer.UpdatedAt = Now;
        offer.UpdatedByUserId = staff.Id;
        await _offers.UpdateAsync(offer);
        await _audit.LogAsync(AuditEntity, offer.Id, "RetireIntroOffer", staff.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(offer) }));

        return ServiceResult<IntroOfferDto>.Ok(ToDto(offer, timesApplied, Now));
    }

    public async Task<ServiceResult<IntroOfferPreviewDto>> PreviewAsync(Guid studFarmId, Guid seasonId)
    {
        if (await GetStaffCallerAsync() == null) return ServiceResult<IntroOfferPreviewDto>.Forbidden(StaffOnlyMessage);
        if (await _farms.GetByIdAsync(studFarmId) == null) return ServiceResult<IntroOfferPreviewDto>.NotFound("Stud farm not found.");
        if (await _seasons.GetByIdAsync(seasonId) == null) return ServiceResult<IntroOfferPreviewDto>.NotFound("Season not found.");

        var now = Now;
        var standard = (await _settings.GetAsync()).StandardListingFeeIncGst;
        var match = IntroOfferResolver.Resolve(studFarmId, seasonId, standard, now,
            await _offers.GetCandidatesAsync(studFarmId, seasonId, now));
        return ServiceResult<IntroOfferPreviewDto>.Ok(new IntroOfferPreviewDto
        {
            StandardFeeIncGst = standard,
            FeeIncGst = match.FeeIncGst,
            IntroOfferId = match.Offer?.Id,
            IntroOfferName = match.Offer?.Name
        });
    }

    private async Task<(Validated? Values, string? Error)> ValidateAsync(SaveIntroOfferRequest r)
    {
        var name = r.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return (null, "A name is required.");
        if (name.Length > 100) return (null, "The name can be at most 100 characters.");

        var typeName = Enum.GetNames<IntroOfferType>()
            .FirstOrDefault(n => string.Equals(n, r.Type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (typeName == null) return (null, "Type must be Waiver or AmountOff.");
        var type = Enum.Parse<IntroOfferType>(typeName);

        decimal? amount = null;
        if (type == IntroOfferType.AmountOff)
        {
            if (r.AmountOffIncGst is not { } a || a <= 0) return (null, "Enter the dollars off the listing fee (more than $0).");
            if (decimal.Round(a, 2) != a) return (null, "The amount is in dollars and cents.");
            if (a > MaxAmountOffIncGst) return (null, "The amount can't be more than $1,000,000.");
            amount = a;
        }

        if (r.StudFarmId is { } farmId && await _farms.GetByIdAsync(farmId) == null) return (null, "Stud farm not found.");
        if (r.SeasonId is { } seasonId && await _seasons.GetByIdAsync(seasonId) == null) return (null, "Season not found.");

        return (new Validated(name, type, amount, r.StudFarmId, r.SeasonId, SydneyTime.EndOfDayUtc(r.EndsOn)), null);
    }

    // Controllers enforce StaffOnly; checked again here because intro offers are fee settings.
    private async Task<User?> GetStaffCallerAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller?.Role == UserRole.Staff ? caller : null;
    }

    private static object Snapshot(IntroOffer o) => new
    {
        o.Name, Type = o.Type.ToString(), o.AmountOffIncGst, o.StudFarmId, o.SeasonId, o.EndsAt, o.IsRetired
    };

    private static IntroOfferDto ToDto(IntroOffer o, int timesApplied, DateTime now) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Type = o.Type.ToString(),
        AmountOffIncGst = o.AmountOffIncGst,
        StudFarmId = o.StudFarmId,
        StudFarmName = o.StudFarm?.Name,
        SeasonId = o.SeasonId,
        SeasonName = o.Season?.Name,
        EndsAt = o.EndsAt,
        EndsOn = SydneyTime.Today(o.EndsAt),
        IsRetired = o.IsRetired,
        State = o.IsRetired ? "Retired" : o.EndsAt <= now ? "Ended" : "Active",
        TimesApplied = timesApplied,
        CreatedAt = o.CreatedAt
    };
}
