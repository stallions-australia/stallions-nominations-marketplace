namespace Stallions.Client.Components;

public static class ListingTypeText
{
    public static string Label(string type) => type switch
    {
        "MakeAnOffer" => "Make an Offer",
        _ => type
    };
}
