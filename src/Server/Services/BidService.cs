using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Bids;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class BidService : IBidService
{
    private readonly IBidRepository _bidRepo;
    private readonly IListingRepository _listingRepo;
    private readonly IUserService _users;

    public BidService(IBidRepository bidRepo, IListingRepository listingRepo, IUserService users)
    {
        _bidRepo = bidRepo;
        _listingRepo = listingRepo;
        _users = users;
    }

    public async Task<ServiceResult<CurrentBidDto>> GetCurrentBidAsync(Guid auctionListingId)
    {
        var highest = await _bidRepo.GetHighestBidAsync(auctionListingId);
        return ServiceResult<CurrentBidDto>.Ok(new CurrentBidDto
        {
            HighestBidIncGst = highest?.AmountIncGst,
            LastBidAt = highest?.PlacedAt
        });
    }

    public async Task<ServiceResult<BidDto>> PlaceBidAsync(Guid auctionListingId, PlaceBidRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<BidDto>.Forbidden();

        if (caller.Status != UserStatus.Active)
            return ServiceResult<BidDto>.Forbidden("Your account must be verified before you can bid.");

        var auction = await _listingRepo.GetAuctionByIdAsync(auctionListingId);
        if (auction == null)
            return ServiceResult<BidDto>.NotFound("Auction listing not found.");

        if (auction.Status != ListingStatus.Active)
            return ServiceResult<BidDto>.BadRequest("This auction is no longer accepting bids.");

        if (auction.EndDateTime <= DateTime.UtcNow)
            return ServiceResult<BidDto>.BadRequest("This auction has ended.");

        var highest = await _bidRepo.GetHighestBidAsync(auctionListingId);
        var minimumRequired = highest != null
            ? highest.AmountIncGst + auction.MinimumBidIncrement
            : auction.StartingPrice;

        if (request.AmountIncGst < minimumRequired)
            return ServiceResult<BidDto>.BadRequest(
                $"Bid amount must be at least ${minimumRequired:N2} (inc. GST).");

        if (highest != null)
        {
            highest.Status = BidStatus.Outbid;
            await _bidRepo.UpdateAsync(highest);
        }

        var bid = new Bid
        {
            AuctionListingId = auctionListingId,
            BuyerUserId = caller.Id,
            AmountIncGst = request.AmountIncGst,
            Status = BidStatus.Active
        };

        var saved = await _bidRepo.AddAsync(bid);
        return ServiceResult<BidDto>.Created(MapToDto(saved));
    }

    public async Task<ServiceResult<IReadOnlyList<BidDto>>> GetHistoryAsync(Guid auctionListingId)
    {
        var bids = await _bidRepo.GetByAuctionListingIdAsync(auctionListingId);
        return ServiceResult<IReadOnlyList<BidDto>>.Ok(bids.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<BidDto>>> GetMineAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<IReadOnlyList<BidDto>>.Forbidden();

        var bids = await _bidRepo.GetByBuyerIdAsync(caller.Id);
        return ServiceResult<IReadOnlyList<BidDto>>.Ok(bids.Select(MapToDto).ToList());
    }

    private static BidDto MapToDto(Bid b) => new()
    {
        Id = b.Id,
        AuctionListingId = b.AuctionListingId,
        BuyerUserId = b.BuyerUserId,
        AmountIncGst = b.AmountIncGst,
        PlacedAt = b.PlacedAt,
        Status = b.Status.ToString()
    };
}
