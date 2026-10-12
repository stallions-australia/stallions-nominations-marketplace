namespace Stallions.Shared.Enums;

/// <summary>Who a terms document is for. Versions are numbered separately per audience.</summary>
public enum TermsAudience
{
    /// <summary>Buyer Terms &amp; Conditions, accepted before bidding or making an offer.</summary>
    Buyer,
    /// <summary>Stud listing terms, accepted before paying a listing fee or publishing a listing.</summary>
    Stud
}
