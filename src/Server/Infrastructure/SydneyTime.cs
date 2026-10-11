namespace Stallions.Server.Infrastructure;

/// <summary>Season dates are Australian calendar dates; this converts them using Sydney time.</summary>
public static class SydneyTime
{
    public static readonly TimeZoneInfo Zone = Find();

    public static DateOnly Today(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), Zone));

    /// <summary>23:59:59 on that date in Sydney, as UTC.</summary>
    public static DateTime EndOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(23, 59, 59)), Zone);

    /// <summary>00:00:00 on that date in Sydney, as UTC.</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Zone);

    private static TimeZoneInfo Find()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time"); }
    }
}
