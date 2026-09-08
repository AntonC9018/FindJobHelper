namespace FindJobHelper.Core.Helper;

public record struct BlurParams()
{
    public required string String;
    public int MaxVisibleLen = 5;
    public int MinVisibleLen = 5;
}

public static class Miscellanious
{
    public static string BlurPhone(BlurParams p)
    {
        var s = p.String;
        int minLenBlurred = Math.Min(s.Length, p.MinVisibleLen);
        int lenToBlur = Math.Max(s.Length - p.MinVisibleLen, minLenBlurred);
        int lenToKeep = s.Length - lenToBlur;
        var start = s.AsSpan()[.. lenToKeep];
        var end = new Repeat("*", lenToBlur);
        return $"{start}{end}";
    }
}
