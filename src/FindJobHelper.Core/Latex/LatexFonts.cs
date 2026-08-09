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

public sealed record LatexFontOptions
{
    public static LatexFontOptions Default { get; } = new(
        mainFontFamily: new("Liberation Serif"),
        sansFontFamily: new("Liberation Sans"),
        monoFontFamily: new("Latin Modern Mono"));

    public LatexFontOptions(
        LatexFontFamilyName mainFontFamily,
        LatexFontFamilyName sansFontFamily,
        LatexFontFamilyName monoFontFamily)
    {
        ArgumentNullException.ThrowIfNull(mainFontFamily);
        ArgumentNullException.ThrowIfNull(sansFontFamily);
        ArgumentNullException.ThrowIfNull(monoFontFamily);
        MainFontFamily = mainFontFamily;
        SansFontFamily = sansFontFamily;
        MonoFontFamily = monoFontFamily;
    }

    public LatexFontFamilyName MainFontFamily { get; }
    public LatexFontFamilyName SansFontFamily { get; }
    public LatexFontFamilyName MonoFontFamily { get; }
}

public static class LatexFontConfigurationRenderer
{
    public static string Render(LatexFontOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"\\setmainfont{{{options.MainFontFamily.Value}}}\n" +
               $"\\setsansfont{{{options.SansFontFamily.Value}}}\n" +
               $"\\setmonofont{{{options.MonoFontFamily.Value}}}";
    }
}

[Flags]
public enum ManuallySpecifiedLatexFontRoles
{
    None = 0,
    Main = 1 << 0,
    Sans = 1 << 1,
    Mono = 1 << 2,
}

public static class ManuallySpecifiedLatexFontRolesHelper
{
    public static ManuallySpecifiedLatexFontRoles Add(
        ManuallySpecifiedLatexFontRoles roles,
        ManuallySpecifiedLatexFontRoles role)
        => roles | role;

    public static bool Contains(
        ManuallySpecifiedLatexFontRoles roles,
        ManuallySpecifiedLatexFontRoles role)
        => (roles & role) == role;
}
