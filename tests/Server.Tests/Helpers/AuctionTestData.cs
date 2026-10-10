using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Helpers;

/// <summary>Seeds auctions, buyers, cards and bids into an in-memory AppDbContext.</summary>
public static class AuctionTestData
{
    public static User Buyer(AppDbContext db, string name = "Buyer")
    {
        var user = new User
        {
            ObjectId = Guid.NewGuid().ToString(), DisplayName = name,
            Email = $"{name.Replace(' ', '.').ToLowerInvariant()}@example.com",
            Role = UserRole.Buyer, Status = UserStatus.Active
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    public static SavedCard Card(AppDbContext db, User buyer, string paymentMethodId = "pm_good",
        string provider = "Fake", int expYear = 2030)
    {
        var card = new SavedCard
        {
            UserId = buyer.Id, Provider = provider, ProviderCustomerId = "cus_" + buyer.Id.ToString("N"),
            ProviderPaymentMethodId = paymentMethodId, Brand = "visa", Last4 = "4242", ExpMonth = 12, ExpYear = expYear
        };
        db.SavedCards.Add(card);
        db.SaveChanges();
        return card;
    }

    public static AuctionListing Auction(AppDbContext db, DateTime endsAt, decimal? reserve = null,
        decimal? buyerFee = 150m, ListingStatus status = ListingStatus.Active)
    {
        var owner = new User
        {
            ObjectId = Guid.NewGuid().ToString(), DisplayName = "Stud Owner", Email = "owner@arrowfield.example",
            Role = UserRole.StudFarmAdmin, Status = UserStatus.Active
        };
        var farm = new StudFarm { Name = "Arrowfield", UserId = owner.Id, User = owner, ContactEmail = "sales@arrowfield.example" };
        var stallion = new Stallion { Name = "Snitzel", StudFarmId = farm.Id, StudFarm = farm };
        var season = new Season { Name = "2026 Season" };
        var listing = new AuctionListing
        {
            StallionId = stallion.Id, Stallion = stallion, SeasonId = season.Id, Season = season,
            StudFarmId = farm.Id, StudFarm = farm, ListingType = ListingType.Auction, Status = status,
            EndDateTime = endsAt, ReservePrice = reserve, IsNoReserve = reserve == null,
            BuyerFeeIncGst = buyerFee, MinimumBidIncrement = 25m
        };
        db.AddRange(owner, farm, stallion, season, listing);
        db.SaveChanges();
        return listing;
    }

    public static Bid Bid(AppDbContext db, AuctionListing listing, User buyer, decimal amount,
        BidStatus status = BidStatus.Active)
    {
        var bid = new Bid { AuctionListingId = listing.Id, BuyerUserId = buyer.Id, AmountIncGst = amount, Status = status };
        db.Bids.Add(bid);
        db.SaveChanges();
        return bid;
    }
}
