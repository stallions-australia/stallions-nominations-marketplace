using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Bindings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class NominationBindingService : INominationBindingService
{
    private readonly INominationBindingRepository _bindingRepo;
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IListingRepository _listingRepo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IUserService _users;

    public NominationBindingService(
        INominationBindingRepository bindingRepo,
        IPurchaseRepository purchaseRepo,
        IListingRepository listingRepo,
        IStudFarmRepository farmRepo,
        IUserService users)
    {
        _bindingRepo = bindingRepo;
        _purchaseRepo = purchaseRepo;
        _listingRepo = listingRepo;
        _farmRepo = farmRepo;
        _users = users;
    }

    public async Task<ServiceResult<NominationBindingDto>> GetByIdAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<NominationBindingDto>.Forbidden();

        var binding = await _bindingRepo.GetByIdAsync(id);
        if (binding == null)
            return ServiceResult<NominationBindingDto>.NotFound("Binding not found.");

        var canAccess = await CanAccessAsync(caller, binding);
        if (!canAccess)
            return ServiceResult<NominationBindingDto>.Forbidden();

        return ServiceResult<NominationBindingDto>.Ok(MapToDto(binding));
    }

    public async Task<ServiceResult<NominationBindingDto>> AcknowledgeAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<NominationBindingDto>.Forbidden();

        var binding = await _bindingRepo.GetByIdAsync(id);
        if (binding == null)
            return ServiceResult<NominationBindingDto>.NotFound("Binding not found.");

        if (binding.Status != BindingStatus.PendingAcknowledgement)
            return ServiceResult<NominationBindingDto>.BadRequest("This binding is not pending acknowledgement.");

        var purchase = await _purchaseRepo.GetByIdAsync(binding.PurchaseId);
        if (purchase == null)
            return ServiceResult<NominationBindingDto>.NotFound("Purchase not found.");

        var listing = await _listingRepo.GetByIdAsync(purchase.ListingId);
        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);

        if (farm == null || listing?.StudFarmId != farm.Id)
            return ServiceResult<NominationBindingDto>.Forbidden("Only the stud farm can acknowledge this binding.");

        binding.Status = BindingStatus.Acknowledged;
        binding.AcknowledgedAt = DateTime.UtcNow;
        binding.AcknowledgedByUserId = caller.Id;
        await _bindingRepo.UpdateAsync(binding);

        // Transition to AwaitingSignatures — PDF generation triggered by Azure Function
        binding.Status = BindingStatus.AwaitingSignatures;
        await _bindingRepo.UpdateAsync(binding);

        return ServiceResult<NominationBindingDto>.Ok(MapToDto(binding));
    }

    public async Task<ServiceResult<NominationBindingDto>> SignAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<NominationBindingDto>.Forbidden();

        var binding = await _bindingRepo.GetByIdAsync(id);
        if (binding == null)
            return ServiceResult<NominationBindingDto>.NotFound("Binding not found.");

        var purchase = await _purchaseRepo.GetByIdAsync(binding.PurchaseId);
        if (purchase == null)
            return ServiceResult<NominationBindingDto>.NotFound("Purchase not found.");

        var listing = await _listingRepo.GetByIdAsync(purchase.ListingId);
        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);

        var isBuyer = purchase.BuyerUserId == caller.Id;
        var isFarm = farm != null && listing?.StudFarmId == farm.Id;
        var isStaff = caller.Role == UserRole.Staff;

        if (!isBuyer && !isFarm && !isStaff)
            return ServiceResult<NominationBindingDto>.Forbidden();

        var signingStatuses = new[]
        {
            BindingStatus.AwaitingSignatures,
            BindingStatus.BuyerSigned,
            BindingStatus.FarmSigned
        };

        if (!signingStatuses.Contains(binding.Status))
            return ServiceResult<NominationBindingDto>.BadRequest("This binding is not awaiting signatures.");

        if (isBuyer && binding.BuyerSignedAt.HasValue)
            return ServiceResult<NominationBindingDto>.BadRequest("You have already signed this binding.");

        if (isFarm && binding.FarmSignedAt.HasValue)
            return ServiceResult<NominationBindingDto>.BadRequest("The farm has already signed this binding.");

        if (isBuyer)
            binding.BuyerSignedAt = DateTime.UtcNow;
        else
            binding.FarmSignedAt = DateTime.UtcNow;

        if (binding.BuyerSignedAt.HasValue && binding.FarmSignedAt.HasValue)
        {
            binding.Status = BindingStatus.Complete;
            binding.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            binding.Status = isBuyer ? BindingStatus.BuyerSigned : BindingStatus.FarmSigned;
        }

        await _bindingRepo.UpdateAsync(binding);

        return ServiceResult<NominationBindingDto>.Ok(MapToDto(binding));
    }

    public async Task<ServiceResult<NominationBindingDto>> DisputeAsync(Guid id)
    {
        var binding = await _bindingRepo.GetByIdAsync(id);
        if (binding == null)
            return ServiceResult<NominationBindingDto>.NotFound("Binding not found.");

        binding.Status = BindingStatus.Disputed;
        await _bindingRepo.UpdateAsync(binding);

        return ServiceResult<NominationBindingDto>.Ok(MapToDto(binding));
    }

    private async Task<bool> CanAccessAsync(User caller, NominationBinding binding)
    {
        if (caller.Role == UserRole.Staff)
            return true;

        var purchase = await _purchaseRepo.GetByIdAsync(binding.PurchaseId);
        if (purchase == null)
            return false;

        if (caller.Role == UserRole.Buyer)
            return purchase.BuyerUserId == caller.Id;

        // StudFarmAdmin — check farm ownership
        var listing = await _listingRepo.GetByIdAsync(purchase.ListingId);
        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        return farm != null && listing?.StudFarmId == farm.Id;
    }

    private static NominationBindingDto MapToDto(NominationBinding b) => new()
    {
        Id = b.Id,
        PurchaseId = b.PurchaseId,
        Status = b.Status.ToString(),
        PdfBlobPath = b.PdfBlobPath,
        AcknowledgedAt = b.AcknowledgedAt,
        BuyerSignedAt = b.BuyerSignedAt,
        FarmSignedAt = b.FarmSignedAt,
        CompletedAt = b.CompletedAt
    };
}
