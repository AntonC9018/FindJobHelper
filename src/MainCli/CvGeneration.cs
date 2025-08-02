using System.Collections.Immutable;
using System.Diagnostics;
using CliWrap;
using CodegenCS;
using CodegenCS.IO;
using ReaderApp.Helper;

namespace MainCli;

public record struct GenerateParams()
{
    public required string ConfigFilePath;
    public required CvDataModel Model;
    public required CancellationToken CancellationToken;
    public bool OpenInOs = false;
}

public static class CvTemplate
{
    public static async Task Generate(GenerateParams p)
    {
        var guid = Guid.NewGuid();
        var tempDir = new DirectoryInfo($"cv_gen_{guid}");

        var codegenContext = new CodegenContext();
        const string latexFileName = "main.tex";
        var writer = codegenContext[latexFileName];

        writer.Write($$$"""
        \input{{{{ p.ConfigFilePath.Replace('\\', '/') }}}}

        \begin{document}

        \pagestyle{fancy}

        % Title Headline
        \vspace{-8pt}
        \begin{center}
            \HUGE \textsc{{{{ p.Model.Name.Last }}} {{{ p.Model.Name.First }}}} \textcolor{sectcol}{\rule[-1mm]{1mm}{0.9cm}} \textsc{Resume}\\[2pt]
            \small {{{ p.Model.Profession.Value }}}
        \end{center}

        \vspace{6pt}

        {{{ MetaLists(p.Model) }}}

        {{{ Symbols.IF(!p.Model.Summary.IsNull) }}}
        % Summary
        \vspace{-6pt}
        \cvsection{Summary}

        {{{ p.Model.Summary }}}\\

        {{{ Symbols.ENDIF }}}

        % Main Content

        {{{ Events(p.Model.WorkExperiences, sectionName: "Experience") }}}

        {{{ Events(p.Model.Educations, "Education") }}}

        {{{ Footer(p.Model) }}}

        \end{document}
        """);

        codegenContext.SaveToFolder(tempDir.FullName);

        // run latex
        var latexmk = Cli.Wrap("latexmk");
        latexmk = latexmk.WithArguments(["-pdflatex", latexFileName]);

        {
            var logFile = Path.Join(tempDir.FullName, "log-stdout.txt");
            latexmk = latexmk.WithStandardOutputPipe(PipeTarget.ToFile(logFile));
        }
        {
            var logFile = Path.Join(tempDir.FullName, "log-stderr.txt");
            latexmk = latexmk.WithStandardErrorPipe(PipeTarget.ToFile(logFile));
        }

        latexmk = latexmk.WithWorkingDirectory(tempDir.FullName);
        latexmk = latexmk.WithValidation(CommandResultValidation.None);

        var result = await latexmk.ExecuteAsync(p.CancellationToken);

        if (!result.IsSuccess)
        {
            if (p.OpenInOs)
            {
                var outputPath = Path.Join(tempDir.FullName, latexFileName);
                ExplorerHelper.OpenFolderAndSelectFile(outputPath);
            }

            throw new InvalidOperationException("Latex execution failure");
        }

        if (p.OpenInOs)
        {
            var pdfOutputName = ReplaceExtension(latexFileName, ".pdf");
            var pdfOutputPath = Path.Join(tempDir.FullName, pdfOutputName);
            ExplorerHelper.OpenFolderAndSelectFile(pdfOutputPath);
        }
    }

    private static readonly RenderEnumerableOptions ListItemSeparator =
        RenderEnumerableOptions.CreateWithCustomSeparator(", ", enforceLineBreakAfterLastItem: false);

    private static FormattableString MetaLists(CvDataModel p)
    {
        int count = Math.Max(p.CategorizedInfos.Length, p.CategorizedInfoLists.Length);
        var counter = Enumerable.Range(0, count);
        var strings = counter.Select(i =>
        {
            T? Item<T>(ImmutableArray<T> items)
            {
                if (items.Length > i)
                {
                    return items[i];
                }
                return default;
            }

            var list = Item(p.CategorizedInfoLists);
            var info = (string?) Item(p.CategorizedInfos).Value.Value;

            var listValues = list.Values;
            if (listValues == default)
            {
                listValues = [];
            }
            var items = listValues.Select(x => x.Value).Render(ListItemSeparator);

            var ret = (FormattableString) $$"""\metasection{{{ info }}}{{{
                Symbols.IF(list != default)
            }}\textbf{{{ list.Category.DisplayName }}:} {{ items }}{{
                Symbols.ENDIF
            }}}""";

            return ret;
        });

        var allMeta = strings.Render(RenderEnumerableOptions.LineBreaksWithoutSpacer);
        return $$"""
            %---------------------------------------------------------------------------------------
            %	META SECTION
            %----------------------------------------------------------------------------------------
            {{ allMeta }}

            \vspace{-2pt}
            \textcolor{softcol}{\hrule}
            \vspace{6pt}

            \normalsize
        """;
    }

    private static string ReplaceExtension(
        string filePath,
        string newExtension)
    {
        int extensionStart = filePath.LastIndexOf('.');
        if (extensionStart <= 0)
        {
            return $"{filePath}{newExtension}";
        }

        var s = filePath.AsSpan()[.. extensionStart];
        return $"{s}{newExtension}";
    }

    private static FormattableString Events(ImmutableArray<Event> events, string sectionName)
    {
        if (events.Length == 0)
        {
            return $"";
        }

        var items = events.Select(e =>
        {
            var subItems = e.SubItems
                .Select(x => (FormattableString) $$"""{{{ x }}}""")
                .Render(RenderEnumerableOptions.LineBreaksWithoutSpacer);
            return (FormattableString) $$"""
                \cvevent{{{ e.DateRange }}}{{{ e.Title }}}{{{ e.Place.Name }}}{
                    {{{ subItems }}}
                }
            """;
        });
        var eventsRendered = items.Render(RenderEnumerableOptions.LineBreaksWithSpacer);
        return $$"""
            \cvsection{{{ sectionName }}}

            {{ eventsRendered }}
        """;
    }


    private static FormattableString Footer(CvDataModel model)
    {
        var website = model.Website;
        var github = model.GitHub;

        int itemCount = 0;
        if (!website.IsNull)
        {
            itemCount += 1;
        }
        if (!github.IsNull)
        {
            itemCount += 1;
        }

        if (itemCount == 0)
        {
            return $"";
        }

        var arr = new FormattableString[itemCount];
        int i = 0;
        {
            if (!website.IsNull)
            {
                arr[i] = $$"""\textnormal{\textcolor{sectcol}{{{ website.Value }}}""";
                i++;
            }
            if (!github.IsNull)
            {
                arr[i] = $$"""\textcolor{sectcol}{{{ github.Value }}}""";
                i++;
            }
            _ = i;
            Debug.Assert(i == arr.Length);
        }
        var list = arr.Render(RenderEnumerableOptions.CreateWithCustomSeparator(@" $\cdot$ "));
        return $$"""
            % Footer
            \null
            \vspace*{\fill}
            \hspace{-0.25\linewidth}\colorbox{white}{\makebox[1.5\linewidth][c]{\mystrut  {{ list }}}
        """;
    }
}

public sealed class CvDataModel
{
    public required Name Name;
    public required Profession Profession;
    public NullableLocation Location = NullableLocation.Null;
    public required ImmutableArray<CategorizedInfoList> CategorizedInfoLists;
    public required ImmutableArray<CategorizedInfo> CategorizedInfos;
    public NullableLatexString Summary = NullableLatexString.Null;
    public ImmutableArray<LanguageProficiencyInfo> Languages = ImmutableArray<LanguageProficiencyInfo>.Empty;
    public ImmutableArray<Event> WorkExperiences = ImmutableArray<Event>.Empty;
    public ImmutableArray<Event> Educations = ImmutableArray<Event>.Empty;
    public MaybeInfoString Website;
    public MaybeInfoString GitHub;
}

public record struct Event()
{
    public required string Title;
    public required Place Place;
    public required DateRange DateRange;
    public NullableLatexString Text = NullableLatexString.Null;
    public required ImmutableArray<LatexString> SubItems;
}

// public record struct EducationItem()
// {
//     public required EducationQualification Qualification;
//     public required Place Place;
//     public required DateRange DateRange;
//     public NullableLatexString Introduction = NullableLatexString.Null;
//     public required ImmutableArray<EducationDescriptionItem> Stuff;
// }
//
// public readonly record struct EducationQualification(string Name);
// public readonly record struct EducationDescriptionItem(LatexString Text);
//
// public record struct WorkExperienceItem()
// {
//     public required JobPosition Position;
//     public required Place Place;
//     public required DateRange DateRange;
//     public required NullableLatexString Introduction = NullableLatexString.Null;
//     public required ImmutableArray<JobResponsibility> Responsibilities;
// }
//
// public readonly record struct JobResponsibility(LatexString Text);
public readonly record struct Place(string Name);
// public readonly record struct JobPosition(string Title);

public readonly record struct OptionalDateParts
{
    public readonly int Year;
    public readonly int Month;
    public readonly int Day;

    public OptionalDateParts(int Year, int Month = 0, int Day = 0)
    {
        Debug.Assert(Month is >= 0 and <= 12);
        Debug.Assert(Day is >= 0 and <= 31);
        if (Year == 0)
        {
            Debug.Assert(Month == 0 && Day == 0);
        }
        if (Month == 0)
        {
            Debug.Assert(Day == 0);
        }

        this.Year = Year;
        this.Month = Month;
        this.Day = Day;
    }

    public static OptionalDateParts Unspecified => default;
    public bool IsUnspecified => Year == 0;
}

public readonly record struct DateRange(
    OptionalDateParts Start,
    OptionalDateParts End) : ISpanFormattable
{
    public static DateRange Current(OptionalDateParts start)
    {
        Debug.Assert(!start.IsUnspecified);
        return new(start, OptionalDateParts.Unspecified);
    }
    public static DateRange Completed(OptionalDateParts start, OptionalDateParts end)
    {
        Debug.Assert(!start.IsUnspecified);
        Debug.Assert(!end.IsUnspecified);
        return new DateRange(start, end);
    }

    public bool IsCurrent => End.IsUnspecified;

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return ToString();
    }
    public override string ToString() => $"{this}";

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = format;

        charsWritten = 0;
        var helper = new WriteHelper(destination, ref charsWritten);
        if (!AppendDate(helper, Start, provider))
        {
            return false;
        }
        helper.Append(" - ");
        if (End.IsUnspecified)
        {
            if (!helper.Append("current"))
            {
                return false;
            }
        }
        else
        {
            if (!AppendDate(helper, End, provider))
            {
                return false;
            }
        }
        return true;

        static bool AppendDate(
            WriteHelper helper,
            OptionalDateParts d,
            IFormatProvider? provider)
        {
            if (d.Day != 0)
            {
                if (!helper.Append(d.Day, provider))
                {
                    return false;
                }
                if (!helper.Append('.'))
                {
                    return false;
                }
            }
            if (d.Month != 0)
            {
                if (!helper.Append(d.Month, provider))
                {
                    return false;
                }
                if (!helper.Append('.'))
                {
                    return false;
                }
            }
            Debug.Assert(d.Year != 0);
            if (!helper.Append(d.Year, provider))
            {
                return false;
            }
            return true;
        }
    }
}

public readonly record struct Language(string Name);
public readonly record struct LanguageProficiencyLevel(string Value)
{
    public static LanguageProficiencyLevel A1 => new("A1");
    public static LanguageProficiencyLevel A2 => new("A2");
    public static LanguageProficiencyLevel B1 => new("B1");
    public static LanguageProficiencyLevel B2 => new("B2");
    public static LanguageProficiencyLevel C1 => new("C1");
    public static LanguageProficiencyLevel C2 => new("C2");
    public static LanguageProficiencyLevel Native => new("Native");
}
// public readonly record struct LanguageClassificationCategory(string Category);
public readonly record struct LanguageProficiencyInfo(
    Language Language,
    LanguageProficiencyLevel GeneralProficiencyLevel);

public readonly record struct LatexString(string Value) : ISpanFormattable
{
    public override string ToString() => $"{this}";

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return ToString();
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        charsWritten = 0;
        var helper = new WriteHelper(destination, ref charsWritten);
        foreach (var ch in Value)
        {
            string? appendContent = ch switch
            {
                '\\' => @"\textbackslash{}",
                '{' => @"\{",
                '}' => @"\}",
                '#' => @"\#",
                '$' => @"\$",
                '%' => @"\%",
                '&' => @"\&",
                '_' => @"\_",
                '^' => @"\^{}",
                '~' => @"\~{}",
                _ => null,
            };
            if (appendContent is null)
            {
                if (!helper.Append(ch))
                {
                    return false;
                }
                continue;
            }
            if (!helper.Append(appendContent))
            {
                return false;
            }
        }
        return true;
    }
}

public readonly record struct NullableLatexString(string? Value) : ISpanFormattable
{
    public static NullableLatexString Null => default;
    public bool IsNull => Value is null;

    public override string ToString() => $"{Value}";

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return ToString();
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        charsWritten = 0;
        if (Value is null)
        {
            return true;
        }

        var latexString = new LatexString(Value);
        if (latexString.TryFormat(destination, out charsWritten, format, provider))
        {
            return true;
        }
        return false;
    }
}

public readonly record struct InfoString(string Value);
public readonly record struct MaybeInfoString
{
    public readonly string? Value;

    public MaybeInfoString(string? value)
    {
        Value = value;
    }

    public MaybeInfoString(InfoString s) : this(s.Value)
    {
    }

    public static MaybeInfoString Null => default;
    public bool IsNull => Value is null;
    public InfoString ToInfoString()
    {
        Debug.Assert(Value is not null);
        return new InfoString(Value);
    }
}

public readonly record struct CategorizedInfo(
    Category Category,
    InfoString Value);

public readonly record struct CategorizedInfoList(
    Category Category,
    ImmutableArray<InfoString> Values);

public readonly record struct Category(string DisplayName)
{
    public static Category Unspecified = new("");
    public static Category Website => new("Website");
    public static Category GitHub => new("GitHub");
    public static Category LinkedIn => new("LinkedIn");
    public static Category Email => new("Email");
    public static Category Location => new("Location");
    public static Category Phone => new("Phone");
    public static Category Technologies => new("Technologies");
}

public readonly record struct Name(
    string First,
    string Last)
{
}

public readonly record struct Profession(string Value);

public readonly record struct Location(
    string City,
    string Country)
{
    public static implicit operator NullableLocation(Location location)
    {
        return new NullableLocation(location.City, location.Country);
    }

    public InfoString FormatInfo()
    {
        return new($"{City}, {Country}");
    }
}

public readonly record struct NullableLocation(
    string? City,
    string? Country)
{
    public static NullableLocation Null => default;
    public readonly bool IsNull
    {
        get
        {
            Debug.Assert(Country is null == City is null);
            return Country is null;
        }
    }
}
