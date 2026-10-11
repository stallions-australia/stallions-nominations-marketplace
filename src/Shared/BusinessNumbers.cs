namespace Stallions.Shared;

/// <summary>
/// Australian Business Numbers and BSBs: typed with spaces or hyphens, stored as digits only, shown grouped
/// (12 345 678 901, 062-000). Validation checks the digit count only.
/// </summary>
public static class BusinessNumbers
{
    public static string Compact(string? value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsAbn(string? compact) => compact is { Length: 11 } && compact.All(char.IsAsciiDigit);

    public static bool IsBsb(string? compact) => compact is { Length: 6 } && compact.All(char.IsAsciiDigit);

    public static string FormatAbn(string? abn)
    {
        var c = Compact(abn);
        return IsAbn(c) ? $"{c[..2]} {c[2..5]} {c[5..8]} {c[8..]}" : abn ?? string.Empty;
    }

    public static string FormatBsb(string? bsb)
    {
        var c = Compact(bsb);
        return IsBsb(c) ? $"{c[..3]}-{c[3..]}" : bsb ?? string.Empty;
    }
}
