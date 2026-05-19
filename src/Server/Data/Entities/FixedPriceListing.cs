namespace Stallions.Server.Data.Entities;

public class FixedPriceListing : Listing
{
    public decimal PriceIncGst { get; set; }
    public int Quantity { get; set; }
    public int QuantityRemaining { get; set; }
}
