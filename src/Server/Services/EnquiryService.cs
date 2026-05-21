using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Enquiries;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class EnquiryService : IEnquiryService
{
    private readonly IEnquiryRepository _enquiryRepo;
    private readonly IListingRepository _listingRepo;
    private readonly IStudFarmRepository _studFarmRepo;
    private readonly IUserService _users;

    public EnquiryService(
        IEnquiryRepository enquiryRepo,
        IListingRepository listingRepo,
        IStudFarmRepository studFarmRepo,
        IUserService users)
    {
        _enquiryRepo = enquiryRepo;
        _listingRepo = listingRepo;
        _studFarmRepo = studFarmRepo;
        _users = users;
    }

    public async Task<ServiceResult<EnquiryDto>> CreateAsync(Guid listingId, OpenEnquiryRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<EnquiryDto>.Forbidden();
        if (caller.Status != UserStatus.Active)
            return ServiceResult<EnquiryDto>.Forbidden("Your account must be verified before submitting enquiries.");

        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) return ServiceResult<EnquiryDto>.NotFound("Listing not found.");
        if (listing.Status != ListingStatus.Active)
            return ServiceResult<EnquiryDto>.BadRequest("This listing is not available for enquiries.");

        var studFarm = await _studFarmRepo.GetByIdAsync(listing.StudFarmId);
        if (studFarm == null) return ServiceResult<EnquiryDto>.NotFound("Stud farm not found.");

        // Duplicate check: buyer can only have one open enquiry per listing
        var existing = await _enquiryRepo.GetByBuyerIdAsync(caller.Id);
        if (existing.Any(e => e.ListingId == listingId && e.Status == EnquiryStatus.Open))
            return ServiceResult<EnquiryDto>.Conflict("You already have an open enquiry on this listing.");

        var enquiry = new Enquiry
        {
            ListingId = listingId,
            BuyerUserId = caller.Id,
            StudFarmUserId = studFarm.UserId,
            Status = EnquiryStatus.Open,
            Messages = new List<EnquiryMessage>
            {
                new() { SenderUserId = caller.Id, Body = request.Body.Trim() }
            }
        };
        var created = await _enquiryRepo.AddAsync(enquiry);
        return ServiceResult<EnquiryDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<IReadOnlyList<EnquirySummaryDto>>> GetAllForCallerAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<EnquirySummaryDto>>.Forbidden();

        var enquiries = caller.Role switch
        {
            UserRole.Staff => await _enquiryRepo.GetAllAsync(),
            UserRole.StudFarmAdmin => await _enquiryRepo.GetByStudFarmUserIdAsync(caller.Id),
            _ => await _enquiryRepo.GetByBuyerIdAsync(caller.Id)
        };
        return ServiceResult<IReadOnlyList<EnquirySummaryDto>>.Ok(
            enquiries.Select(MapToSummary).ToList());
    }

    public async Task<ServiceResult<EnquiryDto>> GetByIdAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<EnquiryDto>.Forbidden();

        var enquiry = await _enquiryRepo.GetByIdAsync(id);
        if (enquiry == null) return ServiceResult<EnquiryDto>.NotFound("Enquiry not found.");
        if (!CanAccess(caller, enquiry)) return ServiceResult<EnquiryDto>.Forbidden();

        return ServiceResult<EnquiryDto>.Ok(MapToDto(enquiry));
    }

    public async Task<ServiceResult<EnquiryMessageDto>> PostMessageAsync(Guid enquiryId, SendMessageRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<EnquiryMessageDto>.Forbidden();

        var enquiry = await _enquiryRepo.GetByIdAsync(enquiryId);
        if (enquiry == null) return ServiceResult<EnquiryMessageDto>.NotFound("Enquiry not found.");

        if (enquiry.Status == EnquiryStatus.Closed)
            return ServiceResult<EnquiryMessageDto>.BadRequest("This enquiry is closed.");

        if (!CanAccess(caller, enquiry))
            return ServiceResult<EnquiryMessageDto>.Forbidden("You are not a participant in this enquiry.");

        var message = new EnquiryMessage
        {
            EnquiryId = enquiryId,
            SenderUserId = caller.Id,
            Body = request.Body.Trim()
        };
        await _enquiryRepo.AddMessageAsync(message);
        return ServiceResult<EnquiryMessageDto>.Created(MapMessageToDto(message));
    }

    public async Task<ServiceResult> CloseAsync(Guid enquiryId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();

        var enquiry = await _enquiryRepo.GetByIdAsync(enquiryId);
        if (enquiry == null) return ServiceResult.NotFound("Enquiry not found.");

        if (caller.Role != UserRole.Staff && enquiry.BuyerUserId != caller.Id)
            return ServiceResult.Forbidden();

        if (enquiry.Status == EnquiryStatus.Closed)
            return ServiceResult.BadRequest("Enquiry is already closed.");

        enquiry.Status = EnquiryStatus.Closed;
        enquiry.ClosedAt = DateTime.UtcNow;
        await _enquiryRepo.UpdateAsync(enquiry);
        return ServiceResult.Ok();
    }

    private static bool CanAccess(User caller, Enquiry enquiry) => caller.Role switch
    {
        UserRole.Staff => true,
        UserRole.Buyer => enquiry.BuyerUserId == caller.Id,
        UserRole.StudFarmAdmin => enquiry.StudFarmUserId == caller.Id,
        _ => false
    };

    private static EnquirySummaryDto MapToSummary(Enquiry e) => new()
    {
        Id = e.Id,
        ListingId = e.ListingId,
        Subject = string.Empty,  // Entity has no Subject field; populated by listing name in future
        Status = e.Status.ToString(),
        MessageCount = e.Messages.Count,
        LastMessageAt = e.Messages.Count > 0 ? e.Messages.MaxBy(m => m.SentAt)?.SentAt : null
    };

    private static EnquiryDto MapToDto(Enquiry e) => new()
    {
        Id = e.Id,
        ListingId = e.ListingId,
        BuyerUserId = e.BuyerUserId,
        StudFarmUserId = e.StudFarmUserId,
        Status = e.Status.ToString(),
        CreatedAt = e.CreatedAt,
        ClosedAt = e.ClosedAt,
        Messages = e.Messages.OrderBy(m => m.SentAt).Select(MapMessageToDto).ToList()
    };

    private static EnquiryMessageDto MapMessageToDto(EnquiryMessage m) => new()
    {
        Id = m.Id,
        SenderUserId = m.SenderUserId,
        Body = m.Body,
        SentAt = m.SentAt,
        IsReadByRecipient = m.IsReadByRecipient
    };
}
