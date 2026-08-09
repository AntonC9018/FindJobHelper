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

public readonly record struct LatexFontRoleArray<T>
{
    public LatexFontRoleArray(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != LatexFontRoles.All.Length)
        {
            throw new ArgumentException(
                $"Expected {LatexFontRoles.All.Length} values, but received {values.Length}.",
                nameof(values));
        }

        Values = values;
    }

    public T[] Values { get; }

    public T this[LatexFontRole role] => Values[(int)role];
}

public static class LatexFontRoles
{
    public static LatexFontRole[] All { get; } = Enum.GetValues<LatexFontRole>();
    public static LatexFontRoleArray<string> SetCommands { get; } = new(
        ["setmainfont", "setsansfont", "setmonofont"]);
    public static LatexFontRoleArray<string> FamilyCommands { get; } = new(
        ["rmfamily", "sffamily", "ttfamily"]);
}

public sealed record LatexFontOptions
{
    public static LatexFontOptions Default { get; } = new(new LatexFontRoleArray<LatexFontFamilyName>(
        [new("Liberation Serif"), new("Liberation Sans"), new("Latin Modern Mono")]));

    public LatexFontOptions(LatexFontRoleArray<LatexFontFamilyName> families)
    {
        ArgumentNullException.ThrowIfNull(families.Values);
        if (families.Values.Any(static family => family is null))
        {
            throw new ArgumentException("LaTeX font families cannot contain null.", nameof(families));
        }

        Families = families;
    }

    public LatexFontRoleArray<LatexFontFamilyName> Families { get; }

    public LatexFontFamilyName this[LatexFontRole role] => Families[role];
}

public static class LatexFontConfigurationRenderer
{
    public static string Render(LatexFontOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.Join('\n', LatexFontRoles.All.Select(
            role => $"\\{LatexFontRoles.SetCommands[role]}{{{options[role].Value}}}"));
    }
}
