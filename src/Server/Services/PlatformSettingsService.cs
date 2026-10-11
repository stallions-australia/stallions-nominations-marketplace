using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared;
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

    private const string BusinessStaffOnlyMessage = "Only Stallions Australia staff can manage the business details.";

    public async Task<ServiceResult<BusinessDetailsDto>> GetBusinessDetailsAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<BusinessDetailsDto>.Forbidden(BusinessStaffOnlyMessage);
        return ServiceResult<BusinessDetailsDto>.Ok(MapBusiness(await _repo.GetAsync()));
    }

    public async Task<ServiceResult<BusinessDetailsDto>> UpdateBusinessDetailsAsync(UpdateBusinessDetailsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<BusinessDetailsDto>.Forbidden(BusinessStaffOnlyMessage);

        var legalName = Text(request.LegalName);
        var address = Text(request.Address);
        var email = Text(request.Email);
        var accountName = Text(request.BankAccountName);
        var abn = Digits(request.Abn);
        var bsb = Digits(request.BankBsb);
        var account = Digits(request.BankAccountNumber);

        if (legalName?.Length > 200) return ServiceResult<BusinessDetailsDto>.BadRequest("The legal name can be at most 200 characters.");
        if (abn != null && !BusinessNumbers.IsAbn(abn)) return ServiceResult<BusinessDetailsDto>.BadRequest("ABN must be 11 digits.");
        if (address?.Length > 500) return ServiceResult<BusinessDetailsDto>.BadRequest("The address can be at most 500 characters.");
        if (email != null && (email.Length > 256 || !email.Contains('@')))
            return ServiceResult<BusinessDetailsDto>.BadRequest("Enter a valid email address.");
        if (accountName?.Length > 200) return ServiceResult<BusinessDetailsDto>.BadRequest("The account name can be at most 200 characters.");
        if (bsb != null && !BusinessNumbers.IsBsb(bsb)) return ServiceResult<BusinessDetailsDto>.BadRequest("BSB must be 6 digits.");
        if (account != null && (account.Length is < 4 or > 10 || !account.All(char.IsAsciiDigit)))
            return ServiceResult<BusinessDetailsDto>.BadRequest("Account number must be 4 to 10 digits.");
        if (request.InvoicePaymentTermsDays is < 1 or > 90)
            return ServiceResult<BusinessDetailsDto>.BadRequest("Payment terms must be between 1 and 90 days.");

        var settings = await _repo.GetAsync();
        var old = BusinessSnapshot(settings);
        settings.BusinessLegalName = legalName;
        settings.BusinessAbn = abn;
        settings.BusinessAddress = address;
        settings.BusinessEmail = email;
        settings.BankAccountName = accountName;
        settings.BankBsb = bsb;
        settings.BankAccountNumber = account;
        settings.InvoicePaymentTermsDays = request.InvoicePaymentTermsDays;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = caller.Id;
        await _repo.UpdateAsync(settings);

        await _auditRepo.LogAsync("PlatformSettings", settings.Id, "UpdateBusinessDetails", caller.Id,
            JsonSerializer.Serialize(new { Old = old, New = BusinessSnapshot(settings) }));

        return ServiceResult<BusinessDetailsDto>.Ok(MapBusiness(settings));
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Digits(string? value)
    {
        var compact = BusinessNumbers.Compact(value);
        return compact.Length == 0 ? null : compact;
    }

    private static object BusinessSnapshot(PlatformSettings s) => new
    {
        s.BusinessLegalName, s.BusinessAbn, s.BusinessAddress, s.BusinessEmail,
        s.BankAccountName, s.BankBsb, s.BankAccountNumber, s.InvoicePaymentTermsDays
    };

    private static BusinessDetailsDto MapBusiness(PlatformSettings s) => new()
    {
        LegalName = s.BusinessLegalName,
        Abn = s.BusinessAbn,
        Address = s.BusinessAddress,
        Email = s.BusinessEmail,
        BankAccountName = s.BankAccountName,
        BankBsb = s.BankBsb,
        BankAccountNumber = s.BankAccountNumber,
        InvoicePaymentTermsDays = s.InvoicePaymentTermsDays,
        CanIssueTaxDocuments = s.HasBusinessDetails,
        CanIssueUnpaidInvoices = s.HasBusinessDetails && s.HasBankDetails
    };

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
