using Stallions.Shared.DTOs.Admin;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Server.Services;

public interface IAdminService
{
    Task<ServiceResult<DashboardDto>> GetDashboardAsync();
    Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync();
    Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync();

    Task<ServiceResult<IReadOnlyList<StudFarmSummaryDto>>> GetAllStudFarmsAsync();
    Task<ServiceResult<StudFarmSummaryDto>> GetStudFarmByIdAsync(Guid id);
    Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetStudFarmStallionsAsync(Guid farmId);
    Task<ServiceResult<StudFarmSummaryDto>> CreateStudFarmAsync(CreateStudFarmRequest request);
    Task<ServiceResult> LinkStudFarmToDirectoryAsync(Guid farmId, Guid studDirectoryId);
    Task<ServiceResult<IReadOnlyList<ListingStaffSummaryDto>>> GetAllListingsStaffAsync();
    Task<ServiceResult> ForceListingStatusAsync(Guid listingId, ForceListingStatusRequest request);
    Task<ServiceResult> SetUserRoleAsync(Guid userId, SetUserRoleRequest request);
}
