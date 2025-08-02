namespace MainCli.Helper;

public static class Miscellanious
{
    public static string BlurPhone(string phone)
    {
        const int len = 3;
        int minLenBlured = Math.Min(phone.Length, 5);
        int lenToBlur = Math.Max(phone.Length - len, minLenBlured);
        int lenToKeep = phone.Length - lenToBlur;
        var start = phone.AsSpan()[.. lenToKeep];
        var end = new Repeat("*", lenToBlur);
        return $"{start}{end}";
    }
}
