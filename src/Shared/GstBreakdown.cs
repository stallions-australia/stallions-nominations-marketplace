namespace Stallions.Shared;

/// <summary>
/// Splits a GST-inclusive Australian amount into its ex-GST and GST parts (GST = 1/11 of the
/// inclusive amount, rounded to cents). ExGst is derived by subtraction so the parts always sum
/// back to the inclusive amount. Use this for every fee split (listing fee, buyer fee).
/// </summary>
public readonly record struct GstBreakdown(decimal IncGst, decimal ExGst, decimal Gst)
{
    public static GstBreakdown FromIncGst(decimal incGst)
    {
        var gst = Math.Round(incGst / 11m, 2, MidpointRounding.AwayFromZero);
        return new GstBreakdown(incGst, incGst - gst, gst);
    }
}
