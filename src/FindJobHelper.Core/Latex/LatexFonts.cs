using System.Globalization;
using System.Text;

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

public sealed record LatexFontScale
{
    public LatexFontScale(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A LaTeX font scale must be positive and finite.");
        }

        Value = value;
    }

    public double Value { get; }

    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);
}

public enum LatexFontRole
{
    Main,
    Sans,
    Mono,
}

public readonly record struct LatexFontRoleArray<T> : IEnumerable<T>
{
    public LatexFontRoleArray(T main, T sans, T monospace)
    {
        Main = main;
        Sans = sans;
        Monospace = monospace;
    }

    public T Main { get; }
    public T Sans { get; }
    public T Monospace { get; }

    public T this[LatexFontRole role] => role switch
    {
        LatexFontRole.Main => Main,
        LatexFontRole.Sans => Sans,
        LatexFontRole.Mono => Monospace,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static LatexFontRoleArray<T> Create(Func<LatexFontRole, T> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        var values = new T[LatexFontRoles.All.Length];
        foreach (var role in LatexFontRoles.All)
        {
            var index = (int)role;
            var value = valueFactory(role);
            values[index] = value;
        }

        var main = values[(int)LatexFontRole.Main];
        var sans = values[(int)LatexFontRole.Sans];
        var monospace = values[(int)LatexFontRole.Mono];
        return new(
            main: main,
            sans: sans,
            monospace: monospace);
    }

    public LatexFontRoleArray<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var source = this;

        TResult SelectRole(LatexFontRole role)
        {
            var value = source[role];
            return selector(value);
        }

        return LatexFontRoleArray<TResult>.Create(SelectRole);
    }

    public IEnumerator<T> GetEnumerator()
    {
        yield return Main;
        yield return Sans;
        yield return Monospace;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class LatexFontRoles
{
    public static LatexFontRole[] All { get; } = Enum.GetValues<LatexFontRole>();
    public static LatexFontRoleArray<string> SetCommands { get; } = new(
        main: "setmainfont",
        sans: "setsansfont",
        monospace: "setmonofont");
    public static LatexFontRoleArray<string> FamilyCommands { get; } = new(
        main: "rmfamily",
        sans: "sffamily",
        monospace: "ttfamily");
}

public sealed record LatexFontOptions
{
    private static readonly LatexFontRoleArray<LatexFontFamilyName> DefaultFamilies = new(
        main: new("Liberation Serif"),
        sans: new("Liberation Sans"),
        monospace: new("Liberation Mono"));
    private static readonly LatexFontRoleArray<LatexFontScale?> DefaultScales = new(
        main: null,
        sans: null,
        monospace: new(0.92));

    public static LatexFontOptions Default { get; } = new(
        families: DefaultFamilies,
        scales: DefaultScales);

    public LatexFontOptions(
        LatexFontRoleArray<LatexFontFamilyName> families,
        LatexFontRoleArray<LatexFontScale?> scales)
    {
        if (families.Any(static family => family is null))
        {
            throw new ArgumentException("LaTeX font families cannot contain null.", nameof(families));
        }

        Families = families;
        Scales = scales;
    }

    public LatexFontRoleArray<LatexFontFamilyName> Families { get; }
    public LatexFontRoleArray<LatexFontScale?> Scales { get; }

    public LatexFontFamilyName this[LatexFontRole role] => Families[role];
}

public static class LatexFontConfigurationRenderer
{
    public static string Render(LatexFontOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new StringBuilder();
        foreach (var role in LatexFontRoles.All)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append('\\');
            var command = LatexFontRoles.SetCommands[role];
            builder.Append(command);
            var scale = options.Scales[role];
            if (scale is not null)
            {
                builder.Append("[Scale=");
                builder.Append(scale);
                builder.Append(']');
            }

            builder.Append('{');
            var family = options.Families[role];
            builder.Append(family.Value);
            builder.Append('}');
        }

        return builder.ToString();
    }
}
