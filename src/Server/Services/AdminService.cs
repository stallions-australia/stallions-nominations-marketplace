using Stallions.Server.Auth;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class AdminService : IAdminService
{
    private readonly IListingRepository _listingRepo;
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IUserRepository _userRepo;
    private readonly IStudFarmRepository _studFarmRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly ICurrentUserService _currentUser;
    private readonly IUserService _users;

    public AdminService(
        IListingRepository listingRepo,
        IPurchaseRepository purchaseRepo,
        IUserRepository userRepo,
        IStudFarmRepository studFarmRepo,
        IAuditLogRepository auditRepo,
        ICurrentUserService currentUser,
        IUserService users)
    {
        _listingRepo = listingRepo;
        _purchaseRepo = purchaseRepo;
        _userRepo = userRepo;
        _studFarmRepo = studFarmRepo;
        _auditRepo = auditRepo;
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync()
    {
        var activeListings = await _listingRepo.GetActiveAsync();
        var allPurchases = await _purchaseRepo.GetAllAsync();
        var pendingUsers = await _userRepo.GetAllAsync(status: UserStatus.PendingVerification);

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var recentCompleted = allPurchases
            .Where(p => p.Status == PurchaseStatus.Completed && p.PaidAt >= cutoff)
            .ToList();

        var dto = new DashboardDto
        {
            ActiveListingCount = activeListings.Count,
            AuctionListingCount = activeListings.Count(l => l.ListingType == ListingType.Auction),
            FixedPriceListingCount = activeListings.Count(l => l.ListingType == ListingType.FixedPrice),
            RecentPurchaseCount = recentCompleted.Count,
            RecentFeeRevenueIncGst = recentCompleted.Sum(p => p.PlatformFeeIncGst),
            PendingVerificationCount = pendingUsers.Count
        };
        return ServiceResult<DashboardDto>.Ok(dto);
    }

    public async Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync()
    {
        var purchases = await _purchaseRepo.GetAllAsync();
        var dtos = purchases.Select(p => new TransactionDto
        {
            PurchaseId = p.Id,
            StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
            BuyerDisplayName = p.Buyer?.DisplayName ?? string.Empty,
            StudFarmName = p.Listing?.StudFarm?.Name ?? string.Empty,
            TotalPriceIncGst = p.TotalPriceIncGst,
            PlatformFeeIncGst = p.PlatformFeeIncGst,
            PlatformFeeExGst = p.PlatformFeeExGst,
            PlatformFeeGst = p.PlatformFeeGst,
            PaidAt = p.PaidAt,
            Status = p.Status.ToString()
        }).ToList();
        return ServiceResult<IReadOnlyList<TransactionDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync()
    {
        var purchases = await _purchaseRepo.GetAllAsync();
        var completed = purchases
            .Where(p => p.Status == PurchaseStatus.Completed && p.PaidAt.HasValue)
            .ToList();

        var invoices = completed
            .GroupBy(p => p.Listing?.StudFarmId ?? Guid.Empty)
            .Select(g => new InvoiceDto
            {
                StudFarmId = g.Key,
                StudFarmName = g.First().Listing?.StudFarm?.Name ?? string.Empty,
                Lines = g.Select(p => new InvoiceLineDto
                {
                    PurchaseId = p.Id,
                    StallionName = p.Listing?.Stallion?.Name ?? string.Empty,
                    SalePriceIncGst = p.TotalPriceIncGst,
                    PlatformFeeIncGst = p.PlatformFeeIncGst,
                    RemittanceAmount = p.TotalPriceIncGst - p.PlatformFeeIncGst,
                    PaidAt = p.PaidAt!.Value
                }).ToList(),
                TotalSalesIncGst = g.Sum(p => p.TotalPriceIncGst),
                TotalPlatformFeesIncGst = g.Sum(p => p.PlatformFeeIncGst),
                TotalRemittance = g.Sum(p => p.TotalPriceIncGst - p.PlatformFeeIncGst)
            }).ToList();

        return ServiceResult<IReadOnlyList<InvoiceDto>>.Ok(invoices);
    }

    public async Task<ServiceResult> SetListingFeeAsync(Guid listingId, SetListingFeeRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        if (request.PlatformFeePercent < 0 || request.PlatformFeePercent > 100)
            return ServiceResult.BadRequest("Fee percent must be between 0 and 100.");

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) return ServiceResult.NotFound("Listing not found.");

        var previousFee = listing.PlatformFeePercent;
        listing.PlatformFeePercent = request.PlatformFeePercent;
        await _listingRepo.UpdateAsync(listing);

        await _auditRepo.LogAsync(
            "Listing",
            listingId,
            "SetListingFee",
            caller.Id,
            $"Fee changed from {previousFee?.ToString() ?? "unset"} to {request.PlatformFeePercent}");

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IReadOnlyList<StudFarmSummaryDto>>> GetAllStudFarmsAsync()
    {
        var farms = await _studFarmRepo.GetAllAsync();
        var dtos = farms.Select(f => new StudFarmSummaryDto
        {
            Id = f.Id,
            Name = f.Name,
            ABN = f.ABN,
            ContactEmail = f.ContactEmail,
            LinkedUserDisplayName = f.User?.DisplayName ?? string.Empty,
            LinkedUserEmail = f.User?.Email ?? string.Empty,
            IsActive = f.IsActive,
            StudDirectoryId = f.StudDirectoryId,
            StudDirectoryName = f.StudDirectory?.Name,
            CreatedAt = f.CreatedAt
        }).ToList();
        return ServiceResult<IReadOnlyList<StudFarmSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StudFarmSummaryDto>> GetStudFarmByIdAsync(Guid id)
    {
        var farm = await _studFarmRepo.GetByIdAsync(id);
        if (farm == null)
            return ServiceResult<StudFarmSummaryDto>.NotFound("Stud farm not found.");

        // Load the user manually (GetByIdAsync doesn't Include User)
        var user = await _userRepo.GetByIdAsync(farm.UserId);

        var dto = new StudFarmSummaryDto
        {
            Id = farm.Id,
            Name = farm.Name,
            ABN = farm.ABN,
            ContactEmail = farm.ContactEmail,
            LinkedUserDisplayName = user?.DisplayName ?? string.Empty,
            LinkedUserEmail = user?.Email ?? string.Empty,
            IsActive = farm.IsActive,
            StudDirectoryId = farm.StudDirectoryId,
            CreatedAt = farm.CreatedAt
        };
        return ServiceResult<StudFarmSummaryDto>.Ok(dto);
    }

    public async Task<ServiceResult> LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId)
    {
        if (studDirectoryId == Guid.Empty)
            return ServiceResult.BadRequest("StudDirectoryId must not be empty.");

        var farm = await _studFarmRepo.GetByIdAsync(farmId);
        if (farm == null) return ServiceResult.NotFound("Stud farm not found.");

        var caller = await _users.GetOrCreateCurrentUserAsync();
        var previous = farm.StudDirectoryId?.ToString() ?? "none";
        farm.StudDirectoryId = studDirectoryId;
        await _studFarmRepo.UpdateAsync(farm);

        await _auditRepo.LogAsync(
            "StudFarm",
            farmId,
            "LinkStudDirectory",
            caller?.Id,
            $"StudDirectoryId changed from {previous} to {studDirectoryId}");

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<StudFarmSummaryDto>> CreateStudFarmAsync(CreateStudFarmRequest request)
    {
        var user = await _userRepo.GetByIdAsync(request.UserId);
        if (user == null)
            return ServiceResult<StudFarmSummaryDto>.NotFound("User not found.");

        if (user.Role != UserRole.StudFarmAdmin)
            return ServiceResult<StudFarmSummaryDto>.BadRequest(
                "User must have the StudFarmAdmin role.");

        var existing = await _studFarmRepo.GetByUserIdAsync(request.UserId);
        if (existing != null)
            return ServiceResult<StudFarmSummaryDto>.BadRequest(
                "This user already has a stud farm linked to their account.");

        if (request.StudDirectoryId.HasValue && request.StudDirectoryId.Value == Guid.Empty)
            return ServiceResult<StudFarmSummaryDto>.BadRequest("StudDirectoryId must not be empty.");

        var caller = await _users.GetOrCreateCurrentUserAsync();

        var farm = new StudFarm
        {
            UserId = request.UserId,
            Name = request.Name,
            ABN = request.ABN,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            Address = request.Address,
            StudDirectoryId = request.StudDirectoryId
        };

        farm = await _studFarmRepo.AddAsync(farm);

        await _auditRepo.LogAsync(
            "StudFarm",
            farm.Id,
            "CreateStudFarm",
            caller?.Id,
            $"Farm '{farm.Name}' created and linked to user {user.Email}");

        var dto = new StudFarmSummaryDto
        {
            Id = farm.Id,
            Name = farm.Name,
            ABN = farm.ABN,
            ContactEmail = farm.ContactEmail,
            LinkedUserDisplayName = user.DisplayName,
            LinkedUserEmail = user.Email,
            IsActive = farm.IsActive,
            StudDirectoryId = farm.StudDirectoryId,
            CreatedAt = farm.CreatedAt
        };
        return ServiceResult<StudFarmSummaryDto>.Ok(dto);
    }

    public async Task<ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>> GetAllListingsStaffAsync()
    {
        var listings = await _listingRepo.GetAllStaffAsync();
        var dtos = listings.Select(l =>
        {
            decimal? price = l switch
            {
                FixedPriceListing fp => fp.PriceIncGst,
                AuctionListing al => al.StartingPrice,
                _ => null
            };
            return new ListingStaffSummaryDto
            {
                Id = l.Id,
                StallionName = l.Stallion?.Name ?? string.Empty,
                StudFarmName = l.StudFarm?.Name ?? string.Empty,
                ListingType = l.ListingType.ToString(),
                Status = l.Status.ToString(),
                PriceIncGst = price,
                PlatformFeePercent = l.PlatformFeePercent,
                PublishedAt = l.PublishedAt
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult> ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) return ServiceResult.NotFound("Listing not found.");

        if (!Enum.TryParse<ListingStatus>(request.Status, ignoreCase: true, out var newStatus))
            return ServiceResult.BadRequest($"'{request.Status}' is not a valid listing status.");

        var previousStatus = listing.Status;
        listing.Status = newStatus;
        await _listingRepo.UpdateAsync(listing);

        await _auditRepo.LogAsync(
            "Listing",
            listingId,
            "ForceListingStatus",
            caller?.Id,
            $"Status forced from {previousStatus} to {newStatus}. Reason: {request.Reason ?? "none"}");

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetUserRoleAsync(Guid userId, SetUserRoleRequest request)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var newRole))
            return ServiceResult.BadRequest($"'{request.Role}' is not a valid role.");

        // Prevent demoting Staff accounts — that could lock someone out.
        if (newRole == UserRole.Staff)
            return ServiceResult.BadRequest("Cannot promote a user to Staff via this endpoint.");

        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) return ServiceResult.NotFound("User not found.");

        // Don't allow changing your own role.
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller?.Id == userId)
            return ServiceResult.BadRequest("You cannot change your own role.");

        var previousRole = user.Role;
        user.Role = newRole;
        await _userRepo.UpdateAsync(user);

        await _auditRepo.LogAsync(
            "User",
            userId,
            "SetUserRole",
            caller?.Id,
            $"Role changed from {previousRole} to {newRole}");

        return ServiceResult.Ok();
    }
}
