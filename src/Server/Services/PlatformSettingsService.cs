using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Settings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class PlatformSettingsService : IPlatformSettingsService
{
    private readonly IPlatformSettingsRepository _repo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IUserService _users;

    public PlatformSettingsService(IPlatformSettingsRepository repo, IAuditLogRepository auditRepo, IUserService users)
    {
        _repo = repo;
        _auditRepo = auditRepo;
        _users = users;
    }

    public async Task<ServiceResult<PlatformSettingsDto>> GetAsync() =>
        ServiceResult<PlatformSettingsDto>.Ok(MapToDto(await _repo.GetAsync()));

    public async Task<ServiceResult<PlatformSettingsDto>> UpdateAsync(UpdatePlatformSettingsRequest request)
    {
        // The controller enforces StaffOnly; checked again here because fee settings are
        // the most sensitive values on the platform.
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<PlatformSettingsDto>.Forbidden("Only Stallions Australia staff can change platform settings.");

        if (request.BuyerFeeIncGst <= 0)
            return ServiceResult<PlatformSettingsDto>.BadRequest("Buyer fee must be greater than zero.");
        if (request.StandardListingFeeIncGst <= 0)
            return ServiceResult<PlatformSettingsDto>.BadRequest("Standard listing fee must be greater than zero.");
        if (request.MinimumBidIncrement <= 0)
            return ServiceResult<PlatformSettingsDto>.BadRequest("Minimum bid increment must be greater than zero.");
        if (request.ChargeGracePeriodHours <= 0)
            return ServiceResult<PlatformSettingsDto>.BadRequest("Charge grace period must be greater than zero.");
        if (request.OfferExpiryDays <= 0)
            return ServiceResult<PlatformSettingsDto>.BadRequest("Offer expiry must be greater than zero.");

        var settings = await _repo.GetAsync();
        var old = Snapshot(settings);

        settings.BuyerFeeIncGst = request.BuyerFeeIncGst;
        settings.StandardListingFeeIncGst = request.StandardListingFeeIncGst;
        settings.MinimumBidIncrement = request.MinimumBidIncrement;
        settings.ChargeGracePeriodHours = request.ChargeGracePeriodHours;
        settings.OfferExpiryDays = request.OfferExpiryDays;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = caller.Id;
        await _repo.UpdateAsync(settings);

        await _auditRepo.LogAsync("PlatformSettings", settings.Id, "UpdatePlatformSettings", caller.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(settings) }));

        return ServiceResult<PlatformSettingsDto>.Ok(MapToDto(settings));
    }

    private static object Snapshot(PlatformSettings s) => new
    {
        s.BuyerFeeIncGst,
        s.StandardListingFeeIncGst,
        s.MinimumBidIncrement,
        s.ChargeGracePeriodHours,
        s.OfferExpiryDays
    };

    private static PlatformSettingsDto MapToDto(PlatformSettings s) => new()
    {
        BuyerFeeIncGst = s.BuyerFeeIncGst,
        StandardListingFeeIncGst = s.StandardListingFeeIncGst,
        MinimumBidIncrement = s.MinimumBidIncrement,
        ChargeGracePeriodHours = s.ChargeGracePeriodHours,
        OfferExpiryDays = s.OfferExpiryDays,
        UpdatedAt = s.UpdatedAt
    };
}
