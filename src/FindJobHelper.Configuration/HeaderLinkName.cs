namespace FindJobHelper.Configuration;

public readonly record struct HeaderLinkName(string Value)
{
    public static HeaderLinkName GitHub => new("GitHub");
    public static HeaderLinkName LinkedIn => new("LinkedIn");
    public static HeaderLinkName YouTube => new("YouTube");
    public static HeaderLinkName Portfolio => new("Portfolio");

    public override string ToString() => Value;
}
