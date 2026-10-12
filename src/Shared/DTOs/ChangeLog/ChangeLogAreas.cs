namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>The areas of Staff change the change log shows.</summary>
public static class ChangeLogAreas
{
    public const string Settings = "Settings";
    public const string BusinessDetails = "BusinessDetails";
    public const string IntroOffers = "IntroOffers";
    public const string Wording = "Wording";
    public const string Terms = "Terms";
    public const string StudTermsAccepted = "StudTermsAccepted";

    public static IReadOnlyList<(string Value, string Label)> All { get; } =
    [
        (Settings, "Platform settings"),
        (BusinessDetails, "Business details"),
        (IntroOffers, "Intro offers"),
        (Wording, "Wording"),
        (Terms, "Terms published"),
        (StudTermsAccepted, "Stud terms accepted")
    ];

    public static bool IsKnown(string area) => All.Any(a => a.Value == area);

    public static string Label(string area) => All.FirstOrDefault(a => a.Value == area).Label ?? area;
}
