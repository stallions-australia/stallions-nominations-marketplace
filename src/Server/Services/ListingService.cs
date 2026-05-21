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
        var listings = await _listingRepo.GetActiveAsync(seasonId, type);
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

        var listing = new AuctionListing
        {
            StallionId = request.StallionId,
            SeasonId = request.SeasonId,
            StudFarmId = farm.Id,
            ListingType = ListingType.Auction,
            Status = ListingStatus.Draft,
            StartingPrice = request.StartingPrice,
            ReservePrice = request.ReservePrice,
            IsNoReserve = request.IsNoReserve,
            MinimumBidIncrement = request.MinimumBidIncrement,
            EndDateTime = request.EndDateTime
        };

        var created = await _listingRepo.AddAsync(listing);
        return ServiceResult<ListingDto>.Created(MapToDto(created, true));
    }

    public async Task<ServiceResult<ListingDto>> CreateFixedPriceListingAsync(CreateFixedPriceListingRequest request)
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

        if (request.Quantity <= 0)
            return ServiceResult<ListingDto>.BadRequest("Quantity must be greater than zero.");
        if (request.PriceIncGst <= 0)
            return ServiceResult<ListingDto>.BadRequest("Price must be greater than zero.");

        var listing = new FixedPriceListing
        {
            StallionId = request.StallionId,
            SeasonId = request.SeasonId,
            StudFarmId = farm.Id,
            ListingType = ListingType.FixedPrice,
            Status = ListingStatus.Draft,
            PriceIncGst = request.PriceIncGst,
            Quantity = request.Quantity,
            QuantityRemaining = request.Quantity
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

        if (listing.Status != ListingStatus.Draft)
            return ServiceResult<ListingDto>.BadRequest("Only Draft listings can be edited.");

        // CRITICAL: PlatformFeePercent is never touched here — only AdminService.SetListingFeeAsync can set it.
        if (listing is AuctionListing al)
        {
            if (request.StartingPrice.HasValue) al.StartingPrice = request.StartingPrice.Value;
            if (request.ReservePrice.HasValue) al.ReservePrice = request.ReservePrice;
            if (request.IsNoReserve.HasValue) al.IsNoReserve = request.IsNoReserve.Value;
            if (request.MinimumBidIncrement.HasValue) al.MinimumBidIncrement = request.MinimumBidIncrement.Value;
            if (request.EndDateTime.HasValue) al.EndDateTime = request.EndDateTime.Value;
        }
        else if (listing is FixedPriceListing fpl)
        {
            if (request.PriceIncGst.HasValue) fpl.PriceIncGst = request.PriceIncGst.Value;
            if (request.Quantity.HasValue)
            {
                fpl.Quantity = request.Quantity.Value;
                fpl.QuantityRemaining = request.Quantity.Value;
            }
        }

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

        listing.Status = ListingStatus.Active;
        listing.PublishedAt = DateTime.UtcNow;
        await _listingRepo.UpdateAsync(listing);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CancelListingAsync(Guid id)
    {
        var listing = await _listingRepo.GetByIdAsync(id);
        if (listing == null)
            return ServiceResult.NotFound("Listing not found.");

        if (listing.Status == ListingStatus.Cancelled)
            return ServiceResult.BadRequest("Listing is already cancelled.");

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

        Listing newListing;

        if (listing is AuctionListing al)
        {
            // PlatformFeePercent is NOT carried over — must be set again by staff before publishing.
            newListing = new AuctionListing
            {
                StallionId = al.StallionId,
                SeasonId = al.SeasonId,
                StudFarmId = al.StudFarmId,
                ListingType = ListingType.Auction,
                Status = ListingStatus.Draft,
                PlatformFeePercent = null,
                StartingPrice = al.StartingPrice,
                ReservePrice = al.ReservePrice,
                IsNoReserve = al.IsNoReserve,
                MinimumBidIncrement = al.MinimumBidIncrement,
                EndDateTime = al.EndDateTime
            };
        }
        else if (listing is FixedPriceListing fpl)
        {
            // PlatformFeePercent is NOT carried over — must be set again by staff before publishing.
            newListing = new FixedPriceListing
            {
                StallionId = fpl.StallionId,
                SeasonId = fpl.SeasonId,
                StudFarmId = fpl.StudFarmId,
                ListingType = ListingType.FixedPrice,
                Status = ListingStatus.Draft,
                PlatformFeePercent = null,
                PriceIncGst = fpl.PriceIncGst,
                Quantity = fpl.Quantity,
                QuantityRemaining = fpl.Quantity
            };
        }
        else
        {
            throw new InvalidOperationException($"Unknown listing type: {listing.GetType().Name}");
        }

        var created = await _listingRepo.AddAsync(newListing);
        return ServiceResult<ListingDto>.Created(MapToDto(created, true));
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
            StartingPrice = al.StartingPrice,
            ReservePrice = isStaff ? al.ReservePrice : null,
            IsNoReserve = al.IsNoReserve,
            MinimumBidIncrement = al.MinimumBidIncrement,
            EndDateTime = al.EndDateTime
        },
        FixedPriceListing fpl => new FixedPriceListingDto
        {
            Id = fpl.Id,
            StallionId = fpl.StallionId,
            StallionName = fpl.Stallion?.Name ?? string.Empty,
            SeasonId = fpl.SeasonId,
            SeasonName = fpl.Season?.Name ?? string.Empty,
            StudFarmId = fpl.StudFarmId,
            StudFarmName = fpl.StudFarm?.Name ?? string.Empty,
            ListingType = fpl.ListingType.ToString(),
            Status = fpl.Status.ToString(),
            PlatformFeePercent = isStaff ? fpl.PlatformFeePercent : null,
            CreatedAt = fpl.CreatedAt,
            PublishedAt = fpl.PublishedAt,
            ClosedAt = fpl.ClosedAt,
            PriceIncGst = fpl.PriceIncGst,
            Quantity = fpl.Quantity,
            QuantityRemaining = fpl.QuantityRemaining
        },
        _ => throw new InvalidOperationException($"Unknown listing type: {l.GetType().Name}")
    };
}
