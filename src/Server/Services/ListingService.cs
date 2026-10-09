using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class ListingService : IListingService
{
    private readonly IListingRepository _listingRepo;
    private readonly ISeasonRepository _seasonRepo;
    private readonly IStallionRepository _stallionRepo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IUserService _users;

    public ListingService(
        IListingRepository listingRepo,
        ISeasonRepository seasonRepo,
        IStallionRepository stallionRepo,
        IStudFarmRepository farmRepo,
        IUserService users)
    {
        _listingRepo = listingRepo;
        _seasonRepo = seasonRepo;
        _stallionRepo = stallionRepo;
        _farmRepo = farmRepo;
        _users = users;
    }

    public async Task<ServiceResult<IReadOnlyList<ListingDto>>> GetActiveAsync(Guid? seasonId, ListingType? type, bool isStaff)
    {
        var listings = await _listingRepo.GetActiveAsync(seasonId, null, type);
        var dtos = listings.Select(l => MapToDto(l, isStaff)).ToList();
        return ServiceResult<IReadOnlyList<ListingDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<ListingDto>> GetByIdAsync(Guid id, bool isStaff)
    {
        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult<ListingDto>.NotFound("Listing not found.");
        return ServiceResult<ListingDto>.Ok(MapToDto(listing, isStaff));
    }

    public async Task<ServiceResult<ListingDto>> GetMineByIdAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<ListingDto>.NotFound("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult<ListingDto>.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult<ListingDto>.Forbidden("You do not have permission to view this listing.");

        return ServiceResult<ListingDto>.Ok(MapToDto(listing, true));
    }

    public async Task<ServiceResult<IReadOnlyList<ListingDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<IReadOnlyList<ListingDto>>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<IReadOnlyList<ListingDto>>.NotFound("No stud farm found for the current user.");

        var listings = await _listingRepo.GetByStudFarmIdAsync(farm.Id);
        // Farm admin sees full details (isStaff = true so fee/reserve are visible)
        var dtos = listings.Select(l => MapToDto(l, true)).ToList();
        return ServiceResult<IReadOnlyList<ListingDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<ListingDto>> CreateAuctionListingAsync(CreateAuctionListingRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<ListingDto>.Forbidden("No stud farm found for the current user.");

        var stallion = await _stallionRepo.GetByIdAsync(request.StallionId);
        if (stallion == null || !stallion.IsActive)
            return ServiceResult<ListingDto>.BadRequest("Stallion not found or is inactive.");
        if (stallion.StudFarmId != farm.Id)
            return ServiceResult<ListingDto>.BadRequest("Stallion does not belong to your stud farm.");

        var season = await _seasonRepo.GetByIdAsync(request.SeasonId);
        if (season == null)
            return ServiceResult<ListingDto>.NotFound("Season not found.");
        if (!season.IsOpen)
            return ServiceResult<ListingDto>.BadRequest("The selected season is not open for listings.");

        if (request.IsNoReserve && request.ReservePrice.HasValue)
            return ServiceResult<ListingDto>.BadRequest("Cannot set a reserve price on a no-reserve auction.");

        if (request.EndDateTime <= DateTime.UtcNow)
            return ServiceResult<ListingDto>.BadRequest("End date/time must be in the future.");

        if (request.MinimumBidIncrement <= 0)
            return ServiceResult<ListingDto>.BadRequest("Minimum bid increment must be greater than zero.");

        var listing = new AuctionListing
        {
            StallionId = request.StallionId,
            SeasonId = request.SeasonId,
            StudFarmId = farm.Id,
            ListingType = ListingType.Auction,
            Status = ListingStatus.Draft,
            Description = request.Description,
            TermsAndConditions = request.TermsAndConditions,
            StartingPrice = request.StartingPrice,
            ReservePrice = request.ReservePrice,
            IsNoReserve = request.IsNoReserve,
            MinimumBidIncrement = request.MinimumBidIncrement,
            EndDateTime = request.EndDateTime
        };

        var created = await _listingRepo.AddAsync(listing);
        return ServiceResult<ListingDto>.Created(MapToDto(created, true));
    }

    public async Task<ServiceResult<ListingDto>> UpdateListingAsync(Guid id, UpdateListingRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<ListingDto>.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult<ListingDto>.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult<ListingDto>.Forbidden("You do not have permission to update this listing.");

        // Cancelled and Sold listings are permanently read-only.
        if (listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold)
            return ServiceResult<ListingDto>.BadRequest("This listing can no longer be edited.");

        // CRITICAL: PlatformFeePercent is never touched here — only AdminService.SetListingFeeAsync can set it.

        // Safe edits: description is always editable (Draft or Active).
        if (request.Description is not null)
            listing.Description = request.Description;

        // Fields that are locked once the listing has ever been published.
        // PublishedAt is set on first publish and intentionally NOT cleared on unpublish,
        // so this sentinel permanently locks price, T&C, type, and auction dates.
        var neverPublished = listing.PublishedAt == null;

        if (neverPublished)
        {
            // Full edit allowed — listing has never gone live.
            if (request.TermsAndConditions is not null)
                listing.TermsAndConditions = request.TermsAndConditions;

            if (listing is AuctionListing al)
            {
                if (request.StartingPrice.HasValue) al.StartingPrice = request.StartingPrice.Value;
                if (request.ReservePrice.HasValue) al.ReservePrice = request.ReservePrice;
                if (request.IsNoReserve.HasValue) al.IsNoReserve = request.IsNoReserve.Value;
                if (request.MinimumBidIncrement.HasValue) al.MinimumBidIncrement = request.MinimumBidIncrement.Value;
                if (request.EndDateTime.HasValue) al.EndDateTime = request.EndDateTime.Value;
            }
        }
        // Once published, only the description is editable (already handled above).

        await _listingRepo.UpdateAsync(listing);
        return ServiceResult<ListingDto>.Ok(MapToDto(listing, true));
    }

    public async Task<ServiceResult> PublishListingAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult.Forbidden("You do not have permission to publish this listing.");

        if (listing.Status != ListingStatus.Draft)
            return ServiceResult.BadRequest("Only Draft listings can be published.");

        if (!listing.PlatformFeePercent.HasValue)
            return ServiceResult.BadRequest("A platform fee must be set by a Stallions Australia staff member before this listing can be published.");

        if (listing is AuctionListing al && al.EndDateTime <= DateTime.UtcNow)
            return ServiceResult.BadRequest("Auction end date must be in the future.");

        listing.Status = ListingStatus.Active;
        listing.PublishedAt = DateTime.UtcNow;
        await _listingRepo.UpdateAsync(listing);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UnpublishListingAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult.Forbidden("You do not have permission to unpublish this listing.");

        if (listing.Status != ListingStatus.Active)
            return ServiceResult.BadRequest("Only Active listings can be unpublished.");

        listing.Status = ListingStatus.Draft;
        // PublishedAt is intentionally NOT cleared — it permanently locks price/T&C
        // even after the listing returns to Draft state.
        await _listingRepo.UpdateAsync(listing);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CloseByStudFarmAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult.Forbidden("You do not have permission to close this listing.");

        if (listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold)
            return ServiceResult.BadRequest($"Listing is already closed (status: {listing.Status}).");

        listing.Status = ListingStatus.Cancelled;
        listing.ClosedAt = DateTime.UtcNow;
        await _listingRepo.UpdateAsync(listing);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CancelListingAsync(Guid id)
    {
        // Staff-only: authorization enforced at controller level via [Authorize(Policy = "StaffOnly")]
        // No ownership check required — staff may cancel any listing
        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult.NotFound("Listing not found.");

        if (listing.Status == ListingStatus.Cancelled || listing.Status == ListingStatus.Sold)
            return ServiceResult.BadRequest($"Listing cannot be cancelled: status is {listing.Status}.");

        listing.Status = ListingStatus.Cancelled;
        listing.ClosedAt = DateTime.UtcNow;
        await _listingRepo.UpdateAsync(listing);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<ListingDto>> RelistAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<ListingDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<ListingDto>.Forbidden("No stud farm found for the current user.");

        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult<ListingDto>.NotFound("Listing not found.");

        if (listing.StudFarmId != farm.Id)
            return ServiceResult<ListingDto>.Forbidden("You do not have permission to relist this listing.");

        if (listing.Status != ListingStatus.Expired)
            return ServiceResult<ListingDto>.BadRequest("Only Expired listings can be relisted.");

        if (listing is not AuctionListing al)
            return ServiceResult<ListingDto>.BadRequest("Only auction listings can be relisted.");

        // PlatformFeePercent is NOT carried over — must be set again by staff before publishing.
        // TermsAndConditions is NOT carried over — new listing requires fresh T&C acceptance.
        var newListing = new AuctionListing
        {
            StallionId = al.StallionId,
            SeasonId = al.SeasonId,
            StudFarmId = al.StudFarmId,
            ListingType = ListingType.Auction,
            Status = ListingStatus.Draft,
            PlatformFeePercent = null,
            Description = al.Description,
            StartingPrice = al.StartingPrice,
            ReservePrice = al.ReservePrice,
            IsNoReserve = al.IsNoReserve,
            MinimumBidIncrement = al.MinimumBidIncrement,
            EndDateTime = DateTime.UtcNow.AddDays(7) // Stale end date not carried over — admin must update before publishing
        };

        var created = await _listingRepo.AddAsync(newListing);
        return ServiceResult<ListingDto>.Created(MapToDto(created, true));
    }

    public async Task<ServiceResult<IReadOnlyList<ListingCardDto>>> GetListingCardsAsync(
        Guid? seasonId, Guid? studFarmId, string? type)
    {
        ListingType? listingType = type switch
        {
            "Auction" => ListingType.Auction,
            _         => null
        };

        var listings = await _listingRepo.GetActiveAsync(seasonId, studFarmId, listingType);

        var auctionIds = listings.OfType<AuctionListing>().Select(l => l.Id).ToList();
        var bidAggregates = auctionIds.Count > 0
            ? await _listingRepo.GetBidAggregatesAsync(auctionIds)
            : new Dictionary<Guid, (int Count, decimal? Highest)>();

        var dtos = listings.Select(l => MapToCardDto(l, bidAggregates)).ToList();
        return ServiceResult<IReadOnlyList<ListingCardDto>>.Ok(dtos);
    }

    private static ListingCardDto MapToCardDto(
        Listing l,
        Dictionary<Guid, (int Count, decimal? Highest)> bidAggregates)
    {
        var primaryImage = l.Stallion?.Images
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.DisplayOrder)
            .FirstOrDefault()?.BlobPath;

        if (l is AuctionListing al)
        {
            bidAggregates.TryGetValue(al.Id, out var bidData);
            return new ListingCardDto
            {
                Id               = al.Id,
                ListingType      = "Auction",
                StallionId       = al.StallionId,
                StallionName     = al.Stallion?.Name ?? string.Empty,
                PrimaryImagePath = primaryImage,
                StudFarmId       = al.StudFarmId,
                StudFarmName     = al.StudFarm?.Name ?? string.Empty,
                SeasonName       = al.Season?.Name,
                PriceIncGst      = al.StartingPrice,
                CurrentHighestBidIncGst = bidData.Highest,
                BidCount         = bidData.Count,
                AuctionClosesAt  = al.EndDateTime,
                ReserveMet       = al.IsNoReserve
                    ? null
                    : bidData.Highest.HasValue
                        ? al.ReservePrice.HasValue && bidData.Highest >= al.ReservePrice
                        : (bool?)null   // no bids yet — unknown, avoid showing "Reserve not met" on day 1
            };
        }

        throw new InvalidOperationException($"Unknown listing type: {l.GetType().Name}");
    }

    private static ListingDto MapToDto(Listing l, bool isStaff) => l switch
    {
        AuctionListing al => new AuctionListingDto
        {
            Id = al.Id,
            StallionId = al.StallionId,
            StallionName = al.Stallion?.Name ?? string.Empty,
            SeasonId = al.SeasonId,
            SeasonName = al.Season?.Name ?? string.Empty,
            StudFarmId = al.StudFarmId,
            StudFarmName = al.StudFarm?.Name ?? string.Empty,
            ListingType = al.ListingType.ToString(),
            Status = al.Status.ToString(),
            PlatformFeePercent = isStaff ? al.PlatformFeePercent : null,
            CreatedAt = al.CreatedAt,
            PublishedAt = al.PublishedAt,
            ClosedAt = al.ClosedAt,
            Description = al.Description,
            TermsAndConditions = al.TermsAndConditions,
            StartingPrice = al.StartingPrice,
            ReservePrice = isStaff ? al.ReservePrice : null,
            IsNoReserve = al.IsNoReserve,
            MinimumBidIncrement = al.MinimumBidIncrement,
            EndDateTime = al.EndDateTime
        },
        _ => throw new InvalidOperationException($"Unknown listing type: {l.GetType().Name}")
    };
}
