using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared;
using Stallions.Shared.DTOs.Subscriptions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class SubscriptionService : ISubscriptionService
{
    private const string AuditEntity = "StallionSeasonSubscription";

    private readonly ISubscriptionRepository _repo;
    private readonly IStallionRepository _stallionRepo;
    private readonly ISeasonRepository _seasonRepo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IPlatformSettingsRepository _settingsRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;

    public SubscriptionService(
        ISubscriptionRepository repo,
        IStallionRepository stallionRepo,
        ISeasonRepository seasonRepo,
        IStudFarmRepository farmRepo,
        IPlatformSettingsRepository settingsRepo,
        IAuditLogRepository auditRepo,
        IUserService users)
    {
        _repo = repo;
        _stallionRepo = stallionRepo;
        _seasonRepo = seasonRepo;
        _farmRepo = farmRepo;
        _settingsRepo = settingsRepo;
        _auditRepo = auditRepo;
        _users = users;
    }

    public async Task<ServiceResult<SubscriptionDto>> CreateAsync(CreateSubscriptionRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);

        if (request.FeeIncGstOverride is <= 0)
            return ServiceResult<SubscriptionDto>.BadRequest(
                "A fee override must be greater than zero. Use Waive to remove the fee entirely.");

        var stallion = await _stallionRepo.GetByIdAsync(request.StallionId);
        if (stallion == null)
            return ServiceResult<SubscriptionDto>.NotFound("Stallion not found.");

        var season = await _seasonRepo.GetByIdAsync(request.SeasonId);
        if (season == null)
            return ServiceResult<SubscriptionDto>.NotFound("Season not found.");

        if (await _repo.GetByStallionAndSeasonAsync(stallion.Id, season.Id) != null)
            return ServiceResult<SubscriptionDto>.Conflict(
                $"{stallion.Name} already has a subscription for {season.Name}.");

        var feeIncGst = request.FeeIncGstOverride ?? (await _settingsRepo.GetAsync()).StandardListingFeeIncGst;
        var fee = GstBreakdown.FromIncGst(feeIncGst);

        var subscription = new StallionSeasonSubscription
        {
            StallionId = stallion.Id,
            SeasonId = season.Id,
            StudFarmId = stallion.StudFarmId,
            FeeIncGst = fee.IncGst,
            FeeExGst = fee.ExGst,
            GstAmount = fee.Gst,
            Status = SubscriptionStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedByUserId = staff.Id
        };

        try
        {
            await _repo.AddAsync(subscription);
        }
        catch (DbUpdateException)
        {
            // Unique (StallionId, SeasonId) index — a concurrent create won the race.
            return ServiceResult<SubscriptionDto>.Conflict(
                $"{stallion.Name} already has a subscription for {season.Name}.");
        }

        await _auditRepo.LogAsync(AuditEntity, subscription.Id, "CreateSubscription", staff.Id,
            JsonSerializer.Serialize(new
            {
                subscription.StallionId,
                subscription.SeasonId,
                subscription.FeeIncGst,
                FeeOverridden = request.FeeIncGstOverride.HasValue
            }));

        var created = await _repo.GetByIdAsync(subscription.Id);
        return ServiceResult<SubscriptionDto>.Created(MapToDto(created!, includeStaffFields: true));
    }

    public async Task<ServiceResult<SubscriptionDto>> MarkPaidAsync(Guid id, MarkSubscriptionPaidRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);

        var method = ParsePaidMethod(request.PaymentMethod);
        if (method == null)
            return ServiceResult<SubscriptionDto>.BadRequest(
                "Payment method must be Card, Invoice or BankTransfer.");

        var subscription = await _repo.GetByIdAsync(id);
        if (subscription == null)
            return ServiceResult<SubscriptionDto>.NotFound("Subscription not found.");

        if (subscription.Status != SubscriptionStatus.Pending)
            return ServiceResult<SubscriptionDto>.BadRequest(
                $"Only pending subscriptions can be marked paid (this one is {subscription.Status}).");

        subscription.Status = SubscriptionStatus.Paid;
        subscription.PaymentMethod = method;
        subscription.PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference)
            ? null : request.PaymentReference.Trim();
        subscription.PaidAt = DateTime.UtcNow;
        await _repo.UpdateAsync(subscription);

        await _auditRepo.LogAsync(AuditEntity, subscription.Id, "MarkSubscriptionPaid", staff.Id,
            JsonSerializer.Serialize(new
            {
                PaymentMethod = method.ToString(),
                subscription.PaymentReference,
                subscription.FeeIncGst
            }));

        return ServiceResult<SubscriptionDto>.Ok(MapToDto(subscription, includeStaffFields: true));
    }

    public async Task<ServiceResult<SubscriptionDto>> WaiveAsync(Guid id, WaiveSubscriptionRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null)
            return ServiceResult<SubscriptionDto>.Forbidden(StaffOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult<SubscriptionDto>.BadRequest("A reason is required to waive the listing fee.");

        var subscription = await _repo.GetByIdAsync(id);
        if (subscription == null)
            return ServiceResult<SubscriptionDto>.NotFound("Subscription not found.");

        if (subscription.Status != SubscriptionStatus.Pending)
            return ServiceResult<SubscriptionDto>.BadRequest(
                $"Only pending subscriptions can be waived (this one is {subscription.Status}).");

        var previousFee = subscription.FeeIncGst;
        subscription.FeeIncGst = 0m;
        subscription.FeeExGst = 0m;
        subscription.GstAmount = 0m;
        subscription.Status = SubscriptionStatus.Waived;
        subscription.PaymentMethod = SubscriptionPaymentMethod.Waived;
        subscription.WaiverReason = request.Reason.Trim();
        await _repo.UpdateAsync(subscription);

        await _auditRepo.LogAsync(AuditEntity, subscription.Id, "WaiveSubscription", staff.Id,
            JsonSerializer.Serialize(new { PreviousFeeIncGst = previousFee, subscription.WaiverReason }));

        return ServiceResult<SubscriptionDto>.Ok(MapToDto(subscription, includeStaffFields: true));
    }

    public async Task<ServiceResult<IReadOnlyList<SubscriptionDto>>> GetForStudFarmAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<IReadOnlyList<SubscriptionDto>>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<IReadOnlyList<SubscriptionDto>>.NotFound("No stud farm found for the current user.");

        var subscriptions = await _repo.GetByStudFarmIdAsync(farm.Id);
        return ServiceResult<IReadOnlyList<SubscriptionDto>>.Ok(
            subscriptions.Select(s => MapToDto(s, includeStaffFields: false)).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<SubscriptionDto>>> GetAllAsync(Guid? seasonId, string? status)
    {
        if (await GetStaffCallerAsync() == null)
            return ServiceResult<IReadOnlyList<SubscriptionDto>>.Forbidden(StaffOnlyMessage);

        SubscriptionStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SubscriptionStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return ServiceResult<IReadOnlyList<SubscriptionDto>>.BadRequest($"'{status}' is not a valid subscription status.");
            statusFilter = parsed;
        }

        var subscriptions = await _repo.GetAllAsync(seasonId, statusFilter);
        return ServiceResult<IReadOnlyList<SubscriptionDto>>.Ok(
            subscriptions.Select(s => MapToDto(s, includeStaffFields: true)).ToList());
    }

    public async Task<bool> HasActiveSubscriptionAsync(Guid stallionId, Guid seasonId)
    {
        var subscription = await _repo.GetByStallionAndSeasonAsync(stallionId, seasonId);
        return subscription?.Status is SubscriptionStatus.Paid or SubscriptionStatus.Waived;
    }

    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage listing fee subscriptions.";

    // Controllers enforce the StaffOnly policy; checked again here because these are fee records.
    private async Task<User?> GetStaffCallerAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller?.Role == UserRole.Staff ? caller : null;
    }

    private static SubscriptionPaymentMethod? ParsePaidMethod(string? value)
    {
        var name = Enum.GetNames<SubscriptionPaymentMethod>()
            .FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        var method = Enum.Parse<SubscriptionPaymentMethod>(name);
        return method == SubscriptionPaymentMethod.Waived ? null : method;
    }

    private static SubscriptionDto MapToDto(StallionSeasonSubscription s, bool includeStaffFields) => new()
    {
        Id = s.Id,
        StallionId = s.StallionId,
        StallionName = s.Stallion?.Name ?? string.Empty,
        SeasonId = s.SeasonId,
        SeasonName = s.Season?.Name ?? string.Empty,
        StudFarmId = s.StudFarmId,
        StudFarmName = s.StudFarm?.Name ?? string.Empty,
        FeeIncGst = s.FeeIncGst,
        FeeExGst = s.FeeExGst,
        GstAmount = s.GstAmount,
        Status = s.Status.ToString(),
        PaymentMethod = s.PaymentMethod?.ToString(),
        PaymentReference = s.PaymentReference,
        PaidAt = s.PaidAt,
        WaiverReason = s.WaiverReason,
        Notes = includeStaffFields ? s.Notes : null,
        CreatedAt = s.CreatedAt
    };
}
