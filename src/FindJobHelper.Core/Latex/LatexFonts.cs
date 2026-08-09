namespace FindJobHelper.CVGeneration;

public sealed record LatexFontFamilyName
{
    private static readonly char[] TexStructuralCharacters =
        ['#', '$', '%', '&', '\\', '^', '_', '{', '}', '~'];

    private static readonly string[] FontFileExtensions = [".otf", ".ttf", ".ttc"];

    public LatexFontFamilyName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A LaTeX font family name cannot be blank.", nameof(value));
        }
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("A LaTeX font family name cannot contain control characters.", nameof(value));
        }
        if (value.IndexOfAny(TexStructuralCharacters) >= 0)
        {
            throw new ArgumentException("A LaTeX font family name cannot contain TeX structural characters.", nameof(value));
        }
        if (value.Contains('/'))
        {
            throw new ArgumentException("A LaTeX font family name cannot be a path.", nameof(value));
        }
        if (FontFileExtensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("A LaTeX font family name cannot identify a font file.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum LatexFontRole
{
    Main,
    Sans,
    Mono,
}

public static class LatexFontRoles
{
    public static IReadOnlyList<LatexFontRole> All { get; } = Enum.GetValues<LatexFontRole>();
    public static IReadOnlyList<string> SetCommands { get; } =
        ["setmainfont", "setsansfont", "setmonofont"];
    public static IReadOnlyList<string> FamilyCommands { get; } =
        ["rmfamily", "sffamily", "ttfamily"];
}

public sealed record LatexFontOptions
{
    public static LatexFontOptions Default { get; } = new([
        new("Liberation Serif"),
        new("Liberation Sans"),
        new("Latin Modern Mono"),
    ]);

    private readonly LatexFontFamilyName[] _families;

    public LatexFontOptions(IReadOnlyList<LatexFontFamilyName> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        if (families.Count != LatexFontRoles.All.Count)
        {
            throw new ArgumentException(
                $"Expected {LatexFontRoles.All.Count} LaTeX font families, but received {families.Count}.",
                nameof(families));
        }
        if (families.Any(static family => family is null))
        {
            throw new ArgumentException("LaTeX font families cannot contain null.", nameof(families));
        }

        _families = [.. families];
    }

    public IReadOnlyList<LatexFontFamilyName> Families => _families;

    public LatexFontFamilyName this[LatexFontRole role] => _families[(int)role];
}

public static class LatexFontConfigurationRenderer
{
    public static string Render(LatexFontOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.Join('\n', LatexFontRoles.All.Select(
            role => $"\\{LatexFontRoles.SetCommands[(int)role]}{{{options[role].Value}}}"));
    }
}
