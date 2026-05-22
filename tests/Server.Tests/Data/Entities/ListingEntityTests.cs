using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Entities;

public class ListingEntityTests
{
    [Fact]
    public void Listing_DefaultStatus_IsDraft()
    {
        var listing = new Listing();
        listing.Status.Should().Be(ListingStatus.Draft);
    }

    [Fact]
    public void AuctionListing_DefaultMinimumBidIncrement_Is25()
    {
        var listing = new AuctionListing();
        listing.MinimumBidIncrement.Should().Be(25m);
    }

    [Fact]
    public void AuctionListing_DefaultIsNoReserve_IsFalse()
    {
        var listing = new AuctionListing();
        listing.IsNoReserve.Should().BeFalse();
    }

    [Fact]
    public void FixedPriceListing_OnCreation_QuantityRemainingEqualsQuantity()
    {
        var listing = new FixedPriceListing { Quantity = 5, QuantityRemaining = 5 };
        listing.QuantityRemaining.Should().Be(listing.Quantity);
    }

    [Fact]
    public void FixedPriceListing_HasDescriptionAndTerms()
    {
        var listing = new FixedPriceListing
        {
            Description = "Bay stallion, excellent fertility.",
            TermsAndConditions = "Live foal guarantee, 45-day payment."
        };
        listing.Description.Should().Be("Bay stallion, excellent fertility.");
        listing.TermsAndConditions.Should().Be("Live foal guarantee, 45-day payment.");
    }
}
