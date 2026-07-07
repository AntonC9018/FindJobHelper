using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using CliWrap;
using CodegenCS;
using CodegenCS.IO;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

public record struct GenerateParams()
{
    public required string ConfigFilePath;
    public required CvDataModel Model;
    public required CancellationToken CancellationToken;
    public bool OpenInOs = false;
    public bool IsDebug = false;
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
        writer.AutoTrimEnd = false;
        writer.CurlyBracesStyle = CodegenTextWriter.CurlyBracesStyleType.C;
        writer.PreserveNonWhitespaceIndentBehavior = CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreserveAnything;

        var languages = Languages(p.Model.Languages);

        FormattableString Events1(ImmutableArray<Event> e, string sectionName)
        {
            return Events(
                isDebug: p.IsDebug,
                sectionName: sectionName,
                events: e);
        }
        var experience = Events1(p.Model.WorkExperiences, "Experience");
        var education = Events1(p.Model.Educations, "Education");
        var personalProjects = Events1(p.Model.PersonalProjects, "Personal Projects") ;
        var sections = new List<FormattableString>();
        foreach (var section in p.Model.SectionOrder)
        {
            sections.Add(section switch
            {
                Section.Education => education,
                Section.Languages => languages,
                Section.PersonalProjects => personalProjects,
                Section.WorkExperience => experience,
                _ => throw null!,
            });
        }

        writer.Write($$$$"""
        \input{{{{{ p.ConfigFilePath.Replace('\\', '/') }}}}}

        \begin{document}

        \pagestyle{fancy}

        % Title Headline
        \vspace{-8pt}
        \begin{center}
            \HUGE \textsc{{{{{ p.Model.Name.Last }}}} {{{{ p.Model.Name.First }}}}} \textcolor{sectcol}{\rule[-1mm]{1mm}{0.9cm}} \textsc{Resume}\\[2pt]
            \small {{{{ p.Model.Profession.Value }}}}
        \end{center}

        \vspace{6pt}

        {{{{ MetaLists(p.Model) }}}}

        {{{{ Symbols.IF(!p.Model.Summary.IsNull) }}}}
        % Summary
        \vspace{-6pt}
        \cvsection{Summary}

        {{{{ p.Model.Summary }}}}\\

        {{{{ Symbols.ENDIF }}}}

        % Main Content

        {{{{ sections.Render() }}}}

        {{{{ Footer(p.Model) }}}}

        \end{document}
        """);

        codegenContext.SaveToFolder(tempDir.FullName);

        // run latex
        var latexmk = Cli.Wrap("latexmk");
        latexmk = latexmk.WithArguments(["-xelatex", latexFileName]);

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

    private static FormattableString Languages(
        ImmutableArray<LanguageProficiencyInfo> languages)
    {
        if (languages.IsEmpty)
        {
            return $"";
        }

        return $$"""
            \cvsection{Languages}

            \languagetable{
            {{
                    languages
                        .Select(lang =>
                        {
                            var skills = lang.Skills
                                .Select(s => s.Text)
                                .Render(ListItemSeparator);

                            return (FormattableString) $$"""{{ lang.Language.Name }} & {{ lang.GeneralProficiencyLevel.Value }} & {{ skills }} \\""";
                        })
                        .Render(RenderEnumerableOptions.LineBreaksWithoutSpacer)
                }}}
        """;
    }

    private static readonly RenderEnumerableOptions ListItemSeparator =
        RenderEnumerableOptions.CreateWithCustomSeparator(", ", enforceLineBreakAfterLastItem: false);

    private static readonly RenderEnumerableOptions BarSeparator =
        RenderEnumerableOptions.CreateWithCustomSeparator(" | ", enforceLineBreakAfterLastItem: false);

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

            static FormattableString Wrap(Category cat, RegularString str)
            {
                if (str == default)
                {
                    return $"";
                }
                if (cat.IsUrl)
                {
                    return $$"""\url{{{ str.Value }}}""";
                }
                return $"{str.Value}";
            }

            var list = Item(p.CategorizedInfoLists);
            var infoItem = Item(p.CategorizedInfos);

            var infoString = Wrap(infoItem.Category, infoItem.Value);

            var listValues = list.Values;
            if (listValues == default)
            {
                listValues = [];
            }
            var formattedList = listValues
                .Select(x => Wrap(list.Category, x))
                .Render(ListItemSeparator);

            var ret = (FormattableString) $$"""\metasection{{{ infoString }}}{{{
                Symbols.IF(list != default)
            }}\textbf{{{ list.Category.DisplayName }}:} {{ formattedList }}{{
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

    private static FormattableString Events(
        bool isDebug,
        ImmutableArray<Event> events,
        string sectionName)
    {
        if (events.Length == 0)
        {
            return $"";
        }

        var items = events.Select(e =>
        {
            var subItemsE = e.SubItems
                .Select(x => (FormattableString) $$"""{{ DebugScore(x.DebugScore, x.DebugTagScores) }}{{ x.String }}""");

            if (!e.Urls.IsEmpty)
            {
                var urls = e.Urls
                    .Select(x => (FormattableString) $$"""\url{{{ x }}}""")
                    .Render(BarSeparator);

                subItemsE = subItemsE.Append((FormattableString) $$"""\textbf{Links:} {{ urls }}""");
            }

            var subItems = subItemsE
                .Select(x => (FormattableString) $"""
                     \item
                        {x}
                     """)
                .Render(RenderEnumerableOptions.LineBreaksWithoutSpacer);

            var place = e.Place.IsPersonal
                ? ""
                : e.Place.Name;

            var title = (FormattableString) $$"""{{ DebugScore(e.DebugScore, e.DebugTagScores) }}{{ e.Title }}""";

            return (FormattableString) $$"""
            \cvevent{{{ e.DateRange }}}{{{ title }}}{{{ place }}}{
                {{ subItems }}}{{{ e.Text }}}
            """;
        });
        var eventsRendered = items.Render(RenderEnumerableOptions.LineBreaksWithSpacer);
        return $$"""
            \begin{flowblock}
            \cvsection{{{ sectionName }}}

            {{ eventsRendered }}
            \end{flowblock}
        """;

        FormattableString DebugScore(
            float score,
            ImmutableArray<DebugTagScore> tagScores = default)
        {
            if (!isDebug)
            {
                return $"";
            }

            var text = FormatDebugScore(score, tagScores);
            return $$"""\debugscore{{{ new LatexEscapedString(text) }}}""";
        }
    }

    private static string FormatDebugScore(
        float score,
        ImmutableArray<DebugTagScore> tagScores)
    {
        var ret = new StringBuilder();
        ret.Append(FormatFloat(score));

        if (!tagScores.IsDefaultOrEmpty)
        {
            ret.Append(" (");
            for (var i = 0; i < tagScores.Length; i++)
            {
                if (i > 0)
                {
                    ret.Append(", ");
                }

                var tagScore = tagScores[i];
                ret.Append(tagScore.Tag.Value);
                ret.Append(':');
                ret.Append(FormatFloat(tagScore.Score));
            }

            ret.Append(')');
        }

        return ret.ToString();
    }

    private static string FormatFloat(float value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value:0.##}");
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
                arr[i] = $$"""\textnormal{\textcolor{sectcol}{ \url{{{ website }}} }""";
                i++;
            }
            if (!github.IsNull)
            {
                arr[i] = $$"""\textcolor{sectcol}{ \url{{{ github }}} }""";
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
    public ImmutableArray<Event> PersonalProjects = ImmutableArray<Event>.Empty;
    public ImmutableArray<Event> Educations = ImmutableArray<Event>.Empty;
    public ImmutableArray<Section> SectionOrder = [
        Section.Languages,
        Section.WorkExperience,
        Section.Education,
        Section.PersonalProjects,
    ];
    public NullableRegularString Website;
    public NullableRegularString GitHub;
}

public enum Section
{
    Languages,
    WorkExperience,
    Education,
    PersonalProjects,
}

public record struct Event()
{
    public required RegularString Title;
    public required Place Place;
    public required DateRange DateRange;
    public float DebugScore;
    public ImmutableArray<DebugTagScore> DebugTagScores = [];
    public NullableLatexString Text = NullableLatexString.Null;
    public ImmutableArray<SubEvent> SubItems = [];
    public ImmutableArray<RegularString> Urls = [];
}

public readonly record struct DebugTagScore(RegularString Tag, float Score);

public readonly record struct SubEvent
{
    public SubEvent(
        float debugScore,
        LatexString @string) : this(debugScore, @string, [])
    {
    }

    public SubEvent(
        float debugScore,
        LatexString @string,
        ImmutableArray<DebugTagScore> debugTagScores)
    {
        DebugScore = debugScore;
        String = @string;
        DebugTagScores = debugTagScores.IsDefault
            ? []
            : debugTagScores;
    }

    public float DebugScore { get; }
    public LatexString String { get; }
    public ImmutableArray<DebugTagScore> DebugTagScores { get; }
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
public readonly record struct Place(RegularString Name)
{
    public static Place Personal => new("Personal");
    public bool IsPersonal => Name == "Personal";
}
// public readonly record struct JobPosition(string Title);

public readonly record struct OptionalDateParts
{
    public readonly int Year { get; }
    public readonly int Month { get; }
    public readonly int Day { get; }

    [JsonConstructor]
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

    [JsonIgnore]
    public static OptionalDateParts Unspecified => default;
    [JsonIgnore]
    public bool IsUnspecified => Year == 0;
}

public readonly record struct DateRange(
    OptionalDateParts Start,
    OptionalDateParts End) : ISpanFormattable
{
    public static DateRange Ongoing(OptionalDateParts start)
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
            // pad with zeros
            const string formatPadLeft = "00";

            if (d.Day != 0)
            {
                if (!helper.Append(d.Day, format: formatPadLeft, provider))
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
                if (!helper.Append(d.Month, format: formatPadLeft, provider))
                {
                    return false;
                }
                if (!helper.Append('.'))
                {
                    return false;
                }
            }
            Debug.Assert(d.Year != 0);
            if (!helper.Append(d.Year, format: null, provider))
            {
                return false;
            }
            return true;
        }
    }
}

public sealed class DateRangeComparer : IComparer<DateRange>
{
    private readonly Func<DateRange, OptionalDateParts> _selector;

    private DateRangeComparer(Func<DateRange, OptionalDateParts> selector)
    {
        _selector = selector;
    }

    public static DateRangeComparer ByStart { get; } = new(dr => dr.Start);
    public static DateRangeComparer ByEnd { get; } = new(dr => dr.End);

    public int Compare(DateRange x, DateRange y)
    {
        return CompareDates(_selector(x), _selector(y));
    }

    private static int CompareDates(OptionalDateParts a, OptionalDateParts b)
    {
        // Unspecified dates are considered "greater than" specified dates
        // (they sort to the end, like ongoing/current dates)
        if (a.IsUnspecified && b.IsUnspecified)
        {
            return 0;
        }
        if (a.IsUnspecified)
        {
            return 1;
        }
        if (b.IsUnspecified)
        {
            return -1;
        }

        // Compare years
        int yearComparison = a.Year.CompareTo(b.Year);
        if (yearComparison != 0)
        {
            return yearComparison;
        }

        // Compare months (0 means unspecified, treat as less precise but equal within year)
        if (a.Month == 0 && b.Month == 0)
        {
            return 0;
        }
        if (a.Month == 0)
        {
            return -1; // Less precise sorts earlier
        }
        if (b.Month == 0)
        {
            return 1;
        }

        int monthComparison = a.Month.CompareTo(b.Month);
        if (monthComparison != 0)
        {
            return monthComparison;
        }

        // Compare days (0 means unspecified)
        if (a.Day == 0 && b.Day == 0)
        {
            return 0;
        }
        if (a.Day == 0)
        {
            return -1; // Less precise sorts earlier
        }
        if (b.Day == 0)
        {
            return 1;
        }

        return a.Day.CompareTo(b.Day);
    }
}

public readonly record struct Language(RegularString Name, RegularString ShortName)
{
    public static Language English => new("English", "EN");
    public static Language Romanian => new("Romanian", "RO");
    public static Language Russian => new("Russian", "RU");
}
public readonly record struct LanguageProficiencyLevel(RegularString Value)
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
    LanguageProficiencyLevel GeneralProficiencyLevel,
    ImmutableArray<LanguageSkill> Skills = default)
{
    public readonly ImmutableArray<LanguageSkill> Skills = Skills == default ? [] : Skills;
}

public readonly record struct LanguageSkill(RegularString Text)
{
}

// NOTE: Cannot make the parameter ReadOnlySpan<char>,
// for some reason ref struct is not supported in interpolation?
public readonly record struct LatexEscapedString(string Value) : ISpanFormattable
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
        if (destination.Length < Value.Length)
        {
            return false;
        }

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
        if (!helper.Append(Value))
        {
            return false;
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

public readonly record struct RegularString(string Value) : ISpanFormattable
{
    public string ToString(
        string? format,
        IFormatProvider? formatProvider)
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
        return new LatexEscapedString(Value).TryFormat(
            destination,
            out charsWritten,
            format,
            provider);
    }

    public override string ToString() => $"{this}";

    public static implicit operator RegularString(string s)
    {
        return new RegularString(s);
    }
}

public readonly record struct NullableRegularString : ISpanFormattable
{
    public readonly string? Value;

    public NullableRegularString(string? value)
    {
        Value = value;
    }

    public NullableRegularString(RegularString s) : this(s.Value)
    {
    }

    public static NullableRegularString Null => default;
    public bool IsNull => Value is null;
    public RegularString ToInfoString()
    {
        Debug.Assert(Value is not null);
        return new RegularString(Value);
    }

    public static implicit operator NullableRegularString(RegularString s)
    {
        return new NullableRegularString(s);
    }

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
        if (Value is null)
        {
            charsWritten = 0;
            return true;
        }

        return new RegularString(Value).TryFormat(
            destination,
            out charsWritten,
            format,
            provider);
    }

    public static implicit operator NullableRegularString(string? s)
    {
        return new NullableRegularString(s);
    }
}

public readonly record struct CategorizedInfo(
    Category Category,
    RegularString Value);

public readonly record struct CategorizedInfoList(
    Category Category,
    ImmutableArray<RegularString> Values);

public readonly record struct Category(string DisplayName, bool IsUrl = false)
{
    public static Category Unspecified = new("");
    public static Category Website => new("Website", IsUrl: true);
    public static Category GitHub => new("GitHub", IsUrl: true);
    public static Category LinkedIn => new("LinkedIn", IsUrl: true);
    public static Category Email => new("Email");
    public static Category Location => new("Location");
    public static Category Phone => new("Phone");
    public static Category Technologies => new("Technologies");
}

public readonly record struct Name(
    RegularString First,
    RegularString Last)
{
}

public readonly record struct Profession(RegularString Value);

public readonly record struct Location(
    string City,
    string Country)
{
    public static implicit operator NullableLocation(Location location)
    {
        return new NullableLocation(location.City, location.Country);
    }

    public RegularString FormatInfo()
    {
        return new($"{City}, {Country}");
    }
}

public readonly record struct NullableLocation(
    NullableRegularString City,
    NullableRegularString Country)
{
    public static NullableLocation Null => default;
    public readonly bool IsNull
    {
        get
        {
            Debug.Assert(Country.IsNull == City.IsNull);
            return Country.IsNull;
        }
    }
}

public static class LatexConverter
{
    public static LatexString ToLatexString(
        this RichText richText)
    {
        var visitor = VisitationMap.CreateVisitor();
        visitor.AddOutput();
        visitor.Visit(richText);
        return new(visitor.GetOutput().ToString());
    }

    private static readonly RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Override<Href>(next => (node, c) =>
        {
            var sb = c.GetOutput();
            var str = new LatexEscapedString(node.Url.ToString());
            sb.Append($@"\href{{{str}}}{{");
            next(node, c);
            sb.Append("}");
        })
        .Override<StyledText>(next => (node, c) =>
        {
            var sb = c.GetOutput();
            var str = new LatexEscapedString(node.Text);

            int indent = 0;
            foreach (var x in new (StyleFlags Flag, string Label)[]
                {
                    (StyleFlags.Bold, "textbf"),
                    // Might fail, consider verb||
                    (StyleFlags.Code, "texttt"),
                    (StyleFlags.Italic, "textit"),
                })
            {
                if (node.Style.HasFlag(x.Flag))
                {
                    sb.Append($@"\{x.Label}{{");
                    indent++;
                }
            }

            sb.Append($"{str}");

            next(node, c);

            sb.Append($"{new Repeat("}", indent)}");
        })
        .Override<PlainText>(next => (node, c) =>
        {
            var sb = c.GetOutput();
            AppendEscapedString(sb, node.Text);
            next(node, c);
        })
        .Default<RichText>()
        .Build();

    private static void AppendEscapedString(StringBuilder sb, string str)
    {
        var latexStr = new LatexEscapedString(str);
        sb.Append($"{latexStr}");
    }

}
