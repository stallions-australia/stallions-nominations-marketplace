using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public enum BuyerAction { Bid, Offer }

/// <summary>Why a buyer can't bid or offer. Forbidden = account not verified (403); otherwise 400.</summary>
public sealed record EligibilityFailure(bool Forbidden, string Message);

/// <summary>
/// The rules a buyer must meet before bidding or making an offer: a verified account, the current
/// T&amp;C accepted (skipped when none is published), and a valid saved card so the buyer fee can be
/// charged automatically on a sale.
/// </summary>
public sealed class BuyerEligibility(ITermsRepository terms, ICardService cards)
{
    public async Task<EligibilityFailure?> CheckAsync(User caller, BuyerAction action)
    {
        if (caller.Status != UserStatus.Active)
            return new(true, action == BuyerAction.Bid
                ? "Your account must be verified before you can bid."
                : "Your account must be verified before you can make an offer.");

        var current = await terms.GetCurrentAsync();
        if (current != null && caller.AcceptedTermsVersion != current.Version)
            return new(false, action == BuyerAction.Bid
                ? "You must accept the current Terms & Conditions before placing a bid."
                : "You must accept the current Terms & Conditions before making an offer.");

        if (!await cards.HasValidCardAsync(caller.Id))
            return new(false, action == BuyerAction.Bid ? "Save a card before bidding." : "Save a card before making an offer.");

        return null;
    }
}
