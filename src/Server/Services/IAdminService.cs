using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Services;

public interface IAdminService
{
    Task<ServiceResult<DashboardDto>> GetDashboardAsync();
    Task<ServiceResult<IReadOnlyList<TransactionDto>>> GetTransactionsAsync();
    Task<ServiceResult<IReadOnlyList<InvoiceDto>>> GetInvoicesAsync();
    Task<ServiceResult> SetListingFeeAsync(Guid listingId, SetListingFeeRequest request);
}
