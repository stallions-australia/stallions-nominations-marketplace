using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
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
    private readonly AppDbContext _db;

    public BidService(IBidRepository bidRepo, IListingRepository listingRepo, IUserService users, AppDbContext db)
    {
        _bidRepo = bidRepo;
        _listingRepo = listingRepo;
        _users = users;
        _db = db;
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
        // Fast guards (no transaction needed)
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<BidDto>.Forbidden();

        if (caller.Status != UserStatus.Active)
            return ServiceResult<BidDto>.Forbidden("Your account must be verified before you can bid.");

        var listing = await _listingRepo.GetAuctionByIdAsync(auctionListingId);
        if (listing == null)
            return ServiceResult<BidDto>.NotFound("Auction listing not found.");

        if (listing.Status != ListingStatus.Active)
            return ServiceResult<BidDto>.BadRequest("This auction is no longer accepting bids.");

        if (listing.EndDateTime <= DateTime.UtcNow)
            return ServiceResult<BidDto>.BadRequest("This auction has ended.");

        // Transactional section: re-read, validate, and mutate atomically
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var highest = await _bidRepo.GetHighestBidAsync(auctionListingId);
            var minimumRequired = highest != null
                ? highest.AmountIncGst + listing.MinimumBidIncrement
                : listing.StartingPrice;

            if (request.AmountIncGst < minimumRequired)
                return ServiceResult<BidDto>.BadRequest(
                    $"Bid must be at least ${minimumRequired:N2} (minimum increment: ${listing.MinimumBidIncrement:N2}).");

            if (highest != null && highest.BuyerUserId == caller.Id)
                return ServiceResult<BidDto>.BadRequest("You already hold the highest bid on this auction.");

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

            var created = await _bidRepo.AddAsync(bid);
            await tx.CommitAsync();
            return ServiceResult<BidDto>.Created(MapToDto(created));
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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
